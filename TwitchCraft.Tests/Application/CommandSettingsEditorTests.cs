using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TwitchCraft_V1.Setup;
using TwitchCraft_V1.Frames;

namespace TwitchCraft.Tests.Application;

public sealed class CommandSettingsEditorTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0.0)]
    [InlineData(17, 0.123456789)]
    [InlineData(86400, 86400.0)]
    public void Rows_PreserveTypedValuesAndSaveOnlyActualEdits(int? perUser, double? global)
    {
        List<(bool Enabled, int? PerUser, double? Global)> saves = [];
        Settings.CommandSettingsRow row = new("heal", new() { CooldownSeconds = perUser, GlobalCooldownSeconds = global },
            edited => saves.Add((edited.Enabled, edited.PerUserCooldown.Value, edited.GlobalCooldown.Value)));
        Assert.Equal(perUser, row.PerUserCooldown.Value);
        Assert.Equal(global, row.GlobalCooldown.Value);
        row.Enabled = true;
        row.PerUserCooldown = row.PerUserCooldown;
        row.GlobalCooldown = null!;
        Assert.Empty(saves);
        row.Enabled = false;
        Assert.Equal((false, perUser, global), Assert.Single(saves));
        row.PerUserCooldown = row.PerUserOptions[0];
        row.GlobalCooldown = row.GlobalOptions[0];
        Assert.Null(row.PerUserCooldown.Value);
        Assert.Null(row.GlobalCooldown.Value);
    }

    [Fact]
    public void Recycling_PreservesEditsWithoutSavingOnScrollOrPrefixChanges()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Settings settings = new();
                settings.Resources["CommandSettingsPrefix"] = "!";
                ((Grid)settings.FindName("CommandsSettingsPage")).Visibility = Visibility.Collapsed;
                ((Grid)settings.FindName("CustomCommandsSettingsPage")).Visibility = Visibility.Visible;
                ListBox list = (ListBox)settings.FindName("CommandCustomizationList");
                int saves = 0;
                Settings.CommandSettingsRow[] rows = Enumerable.Range(0, 200)
                    .Select(i => new Settings.CommandSettingsRow($"command{i}", new() { GlobalCooldownSeconds = 0.123456789 }, _ => saves++)).ToArray();
                Assert.Same(rows[0].PerUserOptions, rows[1].PerUserOptions);
                list.ItemsSource = rows;
                settings.Measure(new Size(800, 450));
                settings.Arrange(new Rect(0, 0, 800, 450));
                settings.UpdateLayout();
                Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 30);
                ListBoxItem first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                byte[] unselectedRow = RenderPixels(first);
                first.IsSelected = true;
                Pump();
                settings.UpdateLayout();
                Assert.Equal(unselectedRow, RenderPixels(first));
                CheckBox enabled = Assert.Single(Descendants<CheckBox>(first));
                enabled.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
                ComboBox perUser = Descendants<ComboBox>(first).First();
                perUser.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, rows[0].PerUserOptions[1]);
                Pump();
                Assert.False(rows[0].Enabled);
                Assert.Equal(0, rows[0].PerUserCooldown.Value);
                Assert.Equal(2, saves);
                VirtualizingStackPanel panel = Assert.Single(Descendants<VirtualizingStackPanel>(list));
                panel.SetVerticalOffset(panel.ExtentHeight);
                list.UpdateLayout();
                Pump();
                settings.UpdateLayout();
                Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(rows.Length - 1));
                Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 30);
                settings.Resources["CommandSettingsPrefix"] = "?";
                panel.SetVerticalOffset(0);
                list.UpdateLayout();
                Pump();
                settings.UpdateLayout();
                first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.False(Assert.Single(Descendants<CheckBox>(first)).IsChecked);
                Assert.Equal(rows[0].PerUserOptions[1], Descendants<ComboBox>(first).First().SelectedItem);
                Assert.Contains(Descendants<TextBlock>(first), text => text.Text == "?command0");
                Assert.Equal(0.123456789, rows[0].GlobalCooldown.Value);
                Assert.Equal(2, saves);
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Command editor test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static byte[] RenderPixels(FrameworkElement element)
    {
        int width = (int)Math.Ceiling(element.ActualWidth);
        int height = (int)Math.Ceiling(element.ActualHeight);
        RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        Assert.Contains(pixels, value => value != 0);
        return pixels;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }
}
