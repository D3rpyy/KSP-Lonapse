# Changelog

## 0.5.2 (2026-11-03)

- On EVA, falling over or lying on the ground no longer counts as spinning. The kerbal also gets about 3 s to get up after a fall (`evaSettleTime`).
- On EVA, hops and bumps no longer count as something hitting the kerbal.
- Mission Control questions like "Station check. Please confirm." now get an answer from the crew. Mission Control no longer acknowledges a crew line with a question nobody answers, and "Confirm status, please." is gone.
- Every crew and Mission Control line now shows its subtitle. When no line from the phrase table fits, the radio stays quiet instead of playing a random clip without a subtitle.

## 0.5.1 (2026-11-03)

- The crew keeps talking during a reentry blackout. Staging and other callouts still work. Only Mission Control drops out and doesn't answer.
- The crew no longer repeats the same stress topic (like "this is shaking a lot") over and over. Each cause comes up at most once every 45 s, at any radio amount.
- The master alarm only plays at very high stress (0.9), not as soon as one nervous kerbal panics.
- Removed chatter that claimed things that may not have happened, like "Booster separation", "Fairing away", "Main engine cutoff" and altitude callouts.
- Mission Control no longer says "Throttle down now" during heavy vibration, and the crew no longer claims to throttle down or open chutes.

## 0.5.0 (2026-11-03)

First public release. The mod was called Quindar during development.

- Crew and Mission Control radio in Lonapse, with stress per kerbal, callouts, cabin intercom and English subtitles.
- Probe mode with a beep voice per probe core, moods and Mission Control talking to the probe.
- EVA suit radio with breathing that speeds up while running, a relay through a nearby ship and lines for falls.
- Signal from CommNet or RemoteTech: static, blackout, answer delay and dropouts.
- Talking kerbals in IVA and in the portraits, with head turns to the speaker.
- Launch countdown, station and base mode, master alarm during panic.
- Old Quindar settings are picked up automatically.
