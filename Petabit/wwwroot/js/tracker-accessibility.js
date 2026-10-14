(() => {
    let lastOpened;
    for (const name of ['iss', 'starlink']) {
        window.addEventListener(`${name}-panel-open`, () => {
            lastOpened = document.getElementById(`${name}-panel`);
            lastOpened.focus({ preventScroll: true });
        });
    }
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || event.defaultPrevented) return;
        const focusedPanel = document.activeElement?.closest('.tracker-panel, .iss-video-panel');
        const panel = focusedPanel && !focusedPanel.hidden ? focusedPanel
            : lastOpened && !lastOpened.hidden ? lastOpened : undefined;
        if (!panel) return;
        event.preventDefault();
        panel.querySelector('.tracker-close')?.click();
    });
})();
