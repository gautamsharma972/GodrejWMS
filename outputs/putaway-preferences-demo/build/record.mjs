// Usage: node record.mjs s2 s3 ...   (records each scene to clips/<id>.mp4 + clips/<id>.json)
import { chromium } from 'playwright';
import { scenes, BASE } from './scenes.mjs';
import { INIT_SCRIPT, HTML, helpers, sleep } from './lib.mjs';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
const AUTH = process.env.AUTH || 'auth.json';
const dur = JSON.parse(fs.readFileSync('audio/durations.json'));
const GAP = 750, LEAD = 450, TAIL = 900;
fs.mkdirSync('clips', { recursive: true }); fs.mkdirSync('raw', { recursive: true });
const ids = process.argv.slice(2);
const browser = await chromium.launch();
{ // fresh login (not recorded) - the app's idle timeout invalidates old sessions
  const c = await browser.newContext({ viewport: { width: 1920, height: 1080 } }); const p = await c.newPage();
  await p.goto(BASE + '/Account/Login'); await p.fill('#Input\\.Email', 'superadmin@godrejwms.local'); await p.fill('#Input\\.Password', process.env.DEMO_PW);
  await p.click('button.login-submit'); await p.waitForLoadState('networkidle'); await p.waitForURL((u) => !u.pathname.includes('/Account/Login'), { timeout: 15000 });
  await c.storageState({ path: AUTH }); await c.close();
}

for (const id of ids) {
  const sc = scenes.find((s) => s.id === id);
  const isHtml = sc.kind === 'html';
  const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 }, recordVideo: { dir: 'raw', size: { width: 1920, height: 1080 } }, ...(isHtml ? {} : { storageState: AUTH }) });
  await ctx.addInitScript(INIT_SCRIPT);
  const page = await ctx.newPage(); const tPage = Date.now();
  const h = helpers(page);
  try {
    if (isHtml) { const f = path.resolve(`raw/${sc.html}.html`); fs.writeFileSync(f, HTML[sc.html]); await page.goto('file://' + f); await sleep(500); }
    else {
      await page.goto(BASE + sc.start); await page.waitForLoadState('networkidle'); await sleep(1500);
      await page.getByText('Not now').click({ timeout: 1500 }).catch(() => {});
      await page.evaluate(() => { window.__showCursor = true; });
      await page.mouse.move(960, 560); await sleep(600);
    }
    const tBeat0 = Date.now(); const beats = [];
    await page.evaluate((t) => window.__d.chip(t, 3600), `Scene ${sc.num} · ${sc.title}`);
    await sleep(LEAD);
    for (let i = 0; i < sc.beats.length; i++) {
      const b = sc.beats[i]; const audioMs = dur[id][i] * 1000;
      await h.clear(); await page.evaluate((t) => window.__d.cap(t), b.cap);
      const start = Date.now();
      console.log(`  [${id}] beat ${i}: ${b.cap.slice(0, 50)}`);
      await Promise.all([Promise.race([b.act(h), sleep(40000).then(() => { throw new Error(`beat ${i} watchdog (act > 40s)`); })]), sleep(audioMs + GAP)]);
      beats.push({ start: (start - tBeat0) / 1000, dur: (Date.now() - start) / 1000, audio: dur[id][i], cap: b.cap });
    }
    await h.clear(); await page.evaluate(() => window.__d.cap('')); await sleep(TAIL);
    const total = (Date.now() - tBeat0) / 1000;
    const offset = (tBeat0 - tPage) / 1000 + Number(process.env.OFFSET_ADJ ?? 0.0);
    const video = page.video(); await ctx.close(); const src = await video.path(); console.log('  closed, encoding');
    const out = `clips/${id}.mp4`;
    execFileSync('ffmpeg', ['-loglevel', 'error', '-y', '-ss', offset.toFixed(3), '-i', src, '-t', total.toFixed(3), '-r', '30', '-c:v', 'libx264', '-crf', '17', '-preset', 'medium', '-pix_fmt', 'yuv420p', '-an', out]);
    fs.writeFileSync(`clips/${id}.json`, JSON.stringify({ id, num: sc.num, title: sc.title, lead: LEAD / 1000, total, beats }, null, 1));
    console.log(id, 'ok', total.toFixed(1) + 's');
  } catch (e) {
    console.error(id, 'FAILED:', e.message.split('\n')[0]);
    await page.screenshot({ path: `raw/fail_${id}.png` }).catch(() => {});
    await ctx.close().catch(() => {});
    process.exitCode = 1; break;
  }
}
await browser.close();
