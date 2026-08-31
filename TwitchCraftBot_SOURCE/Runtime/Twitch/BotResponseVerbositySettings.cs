namespace TwitchCraftBot_V1;

internal enum BotResponseKind
{
    Essential,
    Confirmation,
    Announcement
}

internal static class BotResponseVerbositySettings
{
    internal const string Normal = "Normal";
    internal const string Reduced = "Reduced";
    internal const string EssentialOnly = "Essential Only";

    internal static bool ShouldSend(string verbosity, BotResponseKind kind)
        => verbosity switch
        {
            Reduced => kind != BotResponseKind.Confirmation,
            EssentialOnly => kind == BotResponseKind.Essential,
            _ => true
        };
}
