using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Petabit.Models;
using System.Diagnostics;

namespace Petabit.Controllers;

[AutoValidateAntiforgeryToken]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public HomeController(ILogger<HomeController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    [HttpGet]
    [EnableRateLimiting("iss")]
    [OutputCache(Duration = 10)]
    public async Task<IActionResult> Data(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            var iss = await httpClient.GetFromJsonAsync<IssLocationResponse>(
                "https://api.wheretheiss.at/v1/satellites/25544",
                cancellationToken);
            if (iss is null)
            {
                return Problem("ISS service returned no data.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Json(new
            {
                latitude = iss.Latitude,
                longitude = iss.Longitude,
                speed = iss.Velocity,
                astronautCount = StationStatus.Crew.Count,
                astronauts = StationStatus.Crew,
                dockedVehicles = StationStatus.DockedVehicles,
                stationStatusUpdatedAt = StationStatus.LastVerified,
                stationStatusSource = StationStatus.SourceUrl
            });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Unable to retrieve ISS data.");
            return Problem("ISS data is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException exception)
        {
            _logger.LogWarning(exception, "ISS data request timed out.");
            return Problem("ISS data request timed out.", statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}
