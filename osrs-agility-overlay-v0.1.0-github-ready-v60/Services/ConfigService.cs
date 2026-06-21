using System.Text.Json;
using System.Text.Json.Serialization;
using OSRSAgilityOverlay.Models;

namespace OSRSAgilityOverlay.Services;

public sealed class ConfigService
{
    public string ConfigPath { get; }

    public ConfigService()
    {
        ConfigPath = FindConfigPath();
    }

    public OverlayConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                OverlayConfig created = DefaultConfig();
                Save(created);
                return created;
            }

            string raw = File.ReadAllText(ConfigPath);
            OverlayConfig config = JsonSerializer.Deserialize<OverlayConfig>(raw, JsonOptions()) ?? DefaultConfig();
            Validate(config);
            Save(config);
            return config;
        }
        catch
        {
            return DefaultConfig();
        }
    }

    public void Save(OverlayConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        if (File.Exists(ConfigPath))
        {
            string backup = Path.Combine(Path.GetDirectoryName(ConfigPath)!, "markers.backup.json");
            File.Copy(ConfigPath, backup, overwrite: true);
        }

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions()));
    }

    public static string FindConfigPath()
    {
        string exeDir = AppContext.BaseDirectory;
        string exeConfig = Path.Combine(exeDir, "markers.json");

        if (File.Exists(Path.Combine(exeDir, "OSRSAgilityOverlay.exe")))
            return exeConfig;

        DirectoryInfo? dir = new DirectoryInfo(exeDir);
        while (dir != null)
        {
            string project = Path.Combine(dir.FullName, "OSRSAgilityOverlay.csproj");
            string projectConfig = Path.Combine(dir.FullName, "markers.json");

            if (File.Exists(project) && File.Exists(projectConfig))
                return projectConfig;

            dir = dir.Parent;
        }

        return exeConfig;
    }

    private static void Validate(OverlayConfig config)
    {
        if (config.TickSeconds <= 0) config.TickSeconds = 0.6;
        // Final release: CSV timing logs are runtime-only and enabled with Ctrl+L, not markers.json.
        config.DebugTimingLogEnabled = false;
        config.WorldLagMs = Math.Clamp(config.WorldLagMs, 0, 250);
        config.QueueSafetyMs = Math.Clamp(config.QueueSafetyMs, 0, 100);
        if (config.GlobalClickExtraRadius < 0) config.GlobalClickExtraRadius = 10;
        if (config.TickSyncAreaWidth < 1) config.TickSyncAreaWidth = config.TickSyncAreaWidth == 0 ? 0 : 1;
        if (config.TickSyncAreaHeight < 1) config.TickSyncAreaHeight = config.TickSyncAreaHeight == 0 ? 0 : 1;
        if (!config.HasTickSyncArea) config.TickSyncEnabled = false;

        foreach (Marker marker in config.Markers)
        {
            if (marker.Radius <= 0) marker.Radius = 16;
            if (marker.DelayTicks < 0) marker.DelayTicks = 0;
            if (string.IsNullOrWhiteSpace(marker.Name)) marker.Name = "Marker";
        }

        if (config.Markers.Count == 0)
            config.Markers = DefaultConfig().Markers;
    }

    public static OverlayConfig DefaultConfig() => new()
    {
        TargetWindowTitleContains = "RuneLite",
        AnchorToRuneLiteWindow = true,
        OverlayOpacity = 100,
        MarkerNumberSize = 12,
        TickSeconds = 0.6,
        GlobalClickExtraRadius = 10,
        WorldLagMs = 28,
        QueueSafetyMs = 15,
        TickOffsetSeconds = 0.0,
        ShowInfoOverlay = true,
        MinimalMode = false,
        DebugTimingLogEnabled = false,
        TickSyncEnabled = false,
        TickSyncSinglePixelMode = true,
        TickSyncAreaRelativeToRuneLite = true,
        TickSyncAreaX = 0,
        TickSyncAreaY = 0,
        TickSyncAreaWidth = 0,
        TickSyncAreaHeight = 0,
        Markers = new List<Marker>
        {
            new() { Name = "Obstacle 1", X = 2364, Y = 1646, Radius = 16, DelayTicks = 18 },
            new() { Name = "Obstacle 2", X = 3090, Y = 567, Radius = 16, DelayTicks = 9 },
            new() { Name = "Obstacle 3", X = 1757, Y = 770, Radius = 16, DelayTicks = 14 },
            new() { Name = "Obstacle 4", X = 1756, Y = 770, Radius = 16, DelayTicks = 11 },
            new() { Name = "Obstacle 5", X = 1234, Y = 1377, Radius = 16, DelayTicks = 5 },
            new() { Name = "Obstacle 6", X = 822, Y = 2026, Radius = 16, DelayTicks = 7 },
            new() { Name = "Obstacle 7", X = 1850, Y = 1186, Radius = 16, DelayTicks = 12 }
        }
    };

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
