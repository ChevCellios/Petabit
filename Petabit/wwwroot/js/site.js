document.addEventListener("DOMContentLoaded", function () {
    // DARK MODE TOGGLE
    document.getElementById('toggle-light-mode')?.addEventListener('click', () => {
        document.body.classList.toggle('dark-mode');
        document.body.classList.toggle('light-mode');
    });

    // ELEMENTI
    const pingBtn = document.getElementById("iss-ping-button");
    const pingSound = document.getElementById("ping-sound");
    const issResult = document.getElementById("iss-result");
    const issAstronauts = document.getElementById("iss-astronauts");
    const issSpeedEl = document.getElementById("iss-speed");
    const astronautCount = document.getElementById("astronaut-count");

    // Lokacija
                issResult.innerHTML = `
                    <strong>🌍 Lokacija ISS-a:</strong><br>
                    📍 Latitude: ${lat.toFixed(4)} <br>
                    📍 Longitude: ${lon.toFixed(4)} <br>
                `;

                // Brzina
                if (prevLat && prevLon && prevTime) {
                    const distance = getDistanceFromLatLonInKm(prevLat, prevLon, lat, lon);
                    const timeDiff = timestamp - prevTime;
                    const speed = (distance / (timeDiff / 3600)).toFixed(2);
                    issSpeedEl.innerText = `🚀 Brzina ISS-a: ${speed} km/h`;
                }

                prevLat = lat;
                prevLon = lon;
                prevTime = timestamp;
            });

        // Astronauti
        fetch('https://api.open-notify.org/astros.json')
            .then(res => res.json())
            .then(data => {
                const issPeople = data.people.filter(p => p.craft === "ISS");
                issAstronauts.innerHTML = `
                    <strong>🧑‍🚀 Astronauti na ISS-u:</strong><br>
                    👥 Broj: ${issPeople.length} <br>
                    📋 Imena: ${issPeople.map(p => p.name).join(", ")}
                `;
                if (astronautCount) {
                    astronautCount.innerText = `🧑‍🚀 Astronauta na ISS-u: ${issPeople.length}`;
                }
            });
    

    // PING GUMB
    pingBtn?.addEventListener("click", function () {
        pingBtn.classList.add("ping-animate");
        pingSound.currentTime = 0;
        pingSound.play();
        setTimeout(() => pingBtn.classList.remove("ping-animate"), 300);

        updateISSData();
    });

    // Periodičko ažuriranje
    updateISSData();
    setInterval(updateISSData, 5000);
});


