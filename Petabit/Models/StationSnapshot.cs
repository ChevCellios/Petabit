namespace Petabit.Models;

public record StationEvent(string Id, string Title, string SourceUrl, DateTimeOffset PublishedAt, string Kind);

public record StationSnapshot(
    CrewMember[] Crew,
    DockedVehicle[] DockedVehicles,
    DateTimeOffset CrewUpdatedAt,
    DateTimeOffset VehiclesUpdatedAt,
    DateTimeOffset? CheckedAt,
    string CrewSource,
    string VehicleSource,
    StationEvent[] Events,
    string[] ProcessedIds,
    bool NeedsReview = false)
{
    public static StationSnapshot Bootstrap() => new(
        StationStatus.Crew.ToArray(), StationStatus.DockedVehicles.ToArray(),
        StationStatus.LastVerified, new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null,
        "https://www.nasa.gov/blogs/spacestation/2026/10/02/station-crew-expands-as-nasas-spacex-crew-12-prepares-to-depart/",
        StationStatus.SourceUrl, [], []);
}
