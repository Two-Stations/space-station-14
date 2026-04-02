using System.Threading;
using Content.Server.Administration.Logs;
using Content.Server.AlertLevel;
using Content.Shared.CCVar;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.GameTicking;
using Content.Server.Screens.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.GameTicking;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Station.Components;
using Timer = Robust.Shared.Timing.Timer;
using Content.Server.Station.Components;
using System.Linq;

namespace Content.Server.RoundEnd
{
    /// <summary>
    /// Handles ending rounds normally and also via requesting it (e.g. via comms console)
    /// If you request a round end then an escape shuttle will be used.
    /// </summary>
    public sealed class RoundEndSystem : EntitySystem
    {
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IPrototypeManager _protoManager = default!;
        [Dependency] private readonly ChatSystem _chatSystem = default!;
        [Dependency] private readonly GameTicker _gameTicker = default!;
        [Dependency] private readonly DeviceNetworkSystem _deviceNetworkSystem = default!;
        [Dependency] private readonly EmergencyShuttleSystem _shuttle = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly StationSystem _stationSystem = default!;

        public TimeSpan DefaultCooldownDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Countdown to use where there is no station alert countdown to be found.
        /// </summary>
        public TimeSpan DefaultCountdownDuration { get; set; } = TimeSpan.FromMinutes(10);

    private readonly Dictionary<EntityUid, CancellationTokenSource> _countdownTokens = new();
    private readonly Dictionary<EntityUid, CancellationTokenSource> _cooldownTokens = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
            SubscribeLocalEvent<MapInitEvent>(OnMapInit);
        }

