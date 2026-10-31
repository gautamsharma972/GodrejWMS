// Recording helpers: on-screen cursor + click ripple, highlight/callout boxes, burned-in captions.
export const INIT_SCRIPT = `(() => {
  if (window.__d) return;
  const css = document.createElement('style');
  css.textContent = \`
   h1:focus,h1:focus-visible{outline:none!important}
   .sidebar-user-details,.account-name{filter:blur(7px)!important}
   #__cur{position:fixed;z-index:2147483647;width:26px;height:26px;border-radius:50%;background:rgba(37,99,235,.28);border:3px solid #2563eb;pointer-events:none;transform:translate(-50%,-50%);left:960px;top:560px;display:none}
   .__rip{position:fixed;z-index:2147483646;border:4px solid #2563eb;border-radius:50%;pointer-events:none;width:14px;height:14px;transform:translate(-50%,-50%);animation:__rip .6s ease-out forwards}
   @keyframes __rip{to{width:84px;height:84px;opacity:0}}
   .__hl{position:fixed;z-index:2147483645;border:4px solid #f59e0b;border-radius:10px;box-shadow:0 0 0 6px rgba(245,158,11,.25);pointer-events:none;animation:__pulse 1.3s ease-in-out infinite}
   @keyframes __pulse{50%{box-shadow:0 0 0 14px rgba(245,158,11,.08)}}
   .__lbl{position:fixed;z-index:2147483646;background:#111827;color:#fff;font:600 21px/1.2 -apple-system,'Helvetica Neue',Arial,sans-serif;padding:8px 14px;border-radius:8px;pointer-events:none;box-shadow:0 6px 20px rgba(0,0,0,.28);white-space:nowrap}
   #__cap{position:fixed;left:50%;bottom:30px;transform:translateX(-50%);max-width:1500px;z-index:2147483647;background:rgba(15,23,42,.9);color:#fff;font:500 33px/1.35 -apple-system,'Helvetica Neue',Arial,sans-serif;padding:14px 30px;border-radius:12px;text-align:center;pointer-events:none;display:none}
   #__chip{position:fixed;left:28px;top:84px;z-index:2147483647;background:#2563eb;color:#fff;font:600 24px/1 -apple-system,'Helvetica Neue',Arial,sans-serif;padding:12px 20px;border-radius:999px;pointer-events:none;box-shadow:0 8px 24px rgba(37,99,235,.35);display:none;transition:opacity .4s}
  \`;
  const mk = (id) => { const e = document.createElement('div'); e.id = id; return e; };
  const cur = mk('__cur'), cap = mk('__cap'), chip = mk('__chip');
  const mount = () => { document.documentElement.append(css, cur, cap, chip); };
  if (document.documentElement) mount(); else document.addEventListener('DOMContentLoaded', mount);
  document.addEventListener('mousemove', (e) => { cur.style.display = window.__showCursor === false ? 'none' : 'block'; cur.style.left = e.clientX + 'px'; cur.style.top = e.clientY + 'px'; }, true);
  window.__d = {
    cap(t) { cap.textContent = t || ''; cap.style.display = t ? 'block' : 'none'; },
    chip(t, ms) { chip.textContent = t; chip.style.display = 'block'; chip.style.opacity = 1; setTimeout(() => { chip.style.opacity = 0; }, ms); setTimeout(() => { chip.style.display = 'none'; }, ms + 450); },
    rip(x, y) { const r = document.createElement('div'); r.className = '__rip'; r.style.left = x + 'px'; r.style.top = y + 'px'; document.documentElement.append(r); setTimeout(() => r.remove(), 700); },
    hl(b, label, pos) {
      const e = document.createElement('div'); e.className = '__hl';
      Object.assign(e.style, { left: b.x - 6 + 'px', top: b.y - 6 + 'px', width: b.w + 12 + 'px', height: b.h + 12 + 'px' });
      document.documentElement.append(e);
      if (label) {
        const l = document.createElement('div'); l.className = '__lbl'; l.textContent = label; document.documentElement.append(l);
        const w = l.offsetWidth, hh = l.offsetHeight; let x = b.x, y = b.y + b.h + 16;
        if (pos === 'above') { y = b.y - hh - 16; } else if (pos === 'right') { x = b.x + b.w + 18; y = b.y + (b.h - hh) / 2; } else if (pos === 'left') { x = b.x - w - 18; y = b.y + (b.h - hh) / 2; }
        l.style.left = Math.max(8, Math.min(x, innerWidth - w - 14)) + 'px'; l.style.top = Math.max(8, Math.min(y, innerHeight - hh - 14)) + 'px';
      }
    },
    clear() { document.querySelectorAll('.__hl,.__lbl').forEach((n) => n.remove()); },
  };
})();`;

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export function helpers(page) {
  const h = {
    page, sleep,
    async hl(loc, label, pos = 'below', multi = false) {
      let box;
      if (multi) {
        const n = await loc.count(); let u = null;
        for (let i = 0; i < n; i++) {
          const b = await loc.nth(i).boundingBox(); if (!b) continue;
          u = u ? { x: Math.min(u.x, b.x), y: Math.min(u.y, b.y), x2: Math.max(u.x2, b.x + b.width), y2: Math.max(u.y2, b.y + b.height) }
                : { x: b.x, y: b.y, x2: b.x + b.width, y2: b.y + b.height };
        }
        if (u) box = { x: u.x, y: u.y, width: u.x2 - u.x, height: u.y2 - u.y };
      } else { await h.reveal(loc); box = await h.stable(loc); }
      if (!box) return;
      await page.evaluate(([b, l, p]) => window.__d.hl(b, l, p), [{ x: box.x, y: box.y, w: box.width, h: box.height }, label, pos]);
    },
    async reveal(loc) { await loc.first().evaluate((el) => el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' })); await sleep(350); },
    async stable(loc) { let prev = null; for (let i = 0; i < 12; i++) { const b = await loc.first().boundingBox(); if (prev && b && Math.abs(prev.y - b.y) < 0.5 && Math.abs(prev.x - b.x) < 0.5) return b; prev = b; await sleep(150); } return prev; },
    async navTo(label, heading) {
      await page.mouse.move(960, 560); await page.mouse.move(40, 520, { steps: 30 }); await sleep(900); // hovering expands the collapsed sidebar
      const l = page.locator(`a[aria-label="${label}"]`);
      await h.hl(l, label, 'right'); await h.click(l); await h.settle(heading);
    },
    async clear() { await page.evaluate(() => window.__d.clear()); },
    async click(loc) {
      await loc.first().waitFor({ state: 'visible' }); await h.reveal(loc);
      const b = await h.stable(loc); const x = b.x + b.width / 2, y = b.y + b.height / 2;
      await page.mouse.move(x, y, { steps: 28 }); await sleep(120);
      await page.evaluate(([x, y]) => window.__d.rip(x, y), [x, y]);
      await page.mouse.click(x, y); await sleep(250);
    },
    async type(loc, text, delay = 85) { await loc.focus(); await page.keyboard.type(text, { delay }); await sleep(150); },
    async settle(sel) { await page.waitForSelector(sel); await sleep(800); },
    async inwardLine(idx, mat, qty) {
      const row = page.locator('tbody tr').nth(idx);
      const s = row.locator('input[placeholder^="Search material"]');
      await h.click(s); await h.type(s, mat, 30);
      await page.locator('.material-option').first().waitFor(); await sleep(150);
      await h.click(page.locator('.material-option').first());
      const q = row.locator('input[type=number]'); await h.click(q); await q.fill(''); await h.type(q, String(qty), 40);
      await q.press('Tab'); await sleep(150);
    },
    async flow(n) { await page.evaluate((n) => window.__flow(n), n); },
    async tick(n) { await page.evaluate((n) => window.__tick(n), n); },
  };
  return h;
}

const FONT = "-apple-system,'Helvetica Neue',Arial,sans-serif";
const base = `*{box-sizing:border-box}body{margin:0;width:1920px;height:1080px;font-family:${FONT};color:#fff;background:linear-gradient(135deg,#0b1d3a 0%,#143a7a 60%,#1d4ed8 100%);display:flex;flex-direction:column;align-items:center;justify-content:center}
.brand{position:absolute;top:44px;left:64px;font-weight:700;letter-spacing:.08em;font-size:26px;opacity:.9}.brand b{color:#93c5fd}
h1{font-size:92px;margin:0 0 14px;font-weight:800;letter-spacing:-.02em}.sub{font-size:34px;opacity:.85;margin-bottom:84px}`;
export const HTML = {
  intro: `<style>${base}
.flow{display:flex;align-items:center;gap:40px}.card{width:480px;min-height:340px;border-radius:24px;background:rgba(255,255,255,.08);border:2px solid rgba(255,255,255,.18);padding:34px;transition:all .5s;opacity:.35}
.card.on{opacity:1;background:rgba(255,255,255,.16);border-color:#fbbf24;box-shadow:0 0 0 8px rgba(251,191,36,.18);transform:translateY(-8px)}
.card .n{font-size:24px;font-weight:700;color:#93c5fd;margin-bottom:12px}.card .t{font-size:44px;font-weight:800;margin-bottom:12px}.card .d{font-size:27px;opacity:.88;line-height:1.35}
.arrow{font-size:64px;opacity:.5}</style>
<div class="brand">GODREJ<b>WMS</b></div><h1>Putaway Preferences</h1><div class="sub">Brand and AOP masters → location preferences → smarter putaway</div>
<div class="flow"><div class="card" id="c1"><div class="n">STEP 1</div><div class="t">Brand &amp; AOP Masters</div><div class="d">Maintain the brand and AOP codes used by materials.</div></div><div class="arrow">→</div>
<div class="card" id="c2"><div class="n">STEP 2</div><div class="t">Brand Location Preferences</div><div class="d">Map a brand to a preferred range of locations.</div></div><div class="arrow">→</div>
<div class="card" id="c3"><div class="n">STEP 3</div><div class="t">Inward Putaway</div><div class="d">Suggested locations follow the brand's range.</div></div></div>
<script>window.__flow=(n)=>{for(let i=1;i<=3;i++)document.getElementById('c'+i).classList.toggle('on',n===i)};window.__showCursor=false;</script>`,
  summary: `<style>${base}
ul{list-style:none;padding:0;margin:0}li{font-size:50px;font-weight:600;margin:26px 0;display:flex;align-items:center;gap:26px;opacity:0;transform:translateX(-30px);transition:all .6s}
li.on{opacity:1;transform:none}li i{font-style:normal;width:64px;height:64px;border-radius:50%;background:#22c55e;display:inline-flex;align-items:center;justify-content:center;font-size:40px}
#thanks{position:absolute;bottom:230px;font-size:56px;font-weight:800;opacity:0;transition:opacity .8s}#thanks.on{opacity:1}</style>
<div class="brand">GODREJ<b>WMS</b></div><h1>Best practices</h1><div class="sub">Putaway Preferences</div>
<ul><li id="l1"><i>✓</i>Keep Brand and AOP masters clean and up to date</li><li id="l2"><i>✓</i>Update mappings when brands, racks or locations are added</li><li id="l3"><i>✓</i>Check capacity so the preferred range does not overflow</li></ul>
<div id="thanks">Thank you for watching</div>
<script>window.__tick=(n)=>{for(let i=1;i<=3;i++)document.getElementById('l'+i).classList.toggle('on',i<=n);document.getElementById('thanks').classList.toggle('on',n>=4)};window.__showCursor=false;</script>`,
};
