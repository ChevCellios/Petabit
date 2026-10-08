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
    public void OfflineBootstrapDoesNotPretendToHaveCheckedNasaLive()
    {
        var state = StationSnapshot.Bootstrap();
        Assert.Null(state.CheckedAt);
        Assert.Equal(11, state.Crew.Length);
        Assert.Contains(state.DockedVehicles, v => v.Name == "Crew-13 Dragon");
    }
}
