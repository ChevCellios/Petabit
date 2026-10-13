using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Petabit.Models;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System.Diagnostics;

namespace Petabit.Controllers;

[AutoValidateAntiforgeryToken]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Petabit.Services.StationStatusService _station;

    public HomeController(ILogger<HomeController> logger, IHttpClientFactory httpClientFactory,
        Petabit.Services.StationStatusService station)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _station = station;
    }

    [HttpPost]
    public IActionResult SetLanguage(string culture, string returnUrl = "/")
    {
        var supportedCultures = new[] { "en", "hr", "de" };
        if (!supportedCultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest();
        }

        var requestCulture = new RequestCulture(culture, culture);
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(requestCulture),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax
            });

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Books() => View();

    [HttpGet]
    public IActionResult Apps() => View();

    [HttpGet]
    public IActionResult Blockchain() => View();

    [HttpGet]
    public IActionResult Privacy() => View();

    [HttpGet]
    public IActionResult ISSTracker() => View();

    [HttpGet]
    [EnableRateLimiting("iss")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult StationData() => Json(StationPayload());

    [HttpGet]
    [EnableRateLimiting("iss")]
    public async Task<IActionResult> VideoSource([FromServices] Petabit.Services.NasaLiveVideoService video,
        CancellationToken cancellationToken)
    {
        try { return Json(await video.GetAsync(cancellationToken)); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or FormatException
            or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            _logger.LogWarning(error, "NASA live video source is temporarily unavailable.");
            return Problem("NASA live video is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private object StationPayload()
    {
        var state = _station.Current;
        return new
        {
            astronautCount = state.Crew.Length,
            astronauts = state.Crew,
            dockedVehicles = state.DockedVehicles,
            crewUpdatedAt = state.CrewUpdatedAt,
            vehiclesUpdatedAt = state.VehiclesUpdatedAt,
            stationStatusUpdatedAt = state.CrewUpdatedAt < state.VehiclesUpdatedAt ? state.CrewUpdatedAt : state.VehiclesUpdatedAt,
            stationStatusCheckedAt = state.CheckedAt,
            stationStatusSource = state.VehicleSource,
            crewSource = state.CrewSource,
            stationStatusIsStale = state.CheckedAt is null || DateTimeOffset.UtcNow - state.CheckedAt > TimeSpan.FromMinutes(10),
            stationStatusNeedsReview = state.NeedsReview,
            stationStatusRefreshFailed = _station.RefreshFailed,
            stationStatusPersistenceFailed = _station.PersistenceFailed,
            events = state.Events
        };
    }

    [HttpGet]
    [EnableRateLimiting("iss")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> StarlinkData([FromServices] Petabit.Services.StarlinkService starlink,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await starlink.GetAsync(cancellationToken);
            return Json(new
            {
                data.OperationalCount, data.OnOrbitCount, data.PartiallyOperationalCount, data.NonOperationalCount,
                data.OtherCount, data.StatusCoverageComplete, data.RetrievedAt,
                data.OldestEpoch, data.NewestEpoch, data.Elements,
                isStale = DateTimeOffset.UtcNow - data.RetrievedAt > TimeSpan.FromHours(4)
                    || DateTimeOffset.UtcNow - data.OldestEpoch > TimeSpan.FromDays(3.5),
                starlink.RefreshFailed, starlink.PersistenceFailed,
                sourceUrl = "https://celestrak.org/satcat/status.php"
            });
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            _logger.LogWarning(error, "Starlink data is temporarily unavailable.");
            return Problem("Starlink data is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    [HttpGet]
    [EnableRateLimiting("iss")]
    [OutputCache(Duration = 10, VaryByQueryKeys = new string[] { })]
    public async Task<IActionResult> Data(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("iss");

        try
        {
            // The resilience handler finishes at response headers. Bound JSON body
            // reads as well, even if the source sends headers and then stalls.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var previousTimestamp = timestamp - 30;
            var positions = await httpClient.GetFromJsonAsync<IssLocationResponse?[]>(
                $"satellites/25544/positions?timestamps={previousTimestamp},{timestamp}&units=kilometers",
                deadline.Token);
            var currentPositions = positions?.Where(position => position?.Timestamp == timestamp).ToArray();
            var iss = currentPositions is { Length: 1 } ? currentPositions[0] : null;
            if (iss is null || !HasValidCoordinates(iss) || !double.IsFinite(iss.Velocity) || iss.Velocity < 0)
            {
                return Problem("ISS service returned no valid current position.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var previousPositions = positions?.Where(position => position?.Timestamp == previousTimestamp).ToArray();
            var previous = previousPositions is { Length: 1 } ? previousPositions[0] : null;
            if (previous is not null && !HasValidCoordinates(previous)) previous = null;

            var state = _station.Current;
            return Json(new
            {
                latitude = iss.Latitude,
                longitude = iss.Longitude,
                speed = iss.Velocity,
                positionUpdatedAt = DateTimeOffset.FromUnixTimeSeconds(iss.Timestamp),
                previousPosition = previous is null ? null : new
                {
                    latitude = previous.Latitude,
                    longitude = previous.Longitude,
                    positionUpdatedAt = DateTimeOffset.FromUnixTimeSeconds(previous.Timestamp)
                },
                astronautCount = state.Crew.Length,
                astronauts = state.Crew,
                dockedVehicles = state.DockedVehicles,
                stationStatusUpdatedAt = state.CrewUpdatedAt < state.VehiclesUpdatedAt ? state.CrewUpdatedAt : state.VehiclesUpdatedAt,
                stationStatusSource = state.VehicleSource,
                stationStatusIsStale = state.CheckedAt is null || DateTimeOffset.UtcNow - state.CheckedAt > TimeSpan.FromMinutes(10)
            });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Unable to retrieve ISS data.");
            return Problem("ISS data is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (System.Text.Json.JsonException exception)
        {
            _logger.LogWarning(exception, "ISS source returned malformed data.");
            return Problem("ISS data is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(exception, "ISS data request timed out.");
            return Problem("ISS data request timed out.", statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (TimeoutRejectedException exception)
        {
            _logger.LogWarning(exception, "ISS resilience pipeline timed out.");
            return Problem("ISS data request timed out.", statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (BrokenCircuitException exception)
        {
            _logger.LogWarning(exception, "ISS circuit breaker is open.");
            return Problem("ISS data is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool HasValidCoordinates(IssLocationResponse position) =>
        double.IsFinite(position.Latitude) && position.Latitude is >= -90 and <= 90
        && double.IsFinite(position.Longitude) && position.Longitude is >= -180 and <= 180;
}
