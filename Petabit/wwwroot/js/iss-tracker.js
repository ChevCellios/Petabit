document.addEventListener("DOMContentLoaded", function () {
    const pingButton = document.getElementById("iss-ping-button");
    const resultDiv = document.getElementById("iss-result");
    const speedDiv = document.getElementById("iss-speed");
    const astronautsDiv = document.getElementById("iss-astronauts");
    const astronautCountDiv = document.getElementById("astronaut-count");
    const dockedVehiclesDiv = document.getElementById("docked-vehicles");
    const sourceDiv = document.getElementById("iss-source");
    const pingSound = document.getElementById("ping-sound");

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
        resultDiv.textContent = "⏳ Dohvaćam podatke...";

        try {
            const response = await fetch("/Home/Data");
            if (!response.ok) {
                throw new Error(`Request failed with status ${response.status}`);
            }
            const data = await response.json();
            const lat = data.latitude.toFixed(2);
            const lon = data.longitude.toFixed(2);
            const velocity = data.speed.toFixed(2);

            resultDiv.textContent = `🌍 Lokacija: ${lat}, ${lon}`;
            speedDiv.textContent = `🚀 Brzina: ${velocity} km/h`;

            astronautCountDiv.textContent = `👨‍🚀 Ukupno na ISS-u: ${data.astronautCount}`;
            renderList(astronautsDiv, "Posada", data.astronauts,
                person => `${person.name} — ${person.country} (${person.agency})`);
            renderList(dockedVehiclesDiv, "Spojeno na ISS", data.dockedVehicles,
                vehicle => `${vehicle.name} — ${vehicle.purpose} (${vehicle.operator})`);

            const sourceText = document.createTextNode(
                `Referentno stanje: ${new Date(data.stationStatusUpdatedAt).toLocaleDateString("hr-HR")}, `);
            const sourceLink = document.createElement("a");
            sourceLink.href = "https://www.nasa.gov/international-space-station/space-station-visiting-vehicles/";
            sourceLink.target = "_blank";
            sourceLink.rel = "noopener noreferrer";
            sourceLink.textContent = "NASA";
            sourceDiv.replaceChildren(sourceText, sourceLink, document.createTextNode("."));

            pingSound.play();
        } catch (error) {
            resultDiv.textContent = "❌ Greška prilikom dohvaćanja podataka.";
            console.error("Greška:", error);
        }

        pingButton.disabled = false;
    }

    pingButton.addEventListener("click", pingISS);
});
