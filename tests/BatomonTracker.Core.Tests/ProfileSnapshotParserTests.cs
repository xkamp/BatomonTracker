using BatomonTracker.Core.Parsing;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class ProfileSnapshotParserTests
{
    [Fact]
    public void ParsesKnownFieldsAndIgnoresUnknownFields()
    {
        string json = File.ReadAllText(Path.Combine("Fixtures", "profile_snapshot.json"));

        var profile = new ProfileSnapshotParser().Parse(json);

        Assert.Equal("Test Trainer", profile.DisplayName);
        Assert.Equal(12, profile.GamesPlayed);
        Assert.Equal(3, profile.RunsWon);
        Assert.Equal(1042, profile.Mmr);
        Assert.Equal(2, profile.MatchCount);
    }

    [Fact]
    public void MissingFieldsBecomeNullOrEmpty()
    {
        var profile = new ProfileSnapshotParser().Parse("{}");

        Assert.Null(profile.DisplayName);
        Assert.Null(profile.GamesPlayed);
        Assert.Equal(0, profile.MatchCount);
    }
}
