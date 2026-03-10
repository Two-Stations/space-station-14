using Content.Server.Administration;
using Content.Server.RoundEnd;
using Content.Server.Station.Components;
using Content.Shared.Administration;
using Content.Shared.Station.Components;
using Robust.Shared.Console;
using System;
using System.Linq;
using Content.Server.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Shuttles.Commands;

[AdminCommand(AdminFlags.Host)]
public sealed class CallEmergencyShuttleCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "callevac";
    public string Description => "Calls the emergency shuttle.";
    public string Help => "callevac <time> [stationUid]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteLine("Not enough arguments.");
            return;
        }

        TimeSpan time;
        if (int.TryParse(args[0], out var seconds))
        {
            time = TimeSpan.FromSeconds(seconds);
        }
        else if (!TimeSpan.TryParse(args[0], out time))
        {
            shell.WriteLine("Invalid time format.");
            return;
        }

        var roundEndSystem = _entityManager.System<RoundEndSystem>();

        if (args.Length < 2)
        {
            // Call for all stations
            roundEndSystem.RequestRoundEnd(time, station: null);
            shell.WriteLine("Emergency shuttle called for all stations.");
            return;
        }

        if (!NetEntity.TryParse(args[1], out var stationNet) || !_entityManager.TryGetEntity(stationNet, out var stationUid))
        {
            shell.WriteLine("Invalid station UID.");
            return;
        }

        roundEndSystem.RequestRoundEnd(time, station: stationUid);
        shell.WriteLine($"Emergency shuttle called for station {stationUid}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint("<time>"),
            2 => CompletionResult.FromHintOptions(
                _entityManager.EntityQuery<StationDataComponent>().Select(s => s.Owner.ToString()),
                "[stationUid]"),
            _ => CompletionResult.Empty
        };
    }
}
