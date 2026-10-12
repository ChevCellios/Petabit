(() => {
    'use strict';

    const root = document.documentElement;

    function applyTheme(theme) {
        root.dataset.theme = theme;
        document.body?.classList.toggle('dark-mode', theme === 'dark');
        document.body?.classList.toggle('light-mode', theme === 'light');
        document.getElementById('toggle-dark-mode')?.setAttribute('aria-pressed', String(theme === 'dark'));
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#09131b' : '#9faeb3');
    }

    let initialTheme = 'dark';
    try {
        if (localStorage.getItem('theme') === 'light') initialTheme = 'light';
    } catch {
        // The dark default and theme toggle also work when browser storage is unavailable.
    }
    applyTheme(initialTheme);

    document.addEventListener('DOMContentLoaded', () => applyTheme(root.dataset.theme));
    document.addEventListener('click', event => {
        if (!(event.target instanceof Element) || !event.target.closest('#toggle-dark-mode')) return;

        const theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
        applyTheme(theme);
        try {
            localStorage.setItem('theme', theme);
        } catch {
            // Keep the selected theme for this page when storage is blocked.
        }
    }, true);
})();
