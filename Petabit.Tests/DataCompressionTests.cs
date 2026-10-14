using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Petabit.Services;
using Xunit;

namespace Petabit.Tests;

public sealed class DataCompressionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("petabit-compression-test-").FullName;
    private readonly WebApplicationFactory<Program> application;
    private readonly HttpClient client;

    public DataCompressionTests(WebApplicationFactory<Program> factory)
    {
        var now = DateTimeOffset.UtcNow;
        var elements = Enumerable.Range(1, 400).Select(id => JsonSerializer.SerializeToElement(new
        {
            OBJECT_NAME = $"STARLINK-{id}", NORAD_CAT_ID = id, EPOCH = now.ToString("O", CultureInfo.InvariantCulture),
            MEAN_MOTION = 15.6, ECCENTRICITY = .0003, INCLINATION = 53.1, RA_OF_ASC_NODE = 120,
            ARG_OF_PERICENTER = 90, MEAN_ANOMALY = 270, BSTAR = .0001,
            MEAN_MOTION_DOT = .00001, MEAN_MOTION_DDOT = 0, PETABIT_STATUS = "+"
        })).ToArray();
        var snapshot = new StarlinkSnapshot(400, 400, 0, now, now, now, elements, 0, 0, true);
        Directory.CreateDirectory(Path.Combine(directory, "App_Data"));
        File.WriteAllText(Path.Combine(directory, "App_Data", "starlink-status.json"), JsonSerializer.Serialize(snapshot));
        application = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.Configure<StationSyncOptions>(options => options.Enabled = false);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<StarlinkService>();
                services.AddSingleton(new StarlinkService(new NoSourceFactory(), NullLogger<StarlinkService>.Instance,
                    new EnvironmentStub { ContentRootPath = directory }));
            });
        });
        client = application.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task PublicStarlinkDataNegotiatesCompressionAndPreservesExactJson(string encoding)
    {
        using var plainResponse = await client.GetAsync("/Home/StarlinkData");
        var plain = await plainResponse.Content.ReadAsByteArrayAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Home/StarlinkData");
        request.Headers.AcceptEncoding.ParseAdd(encoding);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(encoding, response.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var compressed = await response.Content.ReadAsByteArrayAsync();
        Assert.True(compressed.Length < plain.Length / 2);
        using var source = new MemoryStream(compressed);
        using Stream decoded = encoding == "br" ? new BrotliStream(source, CompressionMode.Decompress) : new GZipStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await decoded.CopyToAsync(output);
        Assert.Equal(plain, output.ToArray());
        using var json = JsonDocument.Parse(output.ToArray());
        Assert.Equal(400, json.RootElement.GetProperty("elements").GetArrayLength());
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("br;q=0,gzip;q=0")]
    public async Task ClientsCanDeclineCompression(string acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Home/StarlinkData");
        request.Headers.AcceptEncoding.ParseAdd(acceptEncoding);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(400, json.RootElement.GetProperty("operationalCount").GetInt32());
    }

    [Fact]
    public async Task HtmlWithAntiforgeryTokensAndNoncesIsNeverCompressed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptEncoding.ParseAdd("br,gzip");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("nonce=", html);
    }

    public void Dispose()
    {
        client.Dispose(); application.Dispose();
        var relative = Path.GetRelativePath(Path.GetTempPath(), Path.GetFullPath(directory));
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)
            || !Path.GetFileName(directory).StartsWith("petabit-compression-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected compression fixture path.");
        Directory.Delete(directory, recursive: true);
    }

    private sealed class NoSourceFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Fresh saved fixture must not request a live source.");
    }
    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Petabit";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
