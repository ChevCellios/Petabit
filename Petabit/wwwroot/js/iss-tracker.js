document.addEventListener("DOMContentLoaded", function () {
    const pingButton = document.getElementById("iss-ping-button");
    const resultDiv = document.getElementById("iss-result");
    const speedDiv = document.getElementById("iss-speed");
    const astronautsDiv = document.getElementById("iss-astronauts");
    const astronautCountDiv = document.getElementById("astronaut-count");
    const dockedVehiclesDiv = document.getElementById("docked-vehicles");
    const sourceDiv = document.getElementById("iss-source");
    const pingSound = document.getElementById("ping-sound");

    async function pingISS() {
        pingButton.disabled = true;
        resultDiv.innerHTML = "⏳ Dohvaćam podatke...";

        try {
            const response = await fetch("/Home/Data");
            if (!response.ok) {
                throw new Error(`Request failed with status ${response.status}`);
            }
            const data = await response.json();
            const lat = data.latitude.toFixed(2);
            const lon = data.longitude.toFixed(2);
            const velocity = data.speed.toFixed(2);

            resultDiv.innerHTML = `🌍 Lokacija: ${lat}, ${lon}`;
            speedDiv.innerHTML = `🚀 Brzina: ${velocity} km/h`;

            astronautCountDiv.innerHTML = `👨‍🚀 Ukupno na ISS-u: ${data.astronautCount}`;
            astronautsDiv.innerHTML = `<h2>Posada</h2><ul>${data.astronauts
                .map(person => `<li>${person.name} — ${person.country} (${person.agency})</li>`)
                .join("")}</ul>`;
            dockedVehiclesDiv.innerHTML = `<h2>Spojeno na ISS</h2><ul>${data.dockedVehicles
                .map(vehicle => `<li>${vehicle.name} — ${vehicle.purpose} (${vehicle.operator})</li>`)
                .join("")}</ul>`;
            sourceDiv.innerHTML = `Referentno stanje: ${new Date(data.stationStatusUpdatedAt).toLocaleDateString("hr-HR")}, <a href="${data.stationStatusSource}" target="_blank" rel="noopener">NASA</a>.`;

            pingSound.play();
        } catch (error) {
            resultDiv.innerHTML = "❌ Greška prilikom dohvaćanja podataka.";
            console.error("Greška:", error);
        }

        pingButton.disabled = false;
    }

    pingButton.addEventListener("click", pingISS);
});
