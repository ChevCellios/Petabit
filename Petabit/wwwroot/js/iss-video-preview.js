// Loaded locally on every visit; YouTube is contacted only after a manual Ping ISS.
document.addEventListener('DOMContentLoaded', () => {
    const panel = document.getElementById('iss-video-panel');
    if (!panel) return;
    const strings = document.getElementById('iss-led-localization').dataset;
    const stage = document.getElementById('iss-video-stage');
    const status = document.getElementById('iss-video-status');
    let apiPromise;
    let player;
    let generation = 0;
    let playbackTimer;
    let loadingTimer;
    let remaining = 5000;
    let playingSince;
    let finished = false;

    function loadApi() {
        if (window.YT?.Player) return Promise.resolve(window.YT);
        if (apiPromise) return apiPromise;
        apiPromise = new Promise((resolve, reject) => {
            const timeout = window.setTimeout(() => reject(new Error('YouTube API timed out')), 15000);
            window.onYouTubeIframeAPIReady = () => {
                window.clearTimeout(timeout);
                resolve(window.YT);
            };
            const script = document.createElement('script');
            script.src = 'https://www.youtube.com/iframe_api';
            script.async = true;
            script.onerror = () => {
                window.clearTimeout(timeout);
                script.remove();
                reject(new Error('YouTube API unavailable'));
            };
            document.head.append(script);
        }).catch(error => {
            apiPromise = undefined;
            throw error;
        });
        return apiPromise;
    }
    function clearPlaybackTimer() {
        window.clearTimeout(playbackTimer);
        if (playingSince !== undefined) {
            remaining = Math.max(0, remaining - (performance.now() - playingSince));
            playingSince = undefined;
        }
    }
    function finish() {
        clearPlaybackTimer();
        window.clearTimeout(loadingTimer);
        finished = true;
        player?.pauseVideo();
        status.textContent = strings.videoEnded;
        panel.dataset.playback = 'finished';
    }
    function fail() {
        clearPlaybackTimer();
        window.clearTimeout(loadingTimer);
        finished = true;
        player?.destroy();
        player = undefined;
        stage.replaceChildren();
        status.textContent = strings.videoError;
        panel.dataset.playback = 'error';
    }
    async function start() {
        const currentGeneration = ++generation;
        clearPlaybackTimer();
        window.clearTimeout(loadingTimer);
        finished = true;
        player?.destroy();
        player = undefined;
        stage.replaceChildren();
        panel.hidden = false;
        panel.dataset.playback = 'loading';
        status.textContent = strings.videoLoading;
        remaining = 5000;
        playingSince = undefined;
        try {
            const [YT, response] = await Promise.all([loadApi(), fetch('/Home/VideoSource',
                { signal: AbortSignal.timeout(15000), cache: 'no-store' })]);
            if (!response.ok) throw new Error('NASA source unavailable');
            const source = await response.json();
            if (currentGeneration !== generation) return;
            if (!/^[A-Za-z0-9_-]{11}$/.test(source.videoId)) throw new Error('Invalid video ID');
            const iframe = document.createElement('iframe');
            iframe.title = strings.videoTitle;
            iframe.allow = 'autoplay; encrypted-media; picture-in-picture';
            // YouTube requires an embedding origin (otherwise it may return error 153).
            iframe.referrerPolicy = 'strict-origin-when-cross-origin';
            iframe.src = `https://www.youtube-nocookie.com/embed/${source.videoId}?enablejsapi=1&autoplay=1&mute=1&playsinline=1&rel=0&origin=${encodeURIComponent(location.origin)}`;
            stage.replaceChildren(iframe);
            finished = false;
            loadingTimer = window.setTimeout(() => {
                if (currentGeneration === generation && panel.dataset.playback === 'loading') fail();
            }, 25000);
            player = new YT.Player(iframe, {
                events: {
                    onReady: event => {
                        if (currentGeneration !== generation) return;
                        event.target.mute();
                        event.target.playVideo();
                    },
                    onStateChange: event => {
                        if (currentGeneration !== generation) return;
                        if (finished) {
                            if (event.data === YT.PlayerState.PLAYING) event.target.pauseVideo();
                            return;
                        }
                        clearPlaybackTimer();
                        if (event.data === YT.PlayerState.PLAYING) {
                            window.clearTimeout(loadingTimer);
                            if (remaining <= 0) { finish(); return; }
                            status.textContent = strings.videoPlaying;
                            panel.dataset.playback = 'playing';
                            playingSince = performance.now();
                            playbackTimer = window.setTimeout(finish, remaining);
                        } else if (event.data === YT.PlayerState.BUFFERING) {
                            status.textContent = strings.videoLoading;
                            panel.dataset.playback = 'buffering';
                            window.clearTimeout(loadingTimer);
                            loadingTimer = window.setTimeout(() => {
                                if (currentGeneration === generation && !finished) fail();
                            }, 25000);
                        } else if (event.data === YT.PlayerState.PAUSED) {
                            window.clearTimeout(loadingTimer);
                            status.textContent = strings.videoBlocked;
                            panel.dataset.playback = 'paused';
                        }
                    },
                    onAutoplayBlocked: () => {
                        if (currentGeneration !== generation || finished) return;
                        window.clearTimeout(loadingTimer);
                        status.textContent = strings.videoBlocked;
                        panel.dataset.playback = 'blocked';
                    },
                    onError: () => { if (currentGeneration === generation) fail(); }
                }
            });
        } catch {
            if (currentGeneration === generation) fail();
        }
    }
    window.addEventListener('iss-ping', start);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden && player && !finished) {
            clearPlaybackTimer();
            player.pauseVideo();
        }
    });
    window.addEventListener('pagehide', () => {
        ++generation;
        clearPlaybackTimer();
        window.clearTimeout(loadingTimer);
        player?.destroy();
    });
});
