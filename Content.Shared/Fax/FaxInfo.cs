using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Fax;

[Serializable, NetSerializable]
public readonly struct FaxUiPeerInfo
{
    public readonly string Name;

    public FaxUiPeerInfo(string name)
    {
        Name = name;
    }
}

[Serializable, DataDefinition]
public partial struct FaxInfo
{
    [DataField("name")]
    public string Name;

    [DataField("isCentcom")]
    public bool IsCentcom;

    [DataField("station")]
    public EntityUid? Station;

    public FaxInfo(string name, bool isCentcom, EntityUid? station)
    {
        Name = name;
        IsCentcom = isCentcom;
        Station = station;
    }
}
