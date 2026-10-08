using BatomonTracker.Infrastructure.Filesystem;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class GodotSaveLocatorTests
{
    [Fact]
    public void FindsProfileAndLiveRunAcrossAccounts()
    {
        string root = CreateTempDirectory();
        try
        {
            string accounts = Path.Combine(root, "Godot", "app_userdata", "Batomon Showdown", "accounts");
            string first = Path.Combine(accounts, "account-one");
            string second = Path.Combine(accounts, "account-two");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "profile_snapshot.json"), "{}");
            File.WriteAllText(Path.Combine(second, "run_save.json"), "{}");
            File.SetLastWriteTimeUtc(Path.Combine(first, "profile_snapshot.json"), DateTime.UtcNow.AddMinutes(-1));

            var located = new GodotSaveLocator(root).FindAccounts();

            Assert.Equal(2, located.Count);
            Assert.Contains(located, account => account.ProfileSnapshotPath is not null);
            Assert.Contains(located, account => account.RunSavePath is not null);
            Assert.True(new GodotSaveLocator(root).FindActiveAccount()?.HasLiveRun == true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReturnsEmptyWhenGameDirectoryDoesNotExist()
    {
        string root = CreateTempDirectory();
        try
        {
            Assert.Empty(new GodotSaveLocator(root).FindAccounts());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "BatomonTrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
