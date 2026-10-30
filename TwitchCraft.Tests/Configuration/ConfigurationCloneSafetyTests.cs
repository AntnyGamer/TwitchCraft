using System;
using System.Collections.Generic;
using System.Reflection;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

public sealed class ConfigurationCloneSafetyTests
{
    [Fact]
    public void Clone_KeepsMutableConfigurationObjectsIndependent()
    {
        TwitchCraftConfig original = new();
        original.Settings.CommandCustomizations["heal"] = new CommandCustomization { Enabled = false, CooldownSeconds = 5 };

        TwitchCraftConfig copy = ConfigurationStore.Clone(original);
        Assert.NotSame(original, copy);
        Assert.NotSame(original.Server, copy.Server);
        Assert.NotSame(original.Server.Java, copy.Server.Java);
        Assert.NotSame(original.Server.RCON, copy.Server.RCON);
        Assert.NotSame(original.Twitch, copy.Twitch);
        Assert.NotSame(original.Identity, copy.Identity);
        Assert.NotSame(original.Settings, copy.Settings);
        Assert.NotSame(original.Settings.CommandCustomizations, copy.Settings.CommandCustomizations);
        Assert.NotSame(original.Settings.CommandCustomizations["heal"], copy.Settings.CommandCustomizations["heal"]);

        copy.Server.Java.ExecutablePath = "other-java";
        copy.Server.RCON.Password = "other-password";
        copy.Twitch.BotToken = "other-token";
        copy.Identity.StreamerMinecraftName = "other-player";
        copy.Settings.CommandCustomizations["heal"].Enabled = true;
        Assert.NotEqual(copy.Server.Java.ExecutablePath, original.Server.Java.ExecutablePath);
        Assert.NotEqual(copy.Server.RCON.Password, original.Server.RCON.Password);
        Assert.NotEqual(copy.Twitch.BotToken, original.Twitch.BotToken);
        Assert.NotEqual(copy.Identity.StreamerMinecraftName, original.Identity.StreamerMinecraftName);
        Assert.False(original.Settings.CommandCustomizations["heal"].Enabled);
    }

    [Fact]
    public void Clone_AllReferencePropertiesAreExplicitlyAccountedFor()
    {
        Dictionary<Type, string[]> references = new()
        {
            [typeof(TwitchCraftConfig)] = ["Server", "Twitch", "Identity", "Settings"],
            [typeof(ServerConfig)] = ["Java", "RCON"],
            [typeof(JavaConfig)] = [],
            [typeof(RCONConfig)] = [],
            [typeof(TwitchConfig)] = [],
            [typeof(IdentityConfig)] = [],
            [typeof(StartingProfile)] = ["CommandCustomizations"],
            [typeof(CommandCustomization)] = []
        };

        foreach ((Type type, string[] allowed) in references)
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (!property.PropertyType.IsValueType && property.PropertyType != typeof(string))
                    Assert.Contains(property.Name, allowed);
    }
}
