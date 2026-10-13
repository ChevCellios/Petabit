using System.Net;
using Petabit.Services;
using Xunit;

namespace Petabit.Tests;

public class NasaLiveVideoTests
{
    private const string CameraHtml = "<iframe src='https://www.youtube.com/embed/awQzjn72bI0'></iframe>";

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

    [Fact]
    public async Task ConcurrentVisitorsShareOneRefreshAndReuseTheSuccessfulResult()
    {
        var handler = new VideoHandler { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new NasaLiveVideoService(new Factory(handler));
        var visitors = Enumerable.Range(0, 10).Select(_ => service.GetAsync(CancellationToken.None)).ToArray();
        Assert.Equal(1, handler.Requests);
        handler.Hold.SetResult();

        var results = await Task.WhenAll(visitors);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Same(results[0], await service.GetAsync(CancellationToken.None));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task DisconnectedVisitorDoesNotCancelTheSharedRefresh()
    {
        var handler = new VideoHandler { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new NasaLiveVideoService(new Factory(handler));
        using var cancellation = new CancellationTokenSource();
        var disconnected = service.GetAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnected);
        Assert.False(handler.RequestToken.IsCancellationRequested);

        var remainingVisitor = service.GetAsync(CancellationToken.None);
        handler.Hold.SetResult();
        Assert.Equal("awQzjn72bI0", (await remainingVisitor).VideoId);
        await service.GetAsync(CancellationToken.None);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrUnsupportedSourceHasABoundedCooldownInsteadOfRepeatingForEveryVisitor(bool unsupported)
    {
        var clock = new ManualClock();
        var handler = new VideoHandler
        {
            Hold = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Unavailable = !unsupported,
            Html = unsupported ? "NASA changed its camera embed" : CameraHtml
        };
        var service = new NasaLiveVideoService(new Factory(handler), clock);
        var visitors = Enumerable.Range(0, 10).Select(_ => service.GetAsync(CancellationToken.None)).ToArray();
        handler.Hold.SetResult();
        foreach (var visitor in visitors)
        {
            if (unsupported) await Assert.ThrowsAsync<FormatException>(() => visitor);
            else await Assert.ThrowsAsync<HttpRequestException>(() => visitor);
        }
        Assert.Equal(1, handler.Requests);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(29));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync(CancellationToken.None));
        Assert.Equal(1, handler.Requests);

        handler.Unavailable = false;
        handler.Html = CameraHtml;
        handler.Hold = null;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("awQzjn72bI0", (await service.GetAsync(CancellationToken.None)).VideoId);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task DeadlineIncludesAResponseBodyThatNeverFinishesAndFailureIsCached()
    {
        var clock = new ManualClock();
        var body = new StallingStream();
        var handler = new VideoHandler { Content = new StreamContent(body) };
        var service = new NasaLiveVideoService(new Factory(handler), clock);
        var request = service.GetAsync(CancellationToken.None);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(body.BodyToken.CanBeCanceled);

        clock.Advance(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(body.BodyToken.IsCancellationRequested);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync(CancellationToken.None));
        Assert.Equal(1, handler.Requests);
    }

    private sealed class Factory(VideoHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class VideoHandler : HttpMessageHandler
    {
        public int Requests;
        public bool Unavailable;
        public string Html = CameraHtml;
        public TaskCompletionSource? Hold;
        public HttpContent? Content;
        public CancellationToken RequestToken;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            RequestToken = cancellationToken;
            if (Hold is not null) await Hold.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(Unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = Content ?? new StringContent(Html) };
        }
    }

    private sealed class StallingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken BodyToken { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BodyToken = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> timers = [];
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
            foreach (var timer in timers.ToArray()) timer.FireIfDue();
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualClock clock;
            private readonly TimerCallback callback;
            private readonly object? state;
            private DateTimeOffset? dueAt;
            private TimeSpan period;
            private bool disposed;
            public ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                this.clock = clock; this.callback = callback; this.state = state;
                Change(dueTime, period);
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (disposed) return false;
                dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
                this.period = period;
                return true;
            }
            public void FireIfDue()
            {
                if (disposed || dueAt is null || dueAt > clock.now) return;
                dueAt = period == Timeout.InfiniteTimeSpan ? null : clock.now + period;
                callback(state);
            }
            public void Dispose() { disposed = true; dueAt = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
