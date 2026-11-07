// Signal provider chain: override, CommNet, RemoteTech (reflection), distance to KSC.
// Polled at signalPollSec. A provider switch only sticks after signalHoldTime seconds. Every switch is logged.
// An EVA kerbal has no antenna of its own, so its suit radio relays through the best nearby loaded ship.
// The ship's signal, scaled by the suit range, counts (SignalMath.RelaySignal). No ship link means no EVA link.
using System;
using System.Reflection;
using UnityEngine;

namespace Lonapse
{
    public class SignalProvider
    {
        // KSC launch pad on Kerbin, used by the distance fallback (default stock coordinates)
        public const double KscLat = -0.0972, KscLon = -74.5577;

        public float signal = 1f;                      // 0..1, the value the mix uses
        public SignalSource source = SignalSource.None;
        public bool connected = true;                  // false only when CommNet or RemoteTech report no link
        public bool occluded;
        public double distanceKm;
        public double homeKm;                          // km from the surface of the home body (light delay)
        public string relayName;                       // EVA: the ship the suit radio relays through, null for none
        public double relayKm;                         // EVA: km to that ship

        /// <summary>One read of every provider for one vessel.</summary>
        private struct Link
        {
            public bool commNetOn, commNetOk, cnConnected, rtOk, rtConnected, distOk;
            public float cnSignal, rtSignal, distSignal;
        }

        private SignalSource candidate = SignalSource.None;
        private float candidateTime;
        private float pollLeft;
        private bool loggedRaw;

        // RemoteTech by reflection (soft dependency, never a hard reference)
        private bool rtProbed;
        private MethodInfo rtHasKsc;

        public void Reset()
        {
            pollLeft = 0f;
            candidate = SignalSource.None;
            candidateTime = 0f;
        }

        public void Update(float dt, Vessel v, Settings cfg)
        {
            pollLeft -= dt;
            candidateTime += dt;
            if (pollLeft > 0f) return;
            Tuning tu = cfg.tuning;
            pollLeft = Math.Max(0.1f, tu.signalPollSec);

            bool overrideActive = cfg.signalMode != "auto";
            // distance to the home body for the light delay, in every signal mode
            try
            {
                CelestialBody hb = FlightGlobals.GetHomeBody();
                if (v != null && hb != null) homeKm = Math.Max(0.0, ((v.GetWorldPos3D() - hb.position).magnitude - hb.Radius) / 1000.0);
            }
            catch (Exception) { homeKm = 0.0; }
            Link l = new Link { cnSignal = 1f, rtSignal = 1f, distSignal = 1f, cnConnected = true, rtConnected = true };
            relayName = null;
            relayKm = 0.0;
            if (!overrideActive && v != null)
            {
                l = ReadLink(v, cfg, true);
                bool eva = false;
                try { eva = v.isEVA; } catch (Exception) { eva = false; }
                if (eva && cfg.evaRelay) ApplyRelay(v, cfg, ref l);
            }
            bool commNetOn = l.commNetOn, commNetOk = l.commNetOk, rtOk = l.rtOk, distOk = l.distOk;
            float cnSignal = l.cnSignal, rtSignal = l.rtSignal, distSignal = l.distSignal;
            bool cnConnected = l.cnConnected, rtConnected = l.rtConnected;

            SignalSource pick = SignalMath.Choose(overrideActive, commNetOn, commNetOk, rtOk, distOk);
            if (pick != candidate) { candidate = pick; candidateTime = 0f; }
            if (pick != source && (source == SignalSource.None || candidateTime >= tu.signalHoldTime))
            {
                Log.Info("Signal provider: " + source + " -> " + pick);
                source = pick;
            }

            switch (source)
            {
                case SignalSource.Override:
                    signal = cfg.signalMode == "fixed" ? cfg.signalFixed : 1f;
                    connected = true;
                    break;
                case SignalSource.CommNet: signal = cnSignal; connected = cnConnected; break;
                case SignalSource.RemoteTech: signal = rtSignal; connected = rtConnected; break;
                case SignalSource.Distance: signal = distSignal; connected = true; break;
                default: signal = 1f; connected = true; break;
            }
        }

