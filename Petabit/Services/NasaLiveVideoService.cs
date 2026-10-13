using System.Text.RegularExpressions;

namespace Petabit.Services;

public record NasaLiveVideo(string VideoId, string SourceUrl, DateTimeOffset CheckedAt);

public class NasaLiveVideoService(IHttpClientFactory clients, TimeProvider? timeProvider = null)
{
    public const string SourceUrl = "https://eol.jsc.nasa.gov/ESRS/HDEV/";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object refreshLock = new();
    private Task<NasaLiveVideo>? refresh;
    private NasaLiveVideo? cached;
    private DateTimeOffset retryAt;

    public Task<NasaLiveVideo> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (refreshLock)
        {
            var now = clock.GetUtcNow();
            if (cached is not null && now - cached.CheckedAt < TimeSpan.FromMinutes(15))
                return Task.FromResult(cached);
            if (refresh is { IsCompleted: false }) return refresh.WaitAsync(cancellationToken);
            if (now < retryAt)
                return Task.FromException<NasaLiveVideo>(new HttpRequestException("NASA live video source is cooling down after a failed request."));
            refresh = RefreshAsync();
            return refresh.WaitAsync(cancellationToken);
        }
    }

    private async Task<NasaLiveVideo> RefreshAsync()
    {
        try
        {
            // Bound the complete response body independently of any visitor's lifetime.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), clock);
            var html = await clients.CreateClient("nasa").GetStringAsync(SourceUrl, deadline.Token);
            var next = new NasaLiveVideo(ParseVideoId(html), SourceUrl, clock.GetUtcNow());
            lock (refreshLock)
            {
                cached = next;
                retryAt = default;
            }
            return next;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or FormatException
            or RegexMatchTimeoutException)
        {
            lock (refreshLock) retryAt = clock.GetUtcNow().AddSeconds(30);
            throw;
        }
    }

    public static string ParseVideoId(string html)
    {
        var match = Regex.Match(html,
            "<iframe\\b[^>]*\\bsrc=[\"']https://www\\.youtube(?:-nocookie)?\\.com/embed/([A-Za-z0-9_-]{11})(?:[?\"'])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : throw new FormatException("NASA's live camera embed was not found.");
    }
}
