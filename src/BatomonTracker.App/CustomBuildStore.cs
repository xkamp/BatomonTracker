using System.IO;
using System.Text.Json;

namespace BatomonTracker.App;

public sealed record CustomBuild(string Name, IReadOnlyList<string> SpeciesIds, DateTimeOffset CreatedAt);

public sealed class CustomBuildStore
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BatomonTracker",
        "custom-builds.json");

    public IReadOnlyList<CustomBuild> Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return Array.Empty<CustomBuild>();
            }

            return JsonSerializer.Deserialize<List<CustomBuild>>(File.ReadAllText(path)) ?? new List<CustomBuild>();
        }
        catch (IOException)
        {
            return Array.Empty<CustomBuild>();
        }
        catch (JsonException)
        {
            return Array.Empty<CustomBuild>();
        }
    }

    public void Save(IReadOnlyList<CustomBuild> builds)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(builds));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