        /// <summary>Reads CommNet, RemoteTech and the distance fallback for one vessel. active: the active vessel
        /// (logs the raw CommNet value once and fills distanceKm and occluded).</summary>
        private Link ReadLink(Vessel v, Settings cfg, bool active)
        {
            Link l = new Link { cnSignal = 1f, rtSignal = 1f, distSignal = 1f, cnConnected = true, rtConnected = true };
            try
            {
                l.commNetOn = HighLogic.CurrentGame != null && HighLogic.CurrentGame.Parameters.Difficulty.EnableCommNet;
                if (l.commNetOn && v.Connection != null)
                {
                    l.cnConnected = v.Connection.IsConnected;
                    double raw = v.Connection.SignalStrength;
                    if (active && !loggedRaw) { loggedRaw = true; Log.Info("CommNet SignalStrength raw value " + raw.ToString("0.000")); }
                    l.cnSignal = l.cnConnected ? DcMath.Clamp01((float)raw) : 0f;
                    l.commNetOk = !double.IsNaN(raw);
                }
            }
            catch (Exception e) { l.commNetOk = false; Log.WarnOnce("signal:commnet", "CommNet signal not readable: " + e.Message); }

            l.rtOk = ReadRemoteTech(v, out l.rtConnected);
            l.rtSignal = l.rtConnected ? 1f : 0f;

            try { l.distOk = ReadDistance(v, cfg, active, out l.distSignal); }
            catch (Exception e) { l.distOk = false; Log.WarnOnce("signal:distance", "Distance signal failed: " + e.Message); }
            return l;
        }

        /// <summary>
        /// The EVA kerbal's link through the best loaded ship within suit range. Per provider the better of
        /// the kerbal's own link and the ship's link times the suit range wins. The ship must have a control point
        /// (a command pod or probe core), like a real relay would need one. Without a ship nothing changes.
        /// </summary>
        private void ApplyRelay(Vessel eva, Settings cfg, ref Link own)
        {
            Tuning tu = cfg.tuning;
            Vessel best = null;
            Link bestLink = default(Link);
            double bestKm = 0.0, bestScore = -1.0;
            try
            {
                Vector3d pos = eva.GetWorldPos3D();
                foreach (Vessel s in FlightGlobals.VesselsLoaded)
                {
                    if (s == null || ReferenceEquals(s, eva) || s.isEVA || !s.loaded) continue;
                    if (s.vesselType == VesselType.Debris || s.vesselType == VesselType.Flag || s.vesselType == VesselType.SpaceObject) continue;
                    if (!HasControlPoint(s)) continue;
                    double km = (s.GetWorldPos3D() - pos).magnitude / 1000.0;
                    double suit = SignalMath.FromDistance(km, tu.evaRelayFullKm, tu.evaRelayZeroKm);
                    if (suit <= 0.0) continue;
                    Link l = ReadLink(s, cfg, false);
                    double sig = l.commNetOn && l.commNetOk ? l.cnSignal : (l.rtOk ? l.rtSignal : (l.distOk ? l.distSignal : 1f));
                    double score = sig * suit;
                    if (score > bestScore) { bestScore = score; best = s; bestLink = l; bestKm = km; }
                }
            }
            catch (Exception e) { Log.WarnOnce("signal:relay", "EVA relay search failed: " + e.Message); return; }
            if (best == null) return;
            relayName = best.vesselName;
            relayKm = bestKm;
            double full = tu.evaRelayFullKm, zero = tu.evaRelayZeroKm;
            // CommNet: the kerbal's own Connection may be missing, the ship's reading then makes CommNet valid
            if (bestLink.commNetOn && bestLink.commNetOk)
            {
                bool ownCn = own.commNetOk && own.cnConnected;
                own.cnSignal = (float)SignalMath.RelaySignal(own.commNetOk ? own.cnSignal : 0f, bestLink.cnSignal, bestKm, full, zero);
                own.cnConnected = SignalMath.RelayConnected(ownCn, bestLink.cnConnected, bestKm, full, zero);
                own.commNetOn = true;
                own.commNetOk = true;
            }
            if (bestLink.rtOk)
            {
                bool ownRt = own.rtOk && own.rtConnected;
                own.rtConnected = SignalMath.RelayConnected(ownRt, bestLink.rtConnected, bestKm, full, zero);
                own.rtSignal = (float)SignalMath.RelaySignal(ownRt ? 1f : 0f, bestLink.rtConnected ? 1f : 0f, bestKm, full, zero);
                own.rtOk = true;
            }
            if (bestLink.distOk && own.distOk)
                own.distSignal = (float)SignalMath.RelaySignal(0f, bestLink.distSignal, bestKm, full, zero);
        }

