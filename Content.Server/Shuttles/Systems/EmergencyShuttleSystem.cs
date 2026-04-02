using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Communications;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Pinpointer;
using Content.Server.Screens.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.GameTicking;
using Content.Shared.Localizations;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Content.Shared.Tag;
using Content.Shared.Tiles;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Server.RoundEnd;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.Shuttles.Systems;

public sealed partial class EmergencyShuttleSystem : SharedEmergencyShuttleSystem
{
    /*
     * Handles the escape shuttle + CentCom.
     */

    [Dependency] private readonly IAdminLogManager _logger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly CommunicationsConsoleSystem _commsConsole = default!;
    [Dependency] private readonly DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private readonly DockingSystem _dock = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly NavMapSystem _navMap = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    private readonly Dictionary<EntityUid, CancellationTokenSource> _roundEndCancelTokens = new();
    private readonly HashSet<EntityUid> _stationsWithEmergencyShuttle = new();
    private readonly HashSet<EntityUid> _arrivedAtCentcommStations = new();
    private readonly HashSet<EntityUid> _calledStations = new();
    private readonly TimeSpan _bufferTime = TimeSpan.FromSeconds(5);


    private const float ShuttleSpawnBuffer = 1f;

    private static readonly ProtoId<TagPrototype> DockTag = "DockEmergency";

    public override void Initialize()
    {
        base.Initialize();

        _configManager.SetCVar(CCVars.EmergencyShuttleEnabled, true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<StationRemovedEvent>(OnStationRemoved);
        SubscribeLocalEvent<StationEmergencyShuttleComponent, StationPostInitEvent>(OnStationStartup);
        SubscribeLocalEvent<StationCentcommComponent, ComponentShutdown>(OnCentcommShutdown);
        SubscribeLocalEvent<StationCentcommComponent, StationPostInitEvent>(OnStationInit);

        SubscribeLocalEvent<EmergencyShuttleComponent, FTLStartedEvent>(OnEmergencyFTL);
        SubscribeLocalEvent<EmergencyShuttleComponent, FTLCompletedEvent>(OnEmergencyFTLComplete);
        SubscribeLocalEvent<MapInitEvent>(OnMapInit, before: new []{typeof(StationSystem)});
        SubscribeLocalEvent<EmergencyShuttleArrivedAtCentComEvent>(OnShuttleArrivedAtCentcomm);
        SubscribeLocalEvent<EmergencyShuttleDepartedEvent>(OnShuttleDeparted);
    }

    private void OnShuttleDeparted(EmergencyShuttleDepartedEvent ev)
    {
        if (!TryComp<StationCentcommComponent>(ev.Station, out var centcomm) || !centcomm.MapEntity.HasValue)
            return;

        if (!TryComp<StationEmergencyStateComponent>(ev.Station, out var stationState))
            return;

        var transitTime = stationState.TransitTime;
        var query = EntityQueryEnumerator<EscapePodComponent, StationMemberComponent, DockingComponent, ShuttleComponent>();

        while (query.MoveNext(out var uid, out var pod, out var member, out var dock, out var shuttle))
        {
            if (member.Station != ev.Station)
                continue;

            _dock.Undock((uid, dock));
            var targetCoords = new EntityCoordinates(centcomm.MapEntity.Value, _random.NextVector2(1000f));
            _shuttle.FTLToCoordinates(uid, shuttle, targetCoords, Angle.Zero, hyperspaceTime: transitTime);
        }
    }

    private void OnStationRemoved(StationRemovedEvent args)
    {
        var uid = args.Station;
        _calledStations.Remove(uid);
        _arrivedAtCentcommStations.Remove(uid);
        _stationsWithEmergencyShuttle.Remove(uid);
        CheckRoundEnd();
    }

    private void OnShuttleArrivedAtCentcomm(EmergencyShuttleArrivedAtCentComEvent ev)
    {
        _arrivedAtCentcommStations.Add(ev.Station);
        CheckRoundEnd();
    }

    private void CheckRoundEnd()
    {
        var totalShuttles = _stationsWithEmergencyShuttle.Count;
        if (totalShuttles == 0)
            return;

        // For multi-station maps, require 2 shuttles to have arrived.
        // For single-station maps, require 1.
        var requiredArrivals = totalShuttles > 1 ? 2 : 1;

        if (_arrivedAtCentcommStations.Count >= requiredArrivals)
        {
            _roundEnd.EndRound();
        }
    }

    private void OnMapInit(MapInitEvent ev)
    {
    }

    private void OnRoundStart(RoundStartingEvent ev)
    {
        foreach (var comp in EntityQuery<StationEmergencyStateComponent>())
        {
            comp.Status = EmergencyShuttleStatus.Uncalled;
            comp.ShuttleArrivalTime = null;
            comp.EarlyLaunchAuthorized = false;
            comp.LaunchAnnounced = false;
        }

        foreach (var token in _roundEndCancelTokens.Values)
        {
            token.Cancel();
        }
        _roundEndCancelTokens.Clear();
        _arrivedAtCentcommStations.Clear();
        _calledStations.Clear();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var token in _roundEndCancelTokens.Values)
        {
            token.Cancel();
        }
        _roundEndCancelTokens.Clear();
        _stationsWithEmergencyShuttle.Clear();
        _arrivedAtCentcommStations.Clear();
        _calledStations.Clear();
    }

