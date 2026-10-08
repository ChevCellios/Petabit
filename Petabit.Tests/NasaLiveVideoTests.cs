using Petabit.Services;
using Xunit;

namespace Petabit.Tests;

public class NasaLiveVideoTests
{
    [Fact]
    public void CameraIdComesFromNasaEmbedRatherThanOtherPageLinks()
    {
        var html = """
            <a href="https://www.youtube.com/watch?v=other_video">Channel</a>
            <iframe title="NASA live camera" src="https://www.youtube.com/embed/awQzjn72bI0?autoplay=1"></iframe>
            """;
        Assert.Equal("awQzjn72bI0", NasaLiveVideoService.ParseVideoId(html));
    }

    [Theory]
    [InlineData("<iframe src='https://www.youtube.com.attacker.example/embed/awQzjn72bI0'></iframe>")]
    [InlineData("<iframe src='https://attacker.example/awQzjn72bI0'></iframe>")]
    [InlineData("<iframe src='https://www.youtube.com/embed/bad-id'></iframe>")]
    [InlineData("<a href='https://www.youtube.com/embed/awQzjn72bI0'>Not a camera</a>")]
    public void UnsupportedOrUnexpectedSourceIsRejected(string html)
        => Assert.Throws<FormatException>(() => NasaLiveVideoService.ParseVideoId(html));
}
