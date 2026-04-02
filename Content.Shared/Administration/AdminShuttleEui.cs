using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration;

[Serializable, NetSerializable]
public sealed class AdminShuttleEuiState : EuiStateBase
{
    public readonly List<StationInfo> Stations;

    public AdminShuttleEuiState(List<StationInfo> stations)
    {
        Stations = stations;
    }
}

[Serializable, NetSerializable]
public readonly record struct StationInfo(NetEntity StationId, string Name);

[Serializable, NetSerializable]
public sealed class AdminShuttleEuiRefreshRequest : EuiMessageBase
{

}