    public void CallEmergencyShuttle(EntityUid station, TimeSpan time)
    {
        if (!TryComp<StationEmergencyStateComponent>(station, out var stationState) ||
            stationState.Status != EmergencyShuttleStatus.Uncalled)
        {
            return;
        }

        if (!TryComp<StationEmergencyShuttleComponent>(station, out var stationComp))
            return;

        var dockResult = DockSingleEmergencyShuttle(station, stationComp);

        if (dockResult == null)
        {
            Log.Warning($"Could not find any emergency shuttles to dock for station {ToPrettyString(station)}.");
            return;
        }

        var multiplier = dockResult.ResultType switch
        {
            ShuttleDockResultType.OtherDock => _configManager.GetCVar(
                CCVars.EmergencyShuttleDockTimeMultiplierOtherDock),
            ShuttleDockResultType.NoDock => _configManager.GetCVar(
                CCVars.EmergencyShuttleDockTimeMultiplierNoDock),
            _ => 1,
        };

        var shuttleTime = time * multiplier;
        stationState.ShuttleArrivalTime = _timing.CurTime + shuttleTime;
        stationState.AutoCallStartTime = _timing.CurTime;
        stationState.Status = EmergencyShuttleStatus.Called;
        stationState.CountdownEndTime = null;
        _calledStations.Add(station);
        _commsConsole.UpdateCommsConsoleInterface();
        AnnounceShuttleDock(dockResult, multiplier > 1, shuttleTime);
    }

    public void RecallShuttle(EntityUid station)
    {
        if (!TryComp<StationEmergencyStateComponent>(station, out var stationState) ||
            stationState.Status == EmergencyShuttleStatus.Uncalled)
        {
            return;
        }

        stationState.Status = EmergencyShuttleStatus.Uncalled;
        stationState.ShuttleArrivalTime = null;
        stationState.IsDockedAtStation = false;
        _calledStations.Remove(station);

        if (_roundEndCancelTokens.TryGetValue(station, out var token))
        {
            token.Cancel();
            _roundEndCancelTokens.Remove(station);
        }

        if (!TryComp<StationEmergencyShuttleComponent>(station, out var stationComp))
            return;

        var shuttleEntity = stationComp.EmergencyShuttle;
        if (shuttleEntity == null ||
            !TryComp<ShuttleComponent>(shuttleEntity, out var shuttleComp) ||
            !TryComp<DockingComponent>(shuttleEntity, out var dockComp))
            return;

        if (TryComp<StationCentcommComponent>(station, out var centcommComp) && centcommComp.MapEntity.HasValue)
        {
            var homeMap = centcommComp.MapEntity.Value;
            var homeCoords = new EntityCoordinates(homeMap, _random.NextVector2(100f));
            _dock.Undock((shuttleEntity.Value, dockComp));
            _shuttle.FTLToCoordinates(shuttleEntity.Value, shuttleComp, homeCoords, Angle.Zero, hyperspaceTime: 0.1f);
        }
    }

