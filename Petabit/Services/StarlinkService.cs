using System.Globalization;
using System.Text.Json;

namespace Petabit.Services;

public sealed record StarlinkSnapshot(int OperationalCount, int OnOrbitCount, int PartiallyOperationalCount,
    DateTimeOffset RetrievedAt, DateTimeOffset OldestEpoch, DateTimeOffset NewestEpoch, JsonElement[] Elements,
    int NonOperationalCount = 0, int OtherCount = 0, bool StatusCoverageComplete = false);

public sealed class StarlinkService
{
    public const string ElementsUrl = "https://celestrak.org/NORAD/elements/gp.php?GROUP=starlink&FORMAT=JSON";
    public const string CatalogUrl = "https://celestrak.org/satcat/records.php?NAME=STARLINK&FORMAT=JSON";
    private readonly IHttpClientFactory clients;
    private readonly ILogger<StarlinkService> logger;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object refreshLock = new();
    private Task<StarlinkSnapshot>? refresh;
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

    public Task<StarlinkSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (refreshLock)
        {
            // A disconnected visitor must not cancel a refresh shared by every other visitor.
            // The named HttpClient bounds source downloads to 25 seconds.
            if (refresh is null || refresh.IsCompleted) refresh = RefreshAsync();
            return refresh.WaitAsync(cancellationToken);
        }
    }

    private async Task<StarlinkSnapshot> RefreshAsync()
    {
        var cancellationToken = CancellationToken.None;
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
                        if (new FileInfo(path).Length > 12_000_000)
                            throw new FormatException("Starlink cache exceeds the allowed size.");
                        var saved = JsonSerializer.Deserialize<StarlinkSnapshot>(await File.ReadAllTextAsync(path, cancellationToken));
                        if (saved is not null && ValidCache(saved))
                            snapshot = saved;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or FormatException)
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
        if (catalog.RootElement.GetArrayLength() > 50_000 || elements.RootElement.GetArrayLength() > 50_000)
            throw new FormatException("Starlink response exceeds the allowed number of records.");
        var onOrbit = new Dictionary<int, string>();
        foreach (var entry in catalog.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new FormatException("Invalid catalog entry.");
            if (!Text(entry, "OBJECT_NAME").StartsWith("STARLINK-", StringComparison.Ordinal)
                || Text(entry, "OBJECT_TYPE") != "PAY" || Text(entry, "ORBIT_CENTER") != "EA"
                || Text(entry, "ORBIT_TYPE") != "ORB" || !string.IsNullOrEmpty(Text(entry, "DECAY_DATE"))) continue;
            if (!entry.TryGetProperty("NORAD_CAT_ID", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || number <= 0)
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
            if (!entry.TryGetProperty("NORAD_CAT_ID", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number))
                throw new FormatException("Invalid orbital ID.");
            if (!onOrbit.ContainsKey(number) || !seen.Add(number)) continue;
            if (!Text(entry, "OBJECT_NAME").StartsWith("STARLINK-", StringComparison.Ordinal)
                || !DateTimeOffset.TryParse(Text(entry, "EPOCH"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var epoch)
                || epoch > retrievedAt.AddDays(1)) throw new FormatException("Invalid Starlink epoch or name.");
            foreach (var field in new[] { "MEAN_MOTION", "ECCENTRICITY", "INCLINATION", "RA_OF_ASC_NODE",
                         "ARG_OF_PERICENTER", "MEAN_ANOMALY", "BSTAR", "MEAN_MOTION_DOT", "MEAN_MOTION_DDOT" })
                if (!entry.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n))
                    throw new FormatException($"Invalid orbital field {field}.");
            if (entry.GetProperty("MEAN_MOTION").GetDouble() <= 0
                || entry.GetProperty("ECCENTRICITY").GetDouble() is < 0 or >= 1
                || entry.GetProperty("INCLINATION").GetDouble() is < 0 or > 180)
                throw new FormatException("Invalid orbital range.");
            var tagged = new Dictionary<string, JsonElement>();
            foreach (var property in entry.EnumerateObject()) tagged[property.Name] = property.Value;
            tagged["PETABIT_STATUS"] = JsonSerializer.SerializeToElement(onOrbit[number]);
            orbits.Add(JsonSerializer.SerializeToElement(tagged));
            epochs.Add(epoch);
        }
        if (orbits.Count == 0) throw new FormatException("No valid Starlink orbital elements.");
        return new(operational.Count, onOrbit.Count, onOrbit.Count(pair => pair.Value == "P"), retrievedAt,
            epochs.Min(), epochs.Max(), orbits.ToArray(), onOrbit.Count(pair => pair.Value == "-"),
            onOrbit.Count(pair => pair.Value is not ("+" or "P" or "-")), true);
    }

    private static string Text(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static bool ValidCache(StarlinkSnapshot saved)
    {
        if (saved.Elements is not { Length: > 0 and <= 50_000 }
            || saved.OnOrbitCount is <= 0 or > 50_000 || saved.OnOrbitCount < saved.Elements.Length || saved.OperationalCount < 0
            || saved.OperationalCount > saved.OnOrbitCount || saved.PartiallyOperationalCount < 0
            || saved.PartiallyOperationalCount > saved.OnOrbitCount - saved.OperationalCount
            || saved.NonOperationalCount < 0 || saved.OtherCount < 0
            || saved.NonOperationalCount > saved.OnOrbitCount || saved.OtherCount > saved.OnOrbitCount
            || (saved.StatusCoverageComplete && saved.OperationalCount + saved.PartiallyOperationalCount + saved.NonOperationalCount + saved.OtherCount != saved.OnOrbitCount)
            || saved.RetrievedAt > DateTimeOffset.UtcNow) return false;
        // Apply the same schema validation to cached elements as to an upstream response.
        var catalog = saved.Elements.Select(entry => new
        {
            OBJECT_NAME = Text(entry, "OBJECT_NAME"),
            NORAD_CAT_ID = entry.GetProperty("NORAD_CAT_ID"), OBJECT_TYPE = "PAY",
            OPS_STATUS_CODE = saved.StatusCoverageComplete ? Text(entry, "PETABIT_STATUS") : "+",
            ORBIT_CENTER = "EA", ORBIT_TYPE = "ORB", DECAY_DATE = ""
        });
        try
        {
            var validated = Parse(JsonSerializer.Serialize(catalog), JsonSerializer.Serialize(saved.Elements), saved.RetrievedAt);
            return validated.Elements.Length == saved.Elements.Length && validated.OperationalCount <= saved.OperationalCount
                && (!saved.StatusCoverageComplete || (validated.NonOperationalCount <= saved.NonOperationalCount
                    && validated.PartiallyOperationalCount <= saved.PartiallyOperationalCount && validated.OtherCount <= saved.OtherCount))
                && validated.OldestEpoch == saved.OldestEpoch
                && validated.NewestEpoch == saved.NewestEpoch;
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }
}
