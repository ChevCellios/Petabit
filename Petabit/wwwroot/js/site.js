document.addEventListener("DOMContentLoaded", () => {
    const preferenceKey = "petabit-analytics-consent";
    const consentBanner = document.getElementById("analytics-consent");
    const measurementId = document.body.dataset.analyticsId;

    const removeAnalyticsCookies = () => {
        document.cookie.split(";").forEach(cookie => {
            const name = cookie.trim().split("=")[0];
            if (name === "_ga" || name.startsWith("_ga_")) {
                document.cookie = `${name}=; Max-Age=0; Path=/; SameSite=Lax`;
            }
        });
    };

    const loadAnalytics = () => {
        if (!measurementId || document.querySelector("script[data-petabit-analytics]")) return;

        window.dataLayer = window.dataLayer || [];
        window.gtag = function () { window.dataLayer.push(arguments); };
        window.gtag("js", new Date());
        window.gtag("config", measurementId);

        const script = document.createElement("script");
        script.async = true;
        script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`;
        script.dataset.petabitAnalytics = "true";
        document.head.appendChild(script);
    };

    const preference = localStorage.getItem(preferenceKey);
    if (preference === "accepted") {
        loadAnalytics();
    } else if (preference === null && measurementId) {
        consentBanner.hidden = false;
    }

    document.getElementById("analytics-accept")?.addEventListener("click", () => {
        localStorage.setItem(preferenceKey, "accepted");
        window[`ga-disable-${measurementId}`] = false;
        consentBanner.hidden = true;
        loadAnalytics();
    });

    document.getElementById("analytics-reject")?.addEventListener("click", () => {
        localStorage.setItem(preferenceKey, "rejected");
        if (measurementId) {
            window[`ga-disable-${measurementId}`] = true;
        }
        removeAnalyticsCookies();
        consentBanner.hidden = true;
    });

    document.getElementById("manage-analytics-consent")?.addEventListener("click", () => {
        localStorage.removeItem(preferenceKey);
        consentBanner.hidden = false;
        consentBanner.scrollIntoView({ behavior: "smooth", block: "center" });
    });
});
