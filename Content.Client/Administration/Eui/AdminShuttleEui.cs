using Content.Client.Administration.UI.Tabs.AdminTab;
using Content.Client.Eui;
using Content.Shared.Eui;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Client.Administration.Eui;

[UsedImplicitly]
public sealed class AdminShuttleEui : BaseEui
{
    private readonly AdminShuttleWindow _window;

    public AdminShuttleEui()
    {
        _window = new AdminShuttleWindow(this);
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not AdminShuttleEuiState s)
            return;

        _window.Populate(s.Stations);
    }

    public void RequestRefresh()
    {
        SendMessage(new AdminShuttleEuiRefreshRequest());
    }
}
