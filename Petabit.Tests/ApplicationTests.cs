using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Xunit;

namespace Petabit.Tests;

public sealed class ApplicationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApplicationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.Configure<Petabit.Services.StationSyncOptions>(options => options.Enabled = false);
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.RemoveAll<IHttpClientFactory>();
                    services.AddSingleton<IHttpClientFactory>(
                        new StubHttpClientFactory(new IssPositionsHttpMessageHandler()));
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointsReturnSuccess(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TrackerWorkerAndItsChangingModulesHaveContentVersionedUrls()
    {
        var html = await _client.GetStringAsync("/");
        foreach (var name in new[] { "worker", "orbits", "clock" })
        {
            var match = Regex.Match(html, $"data-{name}-url=\"([^\"]+)\"");
            Assert.True(match.Success);
            Assert.Contains("?v=", WebUtility.HtmlDecode(match.Groups[1].Value));
        }
    }

    [Fact]
    public async Task ReadinessReturnsServiceUnavailableWhenIssApiFails()
    {
        using var client = CreateClientWithIssResponse(HttpStatusCode.ServiceUnavailable, "Unavailable");

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LivenessStaysHealthyWhenIssApiFails()
    {
        using var client = CreateClientWithIssResponse(HttpStatusCode.ServiceUnavailable, "Unavailable");

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HomePageReturnsHtmlWithSecurityHeaders()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Opener-Policy")));
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Resource-Policy")));
    }

    [Fact]
    public async Task PrivacyPageLoadsGlobalReadableContentStyles()
    {
        var page = await _client.GetStringAsync("/Home/Privacy");
        var styles = await _client.GetStringAsync("/css/content-pages.css");

        Assert.Contains("class=\"content-page privacy-page\"", page);
        Assert.Contains("/css/content-pages.css", page);
        Assert.Contains("max-width: 880px", styles);
        Assert.Contains("body.dark-mode .table", styles);
    }

    [Theory]
    [InlineData("en", "Cookie policy", "Manage analytics cookies")]
    [InlineData("hr", "Politika kolačića", "Upravljaj analitičkim kolačićima")]
    [InlineData("de", "Cookie-Richtlinie", "Analyse-Cookies verwalten")]
    public async Task PrivacyCookieDetailsUseRequestedLanguage(string culture, string heading, string manageText)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Home/Privacy");
        request.Headers.AcceptLanguage.ParseAdd(culture);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\">", body);
        Assert.Contains(heading, body);
        Assert.Contains(manageText, body);
    }

    [Fact]
    public async Task BooksPageUsesRealPublisherLinks()
    {
        var body = await _client.GetStringAsync("/Home/Books");

        Assert.DoesNotContain("example.com", body);
        Assert.Contains("informit.com/store/clean-code", body);
        Assert.Contains("store.pragprog.com/titles/tpp20", body);
    }

    [Fact]
    public async Task ContentSecurityPolicyMatchesEveryInlineScriptNonce()
    {
        var response = await _client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        var nonce = Regex.Match(policy, "script-src[^;]*'nonce-([^']+)'", RegexOptions.CultureInvariant)
            .Groups[1].Value;

        Assert.NotEmpty(nonce);
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("worker-src 'self'", policy);
        Assert.Contains("script-src-attr 'none'", policy);

        var inlineScripts = Regex.Matches(
            body,
            "<script(?![^>]*\\bsrc=)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.NotEmpty(inlineScripts);
        Assert.All(inlineScripts.Cast<Match>(), script =>
            Assert.Contains($"nonce=\"{nonce}\"", WebUtility.HtmlDecode(script.Value)));
    }

    [Fact]
    public async Task LanguageChangeRejectsMissingAntiforgeryToken()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "hr",
            ["returnUrl"] = "/"
        });

        var response = await _client.PostAsync("/Home/SetLanguage", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LanguageChangeSetsCultureAndReturnsToLocalUrl()
    {
        var token = await GetAntiforgeryTokenAsync(_client);
        using var content = CreateLanguageForm("hr", "/Home/Privacy", token);

        var response = await _client.PostAsync("/Home/SetLanguage", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Home/Privacy", response.Headers.Location?.OriginalString);

        var localizedPage = await _client.GetStringAsync("/");
        Assert.Contains("<html lang=\"hr\">", localizedPage);
    }

    [Fact]
    public async Task LanguageChangeDoesNotRedirectToExternalUrl()
    {
        var token = await GetAntiforgeryTokenAsync(_client);
        using var content = CreateLanguageForm("de", "https://attacker.example/", token);

        var response = await _client.PostAsync("/Home/SetLanguage", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ResponseIncludesAValidCorrelationId()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
        request.Headers.Add("X-Correlation-ID", "4d5f2c87-9eb4-45b6-bc53-04e44960bd8d");

        var response = await _client.SendAsync(request);
        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));

        Assert.Equal("4d5f2c879eb445b6bc5304e44960bd8d", correlationId);
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
    }

    [Fact]
    public void ForwardedHeadersOnlyTrustExplicitlyConfiguredProxies()
    {
        using var factory = new WebApplicationFactory<Program>();
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(1, options.ForwardLimit);
        Assert.True(options.RequireHeaderSymmetry);
        Assert.DoesNotContain(options.KnownProxies, address =>
            address.Equals(IPAddress.Parse("203.0.113.10")));
        Assert.DoesNotContain(options.KnownIPNetworks, network =>
            network.Contains(IPAddress.Parse("203.0.113.10")));
    }

    [Fact]
    public void RailwayDeploymentTrustsExactlyOneIngressHop()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("RAILWAY_ENVIRONMENT_ID", "test-environment"));
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(1, options.ForwardLimit);
        Assert.False(options.RequireHeaderSymmetry);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public async Task MinimalIssTrackerExplainsItsApiPurpose()
    {
        var response = await _client.GetAsync("/Home/ISSTracker");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ISS Tracker – Minimal view", body);
        Assert.Contains("/Home/Data", body);
    }

    [Theory]
    [InlineData("en", "Minimal view", "Analytics settings")]
    [InlineData("hr", "Minimalni prikaz", "Postavke analitičkih kolačića")]
    [InlineData("de", "Minimalansicht", "Analytics-Einstellungen")]
    public async Task LayoutAndMinimalIssTrackerUseRequestedLanguage(
        string culture,
        string trackerText,
        string analyticsText)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Home/ISSTracker");
        request.Headers.AcceptLanguage.ParseAdd(culture);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\">", body);
        Assert.Contains(trackerText, body);
        Assert.Contains(analyticsText, body);
    }

    [Theory]
    [InlineData("en", "ISS location", "ISS SIGNAL UNAVAILABLE")]
    [InlineData("hr", "Lokacija ISS-a", "ISS SIGNAL NIJE DOSTUPAN")]
    [InlineData("de", "ISS-Position", "ISS-SIGNAL NICHT VERFÜGBAR")]
    public async Task HomePageLocalizesLedDisplay(
        string culture,
        string locationText,
        string unavailableText)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptLanguage.ParseAdd(culture);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"data-location=\"{locationText}\"", body);
        Assert.Contains($"data-unavailable=\"{unavailableText}\"", body);
    }

    [Theory]
    [InlineData("en", "Earth hologram", "United States", "Crew spacecraft")]
    [InlineData("hr", "Hologram Zemlje", "SAD", "Posadna letjelica")]
    [InlineData("de", "Erde-Hologramm", "USA", "Bemannte Raumfähre")]
    public async Task HomePageLocalizesHologramAndStationDetails(
        string culture,
        string hologramTitle,
        string countryText,
        string purposeText)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptLanguage.ParseAdd(culture);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($">{hologramTitle}</h3>", body);
        Assert.Contains($"data-country-usa=\"{countryText}\"", body);
        Assert.Contains($"data-purpose-crewed=\"{purposeText}\"", body);
    }

    [Fact]
    public async Task IssDataReturnsJsonWhenUpstreamSucceeds()
    {
        var handler = new IssPositionsHttpMessageHandler();
        using var client = CreateClientWithHandler(handler);

        var response = await client.GetAsync("/Home/Data?test=success");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"latitude\":45.81", body);
        Assert.Contains("\"longitude\":15.98", body);
        using var document = JsonDocument.Parse(body);
        var position = document.RootElement;
        Assert.Equal(27600, position.GetProperty("speed").GetDouble());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(handler.RequestedTimestamps![1]),
            position.GetProperty("positionUpdatedAt").GetDateTimeOffset());
        var previous = position.GetProperty("previousPosition");
        Assert.Equal(45.3, previous.GetProperty("latitude").GetDouble());
        Assert.Equal(15.2, previous.GetProperty("longitude").GetDouble());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(handler.RequestedTimestamps[0]),
            previous.GetProperty("positionUpdatedAt").GetDateTimeOffset());
        Assert.Equal(30, handler.RequestedTimestamps[1] - handler.RequestedTimestamps[0]);
        var upstream = Assert.Single(handler.Requests);
        Assert.EndsWith("/satellites/25544/positions", upstream.AbsolutePath);
        Assert.Equal("kilometers", Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(upstream.Query)["units"]);
    }

    [Theory]
    [InlineData("91", "15.98", "27600", 0)]
    [InlineData("-91", "15.98", "27600", 0)]
    [InlineData("45.81", "181", "27600", 0)]
    [InlineData("45.81", "-181", "27600", 0)]
    [InlineData("1e400", "15.98", "27600", 0)]
    [InlineData("45.81", "15.98", "-1", 0)]
    [InlineData("45.81", "15.98", "1e400", 0)]
    [InlineData("45.81", "15.98", "27600", -30)]
    [InlineData("45.81", "15.98", "27600", 86400)]
    public async Task IssDataRejectsInvalidCurrentPosition(string latitude, string longitude, string velocity, long timestampOffset)
    {
        var handler = new IssPositionsHttpMessageHandler((_, current) =>
            $$"""[{"latitude":{{latitude}},"longitude":{{longitude}},"velocity":{{velocity}},"timestamp":{{current + timestampOffset}}}]""");
        using var client = CreateClientWithHandler(handler);

        using var response = await client.GetAsync("/Home/Data");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("latitude")]
    [InlineData("longitude")]
    [InlineData("velocity")]
    [InlineData("timestamp")]
    public async Task IssDataRejectsMissingCurrentPositionFields(string missingField)
    {
        var handler = new IssPositionsHttpMessageHandler((_, current) =>
        {
            var point = new Dictionary<string, object>
            {
                ["latitude"] = 45.81, ["longitude"] = 15.98, ["velocity"] = 27600, ["timestamp"] = current
            };
            point.Remove(missingField);
            return JsonSerializer.Serialize(new[] { point });
        });
        using var client = CreateClientWithHandler(handler);

        using var response = await client.GetAsync("/Home/Data");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid-coordinate")]
    [InlineData("wrong-time")]
    [InlineData("null")]
    public async Task IssDataRemainsAvailableWithoutValidPreviousPosition(string previousState)
    {
        var handler = new IssPositionsHttpMessageHandler((previous, current) =>
        {
            var previousJson = previousState switch
            {
                "missing" => "",
                "invalid-coordinate" => $$"""{"latitude":91,"longitude":15.2,"timestamp":{{previous}}},""",
                "wrong-time" => $$"""{"latitude":45.3,"longitude":15.2,"timestamp":{{previous - 30}}},""",
                _ => "null,"
            };
            return $$"""[{{previousJson}}{"latitude":45.81,"longitude":15.98,"velocity":27600,"timestamp":{{current}}}]""";
        });
        using var client = CreateClientWithHandler(handler);

        using var response = await client.GetAsync("/Home/Data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("previousPosition").ValueKind);
        Assert.Equal(45.81, document.RootElement.GetProperty("latitude").GetDouble());
    }

    [Fact]
    public async Task IssDataReturnsServiceUnavailableWhenUpstreamFails()
    {
        using var client = CreateClientWithIssResponse(HttpStatusCode.ServiceUnavailable, "Unavailable");

        var response = await client.GetAsync("/Home/Data?test=failure");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task IssDataReturnsGatewayTimeoutWhenUpstreamTimesOut()
    {
        using var client = CreateClientWithHandler(
            new ExceptionHttpMessageHandler(new TaskCanceledException("Timed out.")));

        var response = await client.GetAsync("/Home/Data?test=timeout");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task IssDataReturnsServiceUnavailableWhenCircuitIsOpen()
    {
        using var client = CreateClientWithHandler(
            new ExceptionHttpMessageHandler(new BrokenCircuitException("Circuit is open.")));

        var response = await client.GetAsync("/Home/Data?test=open-circuit");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task IssOutputCacheAvoidsDuplicateUpstreamRequests()
    {
        var handler = new IssPositionsHttpMessageHandler();
        using var client = CreateClientWithHandler(handler);

        using var firstResponse = await client.GetAsync("/Home/Data?test=output-cache");
        using var secondResponse = await client.GetAsync("/Home/Data?test=attacker-cache-buster");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task IssRateLimitRejectsEleventhRequestFromSameClient()
    {
        using var client = CreateClientWithHandler(new IssPositionsHttpMessageHandler());

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var response = await client.GetAsync("/Home/Data?test=rate-limit");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejectedResponse = await client.GetAsync("/Home/Data?test=rate-limit");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task GlobalRateLimitProtectsDynamicPagesButNotLiveness()
    {
        using var client = CreateClientWithIssResponse(
            HttpStatusCode.OK,
            """{"latitude":45.81,"longitude":15.98,"velocity":27600}""");

        for (var requestNumber = 1; requestNumber <= 240; requestNumber++)
        {
            using var response = await client.GetAsync("/Home/Privacy");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejectedResponse = await client.GetAsync("/Home/Privacy");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);

        using var healthResponse = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    [Theory]
    [InlineData("/.git/config")]
    [InlineData("/appsettings.json")]
    [InlineData("/App_Data/starlink-status.json")]
    [InlineData("/Data/starlink-bootstrap.json.gz")]
    [InlineData("/security/backup/archive.mjs")]
    public async Task PrivateRecoveryAndConfigurationFilesAreNotPublished(string route)
    {
        using var response = await _client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClientWithIssResponse(HttpStatusCode statusCode, string content)
        => CreateClientWithHandler(new StubHttpMessageHandler(statusCode, content));

    [Fact]
    public async Task StaticDownloadsAreRateLimitedWhileHealthRemainsAvailable()
    {
        using var client = CreateClientWithIssResponse(HttpStatusCode.OK, "{}");
        for (var i = 0; i < 240; i++)
        {
            using var response = await client.GetAsync("/css/content-pages.css");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await client.GetAsync("/css/content-pages.css");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task RepeatedReadinessChecksDoNotAmplifyRequestsToUpstream()
    {
        var handler = new CountingHttpMessageHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClientWithHandler(handler);
        for (var index = 0; index < 20; index++)
        {
            using var response = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ReadinessCannotBypassConcurrencyLimitsWhileLivenessRemainsAvailable()
    {
        var handler = new GatedHttpMessageHandler();
        using var client = CreateClientWithHandler(handler);
        var requests = Enumerable.Range(0, 5).Select(_ => client.GetAsync("/health/ready")).ToArray();
        try
        {
            var first = await Task.WhenAny(requests).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.TooManyRequests, (await first).StatusCode);
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            handler.Release.TrySetResult();
            var responses = await Task.WhenAll(requests);
            Assert.Equal(4, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.TooManyRequests);
            foreach (var response in responses) response.Dispose();
        }
        finally { handler.Release.TrySetResult(); }
    }

    [Fact]
    public async Task IssJsonBodyHasADeadlineEvenWhenHeadersArriveImmediately()
    {
        using var client = CreateClientWithHandler(new StalledBodyHttpMessageHandler());
        using var response = await client.GetAsync("/Home/Data").WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task HostHeaderOutsideAllowlistIsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "attacker.example";
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StructuredMetadataCannotBreakOutOfJsonScriptElement()
    {
        const string payload = "</script><script>alert('injected')</script>";
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Seo:SiteName", payload);
                builder.ConfigureServices(services => services.Configure<Petabit.Services.StationSyncOptions>(options => options.Enabled = false));
            });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var html = await client.GetStringAsync("/");
        var json = Regex.Match(html, "<script[^>]*application/ld[^>]*>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(json);
        Assert.DoesNotContain("<", json);
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(payload, parsed.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task MalformedIssDataFailsGracefully()
    {
        using var client = CreateClientWithIssResponse(HttpStatusCode.OK, "{malformed");
        using var response = await client.GetAsync("/Home/Data");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SaturatedApplicationRejectsWorkButKeepsLivenessAvailable()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<Petabit.Services.StationSyncOptions>(options => options.Enabled = false)));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var limiter = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>().Value.GlobalLimiter!;
        var leases = new List<System.Threading.RateLimiting.RateLimitLease>();
        try
        {
            for (var i = 0; i < 32; i++)
            {
                var lease = await limiter.AcquireAsync(new Microsoft.AspNetCore.Http.DefaultHttpContext());
                Assert.True(lease.IsAcquired);
                leases.Add(lease);
            }
            using var overloaded = await client.GetAsync("/css/content-pages.css");
            Assert.Equal(HttpStatusCode.TooManyRequests, overloaded.StatusCode);
            using var health = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public async Task StationStatusRemainsAvailableWhenPositionApiFails()
    {
        using var client = CreateClientWithIssResponse(HttpStatusCode.ServiceUnavailable, "Unavailable");
        var response = await client.GetAsync("/Home/StationData");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"astronautCount\":11", body);
        Assert.Contains("Crew-13 Dragon", body);
        Assert.Contains("\"stationStatusIsStale\":true", body);
        Assert.Equal("no-store,no-cache", response.Headers.CacheControl?.ToString()?.Replace(" ", ""));
    }

    private HttpClient CreateClientWithHandler(HttpMessageHandler handler)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.Configure<Petabit.Services.StationSyncOptions>(options => options.Enabled = false);
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.RemoveAll<IHttpClientFactory>();
                    services.AddSingleton<IHttpClientFactory>(
                        new StubHttpClientFactory(handler));
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var body = await client.GetStringAsync("/");
        var match = Regex.Match(
            body,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "The antiforgery form token was not rendered.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent CreateLanguageForm(
        string culture,
        string returnUrl,
        string token) => new(new Dictionary<string, string>
        {
            ["culture"] = culture,
            ["returnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = token
        });

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.wheretheiss.at/v1/")
        };
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class ExceptionHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class GatedHttpMessageHandler : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Release.Task;
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class StalledBodyHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledBodyStream()) });
    }

    private sealed class StalledBodyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class IssPositionsHttpMessageHandler(Func<long, long, string>? contentFactory = null) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public long[]? RequestedTimestamps { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            var content = """{"latitude":45.81,"longitude":15.98,"velocity":27600}""";
            if (uri.AbsolutePath.EndsWith("/positions", StringComparison.Ordinal))
            {
                RequestedTimestamps = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)["timestamps"]
                    .ToString().Split(',').Select(long.Parse).ToArray();
                var previous = RequestedTimestamps[0];
                var current = RequestedTimestamps[1];
                // Return current first to ensure the controller uses source times rather than array order.
                content = contentFactory?.Invoke(previous, current)
                    ?? $$"""[{"latitude":45.81,"longitude":15.98,"velocity":27600,"timestamp":{{current}}},{"latitude":45.3,"longitude":15.2,"velocity":27600,"timestamp":{{previous}}}]""";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }

    private sealed class CountingHttpMessageHandler(HttpStatusCode statusCode, string content)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
        }
    }
}
