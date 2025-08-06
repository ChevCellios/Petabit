using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Localization;
using System.Globalization;

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

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            // Dodaj podržane jezike
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



            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
                context.Response.Headers.Add("X-Frame-Options", "DENY");
                context.Response.Headers.Add("Referrer-Policy", "no-referrer");
                context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");

                context.Response.Headers.Add("Content-Security-Policy",
                    "default-src 'self' https: data: 'unsafe-inline'; " +
                    "script-src 'self' https: 'unsafe-inline' 'unsafe-eval'; " +
                    "style-src 'self' https: 'unsafe-inline'; " +
                    "font-src 'self' https: data:; " +
                    "img-src 'self' https: data:; " +
                    "connect-src 'self' https:; " +
                    "frame-src 'none';"
                );

                await next();
            });


            app.UseRouting();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}

