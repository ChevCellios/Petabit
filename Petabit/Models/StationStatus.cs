namespace Petabit.Models;

public record CrewMember(string Name, string Country, string Agency);

public record DockedVehicle(string Name, string Purpose, string Operator);

public static class StationStatus
{
    // NASA's public station-status pages were checked on 26 July 2026.
    // Keep this curated data separate from the live orbital-position feed.
    public static readonly IReadOnlyList<CrewMember> Crew =
    [
        new("Jessica Meir", "SAD", "NASA"),
        new("Jack Hathaway", "SAD", "NASA"),
        new("Sophie Adenot", "Francuska", "ESA"),
        new("Andrey Fedyaev", "Rusija", "Roscosmos"),
        new("Anil Menon", "SAD", "NASA"),
        new("Pyotr Dubrov", "Rusija", "Roscosmos"),
        new("Anna Kikina", "Rusija", "Roscosmos")
    ];

    public static readonly IReadOnlyList<DockedVehicle> DockedVehicles =
    [
        new("Crew-12 Dragon", "Posadna letjelica", "SpaceX / NASA"),
        new("Cygnus XL", "Teretna letjelica", "Northrop Grumman / NASA"),
        new("Soyuz MS-29", "Posadna letjelica", "Roscosmos"),
        new("Progress 94", "Teretna letjelica", "Roscosmos"),
        new("Progress 95", "Teretna letjelica", "Roscosmos")
    ];

    public static readonly DateTimeOffset LastVerified = new(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
    public const string SourceUrl = "https://www.nasa.gov/international-space-station/space-station-visiting-vehicles/";
}
