using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Server)]
    public sealed class ForceTwoMapsCommand : IConsoleCommand
    {
        [Dependency] private readonly IGameMapManager _gameMapManager = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;

        public string Command => "force2map";
        public string Description => "Force loads two specific maps for the next round.";
        public string Help => "force2map <map1_id> <map2_id>";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 2)
            {
                shell.WriteError("Expected two arguments.");
                return;
            }

            var map1 = args[0];
            var map2 = args[1];

            if (!_gameMapManager.CheckMapExists(map1))
            {
                shell.WriteError($"Map '{map1}' does not exist.");
                return;
            }

            if (!_gameMapManager.CheckMapExists(map2))
            {
                shell.WriteError($"Map '{map2}' does not exist.");
                return;
            }

            _configurationManager.SetCVar(CCVars.GameMap, "");
            _gameMapManager.SetSelectedMaps(map1, map2);
            _entityManager.System<GameTicker>().UpdateInfoText();

            shell.WriteLine($"The next two maps will be {map1} and {map2}.");
        }

        public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            var maps = _gameMapManager.AllMaps().Select(p => p.ID).ToList();

            if (args.Length == 1)
            {
                return CompletionResult.FromHintOptions(maps, "<map1>");
            }

            if (args.Length == 2)
            {
                maps.Remove(args[0]);
                return CompletionResult.FromHintOptions(maps, "<map2>");
            }

            return CompletionResult.Empty;
        }
    }
}
