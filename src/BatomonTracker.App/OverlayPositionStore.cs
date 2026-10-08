using System.IO;
using System.Text.Json;

namespace BatomonTracker.App;

public sealed class OverlayPositionStore
{
    private readonly string path;

    public OverlayPositionStore()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BatomonTracker");
        path = Path.Combine(directory, "overlay-position.json");
    }

    public (double Left, double Top) Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return (0.05, 0.05);
            }

            OverlayPosition? position = JsonSerializer.Deserialize<OverlayPosition>(File.ReadAllText(path));
            return position is null
                ? (0.05, 0.05)
                : (position.RelativeLeft, position.RelativeTop);
        }
        catch (IOException)
        {
            return (0.05, 0.05);
        }
        catch (JsonException)
        {
            return (0.05, 0.05);
        }
    }

    public void Save(double relativeLeft, double relativeTop)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(new OverlayPosition(relativeLeft, relativeTop));
            File.WriteAllText(path, json);
        }
        catch (IOException)
        {
            // Overlay persistence is optional and must never interrupt live tracking.
        }
    }

    private sealed record OverlayPosition(double RelativeLeft, double RelativeTop);
}
