using System.Linq;
using Content.Server.EUI;
using Content.Server.Shuttles.Components;
using Content.Shared.Administration;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Administration.Eui;

[UsedImplicitly]
public sealed class AdminShuttleEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public AdminShuttleEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not AdminShuttleEuiRefreshRequest)
            return;

        StateDirty();
    }

    public override AdminShuttleEuiState GetNewState()
    {
        var stations = new List<StationInfo>();
        var query = _entityManager.EntityQueryEnumerator<StationEmergencyShuttleComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            var stationNetEntity = _entityManager.GetNetEntity(uid);
            var stationName = _entityManager.GetComponent<MetaDataComponent>(uid).EntityName;
            stations.Add(new StationInfo(stationNetEntity, stationName));
        }

        return new AdminShuttleEuiState(stations);
    }
}
