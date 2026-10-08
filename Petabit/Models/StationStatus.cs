namespace Petabit.Models;

public record CrewMember(string Name, string Country, string Agency, string Mission = "");

public record DockedVehicle(string Name, string Purpose, string Operator);

public static class StationStatus
{
    public static readonly TimeSpan MaximumVerificationAge = TimeSpan.FromDays(45);

    // Offline bootstrap, confirmed by NASA's 2 October station report.
    // The background synchronizer replaces this with persisted NASA observations.
    public static readonly IReadOnlyList<CrewMember> Crew =
    [
        new("Jessica Meir", "SAD", "NASA", "Crew-12"),
        new("Jack Hathaway", "SAD", "NASA", "Crew-12"),
        new("Sophie Adenot", "Francuska", "ESA", "Crew-12"),
        new("Andrey Fedyaev", "Rusija", "Roscosmos", "Crew-12"),
        new("Anil Menon", "SAD", "NASA", "Soyuz MS-29"),
        new("Pyotr Dubrov", "Rusija", "Roscosmos", "Soyuz MS-29"),
        new("Anna Kikina", "Rusija", "Roscosmos", "Soyuz MS-29"),
        new("Jessica Watkins", "SAD", "NASA", "Crew-13"),
        new("Luke Delaney", "SAD", "NASA", "Crew-13"),
        new("Joshua Kutryk", "Kanada", "CSA", "Crew-13"),
        new("Sergey Teteryatnikov", "Rusija", "Roscosmos", "Crew-13")
    ];

    public static readonly IReadOnlyList<DockedVehicle> DockedVehicles =
    [
        new("Crew-12 Dragon", "Posadna letjelica", "SpaceX / NASA"),
        new("Crew-13 Dragon", "Posadna letjelica", "SpaceX / NASA"),
        new("Cygnus XL", "Teretna letjelica", "Northrop Grumman / NASA"),
        new("Soyuz MS-29", "Posadna letjelica", "Roscosmos"),
        new("Progress 95", "Teretna letjelica", "Roscosmos"),
        new("Progress 96", "Teretna letjelica", "Roscosmos")
    ];

    public static readonly DateTimeOffset LastVerified = new(2026, 10, 2, 18, 14, 13, TimeSpan.Zero);
    public const string SourceUrl = "https://www.nasa.gov/international-space-station/space-station-overview/";

    public static bool IsStale(DateTimeOffset now) => now - LastVerified > MaximumVerificationAge;
}
