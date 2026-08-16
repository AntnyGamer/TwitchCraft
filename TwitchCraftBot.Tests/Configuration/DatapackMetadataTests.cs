using System.Text.Json;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Configuration;

public sealed class DatapackMetadataTests
{
    [Theory]
    [InlineData(null, false, 15, 0, true)]
    [InlineData("1.20.4", false, 26, 0, false)]
    [InlineData("1.21.11", true, 94, 1, false)]
    [InlineData("26.1.0", true, 101, 1, false)]
    public void BuildPackMetadataJson_UsesTheVersionAppropriateSchema(
        string? version,
        bool modern,
        int formatMajor,
        int formatMinor,
        bool includesFallbackRange)
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

        Assert.Equal(
            includesFallbackRange,
            pack.TryGetProperty("supported_formats", out JsonElement supportedFormats));
        if (includesFallbackRange)
        {
            Assert.Equal(15, supportedFormats.GetProperty("min_inclusive").GetInt32());
            Assert.Equal(61, supportedFormats.GetProperty("max_inclusive").GetInt32());
        }
    }
}
