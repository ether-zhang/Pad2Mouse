using System.IO;
using System.Text.Json;
using DS2Mouse.Models;

namespace DS2Mouse.Services;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public string FilePath { get; }

    public ConfigStore(string? path = null)
    {
        FilePath = path ?? Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath)) return AppConfig.Default();
        try
        {
            var json = File.ReadAllText(FilePath);
            var c = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? AppConfig.Default();
            c.LeftStick ??= new StickConfig();
            c.RightStick ??= new ScrollConfig();
            c.FullscreenWhitelist ??= new List<string>();
            return c;
        }
        catch
        {
            return AppConfig.Default();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(config, Options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Config saves must never crash the app.
        }
    }
}
