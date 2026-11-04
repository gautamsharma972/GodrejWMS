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
const SIDEBAR_STORAGE_KEY = 'godrejWms.sidebarCollapsed';
const ACTIVE_WAREHOUSE_STORAGE_KEY = 'godrejWms.activeWarehouseId';

const idleTimerEvents = ['mousedown', 'mousemove', 'keydown', 'wheel', 'touchstart', 'scroll'];
let idleTimerHandle = null;
let idleTimerReset = null;

function readVisits() {
    try {
        return JSON.parse(window.localStorage.getItem(VISITS_STORAGE_KEY) || '{}');
    } catch {
        return {};
    }
}

window.godrejWms = {
    getSidebarCollapsed: function () {
        try {
            const value = window.localStorage.getItem(SIDEBAR_STORAGE_KEY);
            return value === null ? null : value === 'true';
        } catch {
            return null;
        }
    },

    setSidebarCollapsed: function (collapsed) {
        try {
            window.localStorage.setItem(SIDEBAR_STORAGE_KEY, collapsed ? 'true' : 'false');
        } catch {
            // The sidebar still works for this session when browser storage is unavailable.
        }
    },

    getActiveWarehouseId: function () {
        try {
            const value = window.localStorage.getItem(ACTIVE_WAREHOUSE_STORAGE_KEY);
            const parsed = value === null ? NaN : parseInt(value, 10);
            return Number.isNaN(parsed) ? null : parsed;
        } catch {
            return null;
        }
    },

    setActiveWarehouseId: function (warehouseId) {
        try {
            window.localStorage.setItem(ACTIVE_WAREHOUSE_STORAGE_KEY, String(warehouseId));
        } catch {
            // Just a convenience re-pick on next visit; the picker still works for this session.
        }
    },

    getCollapsedNavFlyoutTop: function (element) {
        if (!element) {
            return 88;
        }

        const rect = element.getBoundingClientRect();
        return Math.max(12, Math.min(rect.top, window.innerHeight - 240));
    },

    revealActiveSidebarItem: function () {
        window.requestAnimationFrame(() => {
            const scroller = document.querySelector('.sidebar .nav-scrollable');
            if (!scroller || scroller.offsetParent === null) {
                return;
            }

            const activeItem = scroller.querySelector('.nav-link.active');
            if (!activeItem) {
                return;
            }

            const scrollerRect = scroller.getBoundingClientRect();
            const activeRect = activeItem.getBoundingClientRect();
            const safeInset = 24;
            const isVisible = activeRect.top >= scrollerRect.top + safeInset &&
                activeRect.bottom <= scrollerRect.bottom - safeInset;

            if (isVisible) {
                return;
            }

            const targetTop = scroller.scrollTop + activeRect.top - scrollerRect.top -
                ((scroller.clientHeight - activeRect.height) / 2);

            scroller.scrollTo({
                top: Math.max(0, targetTop),
                behavior: 'smooth'
            });
        });
    },

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

    // Idle-session timeout. Blazor Server keeps the SignalR circuit (and so the UI) alive for as
    // long as the browser tab is open, independent of real user activity - the server-side auth
    // cookie's sliding expiration only renews on an actual HTTP request, which a live circuit
    // never makes again after its first page load. So genuine "N minutes of inactivity" has to be
    // tracked client-side from real input events, with the server cookie timeout only acting as a
    // backstop (e.g. if JS is blocked, or the tab is closed and reopened later).
    startIdleTimer: function (timeoutMs, dotNetObject) {
        this.stopIdleTimer();

        const fire = () => dotNetObject.invokeMethodAsync('OnIdleTimeoutAsync');
        const reset = () => {
            window.clearTimeout(idleTimerHandle);
            idleTimerHandle = window.setTimeout(fire, timeoutMs);
        };

        idleTimerEvents.forEach((evt) => document.addEventListener(evt, reset, { passive: true }));
        idleTimerReset = reset;
        reset();
    },

    stopIdleTimer: function () {
        if (idleTimerHandle) {
            window.clearTimeout(idleTimerHandle);
            idleTimerHandle = null;
        }
        if (idleTimerReset) {
            idleTimerEvents.forEach((evt) => document.removeEventListener(evt, idleTimerReset));
            idleTimerReset = null;
        }
    },

    submitForm: function (formId) {
        document.getElementById(formId)?.submit();
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


// Keyboard flow for the line-entry screens (Inward, Inward No PKM, Pullout): Up/Down + Enter pick a material from
// the suggestion menu, Enter moves on to the next field of the row, and Add Line focuses the new row's material box.
(function () {
    const rowControls = (row) => [...row.querySelectorAll('input:not([type=hidden]), select, textarea')]
        .filter((el) => !el.disabled && el.offsetParent !== null);

    const focusControl = (el) => {
        el.focus();
        if (el.tagName === 'INPUT') {
            try { el.select(); } catch { /* not selectable */ }
        }
    };

    // Blazor re-renders asynchronously after an event, so poll briefly for the DOM change we are waiting on.
    const waitFor = (test, done, timeoutMs = 2000) => {
        const start = performance.now();
        const tick = () => {
            const result = test();
            if (result) { done(result); return; }
            if (performance.now() - start < timeoutMs) requestAnimationFrame(tick);
        };
        tick();
    };

    document.addEventListener('keydown', (e) => {
        const target = e.target;
        if (!(target instanceof HTMLElement) || !target.closest('.line-table')) return;

        const combo = target.closest('.material-combobox');
        if (combo && target.tagName === 'INPUT') {
            const options = [...combo.querySelectorAll('.material-menu .material-option')];
            if (options.length === 0) return;

            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                const current = options.findIndex((o) => o.classList.contains('is-active'));
                const next = e.key === 'ArrowDown'
                    ? (current + 1) % options.length
                    : (current <= 0 ? options.length - 1 : current - 1);
                options.forEach((o) => o.classList.remove('is-active'));
                options[next].classList.add('is-active');
                options[next].scrollIntoView({ block: 'nearest' });
            } else if (e.key === 'Enter') {
                const typed = target.value.trim();
                const pick = options.find((o) => o.classList.contains('is-active'))
                    ?? options.find((o) => o.querySelector('.record-code')?.textContent.trim() === typed)
                    ?? (options.length === 1 ? options[0] : null);
                if (pick) {
                    e.preventDefault();
                    pick.click();
                }
            }
            return;
        }

        // Enter in any other field of the row moves to the next field in that row.
        if (e.key === 'Enter' && (target.tagName === 'INPUT' || target.tagName === 'SELECT')) {
            const row = target.closest('tr');
            const controls = row ? rowControls(row) : [];
            const index = controls.indexOf(target);
            if (index >= 0 && index < controls.length - 1) {
                e.preventDefault();
                focusControl(controls[index + 1]);
            }
        }
    });

    document.addEventListener('input', (e) => {
        // A new search text gives a new list: drop any stale highlight.
        if (e.target instanceof HTMLElement && e.target.closest('.material-combobox')) {
            e.target.closest('.material-combobox').querySelectorAll('.material-option.is-active')
                .forEach((o) => o.classList.remove('is-active'));
        }
    });

    document.addEventListener('click', (e) => {
        if (!(e.target instanceof HTMLElement)) return;

        // Material chosen (mouse click or the keyboard path above): continue to the Qty field of the same row.
        const option = e.target.closest('.material-option');
        const combo = option?.closest('.material-combobox');
        const row = option?.closest('tr');
        if (option && combo && row) {
            const input = combo.querySelector('input');
            waitFor(() => !row.querySelector('.material-menu .material-option'), () => {
                const controls = rowControls(row);
                const index = controls.indexOf(input);
                if (index >= 0 && index < controls.length - 1) focusControl(controls[index + 1]);
            });
            return;
        }

        const addLine = e.target.closest('[data-add-line]');
        const table = document.querySelector('.line-table');
        if (addLine && table) {
            const before = table.querySelectorAll('tbody tr').length;
            waitFor(() => {
                const rows = table.querySelectorAll('tbody tr');
                return rows.length > before ? rows[rows.length - 1] : null;
            }, (newRow) => {
                const input = newRow.querySelector('.material-combobox input');
                if (input) focusControl(input);
            });
        }
    });
})();

// A form that posts to the server (login, change password, ...) must only be submitted once: a double click or
// Enter + click sends a second POST whose antiforgery token no longer matches the (now signed-in) user and is
// rejected. Swallow repeat submits, and re-arm the form when the page is restored or enhanced navigation re-renders it.
(function () {
    const armed = new WeakSet();
    document.addEventListener('submit', (e) => {
        const form = e.target;
        if (!(form instanceof HTMLFormElement) || (form.method || '').toLowerCase() !== 'post') return;
        if (armed.has(form)) {
            e.preventDefault();
            return;
        }
        armed.add(form);
        setTimeout(() => armed.delete(form), 4000);
    }, true);
    window.addEventListener('pageshow', (e) => { if (e.persisted) location.reload(); });
})();