        private static bool HasControlPoint(Vessel s)
        {
            if (s.parts == null) return false;
            for (int i = 0; i < s.parts.Count; i++)
            {
                Part p = s.parts[i];
                if (p != null && p.FindModuleImplementing<ModuleCommand>() != null) return true;
            }
            return false;
        }

        /// <summary>Static bed level for the mix. signalMode off: the static does not follow the signal.</summary>
        public float StaticLevel(Settings cfg)
        {
            if (cfg.signalMode == "off") return 0f;
            return (float)SignalMath.StaticLevel(signal, cfg.tuning.signalStaticMax);
        }

        private bool ReadDistance(Vessel v, Settings cfg, bool active, out float sig)
        {
            sig = 1f;
            CelestialBody home = FlightGlobals.GetHomeBody();
            if (home == null || v.mainBody == null) return false;
            Tuning tu = cfg.tuning;
            Vector3d ksc = home.GetWorldSurfacePosition(KscLat, KscLon, home.TerrainAltitude(KscLat, KscLon));
            Vector3d pos = v.GetWorldPos3D();
            double km = (pos - ksc).magnitude / 1000.0;
            bool occ = false;
            if (cfg.signalOcclusion && ReferenceEquals(v.mainBody, home))
            {
                Vector3d c = home.position;
                // KSC sits on the surface: lift both ends 50 m so a grazing line at the pad does not count
                Vector3d a = pos - c, b = ksc - c;
                b = b + b.normalized * 50.0;
                occ = SignalMath.Occluded(new double[] { a.x, a.y, a.z }, new double[] { b.x, b.y, b.z }, home.Radius);
            }
            if (active) { distanceKm = km; occluded = occ; }
            sig = occ ? 0f : (float)SignalMath.FromDistance(km, tu.signalRangeFullKm, tu.signalRangeZeroKm);
            return true;
        }

        private bool ReadRemoteTech(Vessel v, out bool connectedToKsc)
        {
            connectedToKsc = true;
            if (!rtProbed)
            {
                rtProbed = true;
                try
                {
                    foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies)
                    {
                        if (la == null || la.name != "RemoteTech") continue;
                        Type api = la.assembly.GetType("RemoteTech.API.API");
                        if (api != null) rtHasKsc = api.GetMethod("HasConnectionToKSC", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Guid) }, null);
                        break;
                    }
                    Log.Info(rtHasKsc != null ? "RemoteTech found, signal via RemoteTech.API.HasConnectionToKSC" : "RemoteTech not installed");
                }
                catch (Exception e) { rtHasKsc = null; Log.Info("RemoteTech probe failed: " + e.Message); }
            }
            if (rtHasKsc == null) return false;
            try
            {
                object r = rtHasKsc.Invoke(null, new object[] { v.id });
                if (!(r is bool)) return false;
                connectedToKsc = (bool)r;
                return true;
            }
            catch (Exception e)
            {
                Log.WarnOnce("signal:rt", "RemoteTech call failed: " + e.Message);
                return false;
            }
        }
    }
}
