using Content.Server.Objectives.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Cuffs.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Objectives.Systems;

namespace Content.Server.Objectives.Systems;

public sealed class EscapeShuttleConditionSystem : EntitySystem
{
    [Dependency] private readonly EmergencyShuttleSystem _emergencyShuttle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmergencyShuttleDepartedEvent>(OnShuttleDepart);
    }

    private void OnShuttleDepart(EmergencyShuttleDepartedEvent ev)
    {
        var query = EntityQueryEnumerator<EscapeShuttleConditionComponent, MindComponent, ObjectiveComponent>();
        while (query.MoveNext(out var uid, out var comp, out var mind, out var obj))
        {
            if (mind.OwnedEntity is not { } owner)
                continue;

            // if they aren't on the shuttle then ignore it
            if (!_emergencyShuttle.IsTargetEscaping(owner))
                continue;

            var evnt = new ObjectiveGetProgressEvent(mind.Owner, mind, 0f);

            // You're not escaping if you're restrained!
            if (TryComp<CuffableComponent>(owner, out var cuffed) && cuffed.CuffedHandCount > 0)
                evnt.Progress = 0.5f;
            else
                evnt.Progress = 1f;

            RaiseLocalEvent(uid, ref evnt);
        }
    }
}
