using System.Text.Json;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Petabit.Services;
using Xunit;

namespace Petabit.Tests;

public class StarlinkTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "petabit-starlink-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-05T00:00:00Z");
    private static object Catalog(int id, string status, string decay = "", string type = "ORB") => new
    {
        OBJECT_NAME = $"STARLINK-{id}", NORAD_CAT_ID = id, OBJECT_TYPE = "PAY", OPS_STATUS_CODE = status,
        ORBIT_CENTER = "EA", ORBIT_TYPE = type, DECAY_DATE = decay
    };
    private static object Orbit(int id, double eccentricity = .0002923) => new
    {
        OBJECT_NAME = $"STARLINK-{id}", NORAD_CAT_ID = id, EPOCH = "2026-10-04T22:36:12.73824",
        MEAN_MOTION = 15.66040205, ECCENTRICITY = eccentricity, INCLINATION = 53.1448,
        RA_OF_ASC_NODE = 256.2279, ARG_OF_PERICENTER = 92.7295, MEAN_ANOMALY = 267.4056,
        BSTAR = .00047264149, MEAN_MOTION_DOT = .00049034, MEAN_MOTION_DDOT = 0
    };
    [Fact]
    public void OperationalIsStrictlyPlusNotAllActiveAndDecayedObjectsAreExcluded()
    {
        var catalog = JsonSerializer.Serialize(new[] { Catalog(100800, "+"), Catalog(2, "P"), Catalog(3, "S"),
            Catalog(4, "-"), Catalog(5, "D", "2026-10-01"), Catalog(6, "+", type: "IMP"), Catalog(7, "+") });
        var state = StarlinkService.Parse(catalog, JsonSerializer.Serialize(new[] { Orbit(100800), Orbit(2), Orbit(3), Orbit(4), Orbit(100800) }), Now);
        Assert.Equal(2, state.OperationalCount);
        Assert.Equal(5, state.OnOrbitCount);
        Assert.Equal(1, state.PartiallyOperationalCount);
        Assert.Equal(4, state.Elements.Length);
        Assert.Equal(1, state.NonOperationalCount);
        Assert.Equal(1, state.OtherCount);
        Assert.True(state.StatusCoverageComplete);
        Assert.Equal(new[] { "+", "P", "S", "-" }, state.Elements.Select(e => e.GetProperty("PETABIT_STATUS").GetString()));
        Assert.Equal(100800, state.Elements[0].GetProperty("NORAD_CAT_ID").GetInt32());
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T22:36:12.73824Z"), state.OldestEpoch);
    }
    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("{}", "[]")]
    public void EmptyOrUnexpectedResponsesAreRejected(string catalog, string elements)
        => Assert.Throws<FormatException>(() => StarlinkService.Parse(catalog, elements, Now));
    [Fact]
    public void InvalidOrbitIsRejectedRatherThanReplacingLastGoodSnapshot()
        => Assert.Throws<FormatException>(() => StarlinkService.Parse(JsonSerializer.Serialize(new[] { Catalog(1, "+") }),
            JsonSerializer.Serialize(new[] { Orbit(1, 2) }), Now));

    private StarlinkService Create(Handler handler) => new(new Factory(handler), NullLogger<StarlinkService>.Instance,
        new EnvironmentStub { ContentRootPath = directory });

    [Theory]
    [InlineData("NORAD_CAT_ID")]
    [InlineData("MEAN_MOTION")]
    [InlineData("ECCENTRICITY")]
    public void WrongNumericTypesAreRejectedAsInvalidSourceData(string field)
    {
        var orbit = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Orbit(1)))!;
        orbit[field] = "malicious-string";
        Assert.Throws<FormatException>(() => StarlinkService.Parse(
            JsonSerializer.Serialize(new[] { Catalog(1, "+") }), $"[{orbit.ToJsonString()}]", Now));
    }

    [Fact]
    public async Task DisconnectedVisitorDoesNotCancelSharedRefreshOrPoisonCooldown()
    {
        var handler = new Handler { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = Create(handler);
        using var cancellation = new CancellationTokenSource();
        var disconnected = service.GetAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnected);
        var anotherVisitor = service.GetAsync(CancellationToken.None);
        handler.Hold.SetResult();
        var result = await anotherVisitor;
        Assert.Equal(1, result.OperationalCount);
        Assert.False(service.RefreshFailed);
        Assert.Equal(2, handler.Requests);
        Assert.Same(result, await service.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CorruptPersistedElementsAreDiscardedAndReplacedFromSource()
    {
        Directory.CreateDirectory(Path.Combine(directory, "App_Data"));
        var snapshot = StarlinkService.Parse(JsonSerializer.Serialize(new[] { Catalog(1, "+") }),
            JsonSerializer.Serialize(new[] { Orbit(1) }), DateTimeOffset.UtcNow);
        var corrupt = snapshot with { Elements = [JsonSerializer.SerializeToElement(new { invalid = true })] };
        await File.WriteAllTextAsync(Path.Combine(directory, "App_Data", "starlink-status.json"), JsonSerializer.Serialize(corrupt));
        var handler = new Handler();
        Assert.Equal(1, (await Create(handler).GetAsync(CancellationToken.None)).OperationalCount);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task MixedStatusSnapshotSurvivesRestartWithoutDownloadingAgain()
    {
        Directory.CreateDirectory(Path.Combine(directory, "App_Data"));
        var snapshot = StarlinkService.Parse(JsonSerializer.Serialize(new[] { Catalog(1, "+"), Catalog(2, "-"), Catalog(3, "P"), Catalog(4, "S") }),
            JsonSerializer.Serialize(new[] { Orbit(1), Orbit(2), Orbit(3), Orbit(4) }), DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(directory, "App_Data", "starlink-status.json"), JsonSerializer.Serialize(snapshot));
        var handler = new Handler();
        var restored = await Create(handler).GetAsync(CancellationToken.None);
        Assert.True(restored.StatusCoverageComplete);
        Assert.Equal(1, restored.NonOperationalCount);
        Assert.Equal(1, restored.OtherCount);
        Assert.Equal(new[] { "+", "-", "P", "S" }, restored.Elements.Select(e => e.GetProperty("PETABIT_STATUS").GetString()));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task LegacyOperationalOnlyCachePreservesUnknownStatusCoverageUntilRefreshDue()
    {
        Directory.CreateDirectory(Path.Combine(directory, "App_Data"));
        var snapshot = new { OperationalCount = 2, OnOrbitCount = 3, PartiallyOperationalCount = 1,
            RetrievedAt = DateTimeOffset.UtcNow, OldestEpoch = DateTimeOffset.Parse("2026-10-04T22:36:12.73824Z"),
            NewestEpoch = DateTimeOffset.Parse("2026-10-04T22:36:12.73824Z"), Elements = new[] { Orbit(1) } };
        await File.WriteAllTextAsync(Path.Combine(directory, "App_Data", "starlink-status.json"), JsonSerializer.Serialize(snapshot));
        var handler = new Handler();
        var restored = await Create(handler).GetAsync(CancellationToken.None);
        Assert.False(restored.StatusCoverageComplete);
        Assert.Equal(2, restored.OperationalCount);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task RepeatedPingAndRestartReuseThePersistedTwoHourCache()
    {
        var handler = new Handler();
        var service = Create(handler);
        var first = await service.GetAsync(CancellationToken.None);
        Assert.Same(first, await service.GetAsync(CancellationToken.None));
        Assert.Equal(2, handler.Requests);
        var restarted = Create(handler);
        Assert.Equal(first.OperationalCount, (await restarted.GetAsync(CancellationToken.None)).OperationalCount);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task FailedRefreshKeepsLastGoodCountAndDoesNotRepeatedlyDownload()
    {
        Directory.CreateDirectory(Path.Combine(directory, "App_Data"));
        var snapshot = StarlinkService.Parse(JsonSerializer.Serialize(new[] { Catalog(1, "+") }),
            JsonSerializer.Serialize(new[] { Orbit(1) }), Now) with { RetrievedAt = DateTimeOffset.UtcNow.AddHours(-3) };
        await File.WriteAllTextAsync(Path.Combine(directory, "App_Data", "starlink-status.json"), JsonSerializer.Serialize(snapshot));
        var handler = new Handler { Unavailable = true };
        var service = Create(handler);
        var result = await service.GetAsync(CancellationToken.None);
        Assert.Equal(snapshot.RetrievedAt, result.RetrievedAt);
        Assert.True(service.RefreshFailed);
        Assert.Equal(1, result.OperationalCount);
        await service.GetAsync(CancellationToken.None);
        Assert.Equal(2, handler.Requests);
    }

    private async Task WriteRecoveryAsync(StarlinkSnapshot value)
    {
        Directory.CreateDirectory(Path.Combine(directory, "Data"));
        await using var file = File.Create(Path.Combine(directory, "Data", "starlink-bootstrap.json.gz"));
        await using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.SmallestSize);
        await JsonSerializer.SerializeAsync(gzip, value);
    }

    [Fact]
    public async Task NewerRecoveryReplacesLegacyCacheAndPersistsPartialPositionsDuringOutage()
    {
        Directory.CreateDirectory(Path.Combine(directory, "App_Data"));
        var legacy = new StarlinkSnapshot(1, 2, 1, DateTimeOffset.UtcNow.AddHours(-4),
            DateTimeOffset.Parse("2026-10-04T22:36:12.73824Z"), DateTimeOffset.Parse("2026-10-04T22:36:12.73824Z"),
            [JsonSerializer.SerializeToElement(Orbit(1))]);
        var recovered = StarlinkService.Parse(JsonSerializer.Serialize(new[] { Catalog(1, "+"), Catalog(2, "P") }),
            JsonSerializer.Serialize(new[] { Orbit(1), Orbit(2) }), DateTimeOffset.UtcNow.AddHours(-3));
        await File.WriteAllTextAsync(Path.Combine(directory, "App_Data", "starlink-status.json"), JsonSerializer.Serialize(legacy));
        await WriteRecoveryAsync(recovered);
        var handler = new Handler { Unavailable = true };
        var service = Create(handler);
        var state = await service.GetAsync(CancellationToken.None);
        Assert.True(state.StatusCoverageComplete);
        Assert.True(service.RefreshFailed);
        Assert.Equal(recovered.RetrievedAt, state.RetrievedAt);
        Assert.Equal("P", state.Elements[1].GetProperty("PETABIT_STATUS").GetString());
        var persisted = JsonSerializer.Deserialize<StarlinkSnapshot>(await File.ReadAllTextAsync(Path.Combine(directory, "App_Data", "starlink-status.json")))!;
        Assert.True(persisted.StatusCoverageComplete);
        Assert.Equal(recovered.RetrievedAt, persisted.RetrievedAt);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task RecoveryNeverReplacesNewerPersistedSnapshot()
    {
        Directory.CreateDirectory(Path.Combine(directory, "App_Data"));
        var current = StarlinkService.Parse(JsonSerializer.Serialize(new[] { Catalog(1, "+") }),
            JsonSerializer.Serialize(new[] { Orbit(1) }), DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(directory, "App_Data", "starlink-status.json"), JsonSerializer.Serialize(current));
        await WriteRecoveryAsync(current with { RetrievedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        var handler = new Handler();
        var state = await Create(handler).GetAsync(CancellationToken.None);
        Assert.Equal(current.RetrievedAt, state.RetrievedAt);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task ExpiredRecoveryIsIgnoredAndNormalSourceIsUsed()
    {
        var old = StarlinkService.Parse(JsonSerializer.Serialize(new[] { Catalog(1, "+") }),
            JsonSerializer.Serialize(new[] { Orbit(1) }), DateTimeOffset.UtcNow) with { RetrievedAt = DateTimeOffset.UtcNow.AddDays(-8) };
        await WriteRecoveryAsync(old);
        var handler = new Handler();
        var state = await Create(handler).GetAsync(CancellationToken.None);
        Assert.True(state.RetrievedAt > old.RetrievedAt);
        Assert.Equal(2, handler.Requests);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    private class Factory(Handler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private class Handler : HttpMessageHandler
    {
        public int Requests;
        public bool Unavailable;
        public TaskCompletionSource? Hold;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (Hold is not null) await Hold.Task.WaitAsync(cancellationToken);
            var json = request.RequestUri!.AbsoluteUri == StarlinkService.CatalogUrl
                ? JsonSerializer.Serialize(new[] { Catalog(1, "+") }) : JsonSerializer.Serialize(new[] { Orbit(1) });
            return new HttpResponseMessage(Unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent(json) };
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

