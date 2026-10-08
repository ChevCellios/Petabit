using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Petabit.Services;
using Xunit;

namespace Petabit.Tests;

public class StationSyncTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "petabit-station-tests", Guid.NewGuid().ToString("N"));
    private const string Overview = """
        <figcaption>Oct. 1, 2026: International Space Station Configuration. Six spaceships are attached to the space station including Crew-12 and Crew-13 Dragons, Cygnus XL, Soyuz MS-29, and Progress 95 and 96.</figcaption>
        """;
    private const string Feed = """
        <rss xmlns:content="http://purl.org/rss/1.0/modules/content/"><channel><item>
        <title>Crew-12 Dragon Undocks from Station</title><link>https://www.nasa.gov/blogs/spacestation/test/</link>
        <pubDate>Sat, 03 Oct 2026 19:19:36 +0000</pubDate>
        <content:encoded><![CDATA[<p>The Crew-12 Dragon undocked from the International Space Station at 1:05 a.m. EDT.</p>]]></content:encoded>
        </item></channel></rss>
        """;

    private StationStatusService Create(Handler handler, string? storage = null) => new(
        new Factory(handler), NullLogger<StationStatusService>.Instance,
        Options.Create(new StationSyncOptions { StoragePath = storage ?? Path.Combine(directory, "status.json") }),
        new EnvironmentStub { ContentRootPath = directory });

    [Fact]
    public async Task ValidUpdateIsPersistedRestoredAndNotReplayed()
    {
        using var service = Create(new Handler(Feed, Overview));
        await service.RefreshAsync(CancellationToken.None);
        Assert.False(service.RefreshFailed);
        Assert.False(service.PersistenceFailed);
        Assert.Equal(7, service.Current.Crew.Length);
        Assert.Equal(5, service.Current.DockedVehicles.Length);
        Assert.True(File.Exists(Path.Combine(directory, "status.json")));
        using var restarted = Create(new Handler(Feed, Overview));
        await restarted.RestoreAsync(CancellationToken.None);
        Assert.Equal(7, restarted.Current.Crew.Length);
        await restarted.RefreshAsync(CancellationToken.None);
        Assert.Single(restarted.Current.Events);
        Assert.Equal(5, restarted.Current.DockedVehicles.Length); // Older diagram cannot re-add Crew-12.
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnavailableOrChangedNasaPreservesPreviousState(bool unavailable, bool malformed)
    {
        var handler = new Handler(Feed, Overview);
        using var service = Create(handler);
        await service.RefreshAsync(CancellationToken.None);
        var previous = service.Current;
        handler.Unavailable = unavailable;
        if (malformed) handler.Overview = "<p>NASA changed its layout</p>";
        await service.RefreshAsync(CancellationToken.None);
        Assert.True(service.RefreshFailed);
        Assert.Same(previous, service.Current);
    }

    [Fact]
    public async Task InvalidSavedFileFallsBackToClearlyUnverifiedBootstrap()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "status.json"), "{broken");
        using var service = Create(new Handler(Feed, Overview));
        await service.RestoreAsync(CancellationToken.None);
        Assert.Null(service.Current.CheckedAt);
        Assert.Equal(11, service.Current.Crew.Length);
    }

    [Fact]
    public async Task StorageFailureIsReportedWithoutDiscardingSuccessfulNasaUpdate()
    {
        Directory.CreateDirectory(directory);
        var blocked = Path.Combine(directory, "blocked");
        await File.WriteAllTextAsync(blocked, "file occupying directory path");
        using var service = Create(new Handler(Feed, Overview), Path.Combine(blocked, "status.json"));
        await service.RefreshAsync(CancellationToken.None);
        Assert.True(service.PersistenceFailed);
        Assert.False(service.RefreshFailed);
        Assert.Equal(7, service.Current.Crew.Length);
    }

    [Fact]
    public async Task ConditionalRequestsReuseUnchangedNasaDocuments()
    {
        var handler = new Handler(Feed, Overview) { Conditional = true };
        using var service = Create(handler);
        await service.RefreshAsync(CancellationToken.None);
        var first = service.Current;
        await service.RefreshAsync(CancellationToken.None);
        Assert.False(service.RefreshFailed);
        Assert.Equal(2, handler.NotModifiedCount);
        Assert.Equal(first.Crew, service.Current.Crew);
        Assert.Single(service.Current.Events);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private class Handler(string feed, string overview) : HttpMessageHandler
    {
        public bool Unavailable { get; set; }
        public bool Conditional { get; set; }
        public int NotModifiedCount;
        public string Overview { get; set; } = overview;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Conditional && request.Headers.IfNoneMatch.Any())
            {
                Interlocked.Increment(ref NotModifiedCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            var response = new HttpResponseMessage(Unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri!.AbsoluteUri == StationStatusService.FeedUrl ? feed : Overview) };
            if (Conditional) response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"station-test\"");
            return Task.FromResult(response);
        }
    }
    private class EnvironmentStub : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Petabit";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
