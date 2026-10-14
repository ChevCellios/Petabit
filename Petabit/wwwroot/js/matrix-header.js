(() => {
    'use strict';

    const canvas = document.getElementById('matrixCanvas');
    const context = canvas?.getContext('2d');
    if (!context) return;

    const navbar = canvas.closest('nav.navbar') || canvas.parentElement;
    if (!navbar) return;

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    // Preserve the owner's original dense, one-pixel green Matrix rain.
    const characters = Array.from('アァイィウエカガキギクグケゲコゴサザシジスズタダチッヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモヤユヨラリルレロワンABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789');
    const fontSize = 1;
    const frameInterval = 33;
    let width = 0;
    let height = 0;
    let drops = [];
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

    function paint() {
        context.fillStyle = 'rgba(0, 0, 0, 0.05)';
        context.fillRect(0, 0, width, height);
        context.fillStyle = '#00ff00';
        context.font = `${fontSize}px monospace`;

        for (let index = 0; index < drops.length; index++) {
            const character = characters[Math.floor(Math.random() * characters.length)];
            context.fillText(character, index * fontSize, drops[index] * fontSize);
            if (drops[index] * fontSize > height || Math.random() > 0.975) drops[index] = 0;
            drops[index]++;
        }
    }

    function schedule() {
        if (frame === null && canAnimate()) frame = window.requestAnimationFrame(draw);
    }

    function draw(time) {
        frame = null;
        if (!canAnimate()) return;

        if (lastPaint === null || time - lastPaint >= frameInterval) {
            // Keep the original cadence without accumulating display-frame drift.
            lastPaint = lastPaint === null ? time : time - ((time - lastPaint) % frameInterval);
            paint();
        }
        schedule();
    }

    function restart() {
        stop();
        if (!pageActive || document.hidden || width <= 0 || height <= 0) return;
        paint();
        lastPaint = performance.now();
        schedule();
    }

    function resize() {
        stop();
        width = Math.max(0, navbar.offsetWidth);
        height = Math.max(0, navbar.offsetHeight);
        canvas.width = width;
        canvas.height = height;
        drops = Array.from({ length: Math.floor(width / fontSize) }).fill(1);
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
