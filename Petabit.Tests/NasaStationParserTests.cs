using Petabit.Models;
using Petabit.Services;
using Xunit;

namespace Petabit.Tests;

public class NasaStationParserTests
{
    private static NasaArticle Article(string title, string text) => new(
        "test", title, "https://www.nasa.gov/blogs/spacestation/test/",
        StationStatus.LastVerified.AddDays(5), text);

    [Fact]
    public void ConfigurationUsesDatedCaptionInsteadOfOutdatedPageCounter()
    {
        var observation = NasaStationParser.ParseVehicles("""
            <div>TODAY: 4 spacecraft</div><figcaption>
            Oct. 1, 2026: International Space Station Configuration. Six spaceships are attached to the space station including the SpaceX Crew-12 and Crew-13 Dragons, Northrop Grumman’s Cygnus XL, the Soyuz MS-29 crew spacecraft, and the Progress 95 and 96 resupply ships.
            </figcaption>
            """);
        Assert.Equal(6, observation.Vehicles.Length);
        Assert.Contains(observation.Vehicles, v => v.Name == "Crew-13 Dragon");
        Assert.Contains(observation.Vehicles, v => v.Name == "Progress 96");
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), observation.Date);
    }

    [Fact]
    public void UnknownVehicleDoesNotProduceAnIncompleteConfiguration()
        => Assert.Throws<FormatException>(() => NasaStationParser.ParseVehicles("""
            <figcaption>Oct. 1, 2026: International Space Station Configuration. Two spaceships are attached including Crew-13 Dragon and a new spacecraft.</figcaption>
            """));

    [Theory]
    [InlineData("NASA, SpaceX Set No Earlier Than Oct. 7 for Crew-12 Departure", "Crew-12 is scheduled to undock tomorrow.")]
    [InlineData("Crew-12 Prepares to Depart", "Crew-12 will depart tomorrow.")]
    [InlineData("Crew-14 Approaching Station", "Crew-14 is scheduled to dock tomorrow.")]
    public void AnnouncementsDoNotChangeStatus(string title, string text)
    {
        var initial = StationSnapshot.Bootstrap();
        var result = NasaStationParser.ApplyArticle(initial, Article(title, text));
        Assert.Equal(initial, result.Snapshot);
        Assert.Equal("news", result.Kind);
    }

    [Fact]
    public void UndockingRemovesOnlyTheDepartingMissionAndItsCrew()
    {
        var result = NasaStationParser.ApplyArticle(StationSnapshot.Bootstrap(), Article(
            "Crew-12 Dragon Undocks from Station", "The Crew-12 Dragon undocked from the International Space Station at 1:05 a.m. EDT."));
        Assert.Equal("departure", result.Kind);
        Assert.Equal(7, result.Snapshot.Crew.Length);
        Assert.Equal(5, result.Snapshot.DockedVehicles.Length);
        Assert.DoesNotContain(result.Snapshot.Crew, c => c.Mission == "Crew-12");
        Assert.Contains(result.Snapshot.DockedVehicles, v => v.Name == "Crew-13 Dragon");
    }

    [Fact]
    public void DockingDoesNotAddCrewBeforeHatchOpening()
    {
        var result = NasaStationParser.ApplyArticle(StationSnapshot.Bootstrap(), Article(
            "Dragon Docks Bringing SpaceX Crew-14 to Station", "The Crew-14 Dragon docked to the station at 7:05 p.m. EDT."));
        Assert.Equal("arrival", result.Kind);
        Assert.Equal(11, result.Snapshot.Crew.Length);
        Assert.Equal(7, result.Snapshot.DockedVehicles.Length);
    }

    [Fact]
    public void HatchOpeningAddsTheNamedCrewWithAgencyAndMission()
    {
        var result = NasaStationParser.ApplyArticle(StationSnapshot.Bootstrap(), Article(
            "Crew-14 Members Enter Station, Join Expedition 76",
            "NASA astronauts Alice Smith and Bob Jones, CSA (Canadian Space Agency) astronaut Carol White, and Roscosmos cosmonaut Dmitry Petrov have entered the International Space Station following hatch opening."));
        Assert.Equal("crew-arrival", result.Kind);
        Assert.Equal(15, result.Snapshot.Crew.Length);
        Assert.Contains(result.Snapshot.Crew, c => c.Name == "Carol White" && c.Agency == "CSA" && c.Mission == "Crew-14");
    }

    [Fact]
    public void UnclearCompletedEventKeepsPreviousStatusAndRequestsReview()
    {
        var initial = StationSnapshot.Bootstrap();
        var result = NasaStationParser.ApplyArticle(initial, Article("Dragon Undocks from Station", "Dragon undocked from the station."));
        Assert.True(result.Snapshot.NeedsReview);
        Assert.Equal(initial.Crew, result.Snapshot.Crew);
        Assert.Equal(initial.DockedVehicles, result.Snapshot.DockedVehicles);
    }

    [Fact]
    public void FeedIgnoresHistoricalImageCaptionsAndUsesUtcPublicationTime()
    {
        var articles = NasaStationParser.ParseFeed("""
            <rss xmlns:content="http://purl.org/rss/1.0/modules/content/"><channel><item>
            <title>Crew-12 Prepares to Depart</title><link>https://www.nasa.gov/blogs/spacestation/test/</link>
            <pubDate>Sat, 03 Oct 2026 19:19:36 +0000</pubDate>
            <content:encoded><![CDATA[<figcaption>Dragon undocked last year.</figcaption><p>Departure is planned.</p>]]></content:encoded>
            </item></channel></rss>
            """);
        Assert.Equal("Departure is planned.", Assert.Single(articles).Text);
        Assert.Equal(TimeSpan.Zero, articles[0].PublishedAt.Offset);
    }

    [Fact]
    public void FeedRejectsNonNasaLinks()
        => Assert.Throws<FormatException>(() => NasaStationParser.ParseFeed("""
            <rss><channel><item><link>https://attacker.example/</link></item></channel></rss>
            """));

    [Fact]
    public void FeedRejectsExternalXmlEntities()
        => Assert.Throws<System.Xml.XmlException>(() => NasaStationParser.ParseFeed("""
            <!DOCTYPE rss [<!ENTITY external SYSTEM "file:///etc/passwd">]><rss><channel>&external;</channel></rss>
            """));

    [Fact]
    public void NewerVehicleDiagramDoesNotPreventConfirmedCrewDeparture()
    {
        var initial = StationSnapshot.Bootstrap() with { VehiclesUpdatedAt = StationStatus.LastVerified.AddDays(6) };
        var result = NasaStationParser.ApplyArticle(initial, Article("Crew-12 Dragon Undocks from Station",
            "The Crew-12 Dragon undocked from the International Space Station."));
        Assert.Equal(7, result.Snapshot.Crew.Length);
        Assert.Equal(initial.DockedVehicles, result.Snapshot.DockedVehicles);
    }
}
