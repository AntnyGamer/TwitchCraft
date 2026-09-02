using System;
using System.Threading.Tasks;
using System.Windows;

namespace TwitchCraftBot_V1;

internal static partial class ErrorHandling
{
    public static void ShowReadmeMissing(object? source, string READMEPath)
    {
        ShowError(source, "File Not Found", $"README.txt was not found here:\n\n{READMEPath}");
    }

    public static void ShowReadmeError(object? source, Exception ex)
    {
        ShowError(source, "Error", "Failed to open README.txt\n\n" + FormatException(ex));
    }

    public static void ShowMainWindowError(object? source)
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

    public static void ShowDeleteConfigError(object? source, Exception ex)
    {
        ShowError(source, "Error", "Failed to delete config.json\n\n" + FormatException(ex));
    }

    public static void ShowLinkError(object? source, Exception ex)
    {
        ShowError(source, "Error", "Failed to open link.\n\n" + FormatException(ex));
    }

    public static void ShowSettingsLoadError(object? source, Exception ex)
    {
        ShowWarning(source, "Settings", "Failed to load settings.\n\n" + FormatException(ex));
    }

    public static bool ConfirmResetDefaults(object? source)
    {
        return ShowQuestion(source, "Reset Defaults", "Reset all settings back to their default values?") == MessageBoxResult.Yes;
    }

    public static bool ConfirmResetCategory(object? source, string categoryName)
    {
        return ShowQuestion(source, "Reset Category", $"Reset {categoryName} settings back to their default values?") == MessageBoxResult.Yes;
    }

