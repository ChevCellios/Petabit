document.addEventListener("DOMContentLoaded", function () {
    const pingButton = document.getElementById("iss-ping-button");
    const resultDiv = document.getElementById("iss-result");
    const speedDiv = document.getElementById("iss-speed");
    const astronautsDiv = document.getElementById("iss-astronauts");
    const astronautCountDiv = document.getElementById("astronaut-count");
    const dockedVehiclesDiv = document.getElementById("docked-vehicles");
    const sourceDiv = document.getElementById("iss-source");
    const pingSound = document.getElementById("ping-sound");
    const strings = document.getElementById("iss-localization").dataset;

    const format = (template, ...values) => values.reduce(
        (result, value, index) => result.replaceAll(`{${index}}`, value), template);
    const countries = { SAD: strings.usa, Francuska: strings.france, Rusija: strings.russia };
    const purposes = { "Posadna letjelica": strings.crewed, "Teretna letjelica": strings.cargo };

    function renderList(container, title, items, formatItem) {
        const heading = document.createElement("h2");
        heading.textContent = title;
        const list = document.createElement("ul");

        items.forEach(item => {
            const listItem = document.createElement("li");
            listItem.textContent = formatItem(item);
            list.appendChild(listItem);
        });

        container.replaceChildren(heading, list);
    }

    async function pingISS() {
        pingButton.disabled = true;
        resultDiv.textContent = `⏳ ${strings.loading}`;

        try {
            const response = await fetch("/Home/Data");
            if (!response.ok) {
                throw new Error(`Request failed with status ${response.status}`);
            }
            const data = await response.json();
            const lat = data.latitude.toFixed(2);
            const lon = data.longitude.toFixed(2);
            const velocity = data.speed.toFixed(2);

            resultDiv.textContent = `🌍 ${format(strings.location, lat, lon)}`;
            speedDiv.textContent = `🚀 ${format(strings.speed, velocity)}`;

            astronautCountDiv.textContent = `👨‍🚀 ${format(strings.count, data.astronautCount)}`;
            renderList(astronautsDiv, strings.crew, data.astronauts,
                person => `${person.name} — ${countries[person.country] ?? person.country} (${person.agency})`);
            renderList(dockedVehiclesDiv, strings.docked, data.dockedVehicles,
                vehicle => `${vehicle.name} — ${purposes[vehicle.purpose] ?? vehicle.purpose} (${vehicle.operator})`);

            const sourceText = document.createTextNode(
                `${format(strings.reference, new Date(data.stationStatusUpdatedAt).toLocaleDateString(document.documentElement.lang))}, `);
            const sourceLink = document.createElement("a");
            sourceLink.href = "https://www.nasa.gov/international-space-station/space-station-visiting-vehicles/";
            sourceLink.target = "_blank";
            sourceLink.rel = "noopener noreferrer";
            sourceLink.textContent = "NASA";
            sourceDiv.replaceChildren(sourceText, sourceLink, document.createTextNode("."));

            pingSound.play();
        } catch (error) {
            resultDiv.textContent = `❌ ${strings.error}`;
            console.error("Greška:", error);
        }

        pingButton.disabled = false;
    }

    pingButton.addEventListener("click", pingISS);
});
