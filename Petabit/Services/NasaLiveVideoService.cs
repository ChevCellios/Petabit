using System.Text.RegularExpressions;

namespace Petabit.Services;

public record NasaLiveVideo(string VideoId, string SourceUrl, DateTimeOffset CheckedAt);

public class NasaLiveVideoService(IHttpClientFactory clients)
{
    public const string SourceUrl = "https://eol.jsc.nasa.gov/ESRS/HDEV/";
    private readonly SemaphoreSlim gate = new(1, 1);
    private NasaLiveVideo? cached;

    public async Task<NasaLiveVideo> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && DateTimeOffset.UtcNow - cached.CheckedAt < TimeSpan.FromMinutes(15)) return cached;
            var html = await clients.CreateClient("nasa").GetStringAsync(SourceUrl, cancellationToken);
            cached = new(ParseVideoId(html), SourceUrl, DateTimeOffset.UtcNow);
            return cached;
        }
        finally { gate.Release(); }
    }

    public static string ParseVideoId(string html)
    {
        var match = Regex.Match(html,
            "<iframe\\b[^>]*\\bsrc=[\"']https://www\\.youtube(?:-nocookie)?\\.com/embed/([A-Za-z0-9_-]{11})(?:[?\"'])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : throw new FormatException("NASA's live camera embed was not found.");
    }
}
