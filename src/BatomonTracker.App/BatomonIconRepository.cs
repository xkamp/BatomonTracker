using System.IO;
using System.Windows.Media.Imaging;

namespace BatomonTracker.App;

public sealed class BatomonIconRepository
{
    private readonly string iconDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");

    public BitmapImage? Load(string speciesId, bool shiny)
    {
        string preferredName = shiny ? $"{speciesId}_shiny.png" : $"{speciesId}.png";
        string preferredPath = Path.Combine(iconDirectory, preferredName);
        string fallbackPath = Path.Combine(iconDirectory, $"{speciesId}.png");
        string path = File.Exists(preferredPath) ? preferredPath : fallbackPath;

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
