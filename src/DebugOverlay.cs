// Debug overlay, top left. Text is rebuilt 4 times per second, never per frame.
using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Lonapse
{
    public class DebugOverlay
    {
        private readonly LonapseAddon host;
        private readonly StringBuilder sb = new StringBuilder(2048);
        private string text = "";
        private int lines = 1;
        private float timer;
        private GUIStyle style;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public DebugOverlay(LonapseAddon host) { this.host = host; }

        public void Tick(float dt)
        {
            if (!host.settings.showDebug) return;
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.25f;
            try { Rebuild(); }
            catch (Exception e) { Log.Exception("DebugOverlay.Rebuild", e); }
        }

        private void F(string name, double v) { sb.Append(name).Append(' ').Append(v.ToString("0.00", Inv)); }

        private void Rebuild()
        {
            StressModel st = host.stress;
            sb.Length = 0;
            sb.Append("Lonapse ").Append(LonapseAddon.Version).Append('\n');
            sb.Append("gate: ").Append(host.gateReason).Append("  gain ").Append(host.gateGain.ToString("0.00", Inv));
            sb.Append("  paused ").Append(host.paused).Append("  gameGain ").Append(host.gameGain.ToString("0.00", Inv)).Append('\n');
            F("stress", st.stress); sb.Append("  "); F("raw", st.raw); sb.Append("  "); F("gStrain", st.gStrain);
            sb.Append("  warmup ").Append(Math.Max(0f, st.warmupLeft).ToString("0.0", Inv)).Append('\n');
            for (int i = 0; i < StressModel.Count; i++)
            {
                sb.Append("  ").Append(StressModel.Names[i]).Append(' ')
                  .Append(st.comp[i].ToString("0.00", Inv)).Append(" x ").Append(st.weight[i].ToString("0.00", Inv))
                  .Append(" = ").Append((st.comp[i] * st.weight[i]).ToString("0.00", Inv)).Append('\n');
            }
            FlightSample s = host.sample;
            sb.Append("omega ").Append(s.omega.ToString("0.00", Inv)).Append(" rad/s  g ").Append(s.g.ToString("0.0", Inv))
              .Append("  q ").Append(s.qKPa.ToString("0.0", Inv)).Append(" kPa  vs ").Append(s.vs.ToString("0.0", Inv))
              .Append("  h ").Append(s.h.ToString("0", Inv)).Append('\n');
            sb.Append("heat ").Append(s.heatRatio.ToString("0.00", Inv)).Append("  fuel ").Append(s.fuelFrac.ToString("0.00", Inv))
              .Append("  throttle ").Append(s.throttle.ToString("0.00", Inv)).Append('\n');
            sb.Append("plasma p ").Append(st.plasma.ToString("0.00", Inv)).Append(" (raw ").Append(st.plasmaRaw.ToString("0.00", Inv))
              .Append(")  qv ").Append(st.qv.ToString("0", Inv)).Append('\n');
            sb.Append("shockTemp ").Append(s.shockTemp.ToString("0", Inv)).Append(s.shockTempValid ? " K" : " K (fallback)")
              .Append("  extTemp ").Append(s.extTemp.ToString("0", Inv)).Append(" K  inPlasmaStock ").Append(s.inPlasmaStock).Append('\n');
            Settings cfg = host.settings;
            sb.Append("groups: radio ").Append(cfg.radioEnabled).Append("  intercom ").Append(cfg.intercomEnabled)
              .Append("  beeps ").Append(cfg.beepsEnabled).Append("  chutes ").Append(host.chutes.Count).Append('\n');
            if (host.stationMode)
                sb.Append("station MC check-in in ").Append(host.director.stationMcLeft.ToString("0", Inv)).Append(" s\n");
            sb.Append("mode: ").Append(host.stationMode ? "station/base" : "normal")
              .Append("  rails warp ").Append(host.railsWarp).Append('\n');
            DockingWatcher dw = host.docking;
            if (dw.hasTarget)
                sb.Append("docking: target ").Append(dw.distance.ToString("0.0", Inv)).Append(" m (port ")
                  .Append(dw.guardDistance.ToString("0.0", Inv)).Append(" m), rel ")
                  .Append(dw.relSpeed.ToString("0.00", Inv)).Append(" m/s, range rate ").Append(dw.rangeRate.ToString("0.00", Inv))
                  .Append(", seenFar ").Append(dw.seenFar).Append(", armed ").Append(dw.armed)
                  .Append(dw.InGuard(host.settings.tuning) ? ", guard " + dw.guardTime.ToString("0.0", Inv) + " s" : "")
                  .Append(dw.guardBroken ? ", guard broken" : "").Append(dw.closePhase ? ", CLOSE" : "").Append('\n');
            if (st.motionQuietLeft > 0f) sb.Append("docking motion quiet ").Append(st.motionQuietLeft.ToString("0.0", Inv)).Append(" s\n");
            ChatterDirector cd = host.director;
            sb.Append("v2: profile ").Append(host.profile != null ? host.profile.Name : "-")
              .Append(host.evaMode ? "  EVA radio" : "").Append("  launch cap ").Append(host.launchPhase ? host.roster.maxBand.ToString() : "off")
              .Append("  panic offset ").Append(host.roster.panicOffset.ToString("0.00", Inv)).Append('\n');
            sb.Append("signal ").Append(host.signal.source).Append(' ').Append(host.signal.signal.ToString("0.00", Inv))
              .Append(host.signal.connected ? "" : " NO LINK").Append(host.signal.occluded ? " occluded" : "")
              .Append(host.signal.relayName != null ? "  via " + host.signal.relayName + " " + host.signal.relayKm.ToString("0.00", Inv) + " km" : "")
              .Append("  static ").Append(host.StaticLevel.ToString("0.00", Inv))
              .Append("  KSC ").Append(host.signal.distanceKm.ToString("0", Inv)).Append(" km")
              .Append("  delay ").Append(host.LightDelay.ToString("0.00", Inv)).Append(" s  dropout ").Append(host.Dropout.ToString("0.00", Inv)).Append('\n');
            sb.Append("duck ").Append(host.duckGain.ToString("0.00", Inv)).Append("  hold ").Append(cd.Holding ? "yes" : "no")
              .Append("  panic downgrades ").Append(cd.panicDowngrades).Append("  rate limited ").Append(cd.bucketDenied)
              .Append("  dock contact ").Append(host.lastDockSpeed.ToString("0.00", Inv)).Append(" m/s\n");
            sb.Append("plan nodes ").Append(host.plan.nodeCount);
            if (host.plan.nextBurnIn >= 0) sb.Append("  burn in ").Append(host.plan.nextBurnIn.ToString("0", Inv)).Append(" s");
            sb.Append("  descent ").Append(host.beeps.descent.Phase).Append("  Lonapse ").Append(PhraseClips.status).Append('\n');
            sb.Append("last: ").Append(cd.lastLine).Append('\n');
            sb.Append("blackout: ").Append(host.blackout.state);
            if (host.blackout.state == BlackoutState.Blackout) sb.Append(' ').Append(host.blackout.blackoutDuration.ToString("0.0", Inv)).Append(" s");
            sb.Append("  intercom ").Append(cd.blackoutIntercomLeft < 0f ? "-" : "in " + Math.Max(0f, cd.blackoutIntercomLeft).ToString("0.0", Inv) + " s");
            sb.Append('\n');
            // v3 context (req 3, 5)
            sb.Append("phase ").Append(cd.CurrentPhase ?? "-").Append(" (classifier ").Append(cd.ClassifiedPhase ?? "-")
              .Append(", force ").Append(cd.forcePhase ?? "auto").Append(")  cause ").Append(cd.DominantCause ?? "-")
              .Append("  body ").Append(s.body ?? "-").Append('\n');

            // plan v5 probe mode
            sb.Append("probe: ").Append(host.probeMode ? "on" : "off").Append("  core ").Append(host.probeCoreKey ?? "-")
              .Append("  voice ").Append(host.probeVoice != null ? host.probeVoice.Name + " (" + host.probeVoiceWhy + ")" : "-")
              .Append("  sleep ").Append(host.probeSleepState).Append("  EC ").Append((s.ecFrac * 100.0).ToString("0", Inv)).Append('%')
              .Append("  asleep flag ").Append(s.probeAsleep).Append('\n');
            if (host.probeMode) sb.Append("  ").Append(cd.ProbeStatus).Append('\n');
            sb.Append("crew ").Append(host.roster.Count).Append('\n');
            for (int i = 0; i < host.roster.voices.Count; i++)
            {
                KerbalVoice k = host.roster.voices[i];
                sb.Append("  ").Append(k.displayName).Append(k.isPilot ? " [pilot]" : "").Append("  sk ").Append(k.sk.ToString("0.00", Inv)).Append(' ').Append(k.BandName)
                  .Append("  pitch ").Append(k.basePitch.ToString("0.000", Inv));
                if (k.outDueToG) sb.Append("  OUT (G)");
                sb.Append('\n');
                // v4 persona (the full text is in the log through Describe)
                sb.Append("    persona ").Append(k.persona != null ? k.persona.ToString() : "-")
                  .Append("  courage ").Append(k.courage.ToString("0.00", Inv)).Append("  stupidity ").Append(k.stupidity.ToString("0.00", Inv))
                  .Append("  panicAt ").Append(k.panicAt.ToString("0.00", Inv)).Append(k.personaEffects ? "" : "  (effects off)").Append('\n');
            }
            // v4 faces and events
            sb.Append("face: ").Append(host.face != null ? (host.face.Status ?? "-") : "no face driver").Append('\n');
            GameEventHub ev = host.events;
            if (ev != null)
                sb.Append("events: sent ").Append(ev.sent).Append("  accepted ").Append(ev.accepted)
                  .Append("  last ").Append(string.IsNullOrEmpty(ev.lastEvent) ? "-" : ev.lastEvent)
                  .Append("  clock ").Append(ev.Now.ToString("0", Inv)).Append(" s\n");
            else sb.Append("events: no hub\n");
            AppendDirectorEvents(host.director);
            ChatterDirector d = host.director;
            float ttn = d.TimeToNext;
            sb.Append("next call ").Append(ttn < 0 ? "radio silent" : ttn.ToString("0.0", Inv) + " s")
              .Append("  T ").Append(d.currentInterval.ToString("0.0", Inv)).Append(" s  queue ").Append(d.QueueCount)
              .Append("  crew cap ").Append(d.CrewCap()).Append('\n');
            float itn = d.IntercomTimeToNext;
            sb.Append("intercom ").Append(itn < 0 ? "off" : "next " + itn.ToString("0", Inv) + " s")
              .Append("  mean interval ").Append(d.intercomInterval.ToString("0", Inv)).Append(" s  quiet coast ").Append(d.QuietCoast)
              .Append(" (").Append(d.quietTimer.ToString("0", Inv)).Append(" s)\n");
            if (host.beeps.descentActive) sb.Append("descent beeps d ").Append(host.beeps.descentD.ToString("0.00", Inv)).Append('\n');
            sb.Append("channels:\n");
            for (int i = 0; i < host.allChannels.Length; i++)
            {
                VoiceChannel c = host.allChannels[i];
                if (!c.Busy) continue;
                sb.Append("  ").Append(c.label).Append(c.intercom ? " [intercom] " : " ").Append(c.clipName);
                if (c.speaker != null) sb.Append(" (").Append(c.speaker.displayName).Append(')');
                sb.Append(" vol ").Append(c.src != null ? c.src.volume.ToString("0.00", Inv) : "-").Append('\n');
            }
            text = sb.ToString();
            lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
        }

        // Hook for the director's v4 event state. Looked up by name once, so this file does not depend on the exact
        // director API: a string member "EventStatus" (pending list) and a float member "SomberLeft" (seconds of
        // somber mood left, 0 or less = off). Members that are missing are simply not shown.
        private bool hookResolved;
        private MemberInfo hookPending, hookSomber;

        private static MemberInfo FindMember(Type t, string name)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public;
            MemberInfo m = t.GetProperty(name, bf);
            if (m == null) m = t.GetField(name, bf);
            return m;
        }

        private static object ReadMember(MemberInfo m, object o)
        {
            PropertyInfo p = m as PropertyInfo;
            if (p != null) return p.GetValue(o, null);
            FieldInfo f = m as FieldInfo;
            return f != null ? f.GetValue(o) : null;
        }

        private void AppendDirectorEvents(ChatterDirector cd)
        {
            if (cd == null) return;
            if (!hookResolved)
            {
                hookResolved = true;
                Type t = cd.GetType();
                hookPending = FindMember(t, "EventStatus");
                hookSomber = FindMember(t, "SomberLeft");
            }
            if (hookPending == null && hookSomber == null) return;
            sb.Append("director events:");
            if (hookPending != null) sb.Append(" pending ").Append(ReadMember(hookPending, cd) ?? "-");
            if (hookSomber != null)
            {
                object o = ReadMember(hookSomber, cd);
                double left = o != null ? Convert.ToDouble(o, Inv) : 0.0;
                sb.Append("  somber ").Append(left > 0.0 ? left.ToString("0", Inv) + " s" : "off");
            }
            sb.Append('\n');
        }

        public void Draw()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.fontSize = 11;
                style.wordWrap = false;
                style.normal.textColor = Color.white;
            }
            float h = 8f + lines * 14f;
            Rect r = new Rect(8f, 40f, 470f, h);
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 6f, r.y + 2f, r.width - 8f, h), text, style);
        }
    }
}
