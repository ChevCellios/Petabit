using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Petabit.Models;

namespace Petabit.Services;

public record NasaArticle(string Id, string Title, string Url, DateTimeOffset PublishedAt, string Text);
public record VehicleObservation(DateTimeOffset Date, DockedVehicle[] Vehicles);

// Deliberately narrow rules: unsupported wording requires review instead of guessing.
public static class NasaStationParser
{
    private static IEnumerable<Match> Matches(string text, string pattern) => Regex.Matches(
        text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>();

    public static string PlainText(string html)
    {
        var text = Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
        return Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ", RegexOptions.None,
            TimeSpan.FromSeconds(1)).Replace('‑', '-').Replace('–', '-').Trim();
    }

    public static NasaArticle[] ParseFeed(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000
        });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "rss") throw new FormatException("Expected NASA RSS.");
        XNamespace content = "http://purl.org/rss/1.0/modules/content/";
        return document.Descendants("item").Select(item =>
        {
            var url = item.Element("link")?.Value ?? "";
            if (!IsNasaUrl(url)) throw new FormatException("Unexpected NASA article URL.");
            var date = DateTimeOffset.ParseExact(item.Element("pubDate")?.Value ?? "",
                "ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
            var html = item.Element(content + "encoded")?.Value ?? item.Element("description")?.Value ?? "";
            // Captions can describe an old photograph; only article paragraphs are evidence.
            var paragraphs = Matches(html, @"<p\b[^>]*>([\s\S]*?)</p>");
            var text = string.Join(" ", paragraphs.Select(p => PlainText(p.Groups[1].Value)));
            return new NasaArticle(url, PlainText(item.Element("title")?.Value ?? ""), url, date, text);
        }).OrderBy(a => a.PublishedAt).ToArray();
    }

    public static bool IsNasaUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "www.nasa.gov";

    public static VehicleObservation ParseVehicles(string html)
    {
        var captions = Matches(html, @"<figcaption\b[^>]*>([\s\S]*?)</figcaption>")
            .Select(m => PlainText(m.Groups[1].Value));
        foreach (var caption in captions)
        {
            var match = Matches(caption, @"(?<date>[A-Za-z]+\.?\s+\d{1,2},\s+\d{4}): International Space Station Configuration\.\s+(?<count>\w+) spaceships are attached[^.]*").FirstOrDefault();
            if (match is null) continue;
            var dateText = match.Groups["date"].Value.Replace(".", "");
            var date = DateTimeOffset.ParseExact(dateText, ["MMM d, yyyy", "MMMM d, yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            var expected = match.Groups["count"].Value.ToLowerInvariant() switch
            {
                "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5,
                "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
                var number when int.TryParse(number, out var count) => count,
                _ => throw new FormatException("Unsupported configuration count.")
            };
            var vehicles = ExtractVehicles(match.Value);
            if (vehicles.Length != expected || expected is < 1 or > 12)
                throw new FormatException("NASA configuration is incomplete or uses unsupported vehicle names.");
            return new(date, vehicles);
        }
        throw new FormatException("NASA dated station configuration was not found.");
    }

    public static DockedVehicle[] ExtractVehicles(string text)
    {
        var vehicles = new List<DockedVehicle>();
        foreach (var match in Matches(text, @"Crew-(\d+)(?:\s+and\s+Crew-(\d+))?"))
            foreach (var number in match.Groups.Cast<Group>().Skip(1).Where(g => g.Success))
                vehicles.Add(new($"Crew-{number.Value} Dragon", "Posadna letjelica", "SpaceX / NASA"));
        foreach (var match in Matches(text, @"Soyuz\s+MS[- ](\d+)"))
            vehicles.Add(new($"Soyuz MS-{match.Groups[1].Value}", "Posadna letjelica", "Roscosmos"));
        foreach (var match in Matches(text, @"Progress\s+(?:MS[- ])?(\d+)(?:\s+and\s+(?:Progress\s+)?(\d+))?"))
            foreach (var number in match.Groups.Cast<Group>().Skip(1).Where(g => g.Success))
                vehicles.Add(new($"Progress {(match.Value.Contains("MS-") ? "MS-" : "")}{number.Value}", "Teretna letjelica", "Roscosmos"));
        if (text.Contains("Cygnus", StringComparison.OrdinalIgnoreCase))
            vehicles.Add(new(text.Contains("Cygnus XL", StringComparison.OrdinalIgnoreCase) ? "Cygnus XL" : "Cygnus",
                "Teretna letjelica", "Northrop Grumman / NASA"));
        foreach (var match in Matches(text, @"(?:CRS|SpaceX)[- ](\d+)(?:\s+Dragon)?"))
            vehicles.Add(new($"CRS-{match.Groups[1].Value} Dragon", "Teretna letjelica", "SpaceX / NASA"));
        return vehicles.DistinctBy(v => v.Name).ToArray();
    }

    public static CrewMember[] ExtractCrew(string text, string mission)
    {
        var result = new List<CrewMember>();
        // Agency prefixes bound each group; names must have at least two capitalized words.
        const string person = @"[A-ZÀ-Ž][a-zà-ž]+(?:[-'][A-ZÀ-Ž]?[a-zà-ž]+)?(?:\s+[A-ZÀ-Ž][a-zà-ž]+(?:[-'][A-ZÀ-Ž]?[a-zà-ž]+)?){1,3}";
        var groups = Regex.Matches(text,
            @"(?<agency>NASA|ESA|CSA|JAXA|Roscosmos)(?:\s*\([^)]*\))?\s+(?:astronauts?|cosmonauts?)\s+(?<names>" + person + @"(?:\s*,?\s*(?:and\s+)?" + person + @")*)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (Match group in groups)
        {
            var agency = group.Groups["agency"].Value;
            var country = agency switch { "NASA" => "SAD", "ESA" => "", "CSA" => "Kanada", "JAXA" => "Japan", _ => "Rusija" };
            foreach (Match name in Regex.Matches(group.Groups["names"].Value, person,
                         RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                result.Add(new(name.Value.Trim(), country, agency, mission));
        }
        return result.DistinctBy(c => c.Name).ToArray();
    }

    public static (StationSnapshot Snapshot, string Kind) ApplyArticle(StationSnapshot current, NasaArticle article)
    {
        var title = article.Title;
        var vehicles = ExtractVehicles(title);
        var departure = Matches(title, @"\b(?:undocks|undocked|departs|departed)\b").Any();
        var docking = Matches(title, @"\b(?:docks|docked|arrives|arrived|berthed|installed)\b").Any();
        var entry = Matches(title, @"\b(?:enter|enters|entered)\s+(?:the\s+)?station\b").Any();
        if (!departure && !docking && !entry) return (current, "news");
        // Require a completed action in the article, not merely a suggestive headline.
        var action = departure ? @"\b(?:undocked|departed)\b" : entry
            ? @"\b(?:have entered|entered)\s+(?:the\s+)?International Space Station\b"
            : @"\b(?:docked|arrived|was installed|was berthed|has docked)\b";
        var evidence = article.Text.Split('.').FirstOrDefault(s => Matches(s, action).Any()
            && !Matches(s, @"\b(?:will|scheduled|planned|expected|targeting|could|would)\b").Any());
        if (evidence is null) return (current with { NeedsReview = true }, "review");
        if (vehicles.Length == 0) vehicles = ExtractVehicles(evidence);
        if (vehicles.Length != 1) return (current with { NeedsReview = true }, "review");
        var vehicle = vehicles[0];
        var mission = vehicle.Name.Replace(" Dragon", "");
        if (entry)
        {
            var people = ExtractCrew(evidence, mission);
            if (people.Length is < 1 or > 4 || (mission.StartsWith("Crew-") && people.Length != 4))
                return (current with { NeedsReview = true }, "review");
            if (article.PublishedAt <= current.CrewUpdatedAt) return (current, "crew-arrival");
            var crew = current.Crew.Concat(people.Select(p => current.Crew.FirstOrDefault(c => c.Name == p.Name) ?? p))
                .DistinctBy(c => c.Name).ToArray();
            return (current with { Crew = crew, CrewUpdatedAt = article.PublishedAt, CrewSource = article.Url }, "crew-arrival");
        }
        var updatedVehicles = departure ? current.DockedVehicles.Where(v => v.Name != vehicle.Name).ToArray()
            : current.DockedVehicles.Append(vehicle).DistinctBy(v => v.Name).ToArray();
        if (departure && article.PublishedAt > current.VehiclesUpdatedAt && !current.DockedVehicles.Any(v => v.Name == vehicle.Name))
            return (current with { NeedsReview = true }, "review");
        var updated = article.PublishedAt > current.VehiclesUpdatedAt
            ? current with { DockedVehicles = updatedVehicles, VehiclesUpdatedAt = article.PublishedAt, VehicleSource = article.Url }
            : current;
        if (departure && vehicle.Purpose == "Posadna letjelica" && article.PublishedAt > current.CrewUpdatedAt)
        {
            var departing = current.Crew.Where(c => c.Mission == mission).ToArray();
            if (departing.Length == 0) return (current with { NeedsReview = true }, "review");
            updated = updated with { Crew = current.Crew.Except(departing).ToArray(), CrewUpdatedAt = article.PublishedAt, CrewSource = article.Url };
        }
        return (updated, departure ? "departure" : "arrival");
    }
}
