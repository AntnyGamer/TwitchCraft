using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TwitchCraftBot_V1;

internal static partial class ErrorHandling
{
    public static void ShowREADMENotFound(object? source, string READMEPath)
    {
        ShowError(source, "File Not Found", $"README.txt was not found here:\n\n{READMEPath}");
    }

    public static void ShowOpenREADMEFailed(object? source, Exception ex)
    {
        ShowError(source, "Error", "Failed to open README.txt\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowMainWindowNotFound(object? source)
    {
        ShowWarning(source, DefaultTitle, "Main window not found.");
    }

    public static bool ConfirmDeleteConfig(object? source)
    {
        return ShowQuestion(
            source,
            "Confirm Delete",
            "Are you sure you want to delete the configuration file?\n\nYou will have to setup the bot again!",
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ShowDeleteConfigFailed(object? source, Exception ex)
    {
        ShowError(source, "Error", "Failed to delete config.json\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowOpenLinkFailed(object? source, Exception ex)
    {
        ShowError(source, "Error", "Failed to open link.\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowSettingsLoadFailed(object? source, Exception ex)
    {
        ShowWarning(source, "Settings", "Failed to load settings.\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowGetBotTokenNotFound(object? source, string expectedPath)
    {
        ShowWarning(
            source,
            "File Not Found",
            $"Could not find GetBotToken.exe in the same folder as TwitchCraft.exe. Expected path:\n\n{expectedPath}");
    }

    public static void ShowOpenGetBotTokenFailed(object? source, Exception ex)
    {
        ShowError(source, "Error", "Failed to open GetBotToken.exe\n\n" + FormatExceptionMessage(ex));
    }

    public static bool ConfirmResetDefaults(object? source)
    {
        return ShowQuestion(source, "Reset Defaults", "Reset all settings back to their default values?") == MessageBoxResult.Yes;
    }

    public static bool ConfirmResetStatistics(object? source)
    {
        return ShowQuestion(
            source,
            "Reset Statistics",
            "Reset all session and total statistics? This cannot be undone.",
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ShowResetDefaultsFailed(object? source, Exception ex)
    {
        ShowError(source, "Reset Defaults", "Failed to reset settings.\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowResetStatisticsFailed(object? source, Exception ex)
    {
        ShowError(source, "Reset Statistics", "Failed to reset statistics.\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowSaveSettingsFailed(object? source, Exception ex)
    {
        ShowError(source, "Settings", "Failed to save settings.\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowRamValuesWillNotSave(object? source)
    {
        ShowWarning(
            source,
            "Settings",
            "Min RAM cannot be higher than Max RAM. The current RAM values will not save unless you fix them.");
    }

    public static void ShowBotTokenHelp(object? source)
    {
        ShowInfo(
            source,
            "Bot Token Help",
            "You can get your bot token through GetBotToken.exe (in the same folder as this .exe).");
    }

    public static void ShowLocalManifestNotFound(object? source, string path)
    {
        string message =
            $"Local Minecraft version manifest was not found. TwitchCraft will use Mojang's online manifest when needed, which can make loading the full version list slower.{Environment.NewLine}{Environment.NewLine}" +
            $"For faster startup, put version_manifest_v2.json here:{Environment.NewLine}{path}";

        ShowWarning(source, "Local Manifest Not Found", message);
    }

    public static void ShowManifestLoadFailed(object? source, Exception ex)
    {
        ShowWarning(
            source,
            "Manifest Error",
            "Failed to load Minecraft version metadata." + Environment.NewLine + Environment.NewLine + FormatExceptionMessage(ex));
    }

    public static void ShowSetupRequiredFields(object? source)
    {
        ShowWarning(
            source,
            "Setup",
            "Minecraft version, server address, bot token, Twitch username, and client ID are all required.");
    }

    public static void ShowInvalidBindIP(object? source, string bindIP)
    {
        ShowWarning(source, "Setup", $"'{bindIP}' is not a valid Bind IP address.");
    }

    public static bool ShowAdvancedBindIPWarning(object? source)
    {
        const string message = @"Changing TwitchCraft's Bind IP is only recommended for advanced users. If you are seeing this message, there is a chance you may have accidentally edited the Bind IP, entered an invalid IPv4 address, or entered a VPN / non-IPv4 Bind IP. In this case, press Yes to reset the Bind IP to its default.

If you have purposely entered a VPN or non-IPv4 Bind IP, the instructions for multiplayer in the README will likely not work for you, and you should instead follow the instructions from this link below:
https://rentry.co/bind-ip-support/

Do you want to reset the Bind IP to its default?";

        return Show(source, message, "Advanced Bind IP", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static bool ConfirmVerifyServerJar(object? source, string MCVersion)
    {
        return ShowQuestion(source, "Verification", $"Verify the selected server jar for Minecraft {MCVersion}?") == MessageBoxResult.Yes;
    }

    public static void ShowVerificationMatched(object? source)
    {
        ShowInfo(source, "Verification", "Verification matched!");
    }

    public static void ShowSetupError(object? source, Exception ex)
    {
        ShowError(source, "Setup Error", "Initial setup failed:\n" + FormatExceptionMessage(ex));
    }

    public static async Task RunSetupActionAsync(object? source, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowSetupError(source, ex);
        }
    }

    public static bool ConfirmRetryMissingJava(object? source, int javaVersion)
    {
        string help = "Java " + javaVersion + " was not found or did not match the required version.\n\n"
                    + "Fix options:\n"
                    + "• Install Java " + javaVersion + "\n"
                    + "• Make sure Java " + javaVersion + " is on PATH\n"
                    + "• Or set JAVA_HOME to your Java " + javaVersion + " folder\n\n"
                    + "TwitchCraft checks JAVA_HOME, PATH, and common install folders like C:\\Program Files\\Java.\n\n"
                    + "Click Yes to retry, or No to cancel setup.";

        return ShowQuestion(source, "Java Not Found", help, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ShowConfigError(object? source, Exception ex)
    {
        ShowWarning(source, "Config Error", FormatExceptionMessage(ex));
    }

    public static void ShowPauseParentNotFound(object? source)
    {
        ShowWarning(source, DefaultTitle, "Pause failed: parent TwitchCraftBot window not found.");
    }

    public static void ShowResetParentNotFound(object? source)
    {
        ShowWarning(source, DefaultTitle, "Reset failed: parent TwitchCraftBot window not found.");
    }

    public static void ShowRestartParentNotFound(object? source)
    {
        ShowWarning(source, DefaultTitle, "Restart failed: parent TwitchCraftBot window not found.");
    }

    public static void ShowCommandParentNotFound(object? source)
    {
        ShowWarning(source, DefaultTitle, "Could not find a parent TwitchCraftBot window, so the command could not be sent.");
    }

    public static void ShowStartWindowNotFound(object? source)
    {
        ShowWarning(source, "Start Error", "Unable to find the main TwitchCraftBot window.");
    }

    public static void ShowImportWorldWindowNotFound(object? source)
    {
        ShowWarning(source, "Import World", "Unable to find the main TwitchCraftBot window.");
    }

    public static void ShowNavigationWindowNotFound(object? source)
    {
        ShowWarning(source, "Navigation Error", "Unable to find the main TwitchCraftBot window.");
    }

    public static void ShowLaunchSettingsWindowNotFound(object? source)
    {
        ShowWarning(source, "Settings", "The main TwitchCraftBot window could not be found.");
    }

    public static void ShowSetupRequiredBeforeImportWorld(object? source)
    {
        ShowWarning(source, "Import World", "Complete setup before importing a world.");
    }

    public static void ShowInvalidWorldFolder(object? source, string? selectedWorldPath)
    {
        string displayPath = string.IsNullOrWhiteSpace(selectedWorldPath) ? "(empty)" : selectedWorldPath;

        ShowWarning(
            source,
            "Import World",
            "That folder does not contain a valid Minecraft world.\n\n" +
            "Did you forget to click on the world folder?\n\n" +
            "Selected path:\n" +
            displayPath);
    }

    public static bool ConfirmOverwriteExistingWorld(object? source)
    {
        return ShowQuestion(
            source,
            "Overwrite Existing World?",
            "A world already exists in the MCServer folder. Do you want to overwrite it?",
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ShowWorldAlreadyCurrent(object? source)
    {
        ShowInfo(source, "Import World", "That world is already the current MCServer world.");
    }

    public static void ShowWorldImportSucceeded(object? source)
    {
        ShowInfo(
            source,
            "Import World",
            "World imported successfully. This imported world will be used when you press Start with your current launcher settings.");
    }

    public static void ShowWorldImportFailed(object? source, Exception ex)
    {
        ShowError(source, "Import World", "Failed to import world.\n\n" + FormatExceptionMessage(ex));
    }

    public static void ShowMissingMinecraftUsername(object? source)
    {
        ShowWarning(source, "Settings", "Please enter your Minecraft username (3-16 chars, letters, numbers, or _).");
    }

    public static void ShowInvalidMinecraftUsername(object? source)
    {
        ShowWarning(source, "Settings", "That is not a valid Minecraft username. Use 3-16 letters, numbers, or _.");
    }

    public static void ShowLaunchSettingsUpdateFailed(object? source, Exception ex)
    {
        ShowError(source, "Settings", "Failed to update launch settings:\n" + FormatExceptionMessage(ex));
    }

    public static void ShowStartupError(object? source, string message)
    {
        ShowError(source, "Startup Error", message);
    }

    public static bool ConfirmCloseRunningJavaAndRetry(object? source)
    {
        return ShowQuestion(
            source,
            "Java Already Running",
            "A Java Minecraft server process still appears to be using the MCServer folder.\r\n\r\nClose it and try starting the bot again now?",
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ShowRestartError(object? source, string message)
    {
        ShowError(source, "Restart Error", message);
    }

    public static void ShowStatisticsLoadWarning()
    {
        ShowWarning(null, "Statistics", "Statistics could not be loaded, so empty totals are being displayed. The statistics database was not reset.");
    }

}