    public void LaunchShuttle(EntityUid station)
    {
        if (!TryComp<StationEmergencyShuttleComponent>(station, out var stationShuttle) ||
            !TryComp<StationEmergencyStateComponent>(station, out var stationState))
            return;

        var shuttle = stationShuttle.EmergencyShuttle;

        if ( shuttle == null ||
            !TryComp<ShuttleComponent>(shuttle, out var shuttleComp) ||
            !TryComp<StationCentcommComponent>(station, out var centcomm))
            return;

        if (!centcomm.MapEntity.HasValue)
        {
            Log.Error($"Centcomm for station {ToPrettyString(station)} has no map entity.");
            return;
        }

        var targetCoords = new EntityCoordinates(centcomm.MapEntity.Value, _random.NextVector2(1000f));
        _shuttle.FTLToCoordinates(shuttle.Value, shuttleComp, targetCoords, Angle.Zero, hyperspaceTime: stationState.TransitTime);
        stationState.Status = EmergencyShuttleStatus.Departing;

        RaiseLocalEvent(new EmergencyShuttleDepartedEvent(shuttle.Value, station));

        _chatSystem.DispatchStationAnnouncement(station, Loc.GetString("emergency-shuttle-left", ("transitTime", stationState.TransitTime)), playDefaultSound: false);
        _audio.PlayGlobal(stationShuttle.DepartingAudio, Filter.Broadcast(), true);
    }

    private void OnCentcommShutdown(EntityUid uid, StationCentcommComponent component, ComponentShutdown args)
    {
        ClearCentcomm(component);
    }

    private void ClearCentcomm(StationCentcommComponent component)
    {
        QueueDel(component.Entity);
        QueueDel(component.MapEntity);
        component.Entity = null;
        component.MapEntity = null;
    }

    public EntityUid? GetShuttle(EntityUid? station)
    {
        if (station == null || !TryComp<StationEmergencyShuttleComponent>(station, out var comp))
            return null;
        return comp.EmergencyShuttle;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<StationEmergencyStateComponent, StationEmergencyShuttleComponent>();
        var curTime = _timing.CurTime;

        while (query.MoveNext(out var uid, out var comp, out var shuttleComp))
        {
            switch (comp.Status)
            {
                case EmergencyShuttleStatus.Uncalled:
                    continue;

                case EmergencyShuttleStatus.Called:
                    if (!comp.ShuttleArrivalTime.HasValue)
                        break;
                    var remaining = comp.ShuttleArrivalTime.Value - curTime;
                    if (remaining.TotalSeconds <= 0)
                    {
                        LaunchShuttle(uid);
                    }
                    else if (!comp.EarlyLaunchAuthorized)
                    {
                        comp.EarlyLaunchAuthorized = true;
                        if (shuttleComp.EmergencyShuttle != null && TryComp<ShuttleConsoleComponent>(shuttleComp.EmergencyShuttle, out var console))
                        {
                            console.AllowedToLaunch = true;
                        }
                    }
                    break;
            }
        }
    }

    private void OnEmergencyFTL(EntityUid uid, EmergencyShuttleComponent component, ref FTLStartedEvent args)
    {
        if (TryComp<ShuttleStationComponent>(uid, out var shuttleStation) &&
            shuttleStation.Station.HasValue &&
            TryComp<StationEmergencyStateComponent>(shuttleStation.Station.Value, out var stationState) &&
            stationState.Status == EmergencyShuttleStatus.Uncalled)
        {
            // The shuttle for this station is being recalled, don't show a timer for its return trip to CentComm.
            return;
        }

        var ftlTime = TimeSpan.FromSeconds
        (
            TryComp<FTLComponent>(uid, out var ftlComp) ? ftlComp.TravelTime : _shuttle.DefaultTravelTime
        );

        if (TryComp<DeviceNetworkComponent>(uid, out var netComp))
        {
            var payload = new NetworkPayload
            {
                [ShuttleTimerMasks.ShuttleMap] = uid,
                [ShuttleTimerMasks.SourceMap] = args.FromMapUid,
                [ShuttleTimerMasks.DestMap] = _transformSystem.GetMap(args.TargetCoordinates),
                [ShuttleTimerMasks.ShuttleTime] = ftlTime,
                [ShuttleTimerMasks.SourceTime] = ftlTime,
                [ShuttleTimerMasks.DestTime] = ftlTime
            };
            _deviceNetworkSystem.QueuePacket(uid, null, payload, netComp.TransmitFrequency);
        }
    }

