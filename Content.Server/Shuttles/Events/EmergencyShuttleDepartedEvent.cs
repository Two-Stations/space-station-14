namespace Content.Server.Shuttles.Events;

/// <summary>
/// Raised when the emergency shuttle departs from the station.
/// </summary>
public sealed class EmergencyShuttleDepartedEvent : EntityEventArgs
{
    public EntityUid Shuttle;
    public EntityUid Station;

    public EmergencyShuttleDepartedEvent(EntityUid shuttle, EntityUid station)
    {
        Shuttle = shuttle;
        Station = station;
    }
}