    public static bool ConfirmStatsReset(object? source)
    {
        return ShowQuestion(
            source,
            "Reset Statistics",
            "Reset all session and total statistics? This cannot be undone.",
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ShowResetDefaultsError(object? source, Exception ex)
    {
        ShowError(source, "Reset Defaults", "Failed to reset settings.\n\n" + FormatException(ex));
    }

    public static void ShowStatsResetError(object? source, Exception ex)
    {
        ShowError(source, "Reset Statistics", "Failed to reset statistics.\n\n" + FormatException(ex));
    }

    public static void ShowSaveSettingsError(object? source, Exception ex)
    {
        ShowError(source, "Settings", "Failed to save settings.\n\n" + FormatException(ex));
    }

    public static void ShowInvalidRam(object? source)
    {
        ShowWarning(
            source,
            "Settings",
            "Min RAM cannot be higher than Max RAM. The current RAM values will not save unless you fix them.");
    }

    public static void ShowAuthError(object? source, string? details)
    {
        string message = "Twitch authorization/setup did not complete.";
        if (!string.IsNullOrWhiteSpace(details))
            message += "\n\n" + details.Trim();

        ShowWarning(source, "Twitch Authorization", message);
    }

    public static void ShowAuthSuccess(object? source, string login, bool savedToConfig)
    {
        string action = savedToConfig
            ? "The renewable authorization was saved automatically. It will be used the next time the bot starts."
            : "The renewable authorization and bot name were filled in automatically. You can continue setup.";
        ShowInfo(source, "Twitch Authorization", "Authorized Twitch account: " + login + "\n\n" + action);
    }

    public static void ShowMissingManifest(object? source, string path)
    {
        string message =
            $"Local Minecraft version manifest was not found. TwitchCraft will use Mojang's online manifest when needed, which can make loading the full version list slower.{Environment.NewLine}{Environment.NewLine}" +
            $"For faster startup, put version_manifest_v2.json here:{Environment.NewLine}{path}";

        ShowWarning(source, "Local Manifest Not Found", message);
    }

    public static void ShowManifestError(object? source, Exception ex)
    {
        ShowWarning(
            source,
            "Manifest Error",
            "Failed to load Minecraft version metadata." + Environment.NewLine + Environment.NewLine + FormatException(ex));
    }

    public static void ShowSetupIncomplete(object? source)
    {
        ShowWarning(
            source,
            "Setup",
            "Complete every setup field and authorize Twitch with your bot account before starting.");
    }

    public static void ShowInvalidBindIP(object? source, string bindIP)
    {
        ShowWarning(source, "Setup", $"'{bindIP}' is not a valid Bind IP address.");
    }

    public static bool ConfirmBindIpReset(object? source)
    {
        const string message = @"Changing TwitchCraft's Bind IP is only recommended for advanced users. If you are seeing this message, there is a chance you may have accidentally edited the Bind IP, entered an invalid IPv4 address, or entered a VPN / non-IPv4 Bind IP. In this case, press Yes to reset the Bind IP to its default.

If you have purposely entered a VPN or non-IPv4 Bind IP, the instructions for multiplayer in the README will likely not work for you, and you should instead follow the instructions from this link below:
https://rentry.co/bind-ip-support/

Do you want to reset the Bind IP to its default?";

        return Show(source, message, "Advanced Bind IP", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static bool ConfirmJarVerify(object? source, string minecraftVersion)
    {
        return ShowQuestion(source, "Verification", $"Verify the selected server jar for Minecraft {minecraftVersion}?") == MessageBoxResult.Yes;
    }

    public static void ShowVerifySuccess(object? source)
    {
        ShowInfo(source, "Verification", "Verification matched!");
    }

    public static void ShowSetupError(object? source, Exception ex)
    {
        ShowError(source, "Setup Error", "Initial setup failed:\n" + FormatException(ex));
    }

    public static async Task RunSetupAsync(object? source, Func<Task> action)
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

    public static bool ConfirmJavaRetry(object? source, int javaVersion)
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
        ShowWarning(source, "Config Error", FormatException(ex));
    }

    public static void ShowPauseWindowError(object? source)
    {
        ShowWarning(source, DefaultTitle, "Pause failed: parent TwitchCraftBot window not found.");
    }

    public static void ShowResetWindowError(object? source)
    {
        ShowWarning(source, DefaultTitle, "Reset failed: parent TwitchCraftBot window not found.");
    }

    public static void ShowRestartWindowError(object? source)
    {
        ShowWarning(source, DefaultTitle, "Restart failed: parent TwitchCraftBot window not found.");
    }

    public static void ShowCommandWindowError(object? source)
    {
        ShowWarning(source, DefaultTitle, "Could not find a parent TwitchCraftBot window, so the command could not be sent.");
    }

    public static void ShowStartWindowError(object? source)
    {
        ShowWarning(source, "Start Error", "Unable to find the main TwitchCraftBot window.");
    }

    public static void ShowImportWindowError(object? source)
    {
        ShowWarning(source, "Import World", "Unable to find the main TwitchCraftBot window.");
    }

    public static void ShowNavigationError(object? source)
    {
        ShowWarning(source, "Navigation Error", "Unable to find the main TwitchCraftBot window.");
    }

    public static void ShowSettingsWindowError(object? source)
    {
        ShowWarning(source, "Settings", "The main TwitchCraftBot window could not be found.");
    }

    public static void ShowSetupRequired(object? source)
    {
        ShowWarning(source, "Import World", "Complete setup before importing a world.");
    }

    public static void ShowWorldFolderError(object? source, string? selectedWorldPath)
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

    public static bool ConfirmOverwrite(object? source)
    {
        return ShowQuestion(
            source,
            "Overwrite Existing World?",
            "A world already exists in the MCServer folder. Do you want to overwrite it?",
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ShowWorldLoaded(object? source)
    {
        ShowInfo(source, "Import World", "That world is already the current MCServer world.");
    }

    public static void ShowImportSuccess(object? source)
    {
        ShowInfo(
            source,
            "Import World",
            "World imported successfully. This imported world will be used when you press Start with your current launcher settings.");
    }

    public static void ShowImportError(object? source, Exception ex)
    {
        ShowError(source, "Import World", "Failed to import world.\n\n" + FormatException(ex));
    }

    public static void ShowDatapackWarning(string? details)
    {
        ShowWarning(null, "Datapack Warning", GetDatapackWarning(details));
    }

    private static string GetDatapackWarning(string? details)
    {
        string message = "The locateplayers support datapack could not be installed. TwitchCraft will continue, but player-location features may be unavailable.";
        return string.IsNullOrWhiteSpace(details)
            ? message
            : message + Environment.NewLine + Environment.NewLine + details.Trim();
    }

    public static void ShowMissingMinecraftName(object? source)
    {
        ShowWarning(source, "Settings", "Please enter your Minecraft username (3-16 chars, letters, numbers, or _).");
    }

    public static void ShowMinecraftNameError(object? source)
    {
        ShowWarning(source, "Settings", "That is not a valid Minecraft username. Use 3-16 letters, numbers, or _.");
    }

    public static void ShowSettingsUpdateError(object? source, Exception ex)
    {
        ShowError(source, "Settings", "Failed to update launch settings:\n" + FormatException(ex));
    }

    public static void ShowStartupError(object? source, string message)
    {
        ShowError(source, "Startup Error", message);
    }

    public static bool ConfirmCloseJava(object? source)
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

    public static void ShowStatsWarning()
    {
        ShowWarning(null, "Statistics", "Statistics could not be loaded, so empty totals are being displayed. The statistics database was not reset.");
    }

}
