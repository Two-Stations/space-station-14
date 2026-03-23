using Robust.Shared.Serialization;

namespace Content.Shared.Fax;

public static class FaxConstants
{
    public const string FaxPingCommand = "FaxPing";
    public const string FaxPongCommand = "FaxPong";
    public const string FaxPrintCommand = "FaxPrint";

    public const string FaxNameData = "FaxName";
    public const string FaxStationId = "FaxStation";
    public const string FaxCentcomData = "FaxCentcom";
    public const string FaxSyndicateData = "FaxSyndicate";

    public const string FaxPaperNameData = "FaxPaperName";
    public const string FaxPaperLabelData = "FaxPaperLabel";
    public const string FaxPaperContentData = "FaxPaperContent";
    public const string FaxPaperStampStateData = "FaxPaperStampState";
    public const string FaxPaperStampedByData = "FaxPaperStampedBy";
    public const string FaxPaperPrototypeData = "FaxPaperPrototype";
    public const string FaxPaperLockedData = "FaxPaperLocked";
}

[Serializable, NetSerializable]
public enum FaxUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class FaxUiState : BoundUserInterfaceState
{
    public string DeviceName { get; }
    public List<FaxStationGroup> StationGroups { get; }
    public string? DestinationAddress { get; }
    public bool IsPaperInserted { get; }
    public bool CanSend { get; }
    public bool CanCopy { get; }

    public FaxUiState(string deviceName,
        List<FaxStationGroup> stationGroups,
        bool canSend,
        bool canCopy,
        bool isPaperInserted,
        string? destAddress)
    {
        DeviceName = deviceName;
        StationGroups = stationGroups;
        IsPaperInserted = isPaperInserted;
        CanSend = canSend;
        CanCopy = canCopy;
        DestinationAddress = destAddress;
    }
}

[Serializable, NetSerializable]
public sealed class FaxStationGroup
{
    public string StationName { get; }
    public Dictionary<string, FaxUiPeerInfo> Peers { get; }

    public FaxStationGroup(string stationName, Dictionary<string, FaxUiPeerInfo> peers)
    {
        StationName = stationName;
        Peers = peers;
    }
}

[Serializable, NetSerializable]
public sealed class FaxFileMessage : BoundUserInterfaceMessage
{
    public string? Label;
    public string Content;
    public bool OfficePaper;

    public FaxFileMessage(string? label, string content, bool officePaper)
    {
        Label = label;
        Content = content;
        OfficePaper = officePaper;
    }
}

public static class FaxFileMessageValidation
{
    public const int MaxLabelSize = 50; // parity with Content.Server.Labels.Components.HandLabelerComponent.MaxLabelChars
    public const int MaxContentSize = 10000;
}

[Serializable, NetSerializable]
public sealed class FaxCopyMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class FaxSendMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class FaxRefreshMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class FaxDestinationMessage : BoundUserInterfaceMessage
{
    public string Address { get; }

    public FaxDestinationMessage(string address)
    {
        Address = address;
    }
}
