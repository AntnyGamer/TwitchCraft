using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TwitchCraft_V1.Frames;

public partial class Settings
{
    private enum NumericComboBoxInputMode
    {
        UnsignedInteger,
        SignedInteger,
        Decimal
    }

    private const int MaxRAMGB = 128;

    private static bool IsAsciiDigitsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool ShouldSaveEditableValue(object sender, RoutedEventArgs e)
    {
        if (_initializing || _updatingCustomValueControls)
        {
            return false;
        }

        // Preset selections save immediately; typed values save when focus leaves the editor.
        return e is not SelectionChangedEventArgs || sender is ComboBox { SelectedItem: string };
    }

    private void RAM_PreviewTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is not TextBox textBox)
        {
            args.Handled = true;
            return;
        }

        args.Handled = !IsValidRAMText(BuildTextCandidate(textBox, args.Text));
    }

    private void RAM_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (sender is not TextBox textBox ||
            args.DataObject.GetData(typeof(string)) is not string pastedText ||
            !IsValidRAMText(BuildTextCandidate(textBox, pastedText)))
        {
            args.CancelCommand();
        }
    }

    private static string BuildTextCandidate(TextBox textBox, string insertedText)
    {
        string current = textBox.Text ?? string.Empty;
        int start = Math.Clamp(textBox.SelectionStart, 0, current.Length);
        int length = Math.Clamp(textBox.SelectionLength, 0, current.Length - start);
        ArgumentNullException.ThrowIfNull(insertedText, "value");
        if (length == 0 && insertedText.Length == 0)
        {
            return current;
        }

        ReadOnlySpan<char> textBeforeSelection = current.AsSpan(0, start);
        ReadOnlySpan<char> textAfterSelection = current.AsSpan(start + length);
        return string.Concat(textBeforeSelection, insertedText.AsSpan(), textAfterSelection);
    }

    private static bool IsValidRAMText(string candidate)
        => IsAsciiDigitsOnly(candidate) && int.TryParse(candidate, out int value) && value <= MaxRAMGB;

    private void SetupNumericBox(ComboBox dropdown)
    {
        dropdown.PreviewTextInput += NumericBox_PreviewTextInput;
        dropdown.GotKeyboardFocus += NumericBox_GotFocus;
        DataObject.AddPastingHandler(dropdown, NumericBox_Pasting);
    }

    private void NumericBox_GotFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is not ComboBox dropdown)
        {
            return;
        }

        dropdown.ApplyTemplate();
        if (dropdown.Template.FindName("PART_EditableTextBox", dropdown) is TextBox editor)
        {
            editor.SelectAll();
        }
    }

    private void NumericBox_PreviewTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is not ComboBox dropdown)
        {
            return;
        }

        string candidate = BuildNumberCandidate(dropdown, args.Text);
        args.Handled = !IsValidNumberText(candidate, GetNumberMode(dropdown));
    }

    private void NumericBox_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (sender is not ComboBox dropdown)
        {
            return;
        }

        if (args.DataObject.GetData(typeof(string)) is not string pastedText ||
            !IsValidNumberText(
                BuildNumberCandidate(dropdown, pastedText),
                GetNumberMode(dropdown)))
        {
            args.CancelCommand();
        }
    }

    private static string BuildNumberCandidate(ComboBox dropdown, string insertedText)
    {
        dropdown.ApplyTemplate();
        if (dropdown.Template.FindName("PART_EditableTextBox", dropdown) is not TextBox editor)
        {
            return (dropdown.Text ?? string.Empty) + insertedText;
        }

        return BuildTextCandidate(editor, insertedText);
    }

    private NumericComboBoxInputMode GetNumberMode(ComboBox dropdown)
    {
        if (ReferenceEquals(dropdown, GlobalCooldownSecondsDropdown) ||
            ReferenceEquals(dropdown, CommandCostMultiplierDropdown))
        {
            return NumericComboBoxInputMode.Decimal;
        }

        return ReferenceEquals(dropdown, NetworkCompressionDropdown)
            ? NumericComboBoxInputMode.SignedInteger
            : NumericComboBoxInputMode.UnsignedInteger;
    }

    private static bool IsValidNumberText(string text, NumericComboBoxInputMode mode)
    {
        if (text.Length == 0)
        {
            return true;
        }

        int index = 0;
        if (mode == NumericComboBoxInputMode.SignedInteger && text[0] == '-')
        {
            index = 1;
            if (text.Length == 1)
            {
                return true;
            }
        }

        bool decimalPointSeen = false;
        for (; index < text.Length; index++)
        {
            char character = text[index];
            if (character >= '0' && character <= '9')
            {
                continue;
            }

            if (mode == NumericComboBoxInputMode.Decimal && character == '.' && !decimalPointSeen)
            {
                decimalPointSeen = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private bool TryGetRAM(out int minRAM, out int maxRAM)
    {
        minRAM = 0;
        maxRAM = 0;

        if (!int.TryParse(MinRAMTextBox.Text.AsSpan().Trim(), out int parsedMin) ||
            !int.TryParse(MaxRAMTextBox.Text.AsSpan().Trim(), out int parsedMax))
        {
            return false;
        }

        if (parsedMin <= 0 || parsedMax <= 0 || parsedMin > parsedMax || parsedMax > MaxRAMGB)
        {
            return false;
        }

        minRAM = parsedMin;
        maxRAM = parsedMax;
        return true;
    }

    private static bool TryGetOption<T>(
        ComboBox dropdown,
        (T Value, string Label)[] options,
        out T value,
        StringComparison comparison = StringComparison.Ordinal)
    {
        if (dropdown.SelectedItem is string selected)
        {
            foreach ((T option, string label) in options)
            {
                if (string.Equals(selected, label, comparison))
                {
                    value = option;
                    return true;
                }
            }
        }

        value = default!;
        return false;
    }

    private static bool TryReadEditableInt(
        ComboBox dropdown,
        (int Value, string Label)[] options,
        int minimum,
        int maximum,
        out int value)
    {
        // SelectionChanged fires before WPF has reliably copied a newly selected item's label
        // into the editable Text property. Prefer SelectedItem so preset clicks always use the
        // value the user actually chose instead of the previous/blank editor text.
        if (TryGetOption(dropdown, options, out value, StringComparison.OrdinalIgnoreCase))
        {
            return value >= minimum && value <= maximum;
        }

        ReadOnlySpan<char> text = dropdown.Text.AsSpan().Trim();
        foreach ((int option, string label) in options)
        {
            if (text.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                value = option;
                return value >= minimum && value <= maximum;
            }
        }

        return int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum;
    }

    private static bool TryReadInt(ComboBox dropdown, int minimum, int maximum, out int value)
    {
        ReadOnlySpan<char> text = (dropdown.SelectedItem as string ?? dropdown.Text).AsSpan().Trim();
        return int.TryParse(
            text,
            NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value) && value >= minimum && value <= maximum;
    }

    private static bool TryReadDouble(
        ComboBox dropdown,
        (double Value, string Label)[] options,
        double minimum,
        double maximum,
        out double value)
    {
        if (TryGetOption(dropdown, options, out value, StringComparison.OrdinalIgnoreCase))
        {
            return value >= minimum && value <= maximum;
        }

        ReadOnlySpan<char> text = dropdown.Text.AsSpan().Trim();
        foreach ((double option, string label) in options)
        {
            if (text.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                value = option;
                return value >= minimum && value <= maximum;
            }
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value >= minimum && value <= maximum;
    }
}
