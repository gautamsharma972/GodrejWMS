const outsideClickHandlers = new Map();
let outsideClickSequence = 0;

function isInsideSelector(event, selector) {
    const path = event.composedPath?.() ?? [];
    return path.some((node) => node instanceof Element && node.closest?.(selector));
}

document.addEventListener('click', (event) => {
    outsideClickHandlers.forEach((handler) => {
        if (isInsideSelector(event, handler.selector)) {
            return;
        }

        handler.dotNetObject.invokeMethodAsync(handler.methodName);
    });
}, true);

const VISITS_STORAGE_KEY = 'godrejWms.visits';

function readVisits() {
    try {
        return JSON.parse(window.localStorage.getItem(VISITS_STORAGE_KEY) || '{}');
    } catch {
        return {};
    }
}

window.godrejWms = {
    downloadFile: function (fileName, base64Content, contentType) {
        const link = document.createElement('a');
        link.href = `data:${contentType};base64,${base64Content}`;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    },

    trackVisit: function (route) {
        try {
            const visits = readVisits();
            visits[route] = (visits[route] || 0) + 1;
            window.localStorage.setItem(VISITS_STORAGE_KEY, JSON.stringify(visits));
        } catch {
            // Private browsing / quota exceeded / storage disabled - frequent visits is a
            // convenience feature, never worth breaking navigation over.
        }
    },

    getFrequentVisits: function (limit) {
        try {
            return Object.entries(readVisits())
                .sort((a, b) => b[1] - a[1])
                .slice(0, limit)
                .map(([route]) => route);
        } catch {
            return [];
        }
    },

    registerOutsideClick: function (selector, dotNetObject, methodName) {
        const id = `outside-click-${++outsideClickSequence}`;
        outsideClickHandlers.set(id, { selector, dotNetObject, methodName });
        return id;
    },

    unregisterOutsideClick: function (id) {
        outsideClickHandlers.delete(id);
    },

    showToast: function (message, type = 'success') {
        let host = document.querySelector('.wms-toast-host');
        if (!host) {
            host = document.createElement('div');
            host.className = 'wms-toast-host';
            host.setAttribute('aria-live', 'polite');
            host.setAttribute('aria-atomic', 'true');
            document.body.appendChild(host);
        }

        const toast = document.createElement('div');
        toast.className = `wms-toast ${type}`;
        toast.innerHTML = `<i class="bi ${type === 'danger' ? 'bi-exclamation-triangle' : 'bi-check2-circle'}" aria-hidden="true"></i><span></span>`;
        toast.querySelector('span').textContent = message;
        host.appendChild(toast);

        window.setTimeout(() => {
            toast.classList.add('leaving');
            window.setTimeout(() => toast.remove(), 180);
        }, 3200);
    }
};

function initializeBootstrapTooltips() {
    if (!window.bootstrap?.Tooltip) {
        return;
    }

    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach((element) => {
        bootstrap.Tooltip.getOrCreateInstance(element, {
            container: 'body',
            trigger: 'hover focus'
        });
    });
}

document.addEventListener('DOMContentLoaded', initializeBootstrapTooltips);
document.addEventListener('enhancedload', initializeBootstrapTooltips);
window.addEventListener('load', initializeBootstrapTooltips);

let tooltipRescanScheduled = false;
function scheduleTooltipRescan() {
    if (tooltipRescanScheduled) {
        return;
    }

    tooltipRescanScheduled = true;
    setTimeout(() => {
        tooltipRescanScheduled = false;
        initializeBootstrapTooltips();
    }, 50);
}

new MutationObserver(scheduleTooltipRescan)
    .observe(document.body, { childList: true, subtree: true });
