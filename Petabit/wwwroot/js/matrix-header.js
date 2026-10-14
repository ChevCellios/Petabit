(() => {
    'use strict';

    const canvas = document.getElementById('matrixCanvas');
    const context = canvas?.getContext('2d');
    if (!context) return;

    const navbar = canvas.closest('nav.navbar') || canvas.parentElement;
    if (!navbar) return;

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const characters = Array.from('アイウエオカキクケコサシスセソタチツテトナニヌネノABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789');
    const fontSize = 10;
    const columnWidth = 12;
    const frameInterval = 1000 / 20;
    let width = 0;
    let height = 0;
    let columns = [];
    let palette = [];
    let frame = null;
    let lastPaint = null;
    let pageActive = true;

    function canAnimate() {
        return pageActive && !document.hidden && !reducedMotion.matches && width > 0 && height > 0;
    }

    function stop() {
        if (frame !== null) window.cancelAnimationFrame(frame);
        frame = null;
        lastPaint = null;
    }

    function paint(elapsed, time) {
        context.clearRect(0, 0, width, height);
        context.font = `${fontSize}px monospace`;
        context.textBaseline = 'top';

        for (let index = 0; index < columns.length; index++) {
            const column = columns[index];
            column.y += elapsed * column.speed;
            if (column.y > height + fontSize * 6) column.y = -fontSize;

            for (let trail = 0; trail < 6; trail++) {
                const y = column.y - trail * fontSize;
                if (y < -fontSize || y > height) continue;
                const alpha = (trail === 0 ? 0.54 : 0.28 * (1 - trail / 6));
                context.fillStyle = `rgba(${palette[index % palette.length]},${alpha})`;
                const character = characters[(column.seed + trail + Math.floor(time / 200)) % characters.length];
                context.fillText(character, index * columnWidth, y);
            }
        }
    }

    function schedule() {
        if (frame === null && canAnimate()) frame = window.requestAnimationFrame(draw);
    }

    function draw(time) {
        frame = null;
        if (!canAnimate()) return;

        if (lastPaint === null || time - lastPaint >= frameInterval) {
            const elapsed = lastPaint === null ? 0 : Math.min(time - lastPaint, 150);
            lastPaint = time;
            paint(elapsed, time);
        }
        schedule();
    }

    function restart() {
        stop();
        if (!pageActive || document.hidden || width <= 0 || height <= 0) return;
        const time = performance.now();
        paint(0, time);
        lastPaint = time;
        schedule();
    }

    function resize() {
        stop();
        const bounds = navbar.getBoundingClientRect();
        width = Math.max(0, bounds.width);
        height = Math.max(0, bounds.height);
        const ratio = Math.min(2, Math.max(1, window.devicePixelRatio || 1));
        canvas.width = Math.round(width * ratio);
        canvas.height = Math.round(height * ratio);
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        palette = document.body.classList.contains('light-mode')
            ? ['0,123,97', '115,51,166', '149,103,0']
            : ['94,224,192', '189,133,255', '255,208,117'];
        columns = Array.from({ length: Math.ceil(width / columnWidth) }, () => ({
            y: Math.random() * (height + fontSize * 6),
            speed: 0.022 + Math.random() * 0.026,
            seed: Math.floor(Math.random() * characters.length)
        }));
        restart();
    }

    window.addEventListener('resize', resize, { passive: true });
    document.addEventListener('visibilitychange', restart);
    reducedMotion.addEventListener('change', restart);
    window.addEventListener('pagehide', () => {
        pageActive = false;
        stop();
    });
    window.addEventListener('pageshow', () => {
        pageActive = true;
        resize();
    });
    document.getElementById('toggle-dark-mode')?.addEventListener('click', resize);

    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(resize).observe(navbar);
    resize();
})();
