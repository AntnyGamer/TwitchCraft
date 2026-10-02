using System.Reflection;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

public sealed class ConfigurationCloneTests
{
    [Fact]
    public void Clone_PreservesEveryScalarAndSeparatesAllMutableGroups()
    {
        TwitchCraftConfig source = new();
        object[] sourceGroups = Groups(source);
        // Nondefault values expose an omitted assignment, including session-only settings.
        foreach (object group in sourceGroups)
        {
            foreach (PropertyInfo property in group.GetType().GetProperties())
            {
                object? value = property.GetValue(group);
                object? replacement = value switch
                {
                    bool flag => !flag,
                    int number => number + 1,
                    double number => number + 0.125,
                    string => property.Name + " value",
                    _ => null
                };
                if (replacement != null)
                    property.SetValue(group, replacement);
            }
        }
        source.Settings.CommandCustomizations["Heal"] = new()
        {
            Enabled = false,
            CooldownSeconds = 17,
            GlobalCooldownSeconds = 0.123456789
        };

        TwitchCraftConfig clone = ConfigurationStore.Clone(source);

        Assert.NotSame(source, clone);
        object[] cloneGroups = Groups(clone);
        for (int i = 0; i < sourceGroups.Length; i++)
        {
            Assert.NotSame(sourceGroups[i], cloneGroups[i]);
            foreach (PropertyInfo property in sourceGroups[i].GetType().GetProperties())
            {
                if (property.PropertyType == typeof(string) || property.PropertyType.IsValueType)
                    Assert.Equal(property.GetValue(sourceGroups[i]), property.GetValue(cloneGroups[i]));
            }
        }
        Assert.NotSame(source.Settings.CommandCustomizations, clone.Settings.CommandCustomizations);
        CommandCustomization originalCommand = Assert.Single(source.Settings.CommandCustomizations).Value;
        CommandCustomization clonedCommand = Assert.Single(clone.Settings.CommandCustomizations).Value;
        Assert.NotSame(originalCommand, clonedCommand);
        Assert.False(clonedCommand.Enabled);
        Assert.Equal(17, clonedCommand.CooldownSeconds);
        Assert.Equal(0.123456789, clonedCommand.GlobalCooldownSeconds);
        Assert.Same(clonedCommand, clone.Settings.CommandCustomizations["HEAL"]);

        clonedCommand.Enabled = true;
        clonedCommand.CooldownSeconds = null;
        clonedCommand.GlobalCooldownSeconds = null;
        source.Settings.CommandCustomizations["lightning"] = new() { Enabled = false };
        clone.Settings.CommandCustomizations.Remove("heal");

        Assert.False(originalCommand.Enabled);
        Assert.Equal(17, originalCommand.CooldownSeconds);
        Assert.Equal(0.123456789, originalCommand.GlobalCooldownSeconds);
        Assert.True(source.Settings.CommandCustomizations.ContainsKey("heal"));
        Assert.False(clone.Settings.CommandCustomizations.ContainsKey("lightning"));
    }

    [Fact]
    public void Clone_NullCommandCustomizationsCreatesAnIndependentUsableDictionary()
    {
        TwitchCraftConfig source = new() { Settings = { CommandCustomizations = null! } };

        TwitchCraftConfig clone = ConfigurationStore.Clone(source);

        Assert.Null(source.Settings.CommandCustomizations);
        Assert.Empty(clone.Settings.CommandCustomizations);
        clone.Settings.CommandCustomizations["Heal"] = new() { Enabled = false };
        Assert.False(clone.Settings.CommandCustomizations["HEAL"].Enabled);
    }

    private static object[] Groups(TwitchCraftConfig config)
        => [config.Server, config.Server.Java, config.Server.RCON, config.Twitch, config.Identity, config.Settings];
}
