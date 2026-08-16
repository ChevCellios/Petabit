using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

namespace Petabit
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            if (!builder.Environment.IsDevelopment())
            {
                builder.Logging.ClearProviders();
                builder.Logging.AddJsonConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
                    options.UseUtcTimestamp = true;
                });
            }

            // Lokalizacija
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

            builder.Services.AddControllersWithViews()
                .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
                .AddDataAnnotationsLocalization();
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.RequireHeaderSymmetry = true;

                // Railway's public ingress is the only route to the container and appends the
                // real client address to X-Forwarded-For. Its proxy addresses are dynamic and
                // no stable CIDR is published, so trust exactly one ingress hop on Railway.
                if (!string.IsNullOrWhiteSpace(builder.Configuration["RAILWAY_ENVIRONMENT_ID"]))
                {
                    options.KnownProxies.Clear();
                    options.KnownIPNetworks.Clear();
                    return;
                }

                foreach (var configuredProxy in builder.Configuration
                             .GetSection("ForwardedHeaders:KnownProxies")
                             .Get<string[]>() ?? [])
                {
                    if (!IPAddress.TryParse(configuredProxy, out var proxyAddress))
                    {
                        throw new InvalidOperationException(
                            $"Invalid trusted proxy address in ForwardedHeaders:KnownProxies: '{configuredProxy}'.");
                    }

                    options.KnownProxies.Add(proxyAddress);
                }

                foreach (var configuredNetwork in builder.Configuration
                             .GetSection("ForwardedHeaders:KnownNetworks")
                             .Get<string[]>() ?? [])
                {
                    if (!System.Net.IPNetwork.TryParse(configuredNetwork, out var network))
                    {
                        throw new InvalidOperationException(
                            $"Invalid trusted proxy network in ForwardedHeaders:KnownNetworks: '{configuredNetwork}'.");
                    }

                    options.KnownIPNetworks.Add(network);
                }
            });
            builder.Services.AddAntiforgery(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });
            builder.Services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromDays(365);
                options.IncludeSubDomains = true;
            });
            builder.Services.AddHttpClient("iss", client =>
            {
                client.BaseAddress = new Uri("https://api.wheretheiss.at/v1/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(250);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 4;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });
            builder.Services.AddHealthChecks();
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

            app.UseForwardedHeaders();
            app.UseMiddleware<RequestObservabilityMiddleware>();

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

            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false
            });
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready")
            });

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
