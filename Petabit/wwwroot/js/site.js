document.addEventListener("DOMContentLoaded", () => {
    const cultureSelector = document.getElementById("culture-selector");
    cultureSelector?.addEventListener("change", () => cultureSelector.form?.requestSubmit());

    const preferenceKey = "petabit-analytics-consent";
    const consentBanner = document.getElementById("analytics-consent");
    const storageStatus = document.getElementById("analytics-storage-status");
    const measurementId = document.body.dataset.analyticsId;
    let storageAvailable = true;
    let preference = null;
    let consentOpener;
    const closeConsent = () => {
        if (consentBanner) consentBanner.hidden = true;
        if (consentOpener?.isConnected) consentOpener.focus();
        else {
            const main = document.querySelector('main');
            main?.setAttribute('tabindex', '-1');
            main?.focus({ preventScroll: true });
        }
    };

    const removeAnalyticsCookies = () => {
        try {
            document.cookie.split(";").forEach(cookie => {
                const name = cookie.trim().split("=")[0];
                if (name === "_ga" || name.startsWith("_ga_")) {
                    document.cookie = `${name}=; Max-Age=0; Path=/; SameSite=Lax`;
                }
            });
        } catch {
            // Tracking stays disabled even if the browser also blocks cookie access.
        }
    };

    const disableAnalytics = () => {
        if (measurementId) window[`ga-disable-${measurementId}`] = true;
        removeAnalyticsCookies();
    };
    const storageUnavailable = () => {
        storageAvailable = false;
        disableAnalytics();
        if (storageStatus) storageStatus.hidden = false;
    };
    const savePreference = value => {
        try {
            localStorage.setItem(preferenceKey, value);
            storageAvailable = true;
            if (storageStatus) storageStatus.hidden = true;
            return true;
        } catch {
            storageUnavailable();
            return false;
        }
    };

    const loadAnalytics = () => {
        if (!storageAvailable || preference !== "accepted" || !measurementId || document.querySelector("script[data-petabit-analytics]")) return;

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

    try {
        const saved = localStorage.getItem(preferenceKey);
        preference = saved === "accepted" || saved === "rejected" ? saved : null;
    } catch {
        storageUnavailable();
    }
    if (preference === "accepted") {
        if (measurementId) window[`ga-disable-${measurementId}`] = false;
        loadAnalytics();
    } else {
        disableAnalytics();
        if (preference === null && measurementId && consentBanner) consentBanner.hidden = false;
    }

    document.getElementById("analytics-accept")?.addEventListener("click", () => {
        if (!savePreference("accepted")) return;
        preference = "accepted";
        if (measurementId) window[`ga-disable-${measurementId}`] = false;
        closeConsent();
        loadAnalytics();
    });

    document.getElementById("analytics-reject")?.addEventListener("click", () => {
        disableAnalytics();
        savePreference("rejected");
        preference = "rejected";
        closeConsent();
    });

    document.querySelectorAll("#manage-analytics-consent, [data-manage-analytics]").forEach(control => {
        control.addEventListener("click", () => {
            consentOpener = control;
            try {
                localStorage.removeItem(preferenceKey);
                storageAvailable = true;
                if (storageStatus) storageStatus.hidden = true;
            } catch {
                storageUnavailable();
            }
            if (!consentBanner) return;
            consentBanner.hidden = false;
            consentBanner.focus({ preventScroll: true });
            const reducedMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
            consentBanner.scrollIntoView({ behavior: reducedMotion ? "auto" : "smooth", block: "center" });
        });
    });
});
