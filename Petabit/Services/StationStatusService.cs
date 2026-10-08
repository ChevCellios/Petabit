using System.Text.Json;
using Microsoft.Extensions.Options;
using Petabit.Models;

namespace Petabit.Services;

public class StationSyncOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 120;
    public string StoragePath { get; set; } = "App_Data/station-status.json";
}

public class StationStatusService : BackgroundService
{
    public const string FeedUrl = "https://www.nasa.gov/blogs/spacestation/feed/";
    private readonly IHttpClientFactory clients;
    private readonly ILogger<StationStatusService> logger;
    private readonly StationSyncOptions options;
    private readonly string path;
    private readonly Dictionary<string, (string Body, string? ETag, DateTimeOffset? Modified)> documents = new();
    private StationSnapshot snapshot = StationSnapshot.Bootstrap();
    public StationSnapshot Current => Volatile.Read(ref snapshot);
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public bool RefreshFailed { get; private set; }
    public bool PersistenceFailed { get; private set; }

    public StationStatusService(IHttpClientFactory clients, ILogger<StationStatusService> logger,
        IOptions<StationSyncOptions> options, IWebHostEnvironment environment)
    {
        this.clients = clients;
        this.logger = logger;
        this.options = options.Value;
        path = Path.GetFullPath(this.options.StoragePath, environment.ContentRootPath);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        await RestoreAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 60, 3600)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<StationSnapshot>(await File.ReadAllTextAsync(path, cancellationToken));
                if (saved is not null && IsValid(saved)) Volatile.Write(ref snapshot, saved);
                else logger.LogWarning("Saved station status is invalid; using the verified bootstrap.");
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Unable to restore station status; using the verified bootstrap.");
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        LastAttemptAt = DateTimeOffset.UtcNow;
        try
        {
            var client = clients.CreateClient("nasa");
            var feedTask = GetDocumentAsync(client, FeedUrl, cancellationToken);
            var overviewTask = GetDocumentAsync(client, StationStatus.SourceUrl, cancellationToken);
            await Task.WhenAll(feedTask, overviewTask);
            var articles = NasaStationParser.ParseFeed(await feedTask);
            var observation = NasaStationParser.ParseVehicles(await overviewTask);
            var next = Current;
            if (observation.Date > DateTimeOffset.UtcNow.AddDays(1)) throw new FormatException("Future NASA configuration.");
            var continuityDate = next.CheckedAt ?? StationStatus.LastVerified;
            if (articles.Length == 0) throw new FormatException("NASA RSS is empty.");
            if (articles[0].PublishedAt > continuityDate)
                next = next with { NeedsReview = true }; // RSS is a finite window; events may have been missed.
            var processed = next.ProcessedIds.ToHashSet(StringComparer.Ordinal);
            var events = next.Events.ToList();
            foreach (var article in articles.Where(a => a.PublishedAt <= DateTimeOffset.UtcNow))
            {
                if (!processed.Add(article.Id)) continue;
                var result = NasaStationParser.ApplyArticle(next, article);
                next = result.Snapshot;
                events.Add(new(article.Id, article.Title, article.Url, article.PublishedAt, result.Kind));
            }
            // A date-only diagram cannot undo a newer timestamped docking/undocking.
            if (observation.Date > next.VehiclesUpdatedAt)
            {
                next = next with { DockedVehicles = observation.Vehicles, VehiclesUpdatedAt = observation.Date, VehicleSource = StationStatus.SourceUrl };
                var missions = observation.Vehicles.Select(v => v.Name.Replace(" Dragon", "")).ToHashSet();
                if (next.Crew.Any(c => !string.IsNullOrEmpty(c.Mission) && !missions.Contains(c.Mission)))
                    next = next with { NeedsReview = true }; // A diagram alone cannot confirm who departed.
            }
            next = next with
            {
                CheckedAt = DateTimeOffset.UtcNow,
                Events = events.OrderByDescending(e => e.PublishedAt).Take(20).ToArray(),
                ProcessedIds = processed.TakeLast(200).ToArray()
            };
            if (!IsValid(next)) throw new FormatException("NASA update failed station validation.");
            // Publish only complete, validated snapshots; readers never see half an update.
            Volatile.Write(ref snapshot, next);
            RefreshFailed = false;
            await SaveAsync(next, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or FormatException
            or System.Xml.XmlException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            RefreshFailed = true;
            logger.LogWarning(error, "NASA station refresh failed; preserving the last valid state.");
        }
    }

    private async Task SaveAsync(StationSnapshot next, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next), cancellationToken);
            File.Move(path + ".tmp", path, overwrite: true);
            PersistenceFailed = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            PersistenceFailed = true;
            logger.LogWarning(error, "Station status updated in memory but could not be persisted.");
        }
    }

    private async Task<string> GetDocumentAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        (string Body, string? ETag, DateTimeOffset? Modified) cached;
        lock (documents) documents.TryGetValue(url, out cached);
        if (cached.ETag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        if (cached.Modified is not null) request.Headers.IfModifiedSince = cached.Modified;
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached.Body is not null) return cached.Body;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        // These two document requests run concurrently; protect the shared dictionary.
        lock (documents) documents[url] = (body, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified);
        return body;
    }

    private static bool IsValid(StationSnapshot state) => state.Crew is { Length: > 0 and <= 20 }
        && state.DockedVehicles is { Length: > 0 and <= 12 } && state.Events is not null && state.ProcessedIds is not null
        && state.Crew.All(c => c is not null && !string.IsNullOrWhiteSpace(c.Name) && c.Name.Length < 100)
        && state.Crew.Select(c => c.Name).Distinct().Count() == state.Crew.Length
        && state.DockedVehicles.All(v => v is not null && !string.IsNullOrWhiteSpace(v.Name))
        && state.DockedVehicles.Select(v => v.Name).Distinct().Count() == state.DockedVehicles.Length
        && NasaStationParser.IsNasaUrl(state.CrewSource) && NasaStationParser.IsNasaUrl(state.VehicleSource)
        && state.Events.All(e => e is not null && NasaStationParser.IsNasaUrl(e.SourceUrl));
}
