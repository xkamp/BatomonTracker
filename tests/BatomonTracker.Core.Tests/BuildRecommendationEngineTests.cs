using BatomonTracker.Core.Models;
using BatomonTracker.Core.Recommendations;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class BuildRecommendationEngineTests
{
    [Fact]
    public void RecommendsShopUnitAlreadyInBuild()
    {
        var state = CreateState(
            new[] { new BatomonUnit("boomagon", 1, false) },
            new[] { new ShopSlot(0, new BatomonUnit("boomagon", 1, false), null) });

        BuildRecommendation recommendation = new BuildRecommendationEngine().Evaluate(state);

        Assert.Equal(RecommendationAction.Buy, recommendation.Action);
        Assert.Equal("boomagon", recommendation.SpeciesId);
    }

    [Fact]
    public void HoldsWhenNoSafePriorityExists()
    {
        BuildRecommendation recommendation = new BuildRecommendationEngine().Evaluate(CreateState(
            new[] { new BatomonUnit("boomagon", 1, false) },
            new[] { new ShopSlot(0, new BatomonUnit("shellter", 1, false), null) }));

        Assert.Equal(RecommendationAction.Hold, recommendation.Action);
    }

    [Fact]
    public void ExplainsWhenPurchaseCanCompleteMerge()
    {
        BuildRecommendation recommendation = new BuildRecommendationEngine().Evaluate(CreateState(
            new[]
            {
                new BatomonUnit("boomagon", 1, false),
                new BatomonUnit("boomagon", 1, false)
            },
            new[] { new ShopSlot(0, new BatomonUnit("boomagon", 1, false), null) }));

        Assert.Equal(RecommendationAction.Buy, recommendation.Action);
        Assert.Contains("merge", recommendation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsidersBenchUnitsForPurchasePriority()
    {
        BuildRecommendation recommendation = new BuildRecommendationEngine().Evaluate(new RunState(
            "recommendation-test", 1, 1, 1, 0, 10, 20, "trainer", 1, 1,
            Array.Empty<BatomonUnit>(),
            new[] { new BatomonUnit("shellter", 1, false) },
            new[] { new ShopSlot(0, new BatomonUnit("shellter", 2, false), null) },
            Array.Empty<string>()));

        Assert.Equal(RecommendationAction.Buy, recommendation.Action);
        Assert.Equal("shellter", recommendation.SpeciesId);
    }

    private static RunState CreateState(IReadOnlyList<BatomonUnit> team, IReadOnlyList<ShopSlot> shop)
    {
        return new RunState("recommendation-test", 1, 1, 1, 0, 10, 20, "trainer", 1, 1,
            team, Array.Empty<BatomonUnit>(), shop, Array.Empty<string>());
    }
}
