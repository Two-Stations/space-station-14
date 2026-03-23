using Content.Server.Power.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Station.Components;
using Content.Shared.Beacon.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Numerics;
using System.Linq;
using Robust.Shared.Log;

namespace Content.Server.Beacon;

public sealed class BeaconSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    // Min/max distance from the station's center grid.
    private const float MinDistance = 400f;
    private const float MaxDistance = 650f;

    /// <summary>
    /// Checks if there is a powered beacon within the valid distance range of the station that owns the source entity.
    /// </summary>
    /// <param name="sourceEntity">The entity that is trying to communicate (e.g., a headset or a fax machine).</param>
    /// <returns>True if a valid beacon is found, false otherwise.</returns>
    public bool IsBeaconActive(EntityUid sourceEntity)
    {
        var sourceStation = _stationSystem.GetOwningStation(sourceEntity);
        if (sourceStation == null)
            return false; // Not part of a station, can't use beacons.

        if (!TryComp<StationDataComponent>(sourceStation.Value, out var stationData))
            return false;

        var stationGrids = stationData.Grids;
        if (stationGrids.Count == 0)
            return false;

        // We'll consider the first grid as the "center" of the station for distance checks.
        // This is a simplification, but good enough for this purpose.
        var stationCenterGrid = stationGrids.First();
        var stationTransform = Transform(stationCenterGrid);
        var stationCoords = _transform.GetWorldPosition(stationTransform);

        Logger.Debug($"--- Beacon Check for {ToPrettyString(sourceEntity)} on station {ToPrettyString(sourceStation.Value)} ---");
        Logger.Debug($"Station center grid: {ToPrettyString(stationCenterGrid)}, Coords: {stationCoords}");

        var query = EntityQueryEnumerator<BeaconComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var power, out var transform))
        {
            var beaconCoords = _transform.GetWorldPosition(transform);
            var distance = (beaconCoords - stationCoords).Length();

            Logger.Debug($"Checking beacon {ToPrettyString(uid)}:");
            Logger.Debug($"  - Powered: {power.Powered}");
            Logger.Debug($"  - MapID Match: {transform.MapID == stationTransform.MapID}");
            Logger.Debug($"  - Coords: {beaconCoords}");
            Logger.Debug($"  - Distance to station center: {distance}");

            if (!power.Powered)
            {
                Logger.Debug($"  - REJECTED: Not powered.");
                continue;
            }

            if (transform.MapID != stationTransform.MapID)
            {
                Logger.Debug($"  - REJECTED: Map ID mismatch.");
                continue;
            }

            if (distance >= MinDistance && distance <= MaxDistance)
            {
                Logger.Debug($"  - ACCEPTED: Beacon is in range.");
                return true;
            }
            else
            {
                Logger.Debug($"  - REJECTED: Out of range. (Range: {MinDistance}-{MaxDistance})");
            }
        }

        Logger.Debug("--- No valid beacon found. ---");
        return false;
    }
}
