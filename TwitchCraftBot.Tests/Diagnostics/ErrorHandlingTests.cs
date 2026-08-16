using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Diagnostics;

public sealed class ErrorHandlingTests
{
    [Fact]
    public void BuildDatapackInstallWarningMessage_ExplainsThatStartupContinues()
    {
        string message = ErrorHandling.BuildDatapackInstallWarningMessage("Bundled files are missing.");

        Assert.Contains("locateplayers", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will continue", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("player-location features may be unavailable", message, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Bundled files are missing.", message, StringComparison.Ordinal);
    }
}
