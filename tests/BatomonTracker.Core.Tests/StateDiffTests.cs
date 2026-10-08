using BatomonTracker.Core.Diff;
using BatomonTracker.Core.Models;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class StateDiffTests
{
    [Fact]
    public void DetectsRoundWinLossAndReroll()
    {
        var previous = State(round: 1, wins: 0, lives: 10, shopRoll: 1);
        var current = State(round: 2, wins: 1, lives: 9, shopRoll: 2);

        var events = new StateDiff().Compare(previous, current);

        Assert.Contains(events, item => item.Type == TrackerEventType.RoundStarted);
        Assert.Contains(events, item => item.Type == TrackerEventType.BattleWon);
        Assert.Contains(events, item => item.Type == TrackerEventType.BattleLost);
        Assert.Contains(events, item => item.Type == TrackerEventType.ShopRerolled);
    }

    [Fact]
    public void DetectsPurchaseWithDeducedConfidence()
    {
        var previous = State(gold: 20, team: Array.Empty<BatomonUnit>());
        var current = State(gold: 15, team: new[] { new BatomonUnit("boomagon", 1, false) });

        var events = new StateDiff().Compare(previous, current);
        var purchase = Assert.Single(events);

        Assert.Equal(TrackerEventType.BatomonBought, purchase.Type);
        Assert.Equal(EventConfidence.Deduced, purchase.Confidence);
        Assert.Equal("boomagon", purchase.Payload["species_id"]);
    }

    [Fact]
    public void DifferentRunProducesOnlyNewRun()
    {
        var previous = State(runId: "old");
        var current = State(runId: "new");

        var events = new StateDiff().Compare(previous, current);

        var eventItem = Assert.Single(events);
        Assert.Equal(TrackerEventType.NewRun, eventItem.Type);
    }

    private static RunState State(
        string runId = "run",
        int round = 1,
        int wins = 0,
        int lives = 10,
        int gold = 20,
        long shopRoll = 1,
        IReadOnlyList<BatomonUnit>? team = null)
    {
        return new RunState(runId, 1, 1, round, wins, lives, gold, "trainer", 1, shopRoll,
            team ?? Array.Empty<BatomonUnit>(), Array.Empty<BatomonUnit>(), Array.Empty<ShopSlot>(), Array.Empty<string>());
    }
}
