// Signal chain math: which provider wins, distance to signal, body occlusion and static level.
// Takes plain doubles and arrays, no Unity types. Pure C#, tested offline.
using System;

namespace Lonapse
{
    public enum SignalSource { None, Override, CommNet, RemoteTech, Distance }

    public static class SignalMath
    {
        // Linear falloff from 1 at fullKm (or closer) to 0 at zeroKm (or farther). NaN gives 0.
        public static double FromDistance(double km, double fullKm, double zeroKm)
        {
            if (double.IsNaN(km)) return 0.0;
            if (km <= fullKm) return 1.0;
            if (km >= zeroKm || zeroKm <= fullKm) return 0.0;
            return (zeroKm - km) / (zeroKm - fullKm);
        }

        // True when the segment a to b passes within bodyRadius of the origin (the body centre).
        // a and b are 3D points in the same frame as the body centre.
        public static bool Occluded(double[] a, double[] b, double bodyRadius)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3) return false;
            double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
            double dd = dx * dx + dy * dy + dz * dz;
            double t = 0.0;
            if (dd > 0.0)
            {
                t = -(a[0] * dx + a[1] * dy + a[2] * dz) / dd;
                if (t < 0.0) t = 0.0;
                else if (t > 1.0) t = 1.0;
            }
            double cx = a[0] + t * dx, cy = a[1] + t * dy, cz = a[2] + t * dz;
            return Math.Sqrt(cx * cx + cy * cy + cz * cz) < bodyRadius;
        }

        // Provider chain: the first valid provider wins. Order: override, CommNet, RemoteTech, distance.
        // CommNet switched off in the game (and no RemoteTech) means a link everywhere, so None: full signal
        // (user decision). The distance fallback only stands in when CommNet is on but cannot be read.
        public static SignalSource Choose(bool overrideActive, bool commNetOn, bool commNetOk, bool rtOk, bool distanceOk)
        {
            if (overrideActive) return SignalSource.Override;
            if (commNetOn && commNetOk) return SignalSource.CommNet;
            if (rtOk) return SignalSource.RemoteTech;
            if (commNetOn && distanceOk) return SignalSource.Distance;
            return SignalSource.None;
        }

        // Plan v6 EVA relay: the suit radio reaches Mission Control through a nearby ship. The ship's signal is scaled by
        // the suit link (full up to fullKm, gone at zeroKm). The kerbal's own link wins when it is better. NaN gives 0.
        public static double RelaySignal(double own, double ship, double km, double fullKm, double zeroKm)
        {
            if (double.IsNaN(own) || own < 0.0) own = 0.0;
            if (double.IsNaN(ship) || ship < 0.0) ship = 0.0;
            double relay = Math.Min(1.0, ship) * FromDistance(km, fullKm, zeroKm);
            return Math.Min(1.0, Math.Max(own, relay));
        }

        // Plan v6 EVA relay: connected when the kerbal has its own link, or the ship has one and is within suit range.
        public static bool RelayConnected(bool own, bool ship, double km, double fullKm, double zeroKm)
        {
            return own || (ship && FromDistance(km, fullKm, zeroKm) > 0.0);
        }

        // Static bed level: maxStatic when there is no signal, 0 at full signal. Signal is clamped to 0 to 1.
        public static double StaticLevel(double signal, double maxStatic)
        {
            if (double.IsNaN(signal)) signal = 0.0;
            if (signal < 0.0) signal = 0.0;
            if (signal > 1.0) signal = 1.0;
            return maxStatic * (1.0 - signal);
        }
    }
}