    private void OnEmergencyFTLComplete(EntityUid uid, EmergencyShuttleComponent component, ref FTLCompletedEvent args)
    {
        var shuttle = args.Entity;

        if (!TryComp<ShuttleStationComponent>(shuttle, out var shuttleStation) ||
            !shuttleStation.Station.HasValue ||
            !TryComp<StationCentcommComponent>(shuttleStation.Station, out var centcomm) ||
            !TryComp<ShuttleComponent>(shuttle, out var shuttleComp))
        {
            return;
        }

        var station = shuttleStation.Station.Value;
        if (!TryComp<StationEmergencyStateComponent>(station, out var stationState))
            return;

        if (centcomm.Entity.HasValue)
            _shuttle.TryFTLDock(shuttle, shuttleComp, centcomm.Entity.Value, out _, DockTag);

        _logger.Add(LogType.EmergencyShuttle, LogImpact.High, $"Emergency shuttle {ToPrettyString(shuttle)} arrived at CentCom for station {ToPrettyString(station)}");

        stationState.Status = EmergencyShuttleStatus.Arrived;
        RaiseLocalEvent(new EmergencyShuttleArrivedAtCentComEvent(shuttle, station));

        if (TryComp<DeviceNetworkComponent>(shuttle, out var net))
        {
            var countdownTime = TimeSpan.FromSeconds(_configManager.GetCVar(CCVars.RoundRestartTime));
            var payload = new NetworkPayload();

            if (centcomm.MapEntity.HasValue)
            {
                payload[ShuttleTimerMasks.SourceMap] = centcomm.MapEntity.Value;
                payload[ShuttleTimerMasks.DestMap] = station;
            }

            payload[ShuttleTimerMasks.ShuttleMap] = shuttle;
            payload[ShuttleTimerMasks.ShuttleTime] = countdownTime;
            payload[ShuttleTimerMasks.SourceTime] = countdownTime;
            payload[ShuttleTimerMasks.DestTime] = countdownTime;
            payload.Add(ScreenMasks.Text, ShuttleTimerMasks.Bye);

            _deviceNetworkSystem.QueuePacket(shuttle, null, payload, net.TransmitFrequency);
        }
    }

