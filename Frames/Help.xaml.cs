using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace TwitchCraftBot_V1.Frames;

public partial class Help : UserControl
{
    public Help()
    {
        InitializeComponent();
    }

    private void OpenREADME_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string READMEPath = Path.Combine(AppHelpers.GetExecutableDirectory(), "README.txt");
            if (!File.Exists(READMEPath))
            {
                ErrorHandling.ShowREADMENotFound(this, READMEPath);
                return;
            }

            AppHelpers.OpenShellTarget(READMEPath);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowOpenREADMEFailed(this, ex);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        AppHelpers.NavigateBack(this);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (AppHelpers.GetParentBot(this) is TwitchCraftBot parent)
        {
            parent.NavigateToSettings();
        }
    }

    private void BotCommands_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            AppHelpers.OpenShellTarget(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowOpenLinkFailed(this, ex);
        }
    }
}
