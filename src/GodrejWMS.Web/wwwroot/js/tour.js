// Guided product tour: spotlight + popover walkthrough of the console.
// Steps target elements by [data-tour="..."]; a step is skipped when its target is
// missing or hidden, which is how the tour adapts to the signed-in user's role,
// the current page and the sidebar state.
(function () {
    'use strict';

    window.godrejWms = window.godrejWms || {};

    const STORAGE_KEY = 'godrejWms.tour.v1';
    const SPOT_PADDING = 6;
    const GAP = 14;
    const EDGE = 12;
    const TOP_BAR_CLEARANCE = 96;
    const POPOVER_ROOM = 290;

    const siteSteps = [
        {
            id: 'welcome',
            plain: true,
            icon: 'bi-stars',
            title: 'Welcome to Godrej WMS',
            body: 'A quick tour of the console: where things live and how a pallet moves from receiving to pick. It takes about a minute, and you can leave at any time.'
        },
        {
            id: 'dash-metrics',
            target: '[data-tour="dash-metrics"]',
            placement: 'bottom',
            title: 'Live warehouse metrics',
            body: 'Pallet occupancy, total stock, active racks and active materials, all read live from the warehouse.'
        },
        {
            id: 'dash-cockpit',
            target: '[data-tour="dash-cockpit"]',
            placement: 'bottom',
            title: 'Operations cockpit',
            body: 'The work queue at a glance: GRNs waiting for put-away confirmation, today\'s movement, partial or failed lines that need attention, and the capacity still free after reservations.'
        },
        {
            id: 'brand',
            target: '[data-tour="brand"]',
            placement: 'right',
            title: 'Your navigation',
            body: 'Everything lives in the sidebar, grouped into Operate, Master Data, Transactions and Reports. You only see the areas your role can use.'
        },
        {
            id: 'nav-dashboard',
            target: '[data-tour="nav-dashboard"]',
            placement: 'right',
            title: 'Dashboard',
            body: 'Your home base. Come back here for the current state of the warehouse.'
        },
        {
            id: 'nav-inventory-master',
            target: '[data-tour="nav-inventory-master"]',
            placement: 'right',
            title: 'Inventory Master',
            body: 'The stock register: what is stored, where it is, and how much, by SKU and manufacturing month.'
        },
        {
            id: 'nav-racks',
            target: '[data-tour="nav-racks"]',
            placement: 'right',
            title: 'Racks & Pallets',
            body: 'A visual map of every rack and pallet position, including a heatmap of how full each location is.'
        },
        {
            id: 'nav-locations',
            target: '[data-tour="nav-locations"]',
            placement: 'right',
            title: 'Location Master',
            body: 'Define pallet locations with their capacity, zone and location type. The allocation engine uses these when it picks a home for incoming stock.'
        },
        {
            id: 'nav-materials',
            target: '[data-tour="nav-materials"]',
            placement: 'right',
            title: 'Materials',
            body: 'The SKU catalogue: pack sizes, design types, seasons and movement classes.'
        },
        {
            id: 'nav-master-data',
            target: '[data-tour="nav-master-data"]',
            placement: 'right',
            title: 'Master data',
            body: 'Design types, seasons, zone types, location types and movement types. Set these up first, because inward allocation depends on them.'
        },
        {
            id: 'nav-inward',
            target: '[data-tour="nav-inward"]',
            placement: 'right',
            title: 'Inward',
            body: 'Receive stock: enter a GRN or upload it from Excel, and the system reserves pallet locations. Then confirm the put-away. You can also reject a GRN or reallocate lines that could not be placed.'
        },
        {
            id: 'nav-pullout',
            target: '[data-tour="nav-pullout"]',
            placement: 'right',
            title: 'Pullout',
            body: 'Create outbound pullouts and confirm the picks. History keeps a full record of what left and from which location.'
        },
        {
            id: 'nav-movement',
            target: '[data-tour="nav-movement"]',
            placement: 'right',
            title: 'Inventory Movement',
            body: 'Move stock between locations, either by location or by SKU, with a reason for every move. History lists everything that has been moved.'
        },
        {
            id: 'nav-reports',
            target: '[data-tour="nav-reports"]',
            placement: 'right',
            title: 'Reports',
            body: 'Operational reports plus Inward, Pullout, Inventory and Movement analytics: turnaround, aging, utilization, valuation and more. Most reports export to CSV.'
        },
        {
            id: 'topbar-toggle',
            target: '[data-tour="topbar-toggle"]',
            placement: 'bottom',
            title: 'Make room',
            body: 'Collapse the sidebar down to icons when you want more space for the work area.'
        },
        {
            id: 'topbar-live',
            target: '[data-tour="topbar-live"]',
            placement: 'bottom',
            title: 'Realtime status',
            body: 'This shows the console is connected and updating live, so what you see is current.'
        },
        {
            id: 'topbar-account',
            target: '[data-tour="topbar-account"]',
            placement: 'bottom',
            title: 'Your account',
            body: 'Manage your account or sign out from here.'
        },
        {
            id: 'topbar-tour',
            target: '[data-tour="topbar-tour"]',
            placement: 'bottom',
            title: 'Replay this tour anytime',
            body: 'Click Take a tour whenever you want a refresher.'
        },
        {
            id: 'done',
            plain: true,
            icon: 'bi-check2-circle',
            title: 'You\'re all set',
            body: 'A good first move: open Inward to receive stock, or Racks & Pallets to see where things are stored. Inward, Pullout and Inventory Master each have their own Page tour button too.'
        }
    ];

    // Page tours: launched from a "Page tour" button in the page header. A step without a
    // target is shown as a centred card, used for what happens after an action.
    const inwardSteps = [
        {
            id: 'inward-lines',
            target: '[data-tour="inward-lines"]',
            placement: 'bottom',
            title: 'Enter what arrived',
            body: 'One row per material. Search by code or description, enter the quantity in boxes, and pick the manufacturing year and month (PKM). FIFO and allocation both work off the PKM.'
        },
        {
            id: 'inward-actions',
            target: '[data-tour="inward-actions"]',
            placement: 'top',
            title: 'Add lines and submit',
            body: 'Add Line gives you another row. Submit Inward creates the GRN and reserves pallet locations for every line automatically.'
        },
        {
            id: 'inward-upload',
            target: '[data-tour="inward-upload"]',
            placement: 'left',
            title: 'Or upload from Excel',
            body: 'Have a long GRN? Download the Sample Format, fill in Material Code, quantity, and the Mfg Year and Month (the month is a dropdown), then upload it.'
        },
        {
            id: 'inward-logic',
            target: '[data-tour="inward-logic"]',
            placement: 'top',
            title: 'How locations are chosen',
            body: 'The same SKU and PKM is topped up first, then empty locations are ranked by subtype, zone, distance, level, velocity and season. Reserved locations count as occupied until confirmed.'
        },
        {
            id: 'inward-after',
            icon: 'bi-clipboard-check',
            title: 'After you submit',
            body: 'The Allocation Result panel opens with each line marked Allocated, Partial or Failed and the locations reserved. From there you can change a location, reject the GRN, or confirm the put-away. To reallocate Partial or Failed lines later, open the GRN from Inward History.'
        }
    ];

    const pulloutSteps = [
        {
            id: 'pullout-lines',
            target: '[data-tour="pullout-lines"]',
            placement: 'bottom',
            title: 'Request what you need',
            body: 'One row per material: search by code or description and enter the quantity of boxes required.'
        },
        {
            id: 'pullout-actions',
            target: '[data-tour="pullout-actions"]',
            placement: 'top',
            title: 'Generate the pick result',
            body: 'The system proposes where to pick from. Nothing changes in inventory yet, so you can review the picks before committing.'
        },
        {
            id: 'pullout-upload',
            target: '[data-tour="pullout-upload"]',
            placement: 'left',
            title: 'Or upload from Excel',
            body: 'For a large request, download the Sample Format, fill in Material Code and quantity, and upload it.'
        },
        {
            id: 'pullout-logic',
            target: '[data-tour="pullout-logic"]',
            placement: 'top',
            title: 'How picks are chosen',
            body: 'Only good stock in active good locations is pickable. The oldest PKM is drained first (FIFO); within a PKM a column is emptied from the bottom up (A-01-01, A-01-02, ...) before the next column.'
        },
        {
            id: 'pullout-after',
            icon: 'bi-clipboard-check',
            title: 'After you generate',
            body: 'The Pick Result shows, per line, what was requested, allocated and short, with the exact locations and PKM to pick from. It is saved in Pullout History; click Confirm Pullout to update inventory.'
        }
    ];

    const inventorySteps = [
        {
            id: 'inv-export',
            target: '[data-tour="inv-export"]',
            placement: 'bottom',
            title: 'Export to Excel',
            body: 'Download the stock register as a spreadsheet.'
        },
        {
            id: 'inv-search',
            target: '[data-tour="inv-search"]',
            placement: 'bottom',
            title: 'Search',
            body: 'Type a material code or description. The list narrows as you type.'
        },
        {
            id: 'inv-filters',
            target: '[data-tour="inv-filters"]',
            placement: 'bottom',
            title: 'Filter the register',
            body: 'Narrow by design type (this one), velocity, season or active status. Filters combine with each other and with the search.'
        },
        {
            id: 'inv-table',
            target: '[data-tour="inv-table"]',
            placement: 'top',
            title: 'Stock by location',
            body: 'Each row is a batch: the material, its stock in cartons (CFB), the manufacturing month and the pallet position holding it. Click a column heading to sort.'
        },
        {
            id: 'inv-pagination',
            target: '[data-tour="inv-pagination"]',
            placement: 'top',
            title: 'Paging',
            body: 'Results load 50 rows at a time. Use Previous and Next to move through them.'
        }
    ];

    const tours = { site: siteSteps, inward: inwardSteps, pullout: pulloutSteps, inventory: inventorySteps };

    let root = null;
    let shield = null;
    let spotlight = null;
    let popover = null;
    let state = null;
    let promptEl = null;
    let offered = false;

    const reduceMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

    function readSeen() {
        try {
            return window.localStorage.getItem(STORAGE_KEY);
        } catch {
            return null;
        }
    }

    function writeSeen(value) {
        try {
            window.localStorage.setItem(STORAGE_KEY, value);
        } catch {
            // Storage can be blocked (private windows); the tour still works without remembering.
        }
    }

    function isVisible(element) {
        if (!element) {
            return false;
        }

        const rect = element.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 && element.getClientRects().length > 0;
    }

    function el(tag, className, attributes) {
        const node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        Object.entries(attributes ?? {}).forEach(([key, value]) => node.setAttribute(key, value));
        return node;
    }

    function button(className, label, iconClass) {
        const node = el('button', className, { type: 'button' });
        if (iconClass) {
            const icon = el('i', `bi ${iconClass}`, { 'aria-hidden': 'true' });
            node.appendChild(icon);
        }
        node.appendChild(document.createTextNode(label));
        return node;
    }

    function build() {
        root = el('div', 'wms-tour');
        shield = el('div', 'wms-tour-shield');
        spotlight = el('div', 'wms-tour-spotlight');

        popover = el('div', 'wms-tour-popover', {
            role: 'dialog',
            'aria-modal': 'true',
            'aria-labelledby': 'wms-tour-title',
            'aria-describedby': 'wms-tour-body',
            tabindex: '-1'
        });

        const arrow = el('div', 'wms-tour-arrow');
        const close = el('button', 'wms-tour-close', { type: 'button', 'aria-label': 'End tour' });
        close.appendChild(el('i', 'bi bi-x-lg', { 'aria-hidden': 'true' }));
        close.addEventListener('click', () => end('skipped'));

        const badge = el('div', 'wms-tour-badge');
        badge.appendChild(el('i', 'bi', { 'aria-hidden': 'true' }));

        const label = el('div', 'wms-tour-step-label', { 'aria-live': 'polite' });
        const title = el('h3', 'wms-tour-title', { id: 'wms-tour-title' });
        const body = el('p', 'wms-tour-body', { id: 'wms-tour-body' });
        const dots = el('div', 'wms-tour-dots', { 'aria-hidden': 'true' });

        const footer = el('div', 'wms-tour-footer');
        const skip = button('wms-tour-skip', 'Skip tour');
        skip.addEventListener('click', () => end('skipped'));
        const actions = el('div', 'wms-tour-actions');
        const back = button('btn btn-sm btn-outline-secondary wms-tour-back', 'Back', 'bi-arrow-left');
        back.addEventListener('click', () => go(-1));
        const next = button('btn btn-sm btn-primary wms-tour-next', 'Next');
        next.addEventListener('click', () => go(1));
        actions.append(back, next);
        footer.append(skip, actions);

        popover.append(arrow, close, badge, label, title, body, dots, footer);
        root.append(shield, spotlight, popover);
        document.body.appendChild(root);
    }

    function activeSteps(tourId) {
        return (tours[tourId] ?? []).filter((step) => !step.target || isVisible(document.querySelector(step.target)));
    }

    function start(tourId) {
        tourId = typeof tourId === 'string' && tours[tourId] ? tourId : 'site';
        if (state) {
            return;
        }

        dismissPrompt();

        // On narrow screens the sidebar is a closed dropdown; open it so its steps can be shown.
        const toggler = tourId === 'site' ? document.querySelector('.navbar-toggler') : null;
        const openedNav = !!toggler && isVisible(toggler) && !toggler.checked;
        if (openedNav) {
            toggler.click();
        }

        // Give the layout a frame to settle after the nav opens.
        requestAnimationFrame(() => {
            const list = activeSteps(tourId);
            if (list.length === 0) {
                return;
            }

            state = { id: tourId, steps: list, index: 0, openedNav, returnFocus: document.activeElement };
            build();
            document.addEventListener('keydown', onKeyDown, true);
            window.addEventListener('resize', reposition);
            window.addEventListener('scroll', reposition, true);
            showStep(0);
        });
    }

    function end(outcome) {
        if (!state) {
            return;
        }

        const { id, openedNav, returnFocus } = state;
        state = null;

        // Only the site tour is remembered: it decides whether to offer itself again.
        if (id === 'site') {
            writeSeen(outcome);
        }

        document.removeEventListener('keydown', onKeyDown, true);
        window.removeEventListener('resize', reposition);
        window.removeEventListener('scroll', reposition, true);
        root?.remove();
        root = shield = spotlight = popover = null;

        if (openedNav) {
            const toggler = document.querySelector('.navbar-toggler');
            if (toggler?.checked) {
                toggler.click();
            }
        }

        if (returnFocus instanceof HTMLElement && document.contains(returnFocus)) {
            returnFocus.focus({ preventScroll: true });
        }
    }

    function go(delta) {
        if (!state) {
            return;
        }

        const next = state.index + delta;
        if (next >= state.steps.length) {
            end('completed');
            return;
        }

        if (next >= 0) {
            showStep(next);
        }
    }

    function showStep(index) {
        state.index = index;
        const step = state.steps[index];
        const total = state.steps.length;
        const isFirst = index === 0;
        const isLast = index === total - 1;

        const badge = popover.querySelector('.wms-tour-badge');
        badge.hidden = !step.icon;
        if (step.icon) {
            badge.querySelector('i').className = `bi ${step.icon}`;
        }

        popover.classList.toggle('centered', !step.target);
        popover.querySelector('.wms-tour-step-label').textContent = step.plain ? '' : `Step ${index + 1} of ${total}`;
        popover.querySelector('.wms-tour-title').textContent = step.title;
        popover.querySelector('.wms-tour-body').textContent = step.body;

        const dots = popover.querySelector('.wms-tour-dots');
        dots.replaceChildren();
        for (let i = 0; i < total; i++) {
            dots.appendChild(el('span', i === index ? 'active' : i < index ? 'done' : ''));
        }

        popover.querySelector('.wms-tour-back').hidden = isFirst;
        popover.querySelector('.wms-tour-skip').hidden = isLast;
        popover.querySelector('.wms-tour-next').textContent = step.plain && isFirst ? 'Start tour' : isLast ? 'Finish' : 'Next';

        const target = step.target ? document.querySelector(step.target) : null;
        if (target) {
            target.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'auto' });

            // Tall targets leave no room beside them when centred: pin them just under the
            // sticky top bar so the popover can sit below (or above for 'top' placement).
            if (step.placement === 'bottom' || step.placement === 'top') {
                const rect = target.getBoundingClientRect();
                const room = window.innerHeight - TOP_BAR_CLEARANCE - rect.height;
                if (room >= POPOVER_ROOM) {
                    const offset = step.placement === 'bottom' ? TOP_BAR_CLEARANCE : room + TOP_BAR_CLEARANCE;
                    window.scrollBy(0, rect.top - offset);
                }
            }
        }

        requestAnimationFrame(() => {
            reposition();
            popover.classList.add('ready');
            popover.querySelector('.wms-tour-next').focus({ preventScroll: true });
        });
    }

    function reposition() {
        if (!state || !popover) {
            return;
        }

        const step = state.steps[state.index];
        const target = step.target ? document.querySelector(step.target) : null;

        if (!target || !isVisible(target)) {
            shield.classList.add('dim');
            spotlight.classList.add('hidden');
            popover.removeAttribute('data-side');
            popover.style.left = '50%';
            popover.style.top = '50%';
            return;
        }

        shield.classList.remove('dim');
        spotlight.classList.remove('hidden');

        const rect = target.getBoundingClientRect();
        spotlight.style.left = `${rect.left - SPOT_PADDING}px`;
        spotlight.style.top = `${rect.top - SPOT_PADDING}px`;
        spotlight.style.width = `${rect.width + SPOT_PADDING * 2}px`;
        spotlight.style.height = `${rect.height + SPOT_PADDING * 2}px`;

        const pw = popover.offsetWidth;
        const ph = popover.offsetHeight;
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const clamp = (value, min, max) => Math.max(min, Math.min(value, Math.max(min, max)));

        const candidates = [step.placement, 'right', 'bottom', 'left', 'top']
            .filter((side, i, all) => side && all.indexOf(side) === i);

        let placement = null;
        for (const side of candidates) {
            let x;
            let y;
            let fits;

            if (side === 'right' || side === 'left') {
                x = side === 'right' ? rect.right + SPOT_PADDING + GAP : rect.left - SPOT_PADDING - GAP - pw;
                y = clamp(rect.top + rect.height / 2 - ph / 2, EDGE, vh - ph - EDGE);
                fits = x >= EDGE && x + pw <= vw - EDGE;
            } else {
                y = side === 'bottom' ? rect.bottom + SPOT_PADDING + GAP : rect.top - SPOT_PADDING - GAP - ph;
                x = clamp(rect.left + rect.width / 2 - pw / 2, EDGE, vw - pw - EDGE);
                fits = y >= EDGE && y + ph <= vh - EDGE;
            }

            if (fits) {
                placement = { side, x, y };
                break;
            }
        }

        if (!placement) {
            // Nothing fits around the target (huge target or tiny viewport): float at the bottom.
            popover.removeAttribute('data-side');
            popover.style.left = `${clamp((vw - pw) / 2, EDGE, vw - pw - EDGE)}px`;
            popover.style.top = `${Math.max(EDGE, vh - ph - EDGE)}px`;
            return;
        }

        popover.style.left = `${placement.x}px`;
        popover.style.top = `${placement.y}px`;
        popover.setAttribute('data-side', placement.side);

        const horizontal = placement.side === 'left' || placement.side === 'right';
        const arrowOffset = horizontal
            ? clamp(rect.top + rect.height / 2 - placement.y, 20, ph - 20)
            : clamp(rect.left + rect.width / 2 - placement.x, 20, pw - 20);
        popover.style.setProperty('--wms-tour-arrow-offset', `${arrowOffset}px`);
    }

    function onKeyDown(event) {
        if (!state) {
            return;
        }

        switch (event.key) {
            case 'Escape':
                event.preventDefault();
                end('skipped');
                break;
            case 'ArrowRight':
                event.preventDefault();
                go(1);
                break;
            case 'ArrowLeft':
                event.preventDefault();
                go(-1);
                break;
            case 'Tab': {
                // Keep keyboard focus inside the popover while the tour is open.
                const focusable = [...popover.querySelectorAll('button')].filter((b) => !b.hidden);
                const first = focusable[0];
                const last = focusable[focusable.length - 1];
                if (!popover.contains(document.activeElement)) {
                    event.preventDefault();
                    first.focus();
                } else if (event.shiftKey && document.activeElement === first) {
                    event.preventDefault();
                    last.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                    event.preventDefault();
                    first.focus();
                }
                break;
            }
        }
    }

    // ---- First-visit offer -------------------------------------------------

    function dismissPrompt() {
        promptEl?.remove();
        promptEl = null;
    }

    function showPrompt() {
        if (state || promptEl || readSeen()) {
            return;
        }

        promptEl = el('div', 'wms-tour-prompt', { role: 'dialog', 'aria-label': 'Take a tour' });

        const icon = el('span', 'wms-tour-prompt-icon');
        icon.appendChild(el('i', 'bi bi-compass', { 'aria-hidden': 'true' }));

        const text = el('div', 'wms-tour-prompt-text');
        const heading = el('strong');
        heading.textContent = 'New to Godrej WMS?';
        const sub = el('span');
        sub.textContent = 'Take a one-minute tour to find your way around.';
        text.append(heading, sub);

        const actions = el('div', 'wms-tour-prompt-actions');
        const later = button('btn btn-sm btn-link text-muted', 'Not now');
        later.addEventListener('click', () => {
            writeSeen('dismissed');
            dismissPrompt();
        });
        const begin = button('btn btn-sm btn-primary', 'Start tour');
        begin.addEventListener('click', start);
        actions.append(later, begin);

        promptEl.append(icon, text, actions);
        document.body.appendChild(promptEl);
    }

    function maybeOffer() {
        if (offered || readSeen()) {
            return;
        }

        // The tour button only renders for signed-in users, so this doubles as the auth check.
        if (!document.querySelector('[data-tour="topbar-tour"]')) {
            return;
        }

        offered = true;
        window.setTimeout(showPrompt, 1200);
    }

    document.addEventListener('click', (event) => {
        const trigger = event.target instanceof Element ? event.target.closest('[data-tour-start]') : null;
        if (trigger) {
            start(trigger.getAttribute('data-tour-start') || 'site');
        }
    });

    let offerScanScheduled = false;
    function scheduleOfferScan() {
        if (offered || offerScanScheduled) {
            return;
        }

        offerScanScheduled = true;
        window.setTimeout(() => {
            offerScanScheduled = false;
            maybeOffer();
        }, 200);
    }

    new MutationObserver(scheduleOfferScan).observe(document.body, { childList: true, subtree: true });
    window.addEventListener('load', scheduleOfferScan);

    window.godrejWms.tour = {
        start,
        end: () => end('skipped'),
        reset: () => {
            try {
                window.localStorage.removeItem(STORAGE_KEY);
            } catch {
                // ignore
            }
            offered = false;
        }
    };
})();