    public ShuttleDockResult? DockSingleEmergencyShuttle(EntityUid stationUid, StationEmergencyShuttleComponent? stationShuttle = null)
    {
        if (!Resolve(stationUid, ref stationShuttle))
            return null;

        if (stationShuttle.EmergencyShuttle == null)
            return null;

        if (!TryComp<StationEmergencyStateComponent>(stationUid, out var stationState) || stationState.Status != EmergencyShuttleStatus.Uncalled)
            return null;

        if (!TryComp(stationShuttle.EmergencyShuttle, out TransformComponent? xform) ||
            !TryComp<ShuttleComponent>(stationShuttle.EmergencyShuttle, out var shuttle))
        {
            Log.Error($"Attempted to call an emergency shuttle for an uninitialized station? Station: {ToPrettyString(stationUid)}. Shuttle: {ToPrettyString(stationShuttle.EmergencyShuttle)}");
            return null;
        }

        var targetGrid = _station.GetLargestGrid(stationUid);

        if (targetGrid == null)
        {
            _logger.Add(
                LogType.EmergencyShuttle,
                LogImpact.High,
                $"Emergency shuttle {ToPrettyString(stationUid)} unable to dock with station {ToPrettyString(stationUid)}");

            return new ShuttleDockResult
            {
                Station = (stationUid, stationShuttle),
                ResultType = ShuttleDockResultType.GoodLuck,
            };
        }

        ShuttleDockResultType resultType;
        if (_shuttle.TryFTLDock(stationShuttle.EmergencyShuttle.Value, shuttle, targetGrid.Value, out var config, DockTag))
        {
            _logger.Add(
                LogType.EmergencyShuttle,
                LogImpact.High,
                $"Emergency shuttle {ToPrettyString(stationUid)} docked with stations");

            resultType = _dock.IsConfigPriority(config, DockTag)
                ? ShuttleDockResultType.PriorityDock
                : ShuttleDockResultType.OtherDock;
        }
        else
        {
            _logger.Add(
                LogType.EmergencyShuttle,
                LogImpact.High,
                $"Emergency shuttle {ToPrettyString(stationUid)} unable to find a valid docking port for {ToPrettyString(stationUid)}");

            resultType = ShuttleDockResultType.NoDock;
        }

        return new ShuttleDockResult
        {
            Station = (stationUid, stationShuttle),
            DockingConfig = config,
            ResultType = resultType,
            TargetGrid = targetGrid,
        };
    }

    public void AnnounceShuttleDock(ShuttleDockResult result, bool extended, TimeSpan shuttleTime)
    {
        if (!result.Station.Owner.IsValid() || !TryComp<StationEmergencyStateComponent>(result.Station.Owner, out var stationState))
            return;

        var stationShuttleComp = result.Station.Comp;
        var shuttle = result.Station.Comp.EmergencyShuttle;
        stationState.IsDockedAtStation = true;

        DebugTools.Assert(shuttle != null);

        if (result.ResultType == ShuttleDockResultType.GoodLuck)
        {
            _chatSystem.DispatchStationAnnouncement(
                (EntityUid) result.Station,
                Loc.GetString(stationShuttleComp.FailureAnnouncement),
                playDefaultSound: false);

            _audio.PlayGlobal(stationShuttleComp.FailureAudio, Filter.Broadcast(), true);
            return;
        }

        DebugTools.Assert(result.TargetGrid != null);

        var targetXform = Transform(result.TargetGrid.Value);
        var angle = _dock.GetAngle(
            shuttle.Value,
            Transform(shuttle.Value),
            result.TargetGrid.Value,
            targetXform);

        var direction = ContentLocalizationManager.FormatDirection(angle.GetDir());
        var location = FormattedMessage.RemoveMarkupPermissive(
            _navMap.GetNearestBeaconString((shuttle.Value, Transform(shuttle.Value))));

        var extendedText = extended ? Loc.GetString(stationShuttleComp.LaunchExtendedMessage) : "";
        var locKey = result.ResultType == ShuttleDockResultType.NoDock
            ? stationShuttleComp.NearbyAnnouncement
            : stationShuttleComp.DockedAnnouncement;

        _chatSystem.DispatchStationAnnouncement(
            (EntityUid) result.Station,
            Loc.GetString(
                locKey,
                ("time", $"{shuttleTime.TotalSeconds:0}"),
                ("direction", direction),
                ("location", location),
                ("extended", extendedText)),
            playDefaultSound: false);

        if (TryComp<DeviceNetworkComponent>(shuttle, out var netComp) &&
            TryComp<StationCentcommComponent>(result.Station, out var centcomm) &&
            centcomm.MapEntity.HasValue)
        {
            var payload = new NetworkPayload
            {
                [ShuttleTimerMasks.ShuttleMap] = shuttle,
                [ShuttleTimerMasks.SourceMap] = targetXform.MapUid,
                [ShuttleTimerMasks.DestMap] = centcomm.MapEntity.Value,
                [ShuttleTimerMasks.ShuttleTime] = shuttleTime,
                [ShuttleTimerMasks.SourceTime] = shuttleTime,
                [ShuttleTimerMasks.DestTime] = shuttleTime + TimeSpan.FromSeconds(stationState.TransitTime),
                [ShuttleTimerMasks.Docked] = true,
            };
            _deviceNetworkSystem.QueuePacket(shuttle.Value, null, payload, netComp.TransmitFrequency);
        }

        var audioFile = result.ResultType == ShuttleDockResultType.NoDock
            ? stationShuttleComp.NearbyAudio
            : stationShuttleComp.DockedAudio;

        _audio.PlayGlobal(audioFile, Filter.Broadcast(), true);
    }

