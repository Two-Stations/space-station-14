using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Server.Station.Components
{
    [RegisterComponent]
    public sealed partial class StationEmergencyStateComponent : Component
    {
        /// <summary>
        /// The current status of the emergency shuttle.
        /// </summary>
        [DataField("status")]
        public EmergencyShuttleStatus Status;

        /// <summary>
        /// The time at which the shuttle will arrive at the station dock.
        /// </summary>
        [DataField("shuttleArrivalTime")]
        public TimeSpan? ShuttleArrivalTime;

        /// <summary>
        /// The time at which the main shuttle call countdown will end.
        /// </summary>
        [DataField("countdownEndTime")]
        public TimeSpan? CountdownEndTime;

        /// <summary>
        /// The time at which the auto-call was first attempted.
        /// </summary>
        [DataField("autoCallStartTime")]
        public TimeSpan AutoCallStartTime;

        /// <summary>
        /// How long the shuttle will take to transit to CentCom.
        /// </summary>
        [DataField("transitTime")]
        public float TransitTime;

        /// <summary>
        /// Whether the emergency shuttle can be recalled or not.
        /// </summary>
        [DataField("cantRecall")]
        public bool CantRecall;

        /// <summary>
        /// Whether the launch of the shuttle has been announced.
        /// </summary>
        [DataField("launchAnnounced")]
        public bool LaunchAnnounced;

        /// <summary>
        /// Whether the pilot is authorized to launch early.
        /// </summary>
        [DataField("earlyLaunchAuthorized")]
        public bool EarlyLaunchAuthorized;

        /// <summary>
        /// The shuttle has arrived at the station and is waiting for departure.
        /// </summary>
        [DataField("isDockedAtStation")]
        public bool IsDockedAtStation;

        [DataField("autoCalledBefore")]
        public bool AutoCalledBefore;
    }

    public enum EmergencyShuttleStatus : byte
    {
        Uncalled,
        Called,
        Arrived,
        Departing,
        Departed,
    }
}
