using System.Net;
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
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.RemoveAll<IHttpClientFactory>();
                    services.AddSingleton<IHttpClientFactory>(
                        new StubHttpClientFactory(new StubHttpMessageHandler(
                            HttpStatusCode.OK,
                            """{"latitude":45.81,"longitude":15.98,"velocity":27600}""")));
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

    [Fact]
    public async Task IssDataReturnsJsonWhenUpstreamSucceeds()
    {
        using var client = CreateClientWithIssResponse(
            HttpStatusCode.OK,
            """{"latitude":45.81,"longitude":15.98,"velocity":27600}""");

        var response = await client.GetAsync("/Home/Data?test=success");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"latitude\":45.81", body);
        Assert.Contains("\"longitude\":15.98", body);
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
        var handler = new CountingHttpMessageHandler(
            HttpStatusCode.OK,
            """{"latitude":45.81,"longitude":15.98,"velocity":27600}""");
        using var client = CreateClientWithHandler(handler);

        using var firstResponse = await client.GetAsync("/Home/Data?test=output-cache");
        using var secondResponse = await client.GetAsync("/Home/Data?test=output-cache");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task IssRateLimitRejectsEleventhRequestFromSameClient()
    {
        using var client = CreateClientWithIssResponse(
            HttpStatusCode.OK,
            """{"latitude":45.81,"longitude":15.98,"velocity":27600}""");

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var response = await client.GetAsync("/Home/Data?test=rate-limit");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejectedResponse = await client.GetAsync("/Home/Data?test=rate-limit");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
    }

    private HttpClient CreateClientWithIssResponse(HttpStatusCode statusCode, string content)
        => CreateClientWithHandler(new StubHttpMessageHandler(statusCode, content));

    private HttpClient CreateClientWithHandler(HttpMessageHandler handler)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
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
