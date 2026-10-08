using BatomonTracker.Core.Models;

namespace BatomonTracker.Infrastructure.Filesystem;

public sealed class GodotSaveLocator
{
    private const string GameDirectoryName = "Batomon Showdown";

    public GodotSaveLocator(string? appDataDirectory = null)
    {
        AppDataDirectory = appDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public string AppDataDirectory { get; }

    public string GameDirectory => Path.Combine(AppDataDirectory, "Godot", "app_userdata", GameDirectoryName);

    public string OpponentCacheDirectory => Path.Combine(GameDirectory, "opponent_cache");

    public IReadOnlyList<AccountFiles> FindAccounts()
    {
        string accountsDirectory = Path.Combine(GameDirectory, "accounts");
        if (!Directory.Exists(accountsDirectory))
        {
            return Array.Empty<AccountFiles>();
        }

        return Directory.EnumerateDirectories(accountsDirectory)
            .Select(CreateAccountFiles)
            .ToArray();
    }

    public AccountFiles? FindActiveAccount()
    {
        return FindAccounts()
            .Where(account => account.RunSavePath is not null || account.ProfileSnapshotPath is not null)
            .OrderByDescending(account => account.HasLiveRun)
            .ThenByDescending(GetLatestWriteTimeUtc)
            .FirstOrDefault();
    }

    private static AccountFiles CreateAccountFiles(string accountDirectory)
    {
        string runSavePath = Path.Combine(accountDirectory, "run_save.json");
        string backupPath = Path.Combine(accountDirectory, "run_save.bak.json");
        string profilePath = Path.Combine(accountDirectory, "profile_snapshot.json");

        return new AccountFiles(
            accountDirectory,
            File.Exists(runSavePath) ? runSavePath : null,
            File.Exists(backupPath) ? backupPath : null,
            File.Exists(profilePath) ? profilePath : null);
    }

    private static DateTime GetLatestWriteTimeUtc(AccountFiles account)
    {
        return new[] { account.RunSavePath, account.ProfileSnapshotPath }
            .Where(path => path is not null)
            .Select(path => File.GetLastWriteTimeUtc(path!))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
    }
}
