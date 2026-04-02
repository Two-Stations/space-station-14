using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using System.Collections.Generic;
using Robust.Shared.Utility;
using System;

namespace Content.Shared.Communications
{
    public partial class SharedCommunicationsConsoleComponent : Component
    {
    }

    [RegisterComponent]
    public sealed partial class CentralCommandConsoleComponent : Component
    {
    }

    
    [Serializable, NetSerializable]
    public sealed class CommunicationsConsoleInterfaceState : BoundUserInterfaceState
    {
        public bool CanAnnounce { get; }
        public bool CanBroadcast { get; }
        public bool CanCall { get; }
        public List<string>? AlertLevels { get; }
        public string CurrentAlert { get; }
        public float CurrentAlertDelay { get; }
        public TimeSpan? ExpectedCountdownEnd { get; }
        public List<StationInfo> Stations { get; }

        public CommunicationsConsoleInterfaceState(
            bool canAnnounce,
            bool canBroadcast,
            bool canCall,
            List<string>? alertLevels,
            string currentAlert,
            float currentAlertDelay,
            List<StationInfo> stations,
            TimeSpan? expectedCountdownEnd = null)
        {
            CanAnnounce = canAnnounce;
            CanBroadcast = canBroadcast;
            CanCall = canCall;
            AlertLevels = alertLevels;
            CurrentAlert = currentAlert;
            CurrentAlertDelay = currentAlertDelay;
            ExpectedCountdownEnd = expectedCountdownEnd;
            Stations = stations;
        }
    }

    [Serializable, NetSerializable]
    public struct StationInfo
    {
        public NetEntity Uid { get; }
        public string Name { get; }

        public StationInfo(NetEntity uid, string name)
        {
            Uid = uid;
            Name = name;
        }
    }

    [Serializable, NetSerializable]
    public sealed class CommunicationsConsoleSelectAlertLevelMessage : BoundUserInterfaceMessage
    {
        public string Level { get; }

        public CommunicationsConsoleSelectAlertLevelMessage(string level)
        {
            Level = level;
        }
    }

    [Serializable, NetSerializable]
    public sealed class CommunicationsConsoleAnnounceMessage : BoundUserInterfaceMessage
    {
        public string Message { get; }

        public CommunicationsConsoleAnnounceMessage(string message)
        {
            Message = message;
        }
    }
    
    [Serializable, NetSerializable]
    public sealed class CommunicationsConsoleTargetedAnnounceMessage : BoundUserInterfaceMessage
    {
        public string Message { get; }
        public List<NetEntity> Targets { get; }

        public CommunicationsConsoleTargetedAnnounceMessage(string message, List<NetEntity> targets)
        {
            Message = message;
            Targets = targets;
        }
    }

    [Serializable, NetSerializable]
    public sealed class CommunicationsConsoleBroadcastMessage : BoundUserInterfaceMessage
    {
        public string Message { get; }
        public CommunicationsConsoleBroadcastMessage(string message)
        {
            Message = message;
        }
    }

    [Serializable, NetSerializable]
    public sealed class CommunicationsConsoleCallEmergencyShuttleMessage : BoundUserInterfaceMessage
    {
    }

    [Serializable, NetSerializable]
    public sealed class CommunicationsConsoleRecallEmergencyShuttleMessage : BoundUserInterfaceMessage
    {
    }

    [Serializable, NetSerializable]
    public enum CommunicationsConsoleUiKey
    {
        Key
    }
}
