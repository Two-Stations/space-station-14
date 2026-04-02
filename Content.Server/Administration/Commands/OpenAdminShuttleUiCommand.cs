using Content.Server.Administration.Eui;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Host)]
    public sealed class OpenAdminShuttleUiCommand : LocalizedEntityCommands
    {
        [Dependency] private readonly EuiManager _euiManager = default!;

        public override string Command => "shuttleui";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var player = shell.Player;
            if (player == null)
            {
                shell.WriteLine(Loc.GetString("shell-cannot-run-command-from-server"));
                return;
            }

            var eui = new AdminShuttleEui();
            _euiManager.OpenEui(eui, player);
        }
    }
}
