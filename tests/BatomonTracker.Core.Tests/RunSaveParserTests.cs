using BatomonTracker.Core.Parsing;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class RunSaveParserTests
{
    [Fact]
    public void ParsesRunAndPreservesEmptyShopSlots()
    {
        string json = File.ReadAllText(Path.Combine("Fixtures", "run_save.json"));

        var state = new RunSaveParser().Parse(json);

        Assert.Equal("test-run-001", state.RunId);
        Assert.Equal(4, state.Round);
        Assert.Single(state.Team);
        Assert.Single(state.Bench);
        Assert.Equal("shellter", state.Bench[0].SpeciesId);
        Assert.Equal(4, state.Shop.Count);
        Assert.Null(state.Shop[1].Unit);
        Assert.Equal("noxalith", state.Shop[2].Unit?.SpeciesId);
        Assert.True(state.Shop[2].Unit?.IsShiny == true);
        Assert.Equal("voucher", state.Shop[3].ItemId);
        Assert.Equal(new[] { "fake_diamond", "gold_nugget" }, state.Trinkets);
    }

    [Fact]
    public void RequiresRunId()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => new RunSaveParser().Parse("{}"));
    }
}
