using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BatomonTracker.App;

public sealed class AuthSessionStore
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BatomonTracker",
        "auth-session.json");

    public AuthSessionData? Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            StoredAuthSession? stored = JsonSerializer.Deserialize<StoredAuthSession>(File.ReadAllText(path));
            if (stored is null || string.IsNullOrWhiteSpace(stored.SteamId))
            {
                return null;
            }

            string accessToken = string.IsNullOrWhiteSpace(stored.ProtectedAccessToken)
                ? stored.AccessToken ?? string.Empty
                : Unprotect(stored.ProtectedAccessToken);
            return new AuthSessionData(stored.SteamId, accessToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public string? LoadSteamId() => Load()?.SteamId;

    public void SaveSteamId(string steamId)
    {
        SaveSession(steamId, string.Empty);
    }

    public void SaveSession(string steamId, string accessToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string protectedToken = Protect(accessToken);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new StoredAuthSession(steamId, protectedToken)));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static string Protect(string value)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(value);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string value)
    {
        byte[] protectedBytes = Convert.FromBase64String(value);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }

}

public sealed record AuthSessionData(string SteamId, string AccessToken);

internal sealed record StoredAuthSession(string SteamId, string? ProtectedAccessToken, string? AccessToken = null);
