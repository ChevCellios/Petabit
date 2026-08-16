using Petabit.Models;
using Xunit;

namespace Petabit.Tests;

public sealed class StationStatusTests
{
    [Fact]
    public void StatusBecomesStaleAfterMaximumVerificationAge()
    {
        Assert.False(StationStatus.IsStale(
            StationStatus.LastVerified + StationStatus.MaximumVerificationAge));
        Assert.True(StationStatus.IsStale(
            StationStatus.LastVerified + StationStatus.MaximumVerificationAge + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    [Trait("Category", "StationStatusFreshness")]
    public void CuratedStationStatusWasVerifiedRecently()
    {
        Assert.False(
            StationStatus.IsStale(DateTimeOffset.UtcNow),
            $"Curated ISS status was last verified on {StationStatus.LastVerified:yyyy-MM-dd}. " +
            "Check the NASA source, update the data and move LastVerified forward.");
    }
}
