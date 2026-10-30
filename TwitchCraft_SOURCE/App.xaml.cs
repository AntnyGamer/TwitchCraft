using System.Windows;

namespace TwitchCraft_V1;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ErrorHandling.Initialize(this);
        base.OnStartup(e);
    }
}
