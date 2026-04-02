using Content.Shared.Station.Components;
using System.Linq;
using Content.Server.RoundEnd;
using Content.Shared.Administration;
using Content.Shared.Localizations;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Round)]
    public sealed class RecallEvacCommand : LocalizedEntityCommands
    {
        [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;

        public override string Command => "recallevac";

        public override string Description => "Recalls the emergency shuttle for a specific station, or all of them.";

        public override string Help => "recallevac [<stationUid>]";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length > 1)
            {
                shell.WriteLine(Help);
                return;
            }

            if (args.Length == 0)
            {
                _roundEndSystem.CancelRoundEndCountdown(shell.Player?.AttachedEntity, false, null);
                shell.WriteLine("Recalled all emergency shuttles.");
                return;
            }

            if (!NetEntity.TryParse(args[0], out var stationNet) || !EntityManager.TryGetEntity(stationNet, out var stationUid))
            {
                shell.WriteLine($"Invalid station UID: {args[0]}");
                return;
            }

            _roundEndSystem.CancelRoundEndCountdown(shell.Player?.AttachedEntity, false, stationUid);
            shell.WriteLine($"Attempted to recall shuttle for station {EntityManager.ToPrettyString(stationUid.Value)}.");
        }

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            if (args.Length == 1)
            {
                var stations = _roundEndSystem.GetStationsWithActiveCountdown()
                    .Select(s => new CompletionOption(EntityManager.GetNetEntity(s).ToString(), EntityManager.ToPrettyString(s)))
                    .ToList();
                return CompletionResult.FromHintOptions(stations, "<station>");
            }

            return CompletionResult.Empty;
        }
    }
}

