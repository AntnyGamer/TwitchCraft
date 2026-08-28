using System.Text.Json;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Configuration;

public sealed class DatapackMetadataTests
{
    [Theory]
    [InlineData("1.20.5", false, 41, 0)]
    [InlineData("1.21.11", true, 94, 1)]
    [InlineData("26.1.0", true, 101, 1)]
    public void BuildPackMetadataJson_UsesTheVersionAppropriateSchema(
        string version,
        bool modern,
        int formatMajor,
        int formatMinor)
    {
        using JsonDocument document = JsonDocument.Parse(
            DatapackInstaller.BuildPackMetadataJson(version));
        JsonElement pack = document.RootElement.GetProperty("pack");

        Assert.Equal(
            "Locate players command for TwitchCraft",
            pack.GetProperty("description").GetString());

        if (modern)
        {
            JsonElement minFormat = pack.GetProperty("min_format");
            JsonElement maxFormat = pack.GetProperty("max_format");
            Assert.Equal(formatMajor, minFormat[0].GetInt32());
            Assert.Equal(formatMinor, minFormat[1].GetInt32());
            Assert.Equal(formatMajor, maxFormat[0].GetInt32());
            Assert.Equal(formatMinor, maxFormat[1].GetInt32());
        }
        else
        {
            Assert.Equal(formatMajor, pack.GetProperty("pack_format").GetInt32());
        }

        Assert.False(pack.TryGetProperty("supported_formats", out _));
    }
}
