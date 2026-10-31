# Putaway Preferences demo – assumptions and gaps

## Deliverables
- `GodrejWMS-Putaway-Preferences-Demo.mp4` – 1920x1080, 30 fps, H.264 + AAC, 5:19, captions burned in.
- `GodrejWMS-Putaway-Preferences-Demo.srt` – the same captions as a separate file (53 cues).
- `narration-script.md` – final narration with timestamps.
- `build/` – the scripts that produced the video (scenes, TTS, recorder, assembler) and the per-sentence audio.
  To regenerate: start the app, then `node tts.mjs`, `node record.mjs <scene ids>`, `node assemble.mjs`
  (needs `npm i playwright`, ffmpeg and `pip install edge-tts`; see the header comments in each file).

## Voice
Microsoft `en-IN-NeerjaNeural` through edge-tts (no Azure key needed). It is the same voice you asked for.
The narration text is sent to Microsoft's online speech service when generating audio; it contains no sensitive data.

## What was recorded
Real screens from the running app (http://localhost:5129, local MySQL), driven by a script, signed in as the
seeded dev Superadmin. The signed-in user's name is blurred in the sidebar.
Data shown: Brand `Stella` added and `Godrej Ninja` edited; AOP `Stella` added and `FAST CARD` edited;
mapping Cinthol / GCPL / rack B1 / B-11-01 to B-12-03; inward lines Cinthol 40035640 (100 boxes) and GK 40019311 (100 boxes).

## Differences from the original brief (the app does not have these)
- Brand Location Preferences has no AOP field and no priority/order field. (Correction: it does have an "Active mapping"
  checkbox on the edit panel, which the video does not show or mention.) The video says so and does not claim otherwise. AOP codes are described as feeding the Material Master.
- Edit and Delete of a mapping are only pointed at, not performed, to keep the demo data intact.
- "Active" is shown and explained on the edit panels but not toggled.
- The inward screen shows the Allocation Result (suggested locations). I did not click Confirm Put-away, so no stock was
  written. The result screen does not display a "reason" text, so the video does not mention one.
- No success-message wording is claimed beyond what the app shows ("Brand saved." and similar toasts appear on screen).

## Not done
- Background music (optional in the brief) was left out to keep the narration clear.
- The video is 5:19, about 20 seconds over the 4-5 minute target, because the live typing and clicking take real time.

## Database state left behind
- Restored: `Godrej Ninja` and `FAST CARD` descriptions reverted; demo mapping and test GRNs deleted.
- `Stella` brand and AOP code remain (they existed in your original data and the demo re-added them).
- 378 design types and the local dev seed accounts (see earlier) are unchanged.
