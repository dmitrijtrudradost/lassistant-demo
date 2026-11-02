using System.Windows;
using Lassistant.Core.Services;

namespace Lassistant.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        UserDataBootstrap.Ensure();
        base.OnStartup(e);
    }
}
