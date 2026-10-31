import { scenes } from './scenes.mjs';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
const EDGE = process.env.EDGE_TTS; const VOICE = 'en-IN-NeerjaNeural'; const RATE = process.env.TTS_RATE || '-4%';
fs.mkdirSync('audio', { recursive: true });
const meta = {}; let total = 0;
for (const s of scenes) {
  meta[s.id] = [];
  for (let i = 0; i < s.beats.length; i++) {
    const f = `audio/${s.id}_${i}.mp3`;
    if (!fs.existsSync(f) || process.env.FORCE) execFileSync(EDGE, ['--voice', VOICE, `--rate=${RATE}`, '--text', s.beats[i].say, '--write-media', f], { stdio: 'pipe' });
    const d = parseFloat(execFileSync('ffprobe', ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', f]).toString());
    meta[s.id].push(d); total += d;
  }
  console.log(s.id, meta[s.id].reduce((a, b) => a + b, 0).toFixed(1) + 's', meta[s.id].map(x => x.toFixed(1)).join(' '));
}
fs.writeFileSync('audio/durations.json', JSON.stringify(meta, null, 1));
console.log('total speech', total.toFixed(1) + 's');
