using System.IO;
using System.Text.Json;

namespace BatomonTracker.App;

public sealed record OverlaySettings(double Scale = 1.0, double Opacity = 0.95, bool StartVisible = true);

public sealed class OverlaySettingsStore
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BatomonTracker",
        "overlay-settings.json");

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new OverlaySettings();
            }

            OverlaySettings? settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path));
            return settings is null
                ? new OverlaySettings()
                : Normalize(settings);
        }
        catch (IOException)
        {
            return new OverlaySettings();
        }
        catch (JsonException)
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(Normalize(settings)));
        }
        catch (IOException)
        {
            // Settings persistence is optional and must not interrupt live tracking.
        }
    }

    private static OverlaySettings Normalize(OverlaySettings settings)
    {
        double scale = double.IsFinite(settings.Scale) ? Math.Clamp(settings.Scale, 0.75, 2.0) : 1.0;
        double opacity = double.IsFinite(settings.Opacity) ? Math.Clamp(settings.Opacity, 0.35, 1.0) : 0.95;
        return settings with { Scale = scale, Opacity = opacity };
    }
}
