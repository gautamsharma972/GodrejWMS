import { order } from './scenes.mjs';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
const ff = (args) => execFileSync('ffmpeg', ['-loglevel', 'error', '-y', ...args]);
fs.mkdirSync('tmp', { recursive: true });
const OUT = process.argv[2] || '../GodrejWMS-Putaway-Preferences-Demo.mp4';
const SRT = OUT.replace(/\.mp4$/, '.srt');
let offset = 0; const vlist = [], alist = [], cues = [], chapters = [];
const ts = (t) => { const ms = Math.round(t * 1000); const p = (n, w = 2) => String(n).padStart(w, '0'); return `${p(Math.floor(ms / 3600000))}:${p(Math.floor(ms / 60000) % 60)}:${p(Math.floor(ms / 1000) % 60)},${p(ms % 1000, 3)}`; };
const wrap = (t, n = 46) => { const w = t.split(' '); const lines = ['']; for (const x of w) { if ((lines.at(-1) + ' ' + x).trim().length > n && lines.at(-1)) lines.push(x); else lines[lines.length - 1] = (lines.at(-1) + ' ' + x).trim(); } return lines.length > 2 ? [lines.slice(0, Math.ceil(lines.length / 2)).join(' '), lines.slice(Math.ceil(lines.length / 2)).join(' ')].join('\n') : lines.join('\n'); };
for (const id of order) {
  const m = JSON.parse(fs.readFileSync(`clips/${id}.json`));
  const n = m.beats.length; const inputs = []; const fc = [];
  m.beats.forEach((b, i) => { inputs.push('-i', `audio/${id}_${i}.mp3`); const d = Math.round((b.start + 0.12) * 1000); fc.push(`[${i}]aformat=sample_rates=48000:channel_layouts=stereo,adelay=${d}|${d}[a${i}]`); });
  fc.push(`${m.beats.map((_, i) => `[a${i}]`).join('')}amix=inputs=${n}:normalize=0:duration=longest,apad,atrim=0:${m.total.toFixed(3)}[out]`);
  ff([...inputs, '-filter_complex', fc.join(';'), '-map', '[out]', '-ar', '48000', `tmp/${id}.wav`]);
  vlist.push(`file '${process.cwd()}/clips/${id}.mp4'`); alist.push(`file '${process.cwd()}/tmp/${id}.wav'`);
  m.beats.forEach((b) => { const s = offset + b.start + 0.1; cues.push({ s, e: s + b.audio + 0.35, t: b.cap }); });
  chapters.push([offset, `Scene ${m.num} · ${m.title}`]);
  offset += m.total;
}
cues.forEach((c, i) => { if (cues[i + 1] && c.e > cues[i + 1].s - 0.04) c.e = cues[i + 1].s - 0.04; });
fs.writeFileSync(SRT, cues.map((c, i) => `${i + 1}\n${ts(c.s)} --> ${ts(c.e)}\n${wrap(c.t)}\n`).join('\n'));
fs.writeFileSync('tmp/v.txt', vlist.join('\n')); fs.writeFileSync('tmp/a.txt', alist.join('\n'));
ff(['-f', 'concat', '-safe', '0', '-i', 'tmp/v.txt', '-f', 'concat', '-safe', '0', '-i', 'tmp/a.txt', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', '-metadata', 'title=GodrejWMS - Putaway Preferences', OUT]);
fs.writeFileSync('tmp/chapters.json', JSON.stringify(chapters.map(([t, n]) => [ts(t).slice(0, 8), n]), null, 1));
console.log('total', offset.toFixed(1) + 's', 'cues', cues.length); console.log(chapters.map(([t, n]) => `${(t / 60 | 0)}:${String(Math.round(t % 60)).padStart(2, '0')} ${n}`).join('\n'));
