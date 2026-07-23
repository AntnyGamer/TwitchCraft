using System;
using System.Windows;
using System.Windows.Controls;

namespace TwitchCraftBot_V1.Frames;

public partial class Setup : UserControl
{
    private sealed class SecretTextBoxController
    {
        private readonly PasswordBox _hiddenBox;
        private readonly TextBox _visibleBox;
        private readonly CheckBox _toggle;
        private bool _syncing;

        public SecretTextBoxController(PasswordBox hiddenBox, TextBox visibleBox, CheckBox toggle)
        {
            ArgumentNullException.ThrowIfNull(hiddenBox);
            ArgumentNullException.ThrowIfNull(visibleBox);
            ArgumentNullException.ThrowIfNull(toggle);

            _hiddenBox = hiddenBox;
            _visibleBox = visibleBox;
            _toggle = toggle;

            _visibleBox.IsUndoEnabled = false;
            _toggle.Checked += VisibilityToggle_Changed;
            _toggle.Unchecked += VisibilityToggle_Changed;
        }

        public string Text => IsVisible ? _visibleBox.Text ?? string.Empty : _hiddenBox.Password ?? string.Empty;

        public void Hide()
        {
            _toggle.IsChecked = false;
            SyncVisibility();
        }

        private bool IsVisible => _toggle.IsChecked == true;

        private void VisibilityToggle_Changed(object sender, RoutedEventArgs e) => SyncVisibility();

        private void SyncVisibility()
        {
            if (_syncing)
                return;

            _syncing = true;
            try
            {
                if (IsVisible)
                {
                    _visibleBox.Text = _hiddenBox.Password ?? string.Empty;
                    _hiddenBox.Visibility = Visibility.Collapsed;
                    _visibleBox.Visibility = Visibility.Visible;
                }
                else
                {
                    _hiddenBox.Password = _visibleBox.Text ?? string.Empty;
                    _visibleBox.Clear();
                    _visibleBox.Visibility = Visibility.Collapsed;
                    _hiddenBox.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
