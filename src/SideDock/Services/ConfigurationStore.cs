using System.Text.Json;

using SideDock.Models;

namespace SideDock.Services;

internal sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal ConfigurationStore(string? profileDirectory = null)
    {
        ProfileDirectory = profileDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SideDock");
        ConfigurationPath = Path.Combine(ProfileDirectory, "config.json");
        BackupPath = Path.Combine(ProfileDirectory, "config.backup.json");
    }

    internal string ProfileDirectory { get; }
    internal string ConfigurationPath { get; }
    internal string BackupPath { get; }

    internal SideDockConfiguration? Load()
    {
        return TryRead(ConfigurationPath) ?? TryRead(BackupPath);
    }

    internal void Save(SideDockConfiguration configuration)
    {
        Directory.CreateDirectory(ProfileDirectory);
        var temporaryPath = ConfigurationPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, JsonOptions));

        if (File.Exists(ConfigurationPath))
        {
            File.Replace(temporaryPath, ConfigurationPath, BackupPath, true);
        }
        else
        {
            File.Move(temporaryPath, ConfigurationPath);
        }
    }

    private static SideDockConfiguration? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var configuration = JsonSerializer.Deserialize<SideDockConfiguration>(File.ReadAllText(path));
            if (configuration == null) return null;
            if (configuration.SchemaVersion < 2)
            {
                configuration.HandleWidth = 12;
                configuration.SchemaVersion = 2;
            }
            if (configuration.SchemaVersion < 3)
            {
                configuration.HandleWidth = 28;
                configuration.SchemaVersion = 3;
            }
            if (configuration.SchemaVersion < 4)
            {
                if (configuration.AnimationMilliseconds == 220)
                {
                    configuration.AnimationMilliseconds = 280;
                }
                configuration.SchemaVersion = 4;
            }
            if (configuration.SchemaVersion < 5)
            {
                configuration.SchemaVersion = 5;
            }
            if (configuration.SchemaVersion < 6)
            {
                configuration.SchemaVersion = 6;
            }
            configuration.DrawerWidth = Math.Clamp(configuration.DrawerWidth, 320, 720);
            configuration.DrawerHeight = Math.Clamp(configuration.DrawerHeight, 420, 1200);
            configuration.HandleWidth = Math.Clamp(configuration.HandleWidth, 20, 48);
            configuration.AnimationMilliseconds = Math.Clamp(configuration.AnimationMilliseconds, 80, 600);
            configuration.Tabs ??= [];
            foreach (var tab in configuration.Tabs)
            {
                tab.Items ??= [];
            }
            return configuration;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