    private void OnStationInit(EntityUid uid, StationCentcommComponent component, ref StationPostInitEvent args)
    {
        if (TryComp(component.Entity, out TransformComponent? xform))
        {
            component.MapEntity = xform.MapUid;
            return;
        }

        AddCentcomm(uid, component);
    }

    private void OnStationStartup(Entity<StationEmergencyShuttleComponent> ent, ref StationPostInitEvent args)
    {
        _stationsWithEmergencyShuttle.Add(ent.Owner);
        AddEmergencyShuttle((ent, ent));
    }

    private void SetupEmergencyShuttle()
    {
        var centcommQuery = AllEntityQuery<StationCentcommComponent>();

        while (centcommQuery.MoveNext(out var uid, out var centcomm))
        {
            AddCentcomm(uid, centcomm);
        }

        var query = AllEntityQuery<StationEmergencyShuttleComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            AddEmergencyShuttle((uid, comp));
        }
    }

    private void AddCentcomm(EntityUid station, StationCentcommComponent component)
    {
        DebugTools.Assert(LifeStage(station) >= EntityLifeStage.MapInitialized);
        if (component.MapEntity != null || component.Entity != null)
        {
            Log.Warning("Attempted to re-add an existing centcomm map.");
            return;
        }

        var query = AllEntityQuery<StationCentcommComponent>();
        while (query.MoveNext(out var otherComp))
        {
            if (otherComp == component)
                continue;

            if (!Exists(otherComp.MapEntity) || !Exists(otherComp.Entity))
            {
                Log.Error($"Discovered invalid centcomm component?");
                ClearCentcomm(otherComp);
                continue;
            }

            component.MapEntity = otherComp.MapEntity;
            component.Entity = otherComp.Entity;
            return;
        }

        if (string.IsNullOrEmpty(component.Map.ToString()))
        {
            Log.Warning("No CentComm map found, skipping setup.");
            return;
        }

        var map = _mapSystem.CreateMap(out var mapId);
        if (!_loader.TryLoadGrid(mapId, component.Map, out var grid))
        {
            Log.Error($"Failed to set up centcomm grid!");
            return;
        }

        if (!Exists(map))
        {
            Log.Error($"Failed to set up centcomm map!");
            QueueDel(grid);
            return;
        }

        if (!Exists(grid))
        {
            Log.Error($"Failed to set up centcomm grid!");
            QueueDel(map);
            return;
        }

        var xform = Transform(grid.Value);
        if (xform.ParentUid != map || xform.MapUid != map)
        {
            Log.Error($"Centcomm grid is not parented to its own map?");
            QueueDel(map);
            QueueDel(grid);
            return;
        }

        component.MapEntity = map;
        _metaData.SetEntityName(map, Loc.GetString("map-name-centcomm"));
        component.Entity = grid;
        _shuttle.TryAddFTLDestination(mapId, true, out _);
        Log.Info($"Created centcomm grid {ToPrettyString(grid)} on map {ToPrettyString(map)} for station {ToPrettyString(station)}");
    }

    public HashSet<EntityUid> GetCentcommMaps()
    {
        var query = AllEntityQuery<StationCentcommComponent>();
        var maps = new HashSet<EntityUid>(Count<StationCentcommComponent>());

        while (query.MoveNext(out var comp))
        {
            if (comp.MapEntity != null)
                maps.Add(comp.MapEntity.Value);
        }

        return maps;
    }

    private void AddEmergencyShuttle(Entity<StationEmergencyShuttleComponent?, StationCentcommComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp1, ref ent.Comp2))
            return;

        var stationState = EnsureComp<StationEmergencyStateComponent>(ent.Owner);
        var minTime = _configManager.GetCVar(CCVars.EmergencyShuttleMinTransitTime);
        var maxTime = _configManager.GetCVar(CCVars.EmergencyShuttleMaxTransitTime);
        stationState.TransitTime = _random.NextFloat(minTime, maxTime);

        if (ent.Comp1.EmergencyShuttle != null)
        {
            if (Exists(ent.Comp1.EmergencyShuttle))
            {
                Log.Error($"Attempted to add an emergency shuttle to {ToPrettyString(ent)}, despite a shuttle already existing?");
                return;
            }

            Log.Error($"Encountered deleted emergency shuttle during initialization of {ToPrettyString(ent)}");
            ent.Comp1.EmergencyShuttle = null;
        }

        if (!Exists(ent.Comp2.MapEntity))
        {
            AddCentcomm(ent.Owner, ent.Comp2);
        }

        if (!TryComp(ent.Comp2.MapEntity, out MapComponent? map))
        {
            Log.Error($"Failed to add emergency shuttle - centcomm has not been initialized? {ToPrettyString(ent)}");
            return;
        }

        var shuttlePath = ent.Comp1.EmergencyShuttlePath;

        // Find the current maximum shuttle index to avoid collisions
        var shuttleIndex = 0f;
        var centcommQuery = AllEntityQuery<StationCentcommComponent>();
        while (centcommQuery.MoveNext(out var comp))
        {
            if (comp.MapEntity != ent.Comp2.MapEntity)
                continue;
            shuttleIndex = Math.Max(shuttleIndex, comp.ShuttleIndex);
        }

        if (!_loader.TryLoadGrid(map.MapId,
            shuttlePath,
            out var shuttle,
            offset: new Vector2(500f + shuttleIndex, 0f)))
        {
            Log.Error($"Unable to spawn emergency shuttle {shuttlePath} for {ToPrettyString(ent)}");
            return;
        }

        // Update our own index for the next shuttle
        ent.Comp2.ShuttleIndex = shuttleIndex + Comp<MapGridComponent>(shuttle.Value).LocalAABB.Width + ShuttleSpawnBuffer;

        ent.Comp1.EmergencyShuttle = shuttle;
        EnsureComp<ProtectedGridComponent>(shuttle.Value);
        EnsureComp<PreventPilotComponent>(shuttle.Value);
        var shuttleStation = EnsureComp<ShuttleStationComponent>(shuttle.Value);
        shuttleStation.Station = ent.Owner;
        EnsureComp<EmergencyShuttleComponent>(shuttle.Value);

        Log.Info($"Added emergency shuttle {ToPrettyString(shuttle)} for station {ToPrettyString(ent)} and centcomm {ToPrettyString(ent.Comp2.Entity)}");
    }

    public bool IsTargetEscaping(EntityUid target)
    {
        var xform = Transform(target);
        if (!TryComp<StationMemberComponent>(xform.GridUid, out var member) ||
            !TryComp<StationEmergencyStateComponent>(member.Station, out var stationState))
        {
            return false;
        }

        if (stationState.Status < EmergencyShuttleStatus.Departing)
            return false;

        if (HasComp<EmergencyShuttleComponent>(xform.GridUid))
            return true;

        return false;
    }

    private bool IsOnGrid(TransformComponent xform, EntityUid shuttle, MapGridComponent? grid = null, TransformComponent? shuttleXform = null)
    {
        if (!Resolve(shuttle, ref grid, ref shuttleXform))
            return false;

        return _transformSystem.GetWorldMatrix(shuttleXform).TransformBox(grid.LocalAABB).Contains(_transformSystem.GetWorldPosition(xform));
    }

    public sealed class ShuttleDockResult
    {
        public Entity<StationEmergencyShuttleComponent> Station;
        public EntityUid? TargetGrid;
        public ShuttleDockResultType ResultType;
        public DockingConfig? DockingConfig;
    }

    public enum ShuttleDockResultType : byte
    {
        PriorityDock,
        OtherDock,
        NoDock,
        GoodLuck,
    }
}
