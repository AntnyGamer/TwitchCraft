using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Diagnostics;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_RemovesRegisteredSecretsWhereverTheyAppear()
    {
        SecretRedactor.Register("registered-secret");

        string result = SecretRedactor.Redact("Connection failed for registered-secret.");

        Assert.Equal("Connection failed for [REDACTED].", result);
    }

    [Theory]
    [InlineData("Authorization: Bearer abc123", "Authorization: Bearer [REDACTED]")]
    [InlineData("PASS oauth:abc123", "PASS oauth:[REDACTED]")]
    [InlineData("token=oauth:abc123", "token=oauth:[REDACTED]")]
    [InlineData("rcon.password=abc123", "rcon.password=[REDACTED]")]
    [InlineData("C:\\Users\\Alice\\AppData\\file.log", "C:\\Users\\[USER]\\AppData\\file.log")]
    public void Redact_RemovesKnownSensitivePatterns(string value, string expected)
    {
        Assert.Equal(expected, SecretRedactor.Redact(value));
    }

    [Fact]
    public void FormatLogMessage_RedactsRegisteredSecretFromException()
    {
        ErrorHandling.RegisterSecrets("exception-secret");

        string result = ErrorHandling.FormatLogMessage("Remote connection failed", new InvalidOperationException("Password exception-secret was rejected"));

        Assert.DoesNotContain("exception-secret", result, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Replacement, result, StringComparison.Ordinal);
    }
}
