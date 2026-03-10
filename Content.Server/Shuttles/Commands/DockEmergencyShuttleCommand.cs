using Content.Server.Administration;
using Content.Server.Shuttles.Systems;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using System;

namespace Content.Server.Shuttles.Commands;

/// <summary>
/// Calls in the emergency shuttle.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class DockEmergencyShuttleCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly EmergencyShuttleSystem _shuttleSystem = default!;

    public override string Command => "dockemergencyshuttle";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var time = TimeSpan.FromSeconds(_configManager.GetCVar(CCVars.EmergencyShuttleDockTime));
        _shuttleSystem.CallEmergencyShuttle(null, time);
    }
}
