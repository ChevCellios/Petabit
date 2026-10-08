using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.DataProtection;
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
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 16 * 1024;
                options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
                options.Limits.MaxRequestHeaderCount = 64;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
            });
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                options.ValueCountLimit = 16;
                options.KeyLengthLimit = 128;
                options.ValueLengthLimit = 4096;
                options.MultipartBodyLengthLimit = 16 * 1024;
            });
            // All upstream addresses are fixed HTTPS sources. Do not follow redirects to other origins.
            builder.Services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(
                () => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }));

            if (!string.IsNullOrWhiteSpace(builder.Configuration["RAILWAY_ENVIRONMENT_ID"]))
            {
                var keyDirectory = new DirectoryInfo(
                    Path.Combine(Path.GetTempPath(), "petabit-data-protection-keys"));
                builder.Services.AddDataProtection()
                    .SetApplicationName("Petabit")
                    .PersistKeysToFileSystem(keyDirectory);
            }

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
                    options.RequireHeaderSymmetry = false;
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
                client.MaxResponseContentBufferSize = 64 * 1024;
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
            builder.Services.AddHttpClient("iss-health", client =>
            {
                client.BaseAddress = new Uri("https://api.wheretheiss.at/v1/");
                client.Timeout = TimeSpan.FromSeconds(3);
            });
            builder.Services.AddSingleton<IssApiHealthCheck>();
            builder.Services.AddHealthChecks()
                .AddCheck<IssApiHealthCheck>("iss-api", tags: ["ready"]);
            builder.Services.Configure<Petabit.Services.StationSyncOptions>(builder.Configuration.GetSection("StationSync"));
            builder.Services.AddSingleton<Petabit.Services.StationStatusService>();
            builder.Services.AddSingleton<Petabit.Services.NasaLiveVideoService>();
            builder.Services.AddSingleton<Petabit.Services.StarlinkService>();
            builder.Services.AddHttpClient("celestrak", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(25);
                client.MaxResponseContentBufferSize = 12_000_000;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Petabit/1.0 (+https://petabit-production.up.railway.app)");
            });
            builder.Services.AddHostedService(services => services.GetRequiredService<Petabit.Services.StationStatusService>());
            builder.Services.AddHttpClient("nasa", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.MaxResponseContentBufferSize = 2_000_000;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Petabit/1.0 (+https://petabit-production.up.railway.app)");
            });
            builder.Services.AddOutputCache();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter(
                        "application", _ => new ConcurrencyLimiterOptions { PermitLimit = 32, QueueLimit = 0 })),
                    PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown-client",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 240,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        })));
                options.AddPolicy("iss", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown-client",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            });

            builder.Services.AddSingleton(Petabit.Services.BuildVersion.FromAssembly(typeof(Program).Assembly));

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
                    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
                    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
                    context.Response.Headers["Content-Security-Policy"] =
                        "default-src 'self'; " +
                        "base-uri 'self'; " +
                        "form-action 'self'; " +
                        "frame-ancestors 'none'; " +
                        "object-src 'none'; " +
                        "worker-src 'self'; " +
                        "script-src-attr 'none'; " +
                        $"script-src 'self' 'nonce-{cspNonce}' https://www.googletagmanager.com https://www.youtube.com; " +
                        "frame-src https://www.youtube-nocookie.com; " +
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


            app.UseRouting();
            app.UseRateLimiter();
            app.UseStaticFiles();
            app.UseOutputCache();
            app.UseAuthorization();

            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false
            }).DisableRateLimiting();
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready")
            }).DisableRateLimiting();

            app.MapGet("/version", (HttpContext context, Petabit.Services.BuildVersion version) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Json(new { commitSha = version.CommitSha },
                    statusCode: version.CommitSha is null ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
            });

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
