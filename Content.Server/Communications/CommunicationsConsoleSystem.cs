using Content.Server.Administration.Logs;
using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Popups;
using Content.Server.RoundEnd;
using Content.Server.Screens.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Communications;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Content.Server.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using System.Collections.Generic;
using Content.Server.Beacon; // Added for BeaconSystem

namespace Content.Server.Communications
{
    public sealed class CommunicationsConsoleSystem : EntitySystem
    {
        [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
        [Dependency] private readonly AlertLevelSystem _alertLevelSystem = default!;
        [Dependency] private readonly ChatSystem _chatSystem = default!;
        [Dependency] private readonly DeviceNetworkSystem _deviceNetworkSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;
        [Dependency] private readonly StationSystem _stationSystem = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        [Dependency] private readonly BeaconSystem _beacon = default!; // Added for Beacon Check

        private const float UIUpdateInterval = 5.0f;

        public override void Initialize()
        {
            // All events that refresh the BUI
            SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
            SubscribeLocalEvent<RoundEndSystemChangedEvent>(_ => OnGenericBroadcastEvent());
            SubscribeLocalEvent<AlertLevelDelayFinishedEvent>(_ => OnGenericBroadcastEvent());

            // Messages from the BUI
            SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleSelectAlertLevelMessage>(OnSelectAlertLevelMessage);
            SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleAnnounceMessage>(OnAnnounceMessage);
            SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleTargetedAnnounceMessage>(OnTargetedAnnounceMessage); // Added new handler
            SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleBroadcastMessage>(OnBroadcastMessage);
            SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleCallEmergencyShuttleMessage>(OnCallShuttleMessage);
            SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleRecallEmergencyShuttleMessage>(OnRecallShuttleMessage);

            // On console init, set cooldown
            SubscribeLocalEvent<CommunicationsConsoleComponent, MapInitEvent>(OnCommunicationsConsoleMapInit);
        }

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<CommunicationsConsoleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                // TODO refresh the UI in a less horrible way
                if (comp.AnnouncementCooldownRemaining >= 0f)
                {
                    comp.AnnouncementCooldownRemaining -= frameTime;
                }

                if (HasComp<CentralCommandConsoleComponent>(uid))
                    continue;

                comp.UIUpdateAccumulator += frameTime;

                if (comp.UIUpdateAccumulator < UIUpdateInterval)
                    continue;

                comp.UIUpdateAccumulator -= UIUpdateInterval;

                if (_uiSystem.IsUiOpen(uid, CommunicationsConsoleUiKey.Key))
                    UpdateCommsConsoleInterface(uid, comp);
            }

            base.Update(frameTime);
        }

        public void OnCommunicationsConsoleMapInit(EntityUid uid, CommunicationsConsoleComponent comp, MapInitEvent args)
        {
            comp.AnnouncementCooldownRemaining = comp.InitialDelay;
            UpdateCommsConsoleInterface(uid, comp);
        }

