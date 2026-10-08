document.addEventListener('DOMContentLoaded', () => {
    const root = document.getElementById('station-dashboard');
    if (!root) return;
    const strings = document.getElementById('iss-led-localization').dataset;
    const button = document.getElementById('iss-ping-button');
    const countries = { SAD: strings.countryUsa, Francuska: strings.countryFrance, Rusija: strings.countryRussia, Kanada: strings.countryCanada };
    const purposes = { 'Posadna letjelica': strings.purposeCrewed, 'Teretna letjelica': strings.purposeCargo };
    const date = value => new Date(value).toLocaleString(strings.locale, { dateStyle: 'medium', timeStyle: 'short' });
    const write = (id, text) => { document.getElementById(id).textContent = text; };
    let busy = false;
    let positionTime;
    let lastStationVersion;
    let latestPosition;
    let latestStation;
    let positionAvailable = false;
    const led = document.getElementById('iss-led-display');
    const ledMessage = document.getElementById('iss-led-message');
    function renderLed(restart = false) {
        if (led.hidden) return;
        const parts = [positionAvailable ? 'ISS LIVE' : strings.unavailable];
        if (latestPosition) parts.push(`${strings.location}: ${latestPosition.latitude.toFixed(2)}° / ${latestPosition.longitude.toFixed(2)}°`,
            `${strings.speed}: ${latestPosition.speed.toFixed(0)} KM/H`);
        if (latestStation) {
            parts.push(`${strings.astronauts}: ${latestStation.astronautCount}`,
                `${strings.crew}: ${latestStation.astronauts.map(p => `${p.name} — ${countries[p.country] ?? p.country} (${p.agency})`).join(' | ')}`,
                `${strings.docked}: ${latestStation.dockedVehicles.map(v => `${v.name} — ${purposes[v.purpose] ?? v.purpose}`).join(' | ')}`,
                latestStation.stationStatusCheckedAt ? `${strings.checked}: ${date(latestStation.stationStatusCheckedAt)}` : strings.stale,
                `${strings.source}: NASA`);
            if (latestStation.stationStatusNeedsReview) parts.push(strings.review);
            if (latestStation.stationStatusIsStale || latestStation.stationStatusRefreshFailed) parts.push(strings.stale);
        }
        ledMessage.textContent = parts.join('  •  ');
        if (restart) {
            led.style.setProperty('--led-duration', `${Math.max(20, (ledMessage.scrollWidth + led.clientWidth) / 85)}s`);
            ledMessage.classList.remove('is-scrolling');
            void ledMessage.offsetWidth;
            ledMessage.classList.add('is-scrolling');
        }
    }
    function link(url, text) {
        const a = document.createElement('a');
        try {
            const target = new URL(url);
            if (target.protocol !== 'https:' || target.hostname !== 'www.nasa.gov') return document.createTextNode(text);
            a.href = target.href;
        } catch { return document.createTextNode(text); }
        a.textContent = text;
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
        return a;
    }
    function list(id, items, makeItem) {
        document.getElementById(id).replaceChildren(...items.map(item => {
            const li = document.createElement('li');
            makeItem(li, item);
            return li;
        }));
    }
    function details(li, name, detail) {
        const strong = document.createElement('strong');
        strong.textContent = name;
        const small = document.createElement('span');
        small.textContent = detail;
        li.append(strong, small);
    }
    function renderStation(data) {
        write('metric-crew', data.astronautCount);
        write('metric-vehicles', data.dockedVehicles.length);
        write('station-freshness', data.stationStatusCheckedAt
            ? `${strings.checked}: ${date(data.stationStatusCheckedAt)} · NASA`
            : strings.stale);
        const warning = document.getElementById('station-warning');
        warning.textContent = [data.stationStatusIsStale || data.stationStatusRefreshFailed ? strings.stale : '',
            data.stationStatusNeedsReview ? strings.review : '', data.stationStatusPersistenceFailed ? strings.persistence : ''].filter(Boolean).join(' ');
        warning.hidden = !warning.textContent;
        const version = JSON.stringify([data.astronauts, data.dockedVehicles, data.events, data.crewUpdatedAt, data.vehiclesUpdatedAt]);
        if (version === lastStationVersion) return;
        lastStationVersion = version;
        list('station-crew', data.astronauts, (li, person) => details(li, person.name,
            [countries[person.country] ?? person.country, person.agency, person.mission].filter(Boolean).join(' · ')));
        list('station-vehicles', data.dockedVehicles, (li, vehicle) => details(li, vehicle.name,
            `${purposes[vehicle.purpose] ?? vehicle.purpose} · ${vehicle.operator}`));
        for (const [id, time, source] of [['crew-reference', data.crewUpdatedAt, data.crewSource],
            ['vehicles-reference', data.vehiclesUpdatedAt, data.stationStatusSource]]) {
            const timestamp = id === 'vehicles-reference'
                ? new Date(time).toLocaleDateString(strings.locale, { dateStyle: 'medium', timeZone: 'UTC' }) : date(time);
            document.getElementById(id).replaceChildren(document.createTextNode(`${strings.confirmed}: ${timestamp} · `), link(source, 'NASA'));
        }
        list('station-events', data.events.slice(0, 8), (li, event) => {
            const kind = { arrival: strings.arrival, departure: strings.departure, 'crew-arrival': strings.crewArrival, review: strings.review, news: strings.news }[event.kind] ?? strings.news;
            const meta = document.createElement('span');
            meta.textContent = `${date(event.publishedAt)} · ${kind}`;
            li.append(meta, link(event.sourceUrl, event.title));
        });
        if (!data.events.length) write('station-events', strings.noEvents);
    }
    async function get(url) {
        const response = await fetch(url, { cache: 'no-store', signal: AbortSignal.timeout(15000) });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }
    async function refresh(manual = false) {
        if (busy || (!manual && document.hidden)) return;
        if (manual) {
            document.querySelector('main[role="main"]').prepend(led);
            led.hidden = false;
            ledMessage.textContent = strings.loading;
            ledMessage.classList.add('is-scrolling');
            window.dispatchEvent(new Event('iss-ping'));
        }
        busy = true;
        button.disabled = true;
        button.textContent = strings.loading;
        const results = await Promise.allSettled([get('/Home/Data'), get('/Home/StationData')]);
        const position = results[0];
        const state = results[1];
        if (position.status === 'fulfilled') {
            const data = position.value;
            latestPosition = data;
            positionAvailable = true;
            positionTime = data.positionUpdatedAt;
            write('metric-position', `${data.latitude.toFixed(2)}° / ${data.longitude.toFixed(2)}°`);
            write('metric-speed', new Intl.NumberFormat(strings.locale, { maximumFractionDigits: 0 }).format(data.speed));
            write('position-freshness', `${strings.positionUpdated}: ${date(positionTime)}`);
            write('iss-result', '');
            window.dispatchEvent(new CustomEvent('iss-position', { detail: { latitude: data.latitude, longitude: data.longitude } }));
            if (manual && document.getElementById('iss-sound-enabled').checked)
                document.getElementById('ping-sound').play().catch(() => {});
        } else {
            positionAvailable = false;
            write('iss-result', strings.unavailable);
            write('position-freshness', positionTime ? `${strings.unavailable} · ${date(positionTime)}` : strings.unavailable);
            document.getElementById('iss-hologram-status').textContent = strings.unavailable;
            document.getElementById('iss-hologram-status').classList.remove('is-live');
            window.dispatchEvent(new Event('iss-position-unavailable'));
        }
        if (state.status === 'fulfilled') {
            latestStation = state.value;
            renderStation(state.value);
        }
        else {
            const warning = document.getElementById('station-warning');
            warning.textContent = strings.stale;
            warning.hidden = false;
        }
        renderLed(manual);
        button.disabled = false;
        button.textContent = strings.refresh;
        busy = false;
    }
    button.addEventListener('click', () => refresh(true));
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
    refresh();
    window.setInterval(refresh, 30000);
});
