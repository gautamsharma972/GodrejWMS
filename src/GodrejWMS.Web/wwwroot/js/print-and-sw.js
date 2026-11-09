// Replaces an inline onclick and an inline <script> so the Content-Security-Policy can forbid inline scripts.
document.addEventListener("click", (event) => {
    if (event.target instanceof Element && event.target.closest("[data-print]")) {
        window.print();
    }
});

if ("serviceWorker" in navigator) {
    window.addEventListener("load", () => {
        navigator.serviceWorker.register("/service-worker.js").catch((error) => {
            console.warn("Service worker registration failed:", error);
        });
    });
}
