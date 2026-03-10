using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Shared.Administration;
using Content.Shared.Localizations;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Round)]
    public sealed class CallShuttleCommand : LocalizedEntityCommands
    {
        [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;

        public override string Command => "callshuttle";

        public override string Help => Loc.GetString("cmd-callshuttle-help");

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length == 0 || args.Length > 2)
            {
                shell.WriteLine(Help);
                return;
            }

            if (!TimeSpan.TryParseExact(args[0], ContentLocalizationManager.TimeSpanMinutesFormats,
                    LocalizationManager.DefaultCulture, out var time))
            {
                shell.WriteLine(Loc.GetString("shell-timespan-minutes-must-be-correct"));
                return;
            }

            EntityUid? station = null;
            if (args.Length > 1 && NetEntity.TryParse(args[1], out var stationNet) &&
                EntityManager.TryGetEntity(stationNet, out var stationUid))
            {
                station = stationUid;
            }

            _roundEndSystem.RequestRoundEnd(time, shell.Player?.AttachedEntity, false, station: station);
        }
    }

    [AdminCommand(AdminFlags.Round)]
    public sealed class RecallShuttleCommand : LocalizedEntityCommands
    {
        [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;

        public override string Command => "recallshuttle";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            _roundEndSystem.CancelRoundEndCountdown(shell.Player?.AttachedEntity, forceRecall: true);
        }
    }
}
