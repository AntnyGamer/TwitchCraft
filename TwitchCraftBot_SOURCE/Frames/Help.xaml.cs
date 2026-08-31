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

    private void OpenReadme_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string READMEPath = Path.Combine(AppHelpers.GetAppDirectory(), "README.txt");
            if (!File.Exists(READMEPath))
            {
                ErrorHandling.ShowReadmeMissing(this, READMEPath);
                return;
            }

            AppHelpers.OpenTarget(READMEPath);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowReadmeError(this, ex);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        AppHelpers.NavigateBack(this);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (AppHelpers.GetBotWindow(this) is TwitchCraftBot parent)
        {
            parent.ShowSettings();
        }
    }

    private void Commands_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            AppHelpers.OpenTarget(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowLinkError(this, ex);
        }
    }
}
