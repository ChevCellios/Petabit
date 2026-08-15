using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

namespace Petabit
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Lokalizacija
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

            builder.Services.AddControllersWithViews()
                .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
                .AddDataAnnotationsLocalization();
            builder.Services.AddHttpClient();
            builder.Services.AddOutputCache();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("iss", _ =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        "iss-endpoint",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 60,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            });

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }
            // Configure supported languages
            var supportedCultures = new[]
            {
    new CultureInfo("en"),
    new CultureInfo("hr"),
    new CultureInfo("de")
};

            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures,
                RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new CookieRequestCultureProvider(), // koristi cookie za promjene jezika
        new AcceptLanguageHeaderRequestCultureProvider() // fallback
    }
            });



            app.Use(async (context, next) =>
            {
                var cspNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                context.Items["CspNonce"] = cspNonce;

                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    context.Response.Headers["X-Frame-Options"] = "DENY";
                    context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
                    context.Response.Headers["Content-Security-Policy"] =
                        "default-src 'self'; " +
                        "base-uri 'self'; " +
                        "form-action 'self'; " +
                        "frame-ancestors 'none'; " +
                        "object-src 'none'; " +
                        $"script-src 'self' 'nonce-{cspNonce}' https://www.googletagmanager.com; " +
                        $"style-src 'self' 'nonce-{cspNonce}'; " +
                        "img-src 'self' data:; " +
                        "font-src 'self' data:; " +
                        "media-src 'self'; " +
                        "connect-src 'self' https://www.google-analytics.com https://region1.google-analytics.com;";

                    return Task.CompletedTask;
                });

                await next();
            });

            app.UseHttpsRedirection();
            app.UseStaticFiles();


            app.UseRouting();
            app.UseRateLimiter();
            app.UseOutputCache();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
