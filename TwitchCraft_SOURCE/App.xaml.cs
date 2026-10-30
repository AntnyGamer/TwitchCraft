using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TwitchCraft_V1;

public partial class App : Application
{
    public static readonly LinearGradientBrush PageFadeBrush = new(
        [new(Color.FromArgb(0, 0, 0, 0), 0), new(Color.FromArgb(221, 0, 0, 0), 1)], new(0, 3), new(0, 0));
    public static readonly DropShadowEffect PageTitleShadow = new() { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 1 };

    static App() { PageFadeBrush.Freeze(); PageTitleShadow.Freeze(); }

    protected override void OnStartup(StartupEventArgs e)
    {
        ErrorHandling.Initialize(this);
        base.OnStartup(e);
    }
}
