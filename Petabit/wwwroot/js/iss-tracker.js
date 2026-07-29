document.addEventListener("DOMContentLoaded", function () {
    const pingButton = document.getElementById("iss-ping-button");
    const resultDiv = document.getElementById("iss-result");
    const speedDiv = document.getElementById("iss-speed");
    const astronautsDiv = document.getElementById("iss-astronauts");
    const astronautCountDiv = document.getElementById("astronaut-count");
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
            const velocity = data.velocity.toFixed(2);

            resultDiv.innerHTML = `🌍 Lokacija: ${lat}, ${lon}`;
            speedDiv.innerHTML = `🚀 Brzina: ${velocity} km/h`;

            astronautCountDiv.innerHTML = `👨‍🚀 Ukupno na ISS-u: ${data.astronautCount}`;
            astronautsDiv.innerHTML = "";

            pingSound.play();
        } catch (error) {
            resultDiv.innerHTML = "❌ Greška prilikom dohvaćanja podataka.";
            console.error("Greška:", error);
        }

        pingButton.disabled = false;
    }

    pingButton.addEventListener("click", pingISS);
});