        private void OnMapInit(MapInitEvent ev)
        {
            var query = AllEntityQuery<StationEmergencyStateComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Status == EmergencyShuttleStatus.Called && comp.ShuttleArrivalTime.HasValue)
                {
                    var token = new CancellationTokenSource();
                    _countdownTokens[uid] = token;

                    var dockTime = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.EmergencyShuttleDockTime));
                    var countdownTime = comp.ShuttleArrivalTime.Value - _gameTiming.CurTime - dockTime;
                    Timer.Spawn(countdownTime, () => _shuttle.CallEmergencyShuttle(uid, dockTime), token.Token);
                }

                if (comp.AutoCallStartTime != default)
                {
                    var token = new CancellationTokenSource();
                    _cooldownTokens[uid] = token;

                    var cooldownTime = DefaultCooldownDuration;
                    Timer.Spawn(cooldownTime, () =>
                    {
                        if (token.IsCancellationRequested)
                            return;
                        _cooldownTokens.Remove(uid);
                        RaiseLocalEvent(RoundEndSystemChangedEvent.Default);
                    }, token.Token);
                }
            }
        }

        private void Reset()
        {
            foreach (var token in _countdownTokens.Values)
            {
                token.Cancel();
            }
            _countdownTokens.Clear();

            foreach (var token in _cooldownTokens.Values)
            {
                token.Cancel();
            }
            _cooldownTokens.Clear();
        }

        public bool CanCall(EntityUid station)
        {
            if (TryComp<StationEmergencyStateComponent>(station, out var stationState) && stationState.CantRecall)
                return false;

            return !_cooldownTokens.ContainsKey(station);
        }

        public bool CanRecall(EntityUid station)
        {
            if (!TryComp<StationEmergencyStateComponent>(station, out var stationState))
                return false;

            if (stationState.CantRecall || stationState.IsDockedAtStation)
                return false;

            return true;
        }

        public bool IsRoundEndRequested(EntityUid? station = null)
        {
            if (station != null)
                return _countdownTokens.ContainsKey(station.Value);
            return _countdownTokens.Count > 0;
        }

        /// <summary>
        /// Starts the process of ending the round by calling evac
        /// </summary>
        /// <param name="requester"></param>
        /// <param name="checkCooldown"></param>
        /// <param name="text">text in the announcement of shuttle calling</param>
        /// <param name="name">name in the announcement of shuttle calling</param>
        /// <param name="cantRecall">if the station shouldn't be able to recall the shuttle</param>
        public void RequestRoundEnd(EntityUid? requester = null, bool checkCooldown = true, string text = "round-end-system-shuttle-called-announcement", string name = "round-end-system-shuttle-sender-announcement", bool cantRecall = false, EntityUid? station = null)
        {
            var duration = DefaultCountdownDuration;
            var stationToCall = station;

            if (requester != null)
            {
                var stationUid = _stationSystem.GetOwningStation(requester.Value);
                if (stationUid != null)
                {
                    stationToCall ??= stationUid;
                    if (TryComp<AlertLevelComponent>(stationUid, out var alertLevel))
                    {
                        duration = _protoManager
                            .Index<AlertLevelPrototype>(AlertLevelSystem.DefaultAlertLevelSet)
                            .Levels[alertLevel.CurrentLevel].ShuttleTime;
                    }
                }
            }

            RequestRoundEnd(duration, requester, checkCooldown, text, name, cantRecall, stationToCall);
        }

        /// <summary>
        /// Starts the process of ending the round by calling evac
        /// </summary>
        /// <param name="countdownTime">time for evac to arrive</param>
        /// <param name="requester"></param>
        /// <param name="checkCooldown"></param>
        /// <param name="text">text in the announcement of shuttle calling</param>
        /// <param name="name">name in the announcement of shuttle calling</param>
        /// <param name="cantRecall">if the station shouldn't be able to recall the shuttle</param>
        /// <param name="station">The station to call the shuttle to.</param>
        public void RequestRoundEnd(TimeSpan countdownTime, EntityUid? requester = null, bool checkCooldown = true, string text = "round-end-system-shuttle-called-announcement", string name = "round-end-system-shuttle-sender-announcement", bool cantRecall = false, EntityUid? station = null)
        {
            if (_gameTicker.RunLevel != GameRunLevel.InRound)
                return;

            if (station == null)
            {
                var query = EntityQueryEnumerator<StationEmergencyShuttleComponent>();
                while (query.MoveNext(out var uid, out _))
                {
                    RequestRoundEnd(countdownTime, requester, checkCooldown, text, name, cantRecall, uid);
                }
                return;
            }

            if (checkCooldown && _cooldownTokens.ContainsKey(station.Value))
                return;

            if (_countdownTokens.ContainsKey(station.Value))
                return;

            var stationState = EnsureComp<StationEmergencyStateComponent>(station.Value);
            stationState.CantRecall = cantRecall;
            stationState.CountdownEndTime = _gameTiming.CurTime + countdownTime;

            var token = new CancellationTokenSource();
            _countdownTokens[station.Value] = token;

            if (requester != null)
            {
                _adminLogger.Add(LogType.ShuttleCalled, LogImpact.High, $"Shuttle called by {ToPrettyString(requester.Value):user} for station {ToPrettyString(station.Value)}");
            }
            else
            {
                _adminLogger.Add(LogType.ShuttleCalled, LogImpact.High, $"Shuttle called for station {ToPrettyString(station.Value)}");
            }

            int time;
            string units;

            if (countdownTime.TotalSeconds < 60)
            {
                time = countdownTime.Seconds;
                units = "eta-units-seconds";
            }
            else
            {
                time = countdownTime.Minutes;
                units = "eta-units-minutes";
            }

            _chatSystem.DispatchStationAnnouncement(station.Value, Loc.GetString(text,
                ("time", time),
                ("units", Loc.GetString(units))),
                Loc.GetString(name),
                false,
                colorOverride: Color.Gold);

            if (!stationState.AutoCalledBefore) _audio.PlayGlobal(new SoundPathSpecifier("/Audio/Announcements/shuttlecalled.ogg"), Filter.Broadcast(), true, AudioParams.Default.AddVolume(-4));
            else _audio.PlayGlobal(new SoundPathSpecifier("/Audio/Corvax/Announcements/crew_s_called.ogg"), Filter.Broadcast(), true, AudioParams.Default.AddVolume(-4));

            stationState.AutoCalledBefore = true;

            var dockTime = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.EmergencyShuttleDockTime));
            Timer.Spawn(countdownTime, () => _shuttle.CallEmergencyShuttle(station.Value, dockTime), token.Token);

            ActivateCooldown(station.Value);
            RaiseLocalEvent(RoundEndSystemChangedEvent.Default);

            if (TryComp<StationEmergencyShuttleComponent>(station, out var stationShuttle) &&
                TryComp<DeviceNetworkComponent>(stationShuttle.EmergencyShuttle, out var net))
            {
                var destMap = Transform(_stationSystem.GetLargestGrid(station.Value)!.Value).MapUid;
                if(TryComp<StationCentcommComponent>(station, out var centcomm))
                {
                    var payload = new NetworkPayload
                    {
                        [ShuttleTimerMasks.ShuttleMap] = stationShuttle.EmergencyShuttle,
                        [ShuttleTimerMasks.SourceMap] = centcomm.MapEntity,
                        [ShuttleTimerMasks.DestMap] = destMap,
                        [ShuttleTimerMasks.ShuttleTime] = countdownTime,
                        [ShuttleTimerMasks.SourceTime] = countdownTime + TimeSpan.FromSeconds(stationState.TransitTime + _cfg.GetCVar(CCVars.EmergencyShuttleDockTime)),
                        [ShuttleTimerMasks.DestTime] = countdownTime,
                    };
                    _deviceNetworkSystem.QueuePacket(stationShuttle.EmergencyShuttle.Value, null, payload, net.TransmitFrequency);
                }
            }
        }

        public IReadOnlyList<EntityUid> GetStationsWithActiveCountdown() => _countdownTokens.Keys.ToList();

        private bool CancelSingleStation(EntityUid station, EntityUid? requester, bool forceRecall)
        {
            if (!TryComp<StationEmergencyStateComponent>(station, out var stationState))
                return false;

            stationState.CountdownEndTime = null;

            // Disallow recalling if the shuttle is already docked, unless forced by an admin.
            if (!forceRecall && (stationState.CantRecall || stationState.IsDockedAtStation))
                return false;

            if (!_countdownTokens.TryGetValue(station, out var token))
                return false;

            _shuttle.RecallShuttle(station);
            token.Cancel();
            _countdownTokens.Remove(station);

            if (requester != null)
                _adminLogger.Add(LogType.ShuttleRecalled, LogImpact.High, $"Shuttle for station {ToPrettyString(station)} recalled by {ToPrettyString(requester.Value):user}");
            else
                _adminLogger.Add(LogType.ShuttleRecalled, LogImpact.High, $"Shuttle for station {ToPrettyString(station)} recalled");

            ActivateCooldown(station);
            RaiseLocalEvent(RoundEndSystemChangedEvent.Default);

            // remove active clientside evac shuttle timers by zeroing the target time
            var zero = TimeSpan.Zero;
            var shuttle = _shuttle.GetShuttle(station);
            if (shuttle != null && TryComp<DeviceNetworkComponent>(shuttle, out var net))
            {
                var payload = new NetworkPayload
                {
                    [ShuttleTimerMasks.ShuttleMap] = shuttle,
                    [ShuttleTimerMasks.ShuttleTime] = zero,
                    [ShuttleTimerMasks.SourceTime] = zero,
                    [ShuttleTimerMasks.DestTime] = zero,
                };

                if (TryComp<StationCentcommComponent>(station, out var centcomm))
                {
                    payload[ShuttleTimerMasks.SourceMap] = centcomm.MapEntity;
                }

                if (TryComp<StationDataComponent>(station, out var stationData))
                {
                    var grid = _stationSystem.GetLargestGrid((station, stationData));
                    if (grid.HasValue)
                    {
                        payload[ShuttleTimerMasks.DestMap] = Transform(grid.Value).MapUid;
                    }
                }

                _deviceNetworkSystem.QueuePacket(shuttle.Value, null, payload, net.TransmitFrequency);
            }

            return true;
        }

        public void CancelRoundEndCountdown(EntityUid? requester = null, bool forceRecall = false, EntityUid? station = null, bool announce = true)
        {
            if (_gameTicker.RunLevel != GameRunLevel.InRound)
                return;

            // No station specified, so recall all of them.
            if (station == null)
            {
                var recalledAny = false;
                var stationsToRecall = GetStationsWithActiveCountdown().ToList();
                foreach (var stationToRecall in stationsToRecall)
                {
                    if (CancelSingleStation(stationToRecall, requester, forceRecall))
                    {
                        recalledAny = true;
                    }
                }

                if (recalledAny && announce)
                {
                    _chatSystem.DispatchGlobalAnnouncement(Loc.GetString("round-end-system-shuttle-recalled-announcement"),
                        Loc.GetString("round-end-system-shuttle-sender-announcement"), false, colorOverride: Color.Gold);

                    _audio.PlayGlobal(new SoundPathSpecifier("/Audio/Announcements/shuttlerecalled.ogg"), Filter.Broadcast(), true, AudioParams.Default.AddVolume(-4));
                }

                return;
            }

            // Logic for a single station
            if (!CancelSingleStation(station.Value, requester, forceRecall))
                return;

            if (announce)
            {
                _chatSystem.DispatchGlobalAnnouncement(Loc.GetString("round-end-system-shuttle-recalled-announcement"),
                    Loc.GetString("round-end-system-shuttle-sender-announcement"), false, colorOverride: Color.Gold);

                _audio.PlayGlobal(new SoundPathSpecifier("/Audio/Announcements/shuttlerecalled.ogg"), Filter.Broadcast(), true, AudioParams.Default.AddVolume(-4)); // Corvax-Announcements: Decrease volume
            }
        }

        public void EndRound(TimeSpan? countdownTime = null)
        {
            if (_gameTicker.RunLevel != GameRunLevel.InRound) return;

            foreach (var token in _countdownTokens.Values)
            {
                token.Cancel();
            }
            _countdownTokens.Clear();
            RaiseLocalEvent(RoundEndSystemChangedEvent.Default);

            _gameTicker.EndRound();
            var roundEndCts = new CancellationTokenSource();

            countdownTime ??= TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.RoundRestartTime));
            int time;
            string unitsLocString;
            if (countdownTime.Value.TotalSeconds < 60)
            {
                time = countdownTime.Value.Seconds;
                unitsLocString = "eta-units-seconds";
            }
            else
            {
                time = countdownTime.Value.Minutes;
                unitsLocString = "eta-units-minutes";
            }
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString(
                    "round-end-system-round-restart-eta-announcement",
                    ("time", time),
                    ("units", Loc.GetString(unitsLocString))));
            Timer.Spawn(countdownTime.Value, AfterEndRoundRestart, roundEndCts.Token);
        }

        /// <summary>
        /// Starts a behavior to end the round
        /// </summary>
        /// <param name="behavior">The way in which the round will end</param>
        /// <param name="time"></param>
        /// <param name="sender"></param>
        /// <param name="textCall"></param>
        /// <param name="textAnnounce"></param>
        public void DoRoundEndBehavior(RoundEndBehavior behavior,
            TimeSpan time,
            string sender = "comms-console-announcement-title-centcom",
            string textCall = "round-end-system-shuttle-called-announcement",
            string textAnnounce = "round-end-system-shuttle-already-called-announcement")
        {
            switch (behavior)
            {
                case RoundEndBehavior.InstantEnd:
                    EndRound();
                    break;
                case RoundEndBehavior.ShuttleCall:
                    // Check is shuttle called or not. We should only dispatch announcement if it's already called
                    if (IsRoundEndRequested())
                    {
                        _chatSystem.DispatchGlobalAnnouncement(Loc.GetString(textAnnounce),
                            Loc.GetString(sender),
                            colorOverride: Color.Gold);
                    }
                    else
                    {
                        RequestRoundEnd(time, null, false, textCall,
                            Loc.GetString(sender));
                    }
                    break;
            }
        }

        private void AfterEndRoundRestart()
        {
            if (_gameTicker.RunLevel != GameRunLevel.PostRound) return;
            Reset();
            _gameTicker.RestartRound();
        }

        private void ActivateCooldown(EntityUid? station = null)
        {
            if (station != null)
            {
                var token = new CancellationTokenSource();
                _cooldownTokens[station.Value] = token;

                Timer.Spawn(DefaultCooldownDuration, () =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    _cooldownTokens.Remove(station.Value);
                    RaiseLocalEvent(RoundEndSystemChangedEvent.Default);
                }, token.Token);
            }
            else
            {
                var query = EntityQueryEnumerator<StationEmergencyShuttleComponent>();
                while (query.MoveNext(out var uid, out _))
                {
                    if (!_cooldownTokens.ContainsKey(uid))
                    {
                        ActivateCooldown(uid);
                    }
                }
            }
        }

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<StationEmergencyStateComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                var mins = comp.AutoCalledBefore ? _cfg.GetCVar(CCVars.EmergencyShuttleAutoCallExtensionTime)
                                                : _cfg.GetCVar(CCVars.EmergencyShuttleAutoCallTime);
                if (mins != 0 && _gameTiming.CurTime - comp.AutoCallStartTime > TimeSpan.FromMinutes(mins))
                {
                    if (comp.Status == EmergencyShuttleStatus.Uncalled)
                    {
                        RequestRoundEnd(null, false, "round-end-system-shuttle-auto-called-announcement", station: uid);
                    }

                    // Always reset auto-call in case of a recall.
                    comp.AutoCallStartTime = _gameTiming.CurTime;
                }
            }
        }
    }

    public sealed class RoundEndSystemChangedEvent : EntityEventArgs
    {
        public static RoundEndSystemChangedEvent Default { get; } = new();
    }

    public enum RoundEndBehavior : byte
    {
        /// <summary>
        /// Instantly end round
        /// </summary>
        InstantEnd,

        /// <summary>
        /// Call shuttle with custom announcement
        /// </summary>
        ShuttleCall,

        /// <summary>
        /// Do nothing
        /// </summary>
        Nothing
    }
}
