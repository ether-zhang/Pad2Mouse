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
        FilePath = path ?? DefaultPath();
        MigrateLegacyLocation();
    }

    /// <summary>%LOCALAPPDATA%\Pad2Mouse\config.json. Picked because the exe's
    /// install location may be read-only (Steam ships under Program Files;
    /// Save would silently swallow the access-denied otherwise).</summary>
    private static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pad2Mouse",
            "config.json");

    /// <summary>Earlier builds wrote next to the exe. If the new location is
    /// empty but a legacy file exists, move it once so the user's settings
    /// carry over. Best-effort: any failure leaves the legacy file in place
    /// and Load falls back to defaults.</summary>
    private void MigrateLegacyLocation()
    {
        try
        {
            if (File.Exists(FilePath)) return;
            var legacy = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(legacy)) return;
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Move(legacy, FilePath);
        }
        catch { /* migration is best-effort */ }
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
            c.Mappings ??= new ButtonMappings();
            c.FullscreenWhitelist ??= new List<string>();
            MigrateLegacyR2OrCross(json, c.Mappings);
            return c;
        }
        catch
        {
            return AppConfig.Default();
        }
    }

    /// <summary>Pre-2026-05-02 schema had a single "R2OrCross" slot that
    /// was OR-aggregated from both inputs. After the split into independent
    /// R2 / Cross slots, fan that legacy value out into both new slots so
    /// the user's customization survives the upgrade.</summary>
    private static void MigrateLegacyR2OrCross(string json, ButtonMappings mappings)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Mappings", out var m)) return;
            if (!m.TryGetProperty("R2OrCross", out var legacy)) return;
            var v = legacy.GetString();
            if (string.IsNullOrEmpty(v)) return;
            mappings.R2 = v;
            mappings.Cross = v;
        }
        catch { /* migration is best-effort */ }
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
