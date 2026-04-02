using Content.Server.Shuttles.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Server.Shuttles.Components;

/// <summary>
/// This is used for controlling evacuation for a station.
/// </summary>
[RegisterComponent]
public sealed partial class StationEmergencyShuttleComponent : Component
{
    /// <summary>
    /// The emergency shuttle assigned to this station.
    /// </summary>
    [DataField, Access(typeof(ShuttleSystem), typeof(EmergencyShuttleSystem), Friend = AccessPermissions.ReadWrite)]
    public EntityUid? EmergencyShuttle;

    [DataField("dockedAudio")]
    public SoundSpecifier? DockedAudio = new SoundPathSpecifier("/Audio/Announcements/shuttle_dock.ogg");

    [DataField("nearbyAudio")]
    public SoundSpecifier? NearbyAudio = new SoundPathSpecifier("/Audio/Announcements/shuttle_nearby.ogg");

    [DataField("dockedAnnouncement")]
    public string DockedAnnouncement { get; private set; } = "emergency-shuttle-docked";

    [DataField("nearbyAnnouncement")]
    public string NearbyAnnouncement { get; private set; } = "emergency-shuttle-nearby";

    [DataField("failureAnnouncement")]
    public string FailureAnnouncement { get; private set; } = "emergency-shuttle-good-luck";

    [DataField("launchExtendedMessage")]
    public string LaunchExtendedMessage { get; private set; } = "emergency-shuttle-extended";

    [DataField("arrivalAudio")]
    public SoundSpecifier? ArrivalAudio;

    [DataField("launchImminentAudio")]
    public SoundSpecifier? LaunchImminentAudio;

    [DataField("launchAuthorizedAudio")]
    public SoundSpecifier? LaunchAuthorizedAudio;

    [DataField("departingAudio")]
    public SoundSpecifier? DepartingAudio;

    [DataField("called")]
    public bool Called;

    /// <summary>
    /// Emergency shuttle map path for this station.
    /// </summary>
    [DataField("emergencyShuttlePath", customTypeSerializer: typeof(ResPathSerializer))]
    public ResPath EmergencyShuttlePath { get; set; } = new("/Maps/Shuttles/emergency.yml");

    /// <summary>
    /// Sound played when the shuttle is unable to find a station.
    /// </summary>
    public SoundSpecifier FailureAudio = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");
}
