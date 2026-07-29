using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Petabit.Models;
using System.Diagnostics;

namespace Petabit.Controllers;

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
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });

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
    public async Task<IActionResult> Data()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            var issTask = httpClient.GetFromJsonAsync<IssLocationResponse>("https://api.wheretheiss.at/v1/satellites/25544");
            var peopleTask = httpClient.GetFromJsonAsync<AstronautsResponse>("http://api.open-notify.org/astros.json");
            await Task.WhenAll(issTask, peopleTask);

            var iss = await issTask;
            if (iss is null)
            {
                return Problem("ISS service returned no data.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var people = await peopleTask;
            return Json(new
            {
                latitude = iss.Latitude,
                longitude = iss.Longitude,
                speed = iss.Velocity,
                astronautCount = people?.People.Count(person => string.Equals(person.Craft, "ISS", StringComparison.OrdinalIgnoreCase)) ?? 0
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
