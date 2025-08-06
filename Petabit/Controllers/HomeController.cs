using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Petabit.Models;
using System.Diagnostics;
using System.Globalization;

namespace Petabit.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public IActionResult SetLanguage(string culture, string returnUrl = "/")
        {
            var requestCulture = new RequestCulture(culture, culture);
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(requestCulture),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
            );
            return LocalRedirect(returnUrl);
        }
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Books()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Apps()
        {
            return View();
        }
        [HttpGet]
        public IActionResult Blockchain()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Privacy()
        {
            return View();
        }
        [HttpGet]
        public IActionResult ISSTracker()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpGet]
        public async Task<IActionResult> Data()
        {
            using var httpClient = new HttpClient();
            var issUrl = "http://api.open-notify.org/iss-now.json";
            var peopleUrl = "http://api.open-notify.org/astros.json";

            var issResponse = await httpClient.GetFromJsonAsync<IssLocationResponse>(issUrl);
            var peopleResponse = await httpClient.GetFromJsonAsync<AstronautsResponse>(peopleUrl);

            // CS1503, CS8602: Use the correct properties and null checks
            var latitude = issResponse?.IssPosition?.Latitude ?? 0.0;
            var longitude = issResponse?.IssPosition?.Longitude ?? 0.0;

            var result = new
            {
                latitude = latitude,
                longitude = longitude,
                speed = 27600, // km/h (approximate)
                astronautCount = peopleResponse?.Number ?? 0
            };

            return Json(result);
    }
    }
}
