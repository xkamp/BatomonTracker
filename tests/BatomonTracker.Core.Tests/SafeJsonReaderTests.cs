using BatomonTracker.Infrastructure.Filesystem;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class SafeJsonReaderTests
{
    [Fact]
    public async Task ReadsValidRunSave()
    {
        string path = Path.Combine("Fixtures", "run_save.json");

        var result = await new SafeJsonReader().ReadRunAsync(path);

        Assert.True(result.Success);
        Assert.Equal("test-run-001", result.Value?.RunId);
    }

    [Fact]
    public async Task ReturnsFailureForMissingFile()
    {
        var result = await new SafeJsonReader().ReadRunAsync("missing-run-save.json", attempts: 1);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
    }
}