        private void OnGenericBroadcastEvent()
        {
            var query = EntityQueryEnumerator<CommunicationsConsoleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                UpdateCommsConsoleInterface(uid, comp);
            }
        }

        private void OnAlertLevelChanged(AlertLevelChangedEvent args)
        {
            var query = EntityQueryEnumerator<CommunicationsConsoleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                var entStation = _stationSystem.GetOwningStation(uid);
                if (args.Station == entStation)
                    UpdateCommsConsoleInterface(uid, comp);
            }
        }

        public void UpdateCommsConsoleInterface()
        {
            var query = EntityQueryEnumerator<CommunicationsConsoleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                UpdateCommsConsoleInterface(uid, comp);
            }
        }

        public void UpdateCommsConsoleInterface(EntityUid uid, CommunicationsConsoleComponent comp)
        {
            var stationUid = _stationSystem.GetOwningStation(uid);
            List<string>? levels = null;
            string currentLevel = default!;
            float currentDelay = 0;
            TimeSpan? expectedCountdownEnd = null;

            if (stationUid != null)
            {
                if (TryComp(stationUid.Value, out AlertLevelComponent? alertComp) &&
                    alertComp.AlertLevels != null)
                {
                    if (alertComp.IsSelectable)
                    {
                        levels = new();
                        foreach (var (id, detail) in alertComp.AlertLevels.Levels)
                        {
                            if (detail.Selectable)
                            {
                                levels.Add(id);
                            }
                        }
                    }

                    currentLevel = alertComp.CurrentLevel;
                    currentDelay = _alertLevelSystem.GetAlertLevelDelay(stationUid.Value, alertComp);
                }

                if (TryComp<StationEmergencyStateComponent>(stationUid.Value, out var stationState))
                {
                    expectedCountdownEnd = stationState.ShuttleArrivalTime ?? stationState.CountdownEndTime;
                }
            }

            var stations = new List<StationInfo>();
            if (HasComp<CentralCommandConsoleComponent>(uid))
            {
                foreach (var station in _stationSystem.GetStations())
                {
                    var stationName = Name(station);
                    if (stationName != null)
                    {
                        stations.Add(new StationInfo(GetNetEntity(station), stationName));
                    }
                }
            }

            _uiSystem.SetUiState(uid, CommunicationsConsoleUiKey.Key, new CommunicationsConsoleInterfaceState(
                CanAnnounce(comp),
                true, // CanBroadcast
                stationUid.HasValue && CanCallOrRecall(comp, stationUid.Value),
                levels,
                currentLevel,
                currentDelay,
                stations,
                expectedCountdownEnd
            ));
        }

        private static bool CanAnnounce(CommunicationsConsoleComponent comp)
        {
            return comp.AnnouncementCooldownRemaining <= 0f;
        }

        private bool CanUse(EntityUid user, EntityUid console)
        {
            if (TryComp<AccessReaderComponent>(console, out var accessReaderComponent))
            {
                return _accessReaderSystem.IsAllowed(user, console, accessReaderComponent);
            }
            return true;
        }

        private bool CanCallOrRecall(CommunicationsConsoleComponent comp, EntityUid station)
        {
            if (_roundEndSystem.IsRoundEndRequested(station))
                return _roundEndSystem.CanRecall(station);

            return _roundEndSystem.CanCall(station);
        }

        private void OnSelectAlertLevelMessage(EntityUid uid, CommunicationsConsoleComponent comp, CommunicationsConsoleSelectAlertLevelMessage message)
        {
            if (message.Actor is not { Valid: true } mob)
                return;

            if (!CanUse(mob, uid))
            {
                _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), message.Actor, PopupType.Medium);
                return;
            }

            var stationUid = _stationSystem.GetOwningStation(uid);
            if (stationUid != null)
            {
                _alertLevelSystem.SetLevel(stationUid.Value, message.Level, true, true);
            }
        }

        private void OnTargetedAnnounceMessage(EntityUid uid, CommunicationsConsoleComponent comp, CommunicationsConsoleTargetedAnnounceMessage message)
        {
            var maxLength = _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength);
            var msg = SharedChatSystem.SanitizeAnnouncement(message.Message, maxLength);
            var author = Loc.GetString("comms-console-announcement-unknown-sender");

            if (message.Actor is not { Valid: true } mob)
                return;

            if (!CanAnnounce(comp))
                return;

            if (!CanUse(mob, uid))
            {
                _popupSystem.PopupEntity(Loc.GetString("comms-console-permission-denied"), uid, message.Actor);
                return;
            }

            var tryGetIdentityShortInfoEvent = new TryGetIdentityShortInfoEvent(uid, mob);
            RaiseLocalEvent(tryGetIdentityShortInfoEvent);
            author = tryGetIdentityShortInfoEvent.Title;

            comp.AnnouncementCooldownRemaining = comp.Delay;
            UpdateCommsConsoleInterface(uid, comp);

            var ev = new CommunicationConsoleAnnouncementEvent(uid, comp, msg, message.Actor);
            RaiseLocalEvent(ref ev);

            Loc.TryGetString(comp.Title, out var title);
            title ??= comp.Title;
            
            if (comp.AnnounceSentBy)
                msg += "\n" + Loc.GetString("comms-console-announcement-sent-by") + " " + author;

            if (comp.Global)
            {
                _chatSystem.DispatchGlobalAnnouncement(msg, title, announcementSound: comp.Sound, colorOverride: comp.Color);
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(message.Actor):player} has sent a global announcement: {msg}");
                return;
            }

            foreach (var stationNet in message.Targets)
            {
                var station = GetEntity(stationNet);
                if (!_beacon.IsBeaconActive(station))
                {
                    _popupSystem.PopupEntity(Loc.GetString("comms-console-announcement-failed-no-beacon", ("station", Name(station))), uid, message.Actor);
                    continue;
                }
                _chatSystem.DispatchStationAnnouncement(station, msg, title, colorOverride: comp.Color);
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(message.Actor):player} sent station announcement to {Name(station)}: {msg}");
            }
        }

        private void OnAnnounceMessage(EntityUid uid, CommunicationsConsoleComponent comp, CommunicationsConsoleAnnounceMessage message)
        {
            var stationUid = _stationSystem.GetOwningStation(uid);
            if (stationUid == null)
                return;

            var targetedMessage = new CommunicationsConsoleTargetedAnnounceMessage(message.Message, new List<NetEntity> { GetNetEntity(stationUid.Value) });
            targetedMessage.Actor = message.Actor;
            OnTargetedAnnounceMessage(uid, comp, targetedMessage);
        }

        private void OnBroadcastMessage(EntityUid uid, CommunicationsConsoleComponent component, CommunicationsConsoleBroadcastMessage message)
        {
            if (!TryComp<DeviceNetworkComponent>(uid, out var net))
                return;

            var payload = new NetworkPayload
            {
                [ScreenMasks.Text] = message.Message
            };

            _deviceNetworkSystem.QueuePacket(uid, null, payload, net.TransmitFrequency);

            _adminLogger.Add(LogType.DeviceNetwork, LogImpact.Low, $"{ToPrettyString(message.Actor):player} has sent the following broadcast: {message.Message:msg}");
        }



        private void OnCallShuttleMessage(EntityUid uid, CommunicationsConsoleComponent comp, CommunicationsConsoleCallEmergencyShuttleMessage message)
        {
            var stationUid = _stationSystem.GetOwningStation(uid);
            if (stationUid == null || !CanCallOrRecall(comp, stationUid.Value))
                return;

            var mob = message.Actor;

            if (!CanUse(mob, uid))
            {
                _popupSystem.PopupEntity(Loc.GetString("comms-console-permission-denied"), uid, message.Actor);
                return;
            }

            var ev = new CommunicationConsoleCallShuttleAttemptEvent(uid, comp, mob);
            RaiseLocalEvent(ref ev);
            if (ev.Cancelled)
            {
                _popupSystem.PopupEntity(ev.Reason ?? Loc.GetString("comms-console-shuttle-unavailable"), uid, message.Actor);
                return;
            }

            _roundEndSystem.RequestRoundEnd(station: stationUid);
            _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(mob):player} has called the shuttle for station {ToPrettyString(stationUid.Value)}.");
        }

        private void OnRecallShuttleMessage(EntityUid uid, CommunicationsConsoleComponent comp, CommunicationsConsoleRecallEmergencyShuttleMessage message)
        {
            var stationUid = _stationSystem.GetOwningStation(uid);
            if (stationUid == null || !CanCallOrRecall(comp, stationUid.Value))
                return;

            if (!CanUse(message.Actor, uid))
            {
                _popupSystem.PopupEntity(Loc.GetString("comms-console-permission-denied"), uid, message.Actor);
                return;
            }

            _roundEndSystem.CancelRoundEndCountdown(station: stationUid);
            _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(message.Actor):player} has recalled the shuttle for station {ToPrettyString(stationUid.Value)}.");
        }
    }

    /// <summary>
    /// Raised on announcement
    /// </summary>
    [ByRefEvent]
    public record struct CommunicationConsoleAnnouncementEvent(EntityUid Uid, CommunicationsConsoleComponent Component, string Text, EntityUid? Sender)
    {
        public EntityUid Uid = Uid;
        public CommunicationsConsoleComponent Component = Component;
        public EntityUid? Sender = Sender;
        public string Text = Text;
    }

    /// <summary>
    /// Raised on shuttle call attempt. Can be cancelled
    /// </summary>
    [ByRefEvent]
    public record struct CommunicationConsoleCallShuttleAttemptEvent(EntityUid Uid, CommunicationsConsoleComponent Component, EntityUid? Sender)
    {
        public bool Cancelled = false;
        public EntityUid Uid = Uid;
        public CommunicationsConsoleComponent Component = Component;
        public EntityUid? Sender = Sender;
        public string? Reason;
    }
}
