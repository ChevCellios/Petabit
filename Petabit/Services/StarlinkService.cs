using System.Globalization;
using System.Text.Json;

namespace Petabit.Services;

public sealed record StarlinkSnapshot(int OperationalCount, int OnOrbitCount, int PartiallyOperationalCount,
    DateTimeOffset RetrievedAt, DateTimeOffset OldestEpoch, DateTimeOffset NewestEpoch, JsonElement[] Elements);

public sealed class StarlinkService
{
    public const string ElementsUrl = "https://celestrak.org/NORAD/elements/gp.php?GROUP=starlink&FORMAT=JSON";
    public const string CatalogUrl = "https://celestrak.org/satcat/records.php?NAME=STARLINK&FORMAT=JSON";
    private readonly IHttpClientFactory clients;
    private readonly ILogger<StarlinkService> logger;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private StarlinkSnapshot? snapshot;
    private DateTimeOffset retryAt;
    private bool restored;
    public bool RefreshFailed { get; private set; }
    public bool PersistenceFailed { get; private set; }

    public StarlinkService(IHttpClientFactory clients, ILogger<StarlinkService> logger, IWebHostEnvironment environment)
    {
        this.clients = clients;
        this.logger = logger;
        path = Path.Combine(environment.ContentRootPath, "App_Data", "starlink-status.json");
    }

    public async Task<StarlinkSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!restored)
            {
                restored = true;
                try
                {
                    if (File.Exists(path))
                    {
                        var saved = JsonSerializer.Deserialize<StarlinkSnapshot>(await File.ReadAllTextAsync(path, cancellationToken));
                        if (saved is { OnOrbitCount: > 0, Elements.Length: > 0 } && saved.RetrievedAt <= DateTimeOffset.UtcNow)
                            snapshot = saved;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                { logger.LogWarning(error, "Unable to restore Starlink cache."); }
            }
            // CelesTrak publishes GP updates every two hours. A click must not re-download identical data.
            var now = DateTimeOffset.UtcNow;
            if (snapshot is not null && now - snapshot.RetrievedAt < TimeSpan.FromHours(2)) return snapshot;
            if (now < retryAt)
                return snapshot ?? throw new HttpRequestException("Starlink source is cooling down after a failed request.");
            // Even a partial success must not trigger a second download in the same update window.
            retryAt = now.AddHours(2);
            try
            {
                var client = clients.CreateClient("celestrak");
                var elementsTask = client.GetStringAsync(ElementsUrl, cancellationToken);
                var catalogTask = client.GetStringAsync(CatalogUrl, cancellationToken);
                await Task.WhenAll(elementsTask, catalogTask);
                snapshot = Parse(await catalogTask, await elementsTask, DateTimeOffset.UtcNow);
                RefreshFailed = false;
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or FormatException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                RefreshFailed = true;
                logger.LogWarning(error, "Starlink refresh failed; preserving the last validated data.");
                if (snapshot is null) throw new HttpRequestException("Starlink data unavailable.", error);
                return snapshot;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(snapshot), cancellationToken);
                File.Move(path + ".tmp", path, overwrite: true);
                PersistenceFailed = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                PersistenceFailed = true;
                logger.LogWarning(error, "Unable to persist Starlink cache.");
            }
            return snapshot;
        }
        finally { gate.Release(); }
    }

    public static StarlinkSnapshot Parse(string catalogJson, string elementsJson, DateTimeOffset retrievedAt)
    {
        using var catalog = JsonDocument.Parse(catalogJson);
        using var elements = JsonDocument.Parse(elementsJson);
        if (catalog.RootElement.ValueKind != JsonValueKind.Array || elements.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("Expected CelesTrak arrays.");
        var onOrbit = new Dictionary<int, string>();
        foreach (var entry in catalog.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new FormatException("Invalid catalog entry.");
            if (!Text(entry, "OBJECT_NAME").StartsWith("STARLINK-", StringComparison.Ordinal)
                || Text(entry, "OBJECT_TYPE") != "PAY" || Text(entry, "ORBIT_CENTER") != "EA"
                || Text(entry, "ORBIT_TYPE") != "ORB" || !string.IsNullOrEmpty(Text(entry, "DECAY_DATE"))) continue;
            if (!entry.TryGetProperty("NORAD_CAT_ID", out var id) || !id.TryGetInt32(out var number) || number <= 0)
                throw new FormatException("Invalid catalog ID.");
            onOrbit[number] = Text(entry, "OPS_STATUS_CODE");
        }
        if (onOrbit.Count == 0) throw new FormatException("Empty Starlink catalog.");
        var operational = onOrbit.Where(pair => pair.Value == "+").Select(pair => pair.Key).ToHashSet();
        var seen = new HashSet<int>();
        var orbits = new List<JsonElement>();
        var epochs = new List<DateTimeOffset>();
        foreach (var entry in elements.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new FormatException("Invalid orbital entry.");
            if (!entry.TryGetProperty("NORAD_CAT_ID", out var id) || !id.TryGetInt32(out var number)
                || !operational.Contains(number) || !seen.Add(number)) continue;
            if (!Text(entry, "OBJECT_NAME").StartsWith("STARLINK-", StringComparison.Ordinal)
                || !DateTimeOffset.TryParse(Text(entry, "EPOCH"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var epoch)
                || epoch > retrievedAt.AddDays(1)) throw new FormatException("Invalid Starlink epoch or name.");
            foreach (var field in new[] { "MEAN_MOTION", "ECCENTRICITY", "INCLINATION", "RA_OF_ASC_NODE",
                         "ARG_OF_PERICENTER", "MEAN_ANOMALY", "BSTAR", "MEAN_MOTION_DOT", "MEAN_MOTION_DDOT" })
                if (!entry.TryGetProperty(field, out var value) || !value.TryGetDouble(out var n) || !double.IsFinite(n))
                    throw new FormatException($"Invalid orbital field {field}.");
            if (entry.GetProperty("MEAN_MOTION").GetDouble() <= 0
                || entry.GetProperty("ECCENTRICITY").GetDouble() is < 0 or >= 1
                || entry.GetProperty("INCLINATION").GetDouble() is < 0 or > 180)
                throw new FormatException("Invalid orbital range.");
            orbits.Add(entry.Clone());
            epochs.Add(epoch);
        }
        if (orbits.Count == 0) throw new FormatException("No operational Starlink orbital elements.");
        return new(operational.Count, onOrbit.Count, onOrbit.Count(pair => pair.Value == "P"), retrievedAt,
            epochs.Min(), epochs.Max(), orbits.ToArray());
    }

    private static string Text(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
