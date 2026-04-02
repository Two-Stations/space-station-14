namespace Content.Server.Shuttles.Events;

/// <summary>
/// Raised when the emergency shuttle arrives at CentCom.
/// </summary>
public sealed class EmergencyShuttleArrivedAtCentComEvent : EntityEventArgs
{
    public EntityUid Shuttle;
    public EntityUid Station;

    public EmergencyShuttleArrivedAtCentComEvent(EntityUid shuttle, EntityUid station)
    {
        Shuttle = shuttle;
        Station = station;
    }
}
