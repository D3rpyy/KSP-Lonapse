// Offline tests for the pure-math parts of Lonapse. Compiled with -define:DC_OFFLINE_TEST against
// plain Mono (no KSP or Unity). Run with tests/run.sh.
using System;
using System.Collections.Generic;
using Lonapse;

public static class Tests
{
    static int pass, fail;

    static void Check(bool ok, string name, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + (detail != null ? "  (" + detail + ")" : "")); }
        else { fail++; Console.WriteLine("FAIL  " + name + (detail != null ? "  (" + detail + ")" : "")); }
    }

    static void Near(double actual, double expected, double tol, string name)
    {
        Check(Math.Abs(actual - expected) <= tol, name, "got " + actual.ToString("0.#####") + ", expected " + expected.ToString("0.#####") + " +- " + tol);
    }

    static FlightSample Calm()
    {
        FlightSample s = new FlightSample();
        s.valid = true; s.h = 100000; s.fuelFrac = 1; s.vs = 0;
        return s;
    }

    static StressModel Fresh()
    {
        StressModel m = new StressModel(new Tuning());
        m.warmupLeft = 0f;
        return m;
    }

    static void Run(StressModel m, FlightSample s, float seconds, float dt)
    {
        int n = (int)Math.Round(seconds / dt);
        for (int i = 0; i < n; i++) m.Tick(ref s, dt);
    }

    public static int Main()
    {
        // ---- FNV-1a test vectors ----
        Check(StableHash.Hash("Jebediah Kerman", 0) == 0x213D3FD0u, "fnv1a Jebediah", StableHash.Hash("Jebediah Kerman", 0).ToString("X8"));
        Check(StableHash.Hash("Bill Kerman", 0) == 0x91AF6BB9u, "fnv1a Bill", StableHash.Hash("Bill Kerman", 0).ToString("X8"));
        Check(StableHash.Hash("Bob Kerman", 0) == 0xCD0D01F1u, "fnv1a Bob", StableHash.Hash("Bob Kerman", 0).ToString("X8"));
        Check(StableHash.Hash("Valentina Kerman", 0) == 0xC07AB29Au, "fnv1a Valentina", StableHash.Hash("Valentina Kerman", 0).ToString("X8"));
        Check(StableHash.Fnv1a("") == 0x811C9DC5u, "fnv1a empty string = offset basis", null);

        Near(KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, true).basePitch, 0.929, 0.001, "basePitch Jeb");
        Near(KerbalVoice.Create("Bill Kerman", 0.5f, 0.5f, false).basePitch, 1.025, 0.001, "basePitch Bill");
        Near(KerbalVoice.Create("Bob Kerman", 0.5f, 0.5f, false).basePitch, 1.076, 0.001, "basePitch Bob");
        Near(KerbalVoice.Create("Valentina Kerman", 0.5f, 0.5f, true).basePitch, 1.065, 0.001, "basePitch Val");
        KerbalVoice j1 = KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, true), j2 = KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, true);
        Check(j1.tremRate == j2.tremRate && j1.timbre == j2.timbre && j1.rngSeed == j2.rngSeed, "voice profile is stable for the same name", null);

        // ---- helpers ----
        Near(DcMath.Ramp(5, 0, 10), 0.5, 1e-6, "Ramp mid");
        Near(DcMath.Ramp(-1, 0, 10), 0, 1e-6, "Ramp below");
        Near(DcMath.Ramp(11, 0, 10), 1, 1e-6, "Ramp above");
        Near(DcMath.Ramp(3, 3, 3), 1, 1e-6, "Ramp zero span at hi");
        Near(DcMath.Ramp(double.NaN, 0, 1), 0, 1e-6, "Ramp NaN -> 0");
        Near(DcMath.Smooth(0.5, 0, 1), 0.5, 1e-6, "Smooth mid");
        Near(DcMath.Smooth(0.25, 0, 1), 0.15625, 1e-6, "Smooth quarter");

        // ---- scheduler interval curve ----
        Near(DcMath.MeanInterval(0f, 110f, 1f, 1f), 110.0, 0.01, "T(0)");
        Near(DcMath.MeanInterval(0.25f, 110f, 1f, 1f), 34.0, 0.3, "T(0.25)");
        Near(DcMath.MeanInterval(0.5f, 110f, 1f, 1f), 10.5, 0.1, "T(0.5)");
        Near(DcMath.MeanInterval(0.75f, 110f, 1f, 1f), 3.2, 0.05, "T(0.75)");
        Near(DcMath.MeanInterval(1f, 110f, 1f, 1f), 1.0, 0.001, "T(1)");
        Near(DcMath.MeanInterval(0f, 110f, 1f, 2f), 55.0, 0.01, "T(0) with rate x2");
        Near(DcMath.MeanInterval(2f, 110f, 1f, 1f), 1.0, 0.001, "T clamps stress above 1");

        // simulate the progress accumulator at constant stress
        foreach (float st in new float[] { 0f, 0.5f, 1f })
        {
            Random rng = new Random(42);
            float T = DcMath.MeanInterval(st, 110f, 1f, 1f);
            float dt = 0.02f, progress = 0f, target = DcMath.Uniform(rng, 0.6f, 1.4f), since = 0f;
            List<float> gaps = new List<float>();
            for (int i = 0; i < 5000000 && gaps.Count < 2000; i++)
            {
                progress += dt / T; since += dt;
                if (progress >= target) { gaps.Add(since); since = 0f; progress = 0f; target = DcMath.Uniform(rng, 0.6f, 1.4f); }
            }
            double sum = 0, mn = double.MaxValue, mx = 0;
            foreach (float g in gaps) { sum += g; mn = Math.Min(mn, g); mx = Math.Max(mx, g); }
            double mean = sum / gaps.Count;
            Check(Math.Abs(mean - T) / T < 0.03 && mn >= 0.6 * T - dt - 1e-3 && mx <= 1.4 * T + dt + 1e-3,
                  "scheduler at stress " + st + ": mean ~T, range 0.6T..1.4T",
                  "T " + T.ToString("0.00") + " s, mean " + mean.ToString("0.00") + ", min " + mn.ToString("0.00") + ", max " + mx.ToString("0.00") + ", n " + gaps.Count);
        }

        // ---- combine ----
        float[] w = { 1.0f, 0.85f, 0.6f, 0.9f, 0.8f, 0.35f, 0.4f, 1.0f };
        float[] c = new float[8];
        Near(StressModel.Combine(c, w), 0, 1e-6, "combine all zero");
        c[0] = 1f;
        Near(StressModel.Combine(c, w), 1, 1e-6, "combine: spin alone reaches 1");
        c = new float[8]; c[2] = 0.5f; c[4] = 0.5f;
        Near(StressModel.Combine(c, w), 1 - (1 - 0.3) * (1 - 0.4), 1e-5, "combine: q 0.5 + heat 0.5 = 0.58 (probabilistic OR)");
        c = new float[8]; for (int i = 0; i < 8; i++) c[i] = 1f;
        Check(StressModel.Combine(c, w) <= 1f, "combine stays <= 1", null);

        // ---- stress model ----
        Tuning tu = new Tuning();
        {
            StressModel m = Fresh();
            FlightSample s = Calm(); s.omega = 6.0;
            Run(m, s, 3.0f, 0.01f);
            Near(m.stress, 1 - Math.Exp(-1), 0.02, "attack: 63% after attackTau (3 s) of manic spin");
            Run(m, s, 25f, 0.01f);
            Check(m.stress > 0.99f, "attack: spin saturates stress to ~1", "stress " + m.stress.ToString("0.000"));
            float peak = m.stress;
            s.omega = 0;
            Run(m, s, 1.4f, 0.01f);
            Near(m.stress, peak, 1e-4, "hold: no decay during the 1.5 s peak hold");
            Run(m, s, 0.1f + 9f, 0.01f);
            Near(m.stress, peak * Math.Exp(-1), 0.02, "decay: 37% left one decayTau (9 s) after the hold");
        }
        {
            StressModel m = new StressModel(new Tuning()); // warmup active
            FlightSample s = Calm(); s.g = 10;
            Run(m, s, 2.9f, 0.02f);
            Check(m.comp[StressModel.GForce] == 0f && m.gStrain == 0f, "warmup: G ignored for the first 3 s", "gforce " + m.comp[1] + ", gStrain " + m.gStrain);
            Run(m, s, 1.5f, 0.02f);
            Near(m.comp[StressModel.GForce], 1.0, 1e-3, "after warmup: 10 g gives gforce component 1");
            Check(m.gStrain > 0.9f, "after warmup: gStrain rises at 10 g", "gStrain " + m.gStrain.ToString("0.000"));
            s.packed = true;
            Run(m, s, 0.1f, 0.02f);
            Check(m.comp[StressModel.GForce] == 0f, "packed: G forced to 0", null);
        }
        {
            Near(StressModel.GroundComponent(false, -30, 300, tu), DcMath.Ramp(30, 6, 40) * Math.Sqrt(DcMath.Ramp(15, 0, 22)), 1e-5, "ground: vs -30 at 300 m (tti 10 s)");
            Near(StressModel.GroundComponent(true, -30, 300, tu), 0, 0, "ground: 0 when landed");
            Near(StressModel.GroundComponent(false, 5, 300, tu), 0, 0, "ground: 0 when climbing");
            Near(StressModel.GroundComponent(false, -30, 6000, tu), 0, 0, "ground: 0 above 5000 m");
            Near(StressModel.GroundComponent(false, -5, 10, tu), 0, 1e-6, "ground: 0 for a gentle 5 m/s touchdown");
        }
        {
            StressModel m = Fresh();
            FlightSample s = Calm(); s.throttle = 1; s.fuelFrac = 0.05;
            m.Tick(ref s, 0.02f);
            Near(m.comp[StressModel.Fuel], 0.5, 1e-5, "fuel: 5% left under throttle -> 0.5");
            s.throttle = 0; m.Tick(ref s, 0.02f);
            Near(m.comp[StressModel.Fuel], 0, 0, "fuel: 0 with throttle off");
        }
        {
            StressModel m = Fresh();
            FlightSample s = Calm(); s.shockTemp = 1500; s.atmDensity = 1e-3; s.qKPa = 10; s.srfSpeed = 150;
            m.Tick(ref s, 0.02f);
            Near(m.plasmaRaw, 0.5, 1e-5, "plasma: qv 1500 between 500 and 2500 -> 0.5");
            s.shockTemp = 900; m.Tick(ref s, 0.02f);
            Near(m.plasmaRaw, 0, 0, "plasma: 0 below CommNet shock temperature");
            s.inPlasmaStock = true; m.Tick(ref s, 0.02f);
            Near(m.plasmaRaw, 1, 0, "plasma: stock InPlasma forces 1");
            Run(m, s, 3f, 0.02f);
            Check(m.plasma > 0.99f, "plasma: smoothed index follows (tau 0.5 s)", "p " + m.plasma.ToString("0.000"));
        }
        {
            StressModel m = Fresh();
            m.AddImpulse(0.4f);
            FlightSample s = Calm();
            Run(m, s, 4f, 0.01f);
            Near(m.impulse, 0.4 * Math.Exp(-1), 0.005, "impulse decays exp(-t/4 s)");
            m.AddImpulse(5f);
            Check(m.impulse <= 1f, "impulse capped at 1", null);
        }

        // ---- a reaction wheel roll does not cause panic, sudden events still count at once ----
        {
            StressModel m = Fresh();
            FlightSample s = Calm(); s.omega = 2.5;   // small ship rolled with E on reaction wheels
            Run(m, s, 10f, 0.02f);
            Check(m.stress < 0.45f, "spin: a 2.5 rad/s roll stays far below panic even after 10 s", "stress " + m.stress.ToString("0.00"));
            m = Fresh(); s = Calm(); s.omega = 6.0;
            Run(m, s, 1f, 0.02f);
            Check(m.stress < 0.35f, "spin: a real tumble needs a few seconds to build up", "stress " + m.stress.ToString("0.00"));
            m = Fresh(); s = Calm();
            m.AddImpulse(0.4f, true);
            Run(m, s, 0.35f, 0.01f);
            Check(m.stress > 0.2f, "impulse: a part death still raises stress at once", "stress " + m.stress.ToString("0.00"));
            Settings m5 = new Settings(); m5.tuning.spinLo = 0.35f; m5.tuning.spinHi = 2.5f; m5.tuning.attackTau = 0.35f;
            m5.MigrateValues(5);
            Check(m5.tuning.spinLo == 1f && m5.tuning.spinHi == 5f && m5.tuning.attackTau == 3f, "migration v6: old spin and attack defaults replaced", null);
        }
        // ---- warmup suppresses spin and impulse too ----
        {
            StressModel m = new StressModel(new Tuning()); // warmup active
            FlightSample s = Calm(); s.omega = 6.0;
            m.AddImpulse(0.4f);
            Run(m, s, 2.9f, 0.02f);
            Check(m.comp[StressModel.Spin] == 0f && m.comp[StressModel.Impulse] == 0f && m.stress == 0f,
                  "warmup: spin and impulse ignored, stress stays 0 on a jittering vessel", "stress " + m.stress);
            Run(m, s, 0.5f, 0.02f);
            Check(m.comp[StressModel.Spin] > 0.99f, "after warmup: spin counts again", null);
            s.packed = true; m.Tick(ref s, 0.02f);
            Check(m.comp[StressModel.Spin] == 0f, "packed: spin forced to 0", null);
        }

        // ---- fuel groups ----
        {
            // index order: LF, Ox, Solid, Mono, Xenon
            Func<double[], double[], bool[], bool, double> F = FuelMath.Fraction;
            bool[] none = new bool[5];
            Near(F(new double[] { 900, 1100, 0, 0, 0 }, new double[] { 900, 1100, 300, 0, 0 }, none, false), 1.0, 1e-9,
                 "fuel: empty SRB/escape tower ignored, full LF+Ox -> 1");
            Near(F(new double[] { 0, 0, 0, 0, 0 }, new double[] { 0, 0, 300, 0, 0 }, none, false), 1.0, 1e-9,
                 "fuel: only an empty SRB on board -> 1 (no fuel stress)");
            Near(F(new double[] { 45, 55, 0, 0, 0 }, new double[] { 900, 1100, 300, 0, 0 }, none, false), 0.05, 1e-9,
                 "fuel: LF+Ox at 5%, empty SRB -> 0.05");
            Near(F(new double[] { 900, 55, 0, 0, 0 }, new double[] { 900, 1100, 0, 0, 0 }, none, false), 0.05, 1e-9,
                 "fuel: LF+Ox is one group, min of members (oxidizer at 5%)");
            Near(F(new double[] { 45, 55, 150, 0, 0 }, new double[] { 900, 1100, 300, 0, 0 }, none, false), 0.5, 1e-9,
                 "fuel: max over groups (solid 50% while liquid 5%)");
            Near(F(new double[] { 900, 1100, 0, 1, 0 }, new double[] { 900, 1100, 0, 100, 0 }, none, false), 1.0, 1e-9,
                 "fuel: monoprop ignored without engine info");
            Near(F(new double[] { 900, 1100, 0, 1, 0 }, new double[] { 900, 1100, 0, 100, 0 }, new bool[] { false, false, false, true, false }, true), 0.01, 1e-9,
                 "fuel: monoprop counts when an ignited engine burns it");
            Near(F(new double[] { 45, 1100, 0, 0, 0 }, new double[] { 900, 1100, 0, 0, 0 }, new bool[] { true, false, false, false, false }, true), 0.05, 1e-9,
                 "fuel: jet engine (LF only) ignores oxidizer");
            Near(F(new double[] { 0, 0, 0, 0, 0 }, new double[] { 900, 1100, 0, 0, 0 }, none, false), 0.0, 1e-9,
                 "fuel: liquid really empty -> 0");
            StressModel m = Fresh();
            FlightSample s = Calm(); s.throttle = 1;
            s.fuelFrac = F(new double[] { 900, 1100, 0, 0, 0 }, new double[] { 900, 1100, 300, 0, 0 }, none, false);
            m.Tick(ref s, 0.02f);
            Near(m.comp[StressModel.Fuel], 0, 0, "fuel: full tanks plus spent SRB under throttle gives no fuel stress");
        }

        // ---- rails warp gate ----
        Check(DcMath.IsRailsWarp(true, 5f), "warp: rails 5x closes the gate", null);
        Check(DcMath.IsRailsWarp(true, 2f), "warp: rails 2x closes the gate (no more >10x rule)", null);
        Check(!DcMath.IsRailsWarp(true, 1f), "warp: rails mode at 1x is not warp", null);
        Check(!DcMath.IsRailsWarp(false, 4f), "warp: physics warp 4x keeps playing", null);

        // ---- docking watcher ----
        {
            Tuning t = new Tuning();
            float dt = 0.1f;
            // normal approach from far away: closing at 1 m/s
            DockingWatcher dw = new DockingWatcher();
            object tgt = new object();
            int fires = 0; float d = 150f; int frames = 0, fireFrame = -1;
            while (d > 1f) { if (dw.Update(true, tgt, d, -1f, 1f, dt, t)) { fires++; fireFrame = frames; } d -= 1f * dt; frames++; }
            Check(fires == 1, "docking: approach from 150 m at 1 m/s gives exactly one call", "fires " + fires);
            float distAtFire = 150f - fireFrame * dt;
            Check(distAtFire < 50f && distAtFire > 47.5f, "docking: call about 1 s after crossing 50 m", "at " + distAtFire.ToString("0.0") + " m");

            // loaded next to a targeted vessel (never seen far): no call even when closing
            dw = new DockingWatcher(); fires = 0; d = 30f;
            for (int i = 0; i < 200; i++) { if (dw.Update(true, tgt, d, -1f, 0.5f, dt, t)) fires++; d -= 0.05f; }
            Check(fires == 0, "docking: no call when the target was never seen beyond 50 m (load next to it)", null);

            // after undock: reset, then moving away inside 50 m
            dw = new DockingWatcher();
            for (int i = 0; i < 20; i++) dw.Update(true, tgt, 80f, -1f, 0.5f, dt, t); // seen far earlier
            dw.Reset(); fires = 0; d = 1f;
            for (int i = 0; i < 300; i++) { if (dw.Update(true, tgt, d, -1f, 0.5f, dt, t)) fires++; d += 0.05f; }
            Check(fires == 0, "docking: no call after undock while drifting away", null);

            // seen far, but now parked inside 50 m (range not closing)
            dw = new DockingWatcher(); fires = 0;
            for (int i = 0; i < 20; i++) dw.Update(true, tgt, 60f, -1f, 0.1f, dt, t);
            for (int i = 0; i < 300; i++) if (dw.Update(true, tgt, 40f, -1f, 0.1f, dt, t)) fires++;
            Check(fires == 0, "docking: no call after a 20 m jump into range, then parked (range rate ~0)", null);
            dw = new DockingWatcher(); fires = 0; d = 60f;
            while (d > 45f) { dw.Update(true, tgt, d, -1f, 1f, dt, t); d -= 1f * dt; } // drifts in, stops right after 50 m
            for (int i = 0; i < 300; i++) if (dw.Update(true, tgt, 45f + 0.01f * (float)Math.Sin(i), -1f, 0.03f, dt, t)) fires++;
            int firesAfterStop = fires;
            Check(firesAfterStop <= 1, "docking: parked with jitter inside 50 m gives no repeated calls", "fires " + fires);
            // moving away inside 50 m after being far
            for (int i = 0; i < 100; i++) if (dw.Update(true, tgt, 40f + i * 0.05f, -1f, 0.5f, dt, t)) fires++;
            Check(fires == 0, "docking: no call while opening the range", null);

            // fast approach does not count
            dw = new DockingWatcher(); fires = 0; d = 120f;
            while (d > 5f) { if (dw.Update(true, tgt, d, -1f, 5f, dt, t)) fires++; d -= 5f * dt; }
            Check(fires == 0, "docking: fast approach (5 m/s) gives no call", null);

            // re-arm rules
            dw = new DockingWatcher(); d = 80f;
            while (d > 20f) { dw.Update(true, tgt, d, -1f, 1f, dt, t); d -= 0.1f; }
            Check(dw.fired && !dw.armed, "docking: disarmed after the call", null);
            dw.Update(true, tgt, 70f, -1f, 1f, dt, t);
            Check(!dw.armed, "docking: 70 m is not enough to re-arm after a call", null);
            dw.Update(true, tgt, 120f, -1f, 1f, dt, t);
            Check(dw.armed, "docking: re-armed beyond 100 m", null);
            dw.Update(true, new object(), 30f, -1f, 1f, dt, t);
            Check(!dw.armed && !dw.seenFar, "docking: new target starts unarmed until seen far", null);
            dw.OnDocked();
            Check(!dw.armed && !dw.seenFar, "docking: after docking the next approach must start far away", null);

            // guard uses the port distance, capped at dockGuardMax, ends when broken
            dw = new DockingWatcher();
            dw.Update(true, tgt, 6f, 2.5f, 0.2f, dt, t);
            Check(dw.InGuard(t), "docking guard: port distance 2.5 m engages (vessel distance 6 m)", null);
            dw.Update(true, tgt, 6f, 4f, 0.2f, dt, t);
            Check(!dw.InGuard(t), "docking guard: port distance 4 m does not", null);
            int guardFrames = 0;
            for (int i = 0; i < 100; i++) { dw.Update(true, tgt, 5f, 1f, 0.1f, dt, t); if (dw.InGuard(t)) guardFrames++; }
            Near(guardFrames * dt, 5.0 - dt, 0.15, "docking guard: capped at about 5 s per approach");
            dw = new DockingWatcher();
            dw.Update(true, tgt, 5f, 1f, 0.1f, dt, t);
            dw.BreakGuard();
            dw.Update(true, tgt, 5f, 1f, 0.1f, dt, t);
            Check(!dw.InGuard(t), "docking guard: ends when parts broke", null);
            dw.Update(false, null, 0f, -1f, 0f, dt, t);
            Check(!dw.InGuard(t) && !dw.hasTarget, "docking: no target, no guard", null);
        }
        {
            StressModel m = Fresh();
            FlightSample s = Calm(); s.omega = 1.5;
            m.QuietMotion(2f);
            m.AddImpulse(0.4f);
            Run(m, s, 1.9f, 0.02f);
            Check(m.stress == 0f && m.impulse == 0f, "docking quiet: spin and gentle impulses ignored for 2 s around contact", "stress " + m.stress);
            Run(m, s, 0.5f, 0.02f);
            Check(m.comp[StressModel.Spin] > 0f, "docking quiet: spin counts again afterwards", null);

            m = Fresh(); s = Calm();
            m.QuietMotion(2f);
            m.AddImpulse(0.4f, true);
            m.Tick(ref s, 0.02f);
            Check(m.impulse > 0.39f && m.comp[StressModel.Impulse] > 0.39f, "docking quiet: part deaths (critical impulse) still count", null);
            m.EndQuietMotion();
            s.omega = 6.0; Run(m, s, 0.1f, 0.02f);
            Check(m.comp[StressModel.Spin] > 0.99f, "docking quiet: EndQuietMotion restores spin at once", null);
        }

        // ---- warp ping cooldown, intercom pitch gap ----
        Check(DcMath.WarpPingAllowed(100f, -999f, 15f), "warp ping: first one allowed", null);
        Check(!DcMath.WarpPingAllowed(110f, 100f, 15f), "warp ping: no second ping within 15 s", null);
        Check(DcMath.WarpPingAllowed(116f, 100f, 15f), "warp ping: allowed again after 15 s", null);
        Near(DcMath.PairPitchNudge(1.00f, 1.20f, 0.05f), 1.0, 1e-6, "intercom pitch: no nudge when far apart");
        Near(1.02f * DcMath.PairPitchNudge(1.00f, 1.02f, 0.05f), 1.05, 1e-4, "intercom pitch: second voice pushed up to a 0.05 gap");
        Near(0.99f * DcMath.PairPitchNudge(1.00f, 0.99f, 0.05f), 0.95, 1e-4, "intercom pitch: second voice pushed down to a 0.05 gap");
        Near(1.00f * DcMath.PairPitchNudge(1.00f, 1.00f, 0.05f), 1.05, 1e-4, "intercom pitch: identical pitches get the full gap");
        {
            KerbalVoice jeb = KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, true), bob = KerbalVoice.Create("Bob Kerman", 0.5f, 0.5f, false);
            KerbalVoice val = KerbalVoice.Create("Valentina Kerman", 0.5f, 0.5f, true);
            float gap = Math.Abs(bob.basePitch - val.basePitch * DcMath.PairPitchNudge(bob.basePitch, val.basePitch, 0.05f));
            Check(gap >= 0.0499f, "intercom pitch: Bob and Val (1.076 vs 1.065) end up >= 0.05 apart", "gap " + gap.ToString("0.000"));
            Check(Math.Abs(jeb.basePitch - bob.basePitch) >= 0.05f, "intercom pitch: Jeb and Bob already distinct", null);
        }

        // ---- station mode and intercom ----
        Near(DcMath.MeanInterval(0f, 110f, 1f, 1f * 0.5f), 220.0, 0.01, "station: rate factor 0.5 doubles the quiet interval");
        Near(DcMath.IntercomInterval(240f, false, false, 2.5f, 2f), 240.0, 1e-3, "intercom: base interval 240 s");
        Near(DcMath.IntercomInterval(240f, true, false, 2.5f, 2f), 96.0, 1e-3, "intercom: station/base 2.5x more often");
        Near(DcMath.IntercomInterval(240f, false, true, 2.5f, 2f), 120.0, 1e-3, "intercom: quiet coast 2x more often");
        Near(DcMath.IntercomInterval(240f, true, true, 2.5f, 2f), 48.0, 1e-3, "intercom: station and quiet boosts combine");
        Check(KerbalVoice.Calmer(Band.Panic) == Band.Tense && KerbalVoice.Calmer(Band.Tense) == Band.Calm && KerbalVoice.Calmer(Band.Calm) == Band.Calm,
              "station: relaxed mix moves one band calmer", null);
        {
            List<KerbalVoice> crewList = new List<KerbalVoice>();
            crewList.Add(KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, true));
            KerbalVoice a, b;
            Random rng = new Random(3);
            Check(!KerbalVoice.PickPair(crewList, rng, out a, out b), "intercom: no chat with 1 crew", null);
            crewList.Add(KerbalVoice.Create("Bob Kerman", 0.3f, 0.1f, false));
            crewList.Add(KerbalVoice.Create("Bill Kerman", 0.5f, 0.8f, false));
            bool distinct = true;
            for (int i = 0; i < 10000; i++)
            {
                if (!KerbalVoice.PickPair(crewList, rng, out a, out b) || ReferenceEquals(a, b)) { distinct = false; break; }
            }
            Check(distinct, "intercom: always two different voices with 3 crew", null);
            crewList[1].outDueToG = true; crewList[2].outDueToG = true;
            Check(!KerbalVoice.PickPair(crewList, rng, out a, out b) && KerbalVoice.SpeakableCount(crewList) == 1,
                  "intercom: kerbals out due to G do not count", null);
        }

        // ---- crew delivery ----
        Near(DcMath.CrewPitch(1.0f, 0f, 0f, 2f, 1.2f), 1.0, 1e-6, "pitch: calm kerbal at base pitch");
        Near(DcMath.CrewPitch(1.0f, 1f, 0f, 2f, 1.2f), Math.Pow(2, 2.0 / 12), 1e-4, "pitch: stress 1 adds 2 semitones (x1.122)");
        Near(DcMath.CrewPitch(1.12f, 1f, 0f, 2f, 1.2f), 1.2, 1e-6, "pitch: total capped at 1.2");
        Near(DcMath.CrewPitch(1.0f, 0f, 1f, 2f, 1.2f), 0.88, 1e-6, "pitch: full G strain lowers 12%");
        Near(DcMath.CrewPitch(1.0f, 1f, 0f, 0.5f, 1.12f, true), 1.0, 1e-6, "pitch: recorded panic clip gets no stress raise");
        Near(DcMath.CrewPitch(1.05f, 1f, 1f, 0.5f, 1.12f, true), 1.05 * 0.88, 1e-6, "pitch: recorded panic keeps base pitch and G drop");
        Near(DcMath.CrewPitch(1.0f, 1f, 0f, 0.5f, 1.12f, false), DcMath.CrewPitch(1.0f, 1f, 0f, 0.5f, 1.12f), 1e-6, "pitch: other clips unchanged");
        Check(DcMath.TruncFraction(1f, 0.85f, 0.8f, true) == 1f && Math.Abs(DcMath.TruncFraction(1f, 0.85f, 0.8f, false) - 0.8f) < 1e-6,
              "truncation: a recorded panic clip is never cut, other lines still are", null);
        Near(DcMath.CrewVolume(0f), 0.70, 1e-6, "volume: calm crew line 0.70");
        Near(DcMath.CrewVolume(1f), 0.90, 1e-6, "volume: full stress crew line 0.90 (about 2 dB louder)");
        Near(20 * Math.Log10(DcMath.CrewVolume(1f) / DcMath.CrewVolume(0f)), 2.18, 0.05, "volume: crew stress range about 2 dB");
        Check(DcMath.CrewVolume(5f) == DcMath.CrewVolume(1f) && DcMath.CalloutVolume(0f) > DcMath.CrewVolume(0f) && DcMath.McVolume == 0.75f,
              "volume: clamped, callouts a little higher, Mission Control steady", null);
        Near(DcMath.TruncFraction(0.8f, 0.85f, 0.8f), 1.0, 1e-6, "truncation: none below sk 0.85");
        Near(DcMath.TruncFraction(1.0f, 0.85f, 0.8f), 0.8, 1e-6, "truncation: at most 20% cut at sk 1");
        {
            Tuning t3 = new Tuning();
            Near(DcMath.DescentInterval(1f, t3.descentSlow, t3.descentFast), 1.5, 1e-6, "descent blips: never faster than 1.5 s");
            Near(DcMath.DescentInterval(0f, t3.descentSlow, t3.descentFast), 5.0, 1e-6, "descent blips: 5 s when barely descending");
            Check(t3.descentMaxH == 500f && t3.descentVolHi <= 0.5f, "descent blips: only below 500 m, softer volume", null);
        }

        // ---- intercom click ----
        {
            float[] c1 = ClickSynth.Generate(44100, 12345, 0.025f), c2 = ClickSynth.Generate(44100, 12345, 0.025f);
            float peak = 0f; double e1 = 0, e2 = 0;
            for (int i = 0; i < c1.Length; i++) { peak = Math.Max(peak, Math.Abs(c1[i])); if (i < c1.Length / 2) e1 += c1[i] * c1[i]; else e2 += c1[i] * c1[i]; }
            bool same = c1.Length == c2.Length; for (int i = 0; same && i < c1.Length; i++) same = c1[i] == c2[i];
            Check(c1.Length == 1102, "click: 25 ms at 44.1 kHz", "samples " + c1.Length);
            Near(peak, 0.8, 1e-4, "click: normalized to 0.8 peak");
            Check(Math.Abs(c1[c1.Length - 1]) < 1e-6 && e1 > e2, "click: decays and ends at zero", null);
            Check(same, "click: deterministic for a seed", null);
        }

        // ---- callouts (staging, parachutes) ----
        {
            CalloutGate g = new CalloutGate();
            Check(g.TryShared(10f, 1.5f), "callout: first stage callout allowed", null);
            Check(g.OnChutes(10.3f, 3, 0, 5f, 3f, 1.5f) == ChuteCallout.None, "callout: staging that deploys chutes gives no second callout within 1.5 s", null);
            Check(g.OnChutes(12f, 1, 0, 5f, 3f, 1.5f) == ChuteCallout.None, "callout: chutes of the same group (within 5 s) stay silent", null);
            Check(g.OnChutes(20f, 2, 0, 5f, 3f, 1.5f) == ChuteCallout.Semi, "callout: a new chute group speaks once", null);
            Check(g.OnChutes(21f, 1, 0, 5f, 3f, 1.5f) == ChuteCallout.None, "callout: second chute 1 s later is grouped", null);
            Check(g.OnChutes(22f, 0, 2, 5f, 3f, 1.5f) == ChuteCallout.None, "callout: full deployment within 3 s of semi is silent", null);
            Check(g.OnChutes(25f, 0, 1, 5f, 3f, 1.5f) == ChuteCallout.None, "callout: full-deploy group consumed (within 5 s)", null);
            Check(g.OnChutes(35f, 0, 2, 5f, 3f, 1.5f) == ChuteCallout.Full, "callout: full deployment later speaks once", null);
            Check(!g.TryShared(35.5f, 1.5f), "callout: staging 0.5 s after a chute callout is suppressed", null);
            Check(g.TryShared(37f, 1.5f), "callout: allowed again after 1.5 s", null);
            CalloutGate g2 = new CalloutGate();
            Check(g2.OnChutes(5f, 1, 1, 5f, 3f, 1.5f) == ChuteCallout.Semi, "callout: semi and full in the same poll speak once (semi)", null);
        }
        {
            List<KerbalVoice> cl = new List<KerbalVoice>();
            KerbalVoice eng = KerbalVoice.Create("Bill Kerman", 0.5f, 0.8f, false);
            KerbalVoice pil = KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, true); pil.isPilot = true;
            cl.Add(eng); cl.Add(pil);
            Random rng = new Random(5);
            bool alwaysPilot = true;
            for (int i = 0; i < 200; i++) if (KerbalVoice.PickCallout(cl, rng) != pil) alwaysPilot = false;
            Check(alwaysPilot, "callout: the pilot speaks", null);
            pil.outDueToG = true;
            Check(KerbalVoice.PickCallout(cl, rng) == eng, "callout: falls back to other crew when the pilot is out", null);
            eng.outDueToG = true;
            Check(KerbalVoice.PickCallout(cl, rng) == null, "callout: nobody when all are out", null);
        }

        // ---- docking close-approach phase ----
        {
            Tuning t = new Tuning();
            DockingWatcher dw = new DockingWatcher();
            object tgt = new object();
            float dt = 0.1f, d = 80f;
            int started = 0, exchanges = 0; List<float> callouts = new List<float>();
            float inClose = 0f;
            while (d > 0.5f)
            {
                dw.Update(true, tgt, d, d, 0.5f, dt, t);
                if (dw.closePhaseJustStarted) { started++; dw.closePhaseJustStarted = false; }
                if (dw.calloutDue > 0f) { callouts.Add(dw.calloutDue); dw.calloutDue = -1f; }
                if (dw.closeExchangeDue) { exchanges++; dw.closeExchangeDue = false; }
                if (dw.closePhase) inClose += dt;
                d -= 0.5f * dt;
            }
            Check(started == 1, "docking close: phase starts once below 30 m", "started " + started);
            Check(callouts.Count == 3 && callouts[0] == 30f && callouts[1] == 15f && callouts[2] == 5f,
                  "docking close: callouts at 30, 15 and 5 m", "callouts " + string.Join(",", callouts.ConvertAll(x => x.ToString()).ToArray()));
            double expected = inClose / ((t.dockCloseMin + t.dockCloseMax) / 2.0);
            Check(exchanges >= 3 && exchanges <= 8, "docking close: exchanges every 8 to 15 s", "exchanges " + exchanges + " over " + inClose.ToString("0") + " s (about " + expected.ToString("0.0") + " expected)");
            dw.OnDocked();
            Check(!dw.closePhase, "docking close: ends on docking", null);

            // parked within 30 m: no phase, no callouts
            dw = new DockingWatcher();
            for (int i = 0; i < 30; i++) dw.Update(true, tgt, 60f, 60f, 0.1f, dt, t);
            int calls = 0;
            for (int i = 0; i < 300; i++) { dw.Update(true, tgt, 20f, 20f, 0.02f, dt, t); if (dw.calloutDue > 0f || dw.closeExchangeDue) calls++; dw.calloutDue = -1f; dw.closeExchangeDue = false; }
            Check(calls == 0 && !dw.closePhase, "docking close: nothing while parked (not closing)", null);

            // backing out beyond 40 m ends the phase
            dw = new DockingWatcher(); d = 60f;
            while (d > 25f) { dw.Update(true, tgt, d, d, 0.5f, dt, t); d -= 0.5f * dt; }
            bool wasClose = dw.closePhase;
            while (d < 45f) { dw.Update(true, tgt, d, d, 0.5f, dt, t); d += 0.5f * dt; }
            Check(wasClose && !dw.closePhase, "docking close: backing out beyond 40 m ends the phase", null);
        }

        // ---- settings migration ----
        {
            Settings old = new Settings();
            old.tuning.intercomInterval = 240f; old.tuning.stationMcFactor = 0.5f; old.tuning.descentMaxH = 3000f;
            old.tuning.descentFast = 0.25f; old.tuning.spinHi = 1.8f; old.maxCrewVoices = 3;
            old.rateMultiplier = 4f; old.crewVolume = 0.83f;
            List<string> reset = old.MigrateValues(2);
            Check(old.tuning.intercomInterval == 60f && old.tuning.stationMcFactor == 1f && old.tuning.descentMaxH == 500f
                  && old.tuning.descentFast == 1.5f && old.maxCrewVoices == 2,
                  "migration: changed defaults reset for a 0.2 file", reset.Count + " value(s) reset");
            Check(old.tuning.spinHi == 1.8f && old.rateMultiplier == 4f && old.crewVolume == 0.83f && old.tuningVersion == Tuning.CurrentVersion,
                  "migration: other user values kept, version bumped", null);
            Settings cur = new Settings(); cur.tuning.intercomInterval = 900f; cur.tuningVersion = Tuning.CurrentVersion;
            Check(cur.MigrateValues(Tuning.CurrentVersion).Count == 0 && cur.tuning.intercomInterval == 900f,
                  "migration: a current file is left alone", null);
        }

        // ---- stress-driven intercom ----
        {
            Tuning t = new Tuning();
            float lo = DcMath.IntercomInterval(t.intercomInterval, false, false, t.intercomStationBoost, t.intercomQuietBoost);
            Near(lo, 60.0, 1e-3, "intercom: 60 s on ordinary ships at stress 0");
            Near(DcMath.IntercomIntervalStress(lo, 0f, t.intercomStressMin, t.intercomStressPeak), 60.0, 1e-2, "intercom: stress 0 keeps the base interval");
            Near(DcMath.IntercomIntervalStress(lo, 0.3f, t.intercomStressMin, t.intercomStressPeak), Math.Sqrt(60.0 * 25.0), 0.05, "intercom: stress 0.3 is the log midpoint (~39 s)");
            Near(DcMath.IntercomIntervalStress(lo, 0.6f, t.intercomStressMin, t.intercomStressPeak), 25.0, 1e-3, "intercom: 25 s at stress 0.6");
            Near(DcMath.IntercomIntervalStress(lo, 0.68f, t.intercomStressMin, t.intercomStressPeak), 25.0, 1e-3, "intercom: flat above the peak");
            bool mono = true; float prev = float.MaxValue;
            for (int i = 0; i <= 70; i++) { float v = DcMath.IntercomIntervalStress(lo, i / 100f, 25f, 0.6f); if (v > prev + 1e-4f) mono = false; prev = v; }
            Check(mono, "intercom: interval never grows with stress", null);
            float st = DcMath.IntercomInterval(t.intercomInterval, true, false, t.intercomStationBoost, t.intercomQuietBoost);
            Near(DcMath.IntercomIntervalStress(st, 0f, 25f, 0.6f), 60.0, 1e-2, "intercom: stations get the same 60 s by default (boost 1)");
            Near(DcMath.IntercomInterval(t.intercomInterval, false, true, t.intercomStationBoost, t.intercomQuietBoost), 60.0, 1e-3, "intercom: quiet coasts get the same 60 s by default (boost 1)");
            Check(t.intercomMaxStress == 0.7f && t.intercomAbortStress > t.intercomMaxStress, "intercom: chats start below 0.7, abort slightly above", null);

            // delivery band follows each speaker's personal band
            KerbalVoice brave = KerbalVoice.Create("Valentina Kerman", 0.9f, 0.1f, true);
            KerbalVoice nervous = KerbalVoice.Create("Bill Kerman", 0.1f, 0.9f, false);
            brave.UpdateStress(0.5f); nervous.UpdateStress(0.5f);
            Check(KerbalVoice.FolderFor(brave.band) == "crew/calm" && KerbalVoice.FolderFor(nervous.band) != "crew/calm",
                  "intercom band: at stress 0.5 the brave kerbal stays calm, the nervous one does not",
                  brave.BandName + " / " + nervous.BandName);
            nervous.UpdateStress(0.68f);
            Check(KerbalVoice.FolderFor(nervous.band) == "crew/panic", "intercom band: nervous kerbal panics at 0.68", nervous.BandName + " sk " + nervous.sk.ToString("0.00"));

            // migration to version 4
            Settings m3 = new Settings(); m3.tuning.intercomInterval = 600f; m3.tuning.intercomMaxStress = 0.35f; m3.tuning.intercomAbortStress = 0.5f;
            m3.MigrateValues(3);
            Check(m3.tuning.intercomInterval == 60f && m3.tuning.intercomMaxStress == 0.7f && m3.tuning.intercomAbortStress == 0.78f,
                  "migration v4: old intercom defaults replaced", null);
            Settings mu = new Settings(); mu.tuning.intercomInterval = 450f;
            mu.MigrateValues(3);
            Check(mu.tuning.intercomInterval == 450f, "migration v4: a user-set intercom interval is kept", null);
            Settings m10 = new Settings(); m10.tuning.intercomInterval = 300f; m10.tuning.probeRateFactor = 1.5f; m10.subtitles = false;
            m10.MigrateValues(10);
            Check(m10.tuning.intercomInterval == 60f && m10.tuning.probeRateFactor == 1f && m10.subtitles,
                  "migration v11: old intercom, probe rate and subtitle defaults move", null);
            Settings m10u = new Settings(); m10u.tuning.intercomInterval = 450f; m10u.tuning.probeRateFactor = 2f;
            m10u.MigrateValues(10);
            Check(m10u.tuning.intercomInterval == 450f && m10u.tuning.probeRateFactor == 2f, "migration v11: user-set values are kept", null);
            Settings m11 = new Settings(); m11.runtimeVoice = false; m11.runtimeReports = false;
            m11.MigrateValues(11);
            Check(m11.runtimeVoice && !m11.runtimeReports, "migration v12: runtime voice on, a user-set report toggle is kept", null);
            Check(new Settings().runtimeVoice && new Settings().runtimeReports && new Settings().runtimeScience,
                  "defaults: all runtime voice settings are on", null);
            Settings m4 = new Settings(); m4.tuning.lightDelayMax = 2f; m4.tuning.lightDelayScale = 0.45f;
            m4.MigrateValues(4);
            Check(m4.tuning.lightDelayMax == 10f && m4.tuning.lightDelayScale == 2.6f, "migration v5: light delay up to 10 s replaces the old defaults", null);
            Settings m4u = new Settings(); m4u.tuning.lightDelayMax = 5f; m4u.tuning.lightDelayScale = 0.45f;
            m4u.MigrateValues(4);
            Check(m4u.tuning.lightDelayMax == 5f, "migration v5: a user-set light delay cap is kept", null);
            Settings big = new Settings(); big.tuning.lightDelayMax = 60f; big.Clamp();
            Check(big.tuning.lightDelayMax == 10f, "light delay: capped at 10 s", big.tuning.lightDelayMax.ToString());
        }

        // ---- per-kerbal ----
        Near(KerbalVoice.Exponent(0.5f, 0.5f, false), 1.0, 1e-6, "exponent neutral kerbal = 1");
        Near(KerbalVoice.Exponent(1f, 0f, false), 0.4, 1e-6, "exponent brave+smart clamps to 0.4");
        Near(KerbalVoice.Exponent(0f, 1f, false), 1.8, 1e-6, "exponent nervous+dumb clamps to 1.8");
        Near(KerbalVoice.Exponent(0.5f, 0.5f, true), 0.75, 1e-6, "exponent badass x0.75");
        Near(KerbalVoice.PersonalStress(0.5f, 1f), 0.5, 1e-6, "sk = stress for e = 1");
        Check(KerbalVoice.PersonalStress(0.5f, 0.4f) < 0.5f && KerbalVoice.PersonalStress(0.5f, 1.8f) > 0.5f, "sk: brave damped, nervous amplified",
              "e 0.4 -> " + KerbalVoice.PersonalStress(0.5f, 0.4f).ToString("0.000") + ", e 1.8 -> " + KerbalVoice.PersonalStress(0.5f, 1.8f).ToString("0.000"));
        Near(KerbalVoice.TenseAt(0.5f, 0.5f), 0.30, 1e-6, "tenseAt neutral 0.30");
        Near(KerbalVoice.PanicAt(0.5f, 0.5f, false), 0.68, 1e-6, "panicAt neutral 0.68");
        Near(KerbalVoice.PanicAt(0.5f, 0.5f, true), 0.76, 1e-6, "panicAt badass +0.08");
        {
            Band b = Band.Calm;
            b = KerbalVoice.NextBand(b, 0.31f, 0.30f, 0.68f); Check(b == Band.Tense, "band: calm -> tense at 0.31", null);
            b = KerbalVoice.NextBand(b, 0.27f, 0.30f, 0.68f); Check(b == Band.Tense, "band: hysteresis keeps tense at 0.27", null);
            b = KerbalVoice.NextBand(b, 0.25f, 0.30f, 0.68f); Check(b == Band.Calm, "band: tense -> calm below 0.26", null);
            b = KerbalVoice.NextBand(b, 0.70f, 0.30f, 0.68f); Check(b == Band.Panic, "band: calm -> panic at 0.70", null);
            b = KerbalVoice.NextBand(b, 0.65f, 0.30f, 0.68f); Check(b == Band.Panic, "band: hysteresis keeps panic at 0.65", null);
            b = KerbalVoice.NextBand(b, 0.63f, 0.30f, 0.68f); Check(b == Band.Tense, "band: panic -> tense below 0.64", null);
        }
        {
            List<KerbalVoice> list = new List<KerbalVoice>();
            list.Add(KerbalVoice.Create("A", 0.5f, 0.5f, false));
            list.Add(KerbalVoice.Create("B", 0.5f, 0.5f, false));
            Random rng = new Random(7);
            int prevPicked = 0, n = 100000;
            for (int i = 0; i < n; i++) if (KerbalVoice.Pick(list, list[0], rng) == list[0]) prevPicked++;
            Near(prevPicked / (double)n, 0.4 * 0.3 / (0.4 * 0.3 + 0.3), 0.01, "speaker: previous speaker weight x0.4");
            list[0].outDueToG = true; list[1].outDueToG = true;
            Check(KerbalVoice.Pick(list, null, rng) == null, "speaker: none when all are out due to G", null);
        }

        // ---- shuffle bag ----
        foreach (int count in new int[] { 1, 2, 3, 4, 10, 24 })
        {
            ShuffleBag bag = new ShuffleBag(count);
            Random rng = new Random(count);
            int window = Math.Min(3, count - 1);
            List<int> hist = new List<int>();
            bool ok = true; int[] seen = new int[count];
            for (int i = 0; i < 20000; i++)
            {
                int k = bag.Next(rng);
                if (k < 0 || k >= count) { ok = false; break; }
                seen[k]++;
                for (int j = Math.Max(0, hist.Count - window); j < hist.Count; j++) if (hist[j] == k) ok = false;
                hist.Add(k);
            }
            bool all = true; for (int i = 0; i < count; i++) if (seen[i] == 0) all = false;
            Check(ok && all, "shuffle bag n=" + count + ": no repeat within last " + window + ", all clips used", null);
        }
        Check(new ShuffleBag(0).Next(new Random(1)) == -1, "shuffle bag empty returns -1", null);

        // ---- settings sanitizing ----
        {
            Settings s = new Settings();
            s.rateMultiplier = 10f; s.masterVolume = float.NaN; s.maxCrewVoices = 9;
            s.tuning.decayTau = float.NaN; s.tuning.spinHi = 0.1f; s.tuning.gateFadeTime = 2f;
            s.Clamp();
            Check(s.rateMultiplier == 4f && s.masterVolume == 0f && s.maxCrewVoices == 3, "settings clamp", null);
            Check(s.tuning.decayTau == 9f && s.tuning.spinHi > s.tuning.spinLo && s.tuning.gateFadeTime <= 0.45f,
                  "tuning sanitize: NaN -> default, hi > lo, gate fade <= 0.45 s", null);
        }

        CoreModuleTests.Run();
        StressCrewTests.Run();
        V3Tests.Run();
        V4Tests.Run();

        // ---- Lonapse phrase table (self-contained block, src/PhraseTable.cs) ----
        {
            Check(LonapseText.ReverseWord("hola") == "aloh", "lonapse: hola -> aloh", null);
            string espanol = "espa\u00f1ol";
            string espanolNfd = "espan\u0303ol";
            Check(LonapseText.ReverseText("Espa\u00f1ol") == "lo\u00f1apsE", "lonapse: Espanol (NFC) -> lo\u00f1apsE", null);
            Check(LonapseText.ReverseText("Espan\u0303ol") == "lo\u00f1apsE", "lonapse: Espanol (NFD) -> lo\u00f1apsE after NFC", null);
            Check(LonapseText.ReverseText(espanol) == "lo\u00f1apse", "lonapse: lowercase espanol keeps its case -> lo\u00f1apse", null);
            Check(LonapseText.ReverseText(espanolNfd) == LonapseText.ReverseText(espanol), "lonapse: NFD and NFC give identical output", null);
            Check(LonapseText.ReverseText("¿Qué tal?") == "¿éuQ lat?", "lonapse: punctuation and spaces stay in place", null);
            Check(LonapseText.ReverseText("Separación") == "nóicarapeS", "lonapse: Separacion -> noicarapeS (accent stays on its letter)", null);
            Check(LonapseText.ReverseText("") == "", "lonapse: empty text", null);
            string marks = "ÁÉÍÓÚáéíóúÜüñÑ";
            Check(LonapseText.ComposeSpanish(marks) == marks.Normalize(System.Text.NormalizationForm.FormC),
                  "lonapse: hand-made composition equals NFC for Spanish marks", null);
            Check(LonapseText.ComposeSpanish(espanolNfd) == espanol, "lonapse: hand-made composition of espanol", null);

            List<string> good = new List<string> {
                "# comment line",
                "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish",
                "stg_a_1\tstaging\tcallout\tnone\tcrew\tstg_a\t1\tboth\tSeparación",
                "stg_a_2\tstaging\tcallout\tnone\tmc\tstg_a\t2\tm\tRecibido",
                "chat_c_1\tchatter\tchatter\tcalm\tcrew\tchat_c\t1\tboth\tHola mundo" };
            List<string> errs = new List<string>();
            PhraseTable t = PhraseTable.Parse(good, errs);
            Check(errs.Count == 0 && t.Count == 3, "phrase table: good rows parse, comments skipped", errs.Count + " errors");
            Check(t.Get("chat_c_1") != null && t.Get("chat_c_1").LonapseText == "aloH odnum", "phrase table: lonapse computed when column is absent", null);
            Check(t.Find("staging", "none", "crew").Count == 1 && t.Find("staging", "none", "mc").Count == 1, "phrase table: Find by event, band, role", null);
            Check(LonapseText.SubtitleFor(t, "chat_c_1") == "aloH odnum" && LonapseText.SubtitleFor(t, "nope") == null,
                  "subtitle: without an english column the Lonapse text, unknown key gives null", null);

            // english column (subtitles): last column in the source and in the built table
            List<string> en = new List<string> {
                "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish\tenglish",
                "e_1\tchatter\tchatter\tcalm\tcrew\te_1\t1\tboth\tHola mundo\tHello world",
                "e_2\tchatter\tchatter\tcalm\tcrew\te_2\t1\tboth\tHola\t" };
            List<string> errEn = new List<string>();
            PhraseTable te = PhraseTable.Parse(en, errEn);
            Check(te.Count == 1 && errEn.Count == 1 && LonapseText.SubtitleFor(te, "e_1") == "Hello world" && te.Get("e_1").LonapseText == "aloH odnum",
                  "subtitle: english column gives English only, an empty english is an error", errEn.Count + " errors");
            List<string> enBuilt = new List<string> {
                "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish\tlonapse_text\tfolder\tenglish",
                "e_1\tchatter\tchatter\tcalm\tcrew\te_1\t1\tboth\tHola\taloH\tcrew/calm\tHello" };
            List<string> errEnB = new List<string>();
            PhraseTable teb = PhraseTable.Parse(enBuilt, errEnB);
            Check(errEnB.Count == 0 && teb.Count == 1 && teb.Get("e_1").English == "Hello" && teb.Get("e_1").Folder == "crew/calm",
                  "phrase table: built table with folder and english parses", errEnB.Count + " errors");
            {
                KerbalVoice k1 = KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, true);
                KerbalVoice k2 = KerbalVoice.Create("Bill Kerman", 0.5f, 0.5f, false);
                List<KerbalVoice> cr = new List<KerbalVoice> { k1, k2 };
                bool before = KerbalVoice.AnyPanic(cr);
                k2.band = Band.Panic;
                Check(!before && KerbalVoice.AnyPanic(cr), "alarm: AnyPanic only when a kerbal is in the panic band", null);
            }
            Check(PacingRules.ClassifyStage(true, false) == StageKind.Separation && PacingRules.ClassifyStage(true, true) == StageKind.Separation
                  && PacingRules.ClassifyStage(false, true) == StageKind.Ignition && PacingRules.ClassifyStage(false, false) == StageKind.Any,
                  "staging: decoupler gives separation (also with an engine), engine alone ignition, neither neutral", null);
            Check(PacingRules.StagingSceneKind("staging_ign_s1") == StageKind.Ignition && PacingRules.StagingSceneKind("staging_any_01") == StageKind.Any
                  && PacingRules.StagingSceneKind("staging_s1") == StageKind.Separation && PacingRules.StagingSceneKind("staging_01") == StageKind.Separation,
                  "staging: scene ids map to separation, ignition and neutral", null);
            Check(PacingRules.IsScream("scream_02") && !PacingRules.IsScream("chat_panic_s1") && !PacingRules.IsScream(null),
                  "scream: scene ids scream_* get the scream cooldown", null);

            // generated file: lonapse_text column is read as written
            List<string> gen = new List<string> {
                "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish\tlonapse_text",
                "a_1\tchatter\tchatter\tcalm\tcrew\ta\t1\tboth\tHola\taloH" };
            List<string> errsGen = new List<string>();
            PhraseTable tg = PhraseTable.Parse(gen, errsGen);
            Check(errsGen.Count == 0 && tg.Get("a_1").LonapseText == "aloH", "phrase table: lonapse_text column is used as written", null);

            // each bad row is reported and skipped, parsing goes on
            string[] badRows = {
                "b_1\tbogus\tchatter\tcalm\tcrew\tb_1\t1\tboth\tHola",
                "b_2\tchatter\tchatter\tcalm\tcrew\tb_2\t0\tboth\tHola",
                "b_3\tchatter\tchatter\tcalm\tmc\tb_3\t1\tf\tHola",
                "b_4\tchatter\tchatter\tcalm\tcrew\tb_4\t1\tboth\t",
                "Bad-Key\tchatter\tchatter\tcalm\tcrew\tbk\t1\tboth\tHola",
                "b_6\tchatter\tchatter\tcalm\tcrew\tb_6\t1\tboth",
                "chat_c_1\tchatter\tchatter\tcalm\tcrew\tchat_c\t2\tboth\tOtra",
                "b_8\tchatter\tchatter\ttense\tcrew\tchat_c\t2\tboth\tOtra" };
            List<string> lines = new List<string> { good[1] };
            lines.Add(good[4]);
            lines.AddRange(badRows);
            List<string> errBad = new List<string>();
            PhraseTable tb = PhraseTable.Parse(lines, errBad);
            Check(tb.Count == 1 && errBad.Count == badRows.Length, "phrase table: each bad row reported and skipped (" + errBad.Count + " errors)", null);

            List<string> errHead = new List<string>();
            Check(PhraseTable.Parse(new List<string> { "clip\tevent" }, errHead).Count == 0 && errHead.Count == 1,
                  "phrase table: wrong header rejected", null);

            // the source tables in audio/lonapse/phrases and, when built, the generated phrases.tsv
            string here = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            V3Tests.SourceTables(System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/lonapse/phrases")));

            string builtPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/build/lonapse/phrases.tsv"));
            if (System.IO.File.Exists(builtPath))
            {
                List<string> e2 = new List<string>();
                PhraseTable built = PhraseTable.Parse(new List<string>(System.IO.File.ReadAllLines(builtPath, System.Text.Encoding.UTF8)), e2);
                bool same = true;
                foreach (PhraseEntry pe in built.Entries)
                {
                    if (pe.LonapseText != LonapseText.ReverseText(pe.Spanish)) same = false;
                }
                Check(e2.Count == 0 && built.Count >= 40 && same, "phrase table: built phrases.tsv parses, lonapse_text matches ReverseText (" + built.Count + " rows)", e2.Count + " errors");
                // Parse rejects a folder column that disagrees with FolderFor, so e2 == 0 means the Python and
                // C# folder rules agree on every row. Also check every clip of the table exists on disk.
                string sounds = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/build/Sounds"));
                int missing = 0;
                string firstMissing = null;
                foreach (PhraseEntry pe in built.Entries)
                {
                    if (pe.SubtitleOnly) continue; // probe rows make no clip
                    List<string> gs = new List<string>();
                    string fixedG = PhraseTable.FixedGender(pe.Role);
                    if (fixedG != null) gs.Add(fixedG);
                    else
                    {
                        if (pe.Gender != "f") gs.Add("m");
                        if (pe.Gender != "m") gs.Add("f");
                    }
                    foreach (string g in gs)
                    {
                        string p = System.IO.Path.Combine(sounds, pe.Folder + "/" + pe.ClipKey + "_" + g + ".ogg");
                        if (!System.IO.File.Exists(p)) { missing++; if (firstMissing == null) firstMissing = p; }
                    }
                }
                Check(missing == 0, "phrase table: every built row has its clips in Sounds/<folder>/", missing + " missing, e.g. " + firstMissing);
            }
            else Console.WriteLine("SKIP  phrase table: built phrases.tsv not found (run audio/build_lonapse.py first)");

            // folder rule: crew/control folder per band
            Check(PhraseTable.FolderFor("crew", "callout", "none", "staging") == "crew/callout", "folder: crew callout", null);
            Check(PhraseTable.FolderFor("crew", "chatter", "none", "relief") == "crew/relief", "folder: crew relief", null);
            Check(PhraseTable.FolderFor("crew", "chatter", "panic", "chatter") == "crew/panic", "folder: crew panic", null);
            Check(PhraseTable.FolderFor("crew", "chatter", "none", "touchdown") == "crew/calm", "folder: crew band none goes to calm", null);
            Check(PhraseTable.FolderFor("mc", "callout", "none", "staging") == "control/calm", "folder: mc callout goes to control/calm", null);
            Check(PhraseTable.FolderFor("mc", "warning", "tense", "burn_soon") == "control/tense", "folder: mc tense", null);
            Check(PhraseTable.FolderFor("mc", "chatter", "panic", "chatter") == "control/tense", "folder: mc panic goes to control/tense", null);
            List<string> eBadFolder = new List<string>();
            PhraseTable.Parse(new List<string> {
                "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish\tlonapse_text\tfolder",
                "k1\tchatter\tchatter\tcalm\tcrew\ts1\t1\tboth\tHola\taloH\tcrew/panic" }, eBadFolder);
            Check(eBadFolder.Count == 1, "folder: a folder column that breaks the rule is rejected", eBadFolder.Count.ToString());

            // ---- scenes ----
            {
                List<string> es = new List<string>();
                PhraseTable st = PhraseTable.Parse(new List<string> {
                    "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish",
                    "a2\tchatter\tchatter\tcalm\tmc\tsa\t2\tm\tRecibido",
                    "a1\tchatter\tchatter\tcalm\tcrew\tsa\t1\tboth\tTodo bien",
                    "b1\tchatter\tchatter\tcalm\tcrew\tsb\t1\tm\tUno",
                    "b2\tchatter\tchatter\tcalm\tcrew\tsb\t2\tboth\tDos",
                    "c1\tchatter\tchatter\ttense\tcrew\tsc\t1\tboth\tTres" }, es);
                Check(es.Count == 0 && st.Scenes.Count == 3, "scene: rows group into 3 scenes", es.Count + " errors, " + st.Scenes.Count);
                PhraseScene sa = st.Scenes[0], sb2 = st.Scenes[1];
                Check(sa.Lines[0].ClipKey == "a1" && sa.Lines[1].ClipKey == "a2", "scene: lines sorted by seq", sa.Lines[0].ClipKey);
                Check(!sa.IsIntercom && sa.McLines == 1 && sa.CrewLines == 1, "scene: crew plus MC is a radio exchange", null);
                Check(sb2.IsIntercom, "scene: two crew-only lines are an intercom talk", null);
                Check(sb2.GenderOk(false) && !sb2.GenderOk(true), "scene: a male-only line rules out a female speaker", null);
                Check(sa.GenderOk(true), "scene: MC lines do not count for the crew gender", null);
                Check(st.ScenesFor("chatter", "calm", null).Count == 2 && st.ScenesFor("chatter", null, x => x.IsIntercom).Count == 1,
                      "scene: ScenesFor filters by event, band and predicate", null);
                Random pr = new Random(3);
                List<PhraseScene> two = st.ScenesFor("chatter", "calm", null);
                bool avoided = true;
                for (int i = 0; i < 50; i++) if (PhraseTable.Pick(two, pr, "sa").Id == "sa") avoided = false;
                Check(avoided, "scene: Pick avoids the last scene when there is another", null);
                Check(PhraseTable.Pick(st.ScenesFor("chatter", "tense", null), pr, "sc").Id == "sc", "scene: Pick returns the only scene even when it was the last", null);
                Check(PhraseTable.Pick(new List<PhraseScene>(), pr, null) == null, "scene: Pick on an empty list gives null", null);
            }
            string builtScenes = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/build/lonapse/phrases.tsv"));
            if (System.IO.File.Exists(builtScenes))
            {
                PhraseTable bt = PhraseTable.Parse(new List<string>(System.IO.File.ReadAllLines(builtScenes, System.Text.Encoding.UTF8)), new List<string>());
                string[] needEvents = { "staging", "chute_semi", "chute_full", "throttle_full", "throttle_cut", "touchdown", "splashdown",
                                        "relief", "dock_approach", "dock_close", "dock_hard", "docked", "plan_node", "burn_soon",
                                        "station_check", "eva_check", "eva_fall" };
                string lack = null;
                foreach (string ev in needEvents) if (bt.ScenesFor(ev, null, null).Count == 0 && lack == null) lack = ev;
                Check(lack == null, "scene: the built table has a scene for every event the director uses", lack);
                string[] callouts = { "staging", "chute_semi", "chute_full", "throttle_full", "throttle_cut" };
                string bad = null;
                foreach (string ev in callouts)
                    if (bt.ScenesFor(ev, null, x => x.Lines[0].Role == "crew").Count == 0 && bad == null) bad = ev;
                Check(bad == null, "scene: every callout event has a scene that starts with a crew line", bad);
                int radio = 0, intercom = 0;
                foreach (string b in new[] { "calm", "tense", "panic" })
                {
                    radio += bt.ScenesFor("chatter", b, x => !x.IsIntercom && x.GenderOk(true) && x.GenderOk(false)).Count > 0 ? 1 : 0;
                }
                intercom = bt.ScenesFor("chatter", null, x => x.IsIntercom).Count;
                Check(radio == 3 && intercom >= 2, "scene: radio chatter for both genders in every band, and intercom scenes", radio + " bands, " + intercom + " intercom");
            }

            // ---- launch countdown (counts up in Lonapse) ----
            {
                LaunchCountdown lc = new LaunchCountdown(0.6f, 1f);
                Check(lc.Update(1f) == 0 && !lc.Running, "countdown: nothing before Start", null);
                lc.Start();
                var said = new List<int>();
                float tt = 0f;
                while (lc.Running && tt < 30f) { int st = lc.Update(0.1f); tt += 0.1f; if (st != 0) said.Add(st); }
                bool order = said.Count == 11;
                for (int i = 0; i < said.Count; i++) if (said[i] != i + 1) order = false;
                Check(order, "countdown: says 1 to 10 then liftoff, once each", string.Join(",", said));
                Check(tt > 10.4f && tt < 10.8f, "countdown: liftoff about 10.6 s after Start", tt.ToString("0.0"));
                Check(LaunchCountdown.ClipKey(1) == "countdown_01" && LaunchCountdown.ClipKey(10) == "countdown_10"
                      && LaunchCountdown.ClipKey(LaunchCountdown.Liftoff) == "countdown_liftoff" && LaunchCountdown.ClipKey(0) == null,
                      "countdown: clip keys", null);
                lc.Start(); lc.Update(2f); lc.Abort();
                Check(!lc.Running && lc.Update(5f) == 0, "countdown: Abort stops it", null);
            }
            if (System.IO.File.Exists(builtScenes))
            {
                PhraseTable ct = PhraseTable.Parse(new List<string>(System.IO.File.ReadAllLines(builtScenes, System.Text.Encoding.UTF8)), new List<string>());
                bool allKeys = ct.Get(LaunchCountdown.AbortKey) != null;
                for (int i = 1; i <= LaunchCountdown.Liftoff; i++) if (ct.Get(LaunchCountdown.ClipKey(i)) == null) allKeys = false;
                Check(allKeys, "countdown: the built table has every countdown clip and the abort scream", null);
            }

            // ---- pacing rules (11, 14, 18) ----
            Check(PacingRules.StationChoice("auto", 0.2f, 0.6f) == StationAction.Check && PacingRules.StationChoice("auto", 0.7f, 0.6f) == StationAction.Exchange,
                  "station: auto is check-in below burstBand and exchange above", null);
            Check(PacingRules.StationChoice("check", 0.9f, 0.6f) == StationAction.Check && PacingRules.StationChoice("exchange", 0.1f, 0.6f) == StationAction.Exchange,
                  "station: forced styles ignore stress", null);
            Check(PacingRules.PlanSignature(1, new[] { 100.2 }, new[] { 50.0 }) == PacingRules.PlanSignature(1, new[] { 100.1 }, new[] { 50.02 }),
                  "plan: UT jitter under 0.25 s and tiny dv changes keep the signature", null);
            Check(PacingRules.PlanSignature(1, new[] { 100.0 }, new[] { 50.0 }) != PacingRules.PlanSignature(1, new[] { 130.0 }, new[] { 50.0 })
                  && PacingRules.PlanSignature(1, new[] { 100.0 }, new[] { 50.0 }) != PacingRules.PlanSignature(2, new[] { 100.0, 200.0 }, new[] { 50.0, 1.0 }),
                  "plan: moving a node or adding one changes the signature", null);
            Check(!PacingRules.PanicAllowed(10f, 5f, 8f, true) && PacingRules.PanicAllowed(14f, 5f, 8f, true) && !PacingRules.PanicAllowed(14f, 5f, 8f, false),
                  "panic: global gap and speaker cooldown both gate a panic line", null);

            // ---- air arbiter: intercom vs radio (intercom over the radio) ----
            Check(!AirRules.IntercomMayStart(true, false, false, 99f) && !AirRules.IntercomMayStart(false, true, false, 99f)
                  && !AirRules.IntercomMayStart(false, false, true, 99f), "air: no chat while the radio plays, is queued or pending", null);
            Check(!AirRules.IntercomMayStart(false, false, false, 1.9f) && AirRules.IntercomMayStart(false, false, false, 2.0f),
                  "air: a new chat waits 2 s after the radio", null);
            Check(!AirRules.ChatTurnMayStart(true, false, false, false, 1.4f) && AirRules.ChatTurnMayStart(true, false, false, false, 1.5f)
                  && !AirRules.ChatTurnMayStart(false, false, false, false, 1.5f), "air: a paused chat resumes 1.5 s after the radio, a new one needs 2 s", null);
            Check(!AirRules.ChatTurnMayStart(true, true, false, false, 99f), "air: a paused chat never resumes over the radio", null);
            Check(!AirRules.RadioMayStart(true) && AirRules.RadioMayStart(false), "air: the radio waits for the open intercom turn", null);
            Check(AirRules.ChatShouldYield(true, true) && !AirRules.ChatShouldYield(false, true) && !AirRules.ChatShouldYield(true, false),
                  "air: the chat yields only between turns and only when radio waits", null);
            Check(!AirRules.ChatPausedTooLong(20f) && AirRules.ChatPausedTooLong(20.1f), "air: a chat paused over 20 s is dropped", null);
            // ---- air queue: a started chat holds the air, no chat right before the radio (radio cut into a chat) ----
            Check(AirRules.ChatHoldsAir(true, true, false) && AirRules.ChatHoldsAir(false, false, true)
                  && !AirRules.ChatHoldsAir(false, true, false) && !AirRules.ChatHoldsAir(true, false, false),
                  "air: a started chat with items left, or an audible line, holds the air", null);
            Check(!AirRules.RadioMayStart(false, true) && !AirRules.RadioMayStart(true, false) && AirRules.RadioMayStart(false, false),
                  "air: the radio waits for the whole started chat", null);
            Check(!AirRules.IntercomMayStart(false, false, false, 99f, 14.9f, 0f) && AirRules.IntercomMayStart(false, false, false, 99f, 15f, 0f)
                  && AirRules.IntercomMayStart(false, false, false, 99f, -1f, 0f),
                  "air: no new chat when the radio fires within 15 s (radio off: no look ahead)", null);
            Check(!AirRules.IntercomMayStart(false, false, false, 99f, 5f, 44.9f) && AirRules.IntercomMayStart(false, false, false, 99f, 5f, 45f)
                  && !AirRules.IntercomMayStart(true, false, false, 99f, 99f, 99f),
                  "air: a chat that waited 45 s for the radio starts anyway, never over the radio", null);
            Check(AirRules.CalloutRank("staging") == 2 && AirRules.CalloutRank("chute_semi") == 2 && AirRules.CalloutRank("chute_full") == 2
                  && AirRules.CalloutRank("throttle_full") == 1 && AirRules.CalloutRank(null) == 1, "air: staging and chutes outrank throttle and docking", null);
            Check(AirRules.CalloutReplacesPending("throttle_cut", "staging") && !AirRules.CalloutReplacesPending("staging", "throttle_full")
                  && !AirRules.CalloutReplacesPending("chute_full", null) && AirRules.CalloutReplacesPending(null, null)
                  && AirRules.CalloutReplacesPending("staging", "chute_semi") && AirRules.CalloutReplacesPending("staging", "staging"),
                  "air: the waiting slot goes to the higher rank, a newer docking distance replaces the older one", null);
            Check(AirRules.CalloutMaxDeferFor("staging") == 4f && AirRules.CalloutMaxDeferFor(null) == 2f,
                  "air: callouts wait at most 4 s for the intercom, docking distances 2 s", null);
            {
                // ring of 8 starting at 6, wrapping: chat items (odd) and radio items (even) mixed
                int[] ring = new int[8], scratch = new int[8];
                int[] items = { 1, 3, 2, 5, 4, 7 }; // chat 1,3 then radio 2, chat 5, radio 4, chat 7
                for (int i = 0; i < items.Length; i++) ring[(6 + i) % 8] = items[i];
                int front = AirRules.StablePartitionRing(ring, 6, items.Length, x => x % 2 == 0, scratch);
                string got = "";
                for (int i = 0; i < items.Length; i++) got += ring[(6 + i) % 8];
                Check(front == 2 && got == "241357", "air: radio items move ahead of the chat, both keep their order (wrapping ring)", got);
                int front2 = AirRules.StablePartitionRing(ring, 6, 0, x => true, scratch);
                Check(front2 == 0 && ring[6] == 2, "air: partition of an empty slice changes nothing", null);
            }
            RecentMax rm = new RecentMax(0.5f);
            rm.Add(0.9f, 0.1f); for (int i = 0; i < 3; i++) rm.Add(0.1f, 0.1f);
            bool keeps = Math.Abs(rm.Max - 0.9f) < 1e-4f;
            for (int i = 0; i < 6; i++) rm.Add(0.1f, 0.1f);
            Check(keeps && Math.Abs(rm.Max - 0.1f) < 1e-4f, "dock: RecentMax keeps the peak for the window, then forgets it", rm.Max.ToString());
            Check(KerbalVoice.NextBand(Band.Tense, 0.80f, 0.4f, 0.75f, Band.Panic) == Band.Panic, "escalation: base panic threshold", null);
            KerbalVoice ev1 = KerbalVoice.Create("Escalation Kerman", 0.5f, 0.5f, false);
            ev1.panicAt = 0.75f; ev1.tenseAt = 0.4f; ev1.exponent = 1f; ev1.band = Band.Tense;
            ev1.UpdateStress(0.8f, Band.Panic, 0.16f);
            Check(ev1.band == Band.Tense, "escalation: an offset of 0.16 holds a stress of 0.8 at tense", ev1.band.ToString());
        }

        Check(LaunchCountdown.ClipKey(1, false) == "countdown_01" && LaunchCountdown.ClipKey(10, false) == "countdown_10"
              && LaunchCountdown.ClipKey(1, true) == "countdown_10" && LaunchCountdown.ClipKey(10, true) == "countdown_01"
              && LaunchCountdown.ClipKey(LaunchCountdown.Liftoff, true) == "countdown_liftoff" && LaunchCountdown.ClipKey(0, true) == null,
              "countdown: up says 1 .. 10, down says 10 .. 1, both end with liftoff", null);
        SceneMemoryTests();
        SubtitleSlotTests();
        RuntimeSpeechTests();
        ProbeCoreTests();
        ProbeSynthTests();
        ProbeSettingsTests();
        ProbeRulesTests();
        ReviewFixTests();

        Console.WriteLine();
        Console.WriteLine("Result: " + pass + " passed, " + fail + " failed");
        return fail == 0 ? 0 : 1;
    }

    // ---- director rules in probe mode (ProbeRules) ----
    static void ProbeRulesTests()
    {
        Near(ProbeRules.BeepPitch(0f, false, 1.35f), 1f, 1e-5f, "probe rules: beep pitch 1 at stress 0");
        Near(ProbeRules.BeepPitch(0.8f, false, 1.35f), 1.2f, 1e-5f, "probe rules: beep pitch 1 + 0.25 x stress");
        Near(ProbeRules.BeepPitch(1f, false, 1.2f), 1.2f, 1e-5f, "probe rules: beep pitch capped at probeBeepPitchMax");
        Near(ProbeRules.BeepPitch(5f, false, 9f), 1.25f, 1e-5f, "probe rules: stress above 1 counts as 1");
        Near(ProbeRules.BeepPitch(float.NaN, false, 0.5f), 1f, 1e-5f, "probe rules: NaN stress and a cap below 1 give pitch 1");
        Near(ProbeRules.BeepPitch(1f, true, 1.35f), 0.8f, 1e-5f, "probe rules: drowsy beeps at pitch 0.8");

        Check(ProbeRules.BeepMood("proud", false) == "proud" && ProbeRules.BeepMood("proud", true) == "scared"
              && ProbeRules.BeepMood("", false) == "happy" && ProbeRules.BeepMood("weird", false) == "happy"
              && ProbeRules.BeepMood(null, false) == "happy", "probe rules: beep mood from the row, scared when tense, happy fallback", null);
        Check(ProbeRules.BeepSubtitle("scared") == "[beeps nervously]" && ProbeRules.BeepSubtitle(null) == "[beeps happily]"
              && ProbeRules.BeepSubtitle("bored") == "[beeps flatly]", "probe rules v6: short beep subtitle", ProbeRules.BeepSubtitle("scared"));
        bool allMoodsOk = true;
        foreach (string m in ProbeCores.Moods) if (!PhraseTable.ProbeSubtitleOk(ProbeRules.BeepSubtitle(m))) allMoodsOk = false;
        Check(allMoodsOk && PhraseTable.ProbeSubtitleOk("[beeps excitedly]") && PhraseTable.ProbeSubtitleOk("[beeps a bit louder]")
              && !PhraseTable.ProbeSubtitleOk("[beeps excitedly, then something else]") && !PhraseTable.ProbeSubtitleOk("[happy beeps]")
              && !PhraseTable.ProbeSubtitleOk("[beeps]") && !PhraseTable.ProbeSubtitleOk("[beeps one two three four]")
              && !PhraseTable.ProbeSubtitleOk(null), "probe rules v6: probe subtitles are [beeps] plus 1 to 3 lowercase words", null);

        Check(ProbeRules.SpeakerName("probe", "okto") == "OKTO"
              && ProbeRules.SpeakerName("mc", "hecs") == "MC"
              && ProbeRules.SpeakerName("probe", "snob") == "Probe", "probe rules: subtitle speaker names", null);

        // forced sleep from the core states
        Check(!ProbeRules.ForcedAsleep(new List<int> { 6, 5 }, new List<bool> { false, false }), "probe rules: nominal cores are awake", null);
        Check(ProbeRules.ForcedAsleep(new List<int> { 6, 1 }, new List<bool> { false, false }), "probe rules: a core without power forces sleep", null);
        Check(ProbeRules.ForcedAsleep(new List<int> { 6 }, new List<bool> { true }), "probe rules: a hibernating core forces sleep", null);
        Check(!ProbeRules.ForcedAsleep(null, null) && !ProbeRules.ForcedAsleep(new List<int>(), new List<bool>()), "probe rules: no cores, no forced sleep", null);

        // event plan
        Check(ProbeRules.PlanFor("crew_lost") == ProbeEventPlan.Drop && ProbeRules.PlanFor("signal_lost") == ProbeEventPlan.Drop
              && ProbeRules.PlanFor("science") == ProbeEventPlan.Radio && ProbeRules.PlanFor("signal_back") == ProbeEventPlan.Radio
              && ProbeRules.PlanFor("touchdown") == ProbeEventPlan.Radio && ProbeRules.PlanFor("burn_soon") == ProbeEventPlan.Radio,
              "probe rules: crew_lost and signal_lost dropped, the rest radio", null);
        // Only Mission Control and the probe talk in probe mode
        Check(PhraseTable.Roles.Length == 3 && Array.IndexOf(PhraseTable.Roles, "diary") < 0 && Array.IndexOf(PhraseTable.Roles, "flight") < 0 && Array.IndexOf(PhraseTable.Roles, "intern") < 0
              && Array.IndexOf(PhraseTable.Roles, "coffee") < 0 && Array.IndexOf(PhraseTable.Events, "room_chat") < 0
              && Array.IndexOf(PhraseTable.Events, "probe_bet_open") < 0 && PhraseTable.IsProbeEvent("probe_hail"),
              "probe rules v6: no room or diary roles, no room events, probe_hail is a probe event", null);
        Check(Array.IndexOf(PhraseTable.ProbeVariantEvents, "touchdown") >= 0 && Array.IndexOf(PhraseTable.ProbeVariantEvents, "splashdown") >= 0
              && Array.IndexOf(PhraseTable.ProbeVariantEvents, "docked") >= 0 && Array.IndexOf(PhraseTable.ProbeVariantEvents, "burn_soon") >= 0,
              "probe rules v6: touchdown, splashdown, docked and burn_soon have probe variants", null);
        Check(ProbeRules.HailMayStart("orbit", true, false) && !ProbeRules.HailMayStart("orbit", false, false)
              && !ProbeRules.HailMayStart("orbit", true, true) && !ProbeRules.HailMayStart("launch", true, false),
              "probe rules v6: a hail needs the link, an awake probe and a calm phase", null);
        bool moodsOk = true;
        foreach (string ev in PhraseTable.ProbeVariantEvents)
        {
            string m = ProbeRules.FallbackMood(ev);
            if (ev == "signal_lost") { if (m != null) moodsOk = false; }
            else if (m == null || Array.IndexOf(ProbeCores.Moods, m) < 0) moodsOk = false;
        }
        Check(moodsOk && ProbeRules.FallbackMood("crew_lost") == null, "probe rules: every probe variant event but signal_lost has a known fallback mood", null);

        // scene shapes and fits, on a parsed table
        List<string> rows = new List<string> {
            "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tphase\tcause\tcore\tbeep\tspanish\tenglish",
            "pt_a_1\tprobe_chat\tchatter\tcalm\tmc\tpt_a\t1\tm\tany\tany\tokto\t\tHola\tHello little one",
            "pt_a_2\tprobe_chat\tchatter\tcalm\tprobe\tpt_a\t2\tboth\tany\tany\tokto\thappy\t\t[beeps happily]",
            "pt_b_1\tprobe_chat\tchatter\tcalm\tmc\tpt_b\t1\tm\tany\tany\tany\t\tHola\tHello probe",
            "pt_b_2\tprobe_chat\tchatter\tcalm\tprobe\tpt_b\t2\tboth\tany\tany\tany\tscared\t\t[beeps nervously]",
            "ph_a_1\tprobe_hail\tchatter\tcalm\tprobe\tph_a\t1\tboth\tgoofy\tany\tany\tcurious\t\t[beeps curiously]",
            "ph_a_2\tprobe_hail\tchatter\tcalm\tmc\tph_a\t2\tm\tgoofy\tany\tany\t\tSi\tYes?",
            "td_a_1\ttouchdown\tchatter\tnone\tprobe\ttd_a\t1\tboth\tany\tany\tany\thappy\t\t[beeps with relief]" };
        // The diary role and the long probe subtitles are rejected
        List<string> badErrs = new List<string>();
        PhraseTable.Parse(new List<string> { rows[0],
            "dy_a_1\tprobe_chat\tchatter\tcalm\tdiary\tdy_a\t1\tboth\tany\tany\tany\t\t\tDay 3.",
            "lb_a_1\tprobe_chat\tchatter\tcalm\tprobe\tlb_a\t1\tboth\tany\tany\tany\thappy\t\t[beeps excitedly, then a song]" }, badErrs);
        Check(badErrs.Count == 2, "probe rules v6: diary role and long probe subtitle rejected", string.Join(" | ", badErrs.ToArray()));

        // Probe and crew in the cabin (probe_crew). Probe and crew rows, never MC. A probe scene, so the crew
        // pickers skip it, and it is found by its event like the probe events.
        List<string> pcErr = new List<string>();
        PhraseTable pct = PhraseTable.Parse(new List<string> { rows[0],
            "pc_a_1\tprobe_crew\tchatter\tcalm\tprobe\tpc_a\t1\tboth\tgoofy\tany\tany\tproud\t\t[beeps with a report]",
            "pc_a_2\tprobe_crew\tchatter\tcalm\tcrew\tpc_a\t2\tboth\tgoofy\tany\tany\t\tBuenos datos\tGood data." }, pcErr);
        PhraseScene pcs = pcErr.Count == 0 && pct.Scenes.Count == 1 ? pct.Scenes[0] : null;
        Check(pcs != null && pcs.IsProbe && pcs.CrewLines == 1 && !pcs.IsIntercom,
              "0.6.0 probe_crew: probe plus crew parses, is a probe scene, not an intercom scene", string.Join(" | ", pcErr.ToArray()));
        Check(pcs != null && pct.ProbeScenesFor("probe_crew", "okto", "orbit", false, null).Count == 1
              && pct.ProbeScenesFor("probe_crew", "okto", "ascent", false, null).Count == 0,
              "0.6.0 probe_crew: found by its event, only in its phase", null);
        List<string> pcBad = new List<string>();
        PhraseTable.Parse(new List<string> { rows[0],
            "pc_b_1\tprobe_crew\tchatter\tcalm\tmc\tpc_b\t1\tm\tgoofy\tany\tany\t\tHola\tHello." }, pcBad);
        Check(pcBad.Count == 1 && pcBad[0].Contains("cannot have mc rows"), "0.6.0 probe_crew: an mc row is rejected",
              string.Join(" | ", pcBad.ToArray()));
        Check(!PhraseTable.IsProbeEvent("probe_crew") && PhraseTable.IsProbeCrewEvent("probe_crew"),
              "0.6.0 probe_crew: not a probe mode event", null);
        List<string> errs = new List<string>();
        PhraseTable t = PhraseTable.Parse(rows, errs);
        Check(errs.Count == 0, "probe rules: test probe table parses", string.Join(" | ", errs.ToArray()));
        if (errs.Count == 0)
        {
            PhraseScene pa = null, rm = null, td = null;
            foreach (PhraseScene sc in t.Scenes)
            {
                if (sc.Id == "pt_a") pa = sc; else if (sc.Id == "ph_a") rm = sc; else if (sc.Id == "td_a") td = sc;
            }
            ProbeShape sa = ProbeRules.ShapeOf(pa), sr = ProbeRules.ShapeOf(rm);
            Check(sa.mc == 1 && sa.probe == 1 && sa.NeedsSignal, "probe rules: pet talk shape", null);
            Check(sr.mc == 1 && sr.probe == 1 && ProbeRules.StartsWithProbe(rm.Lines) && !ProbeRules.StartsWithProbe(pa.Lines)
                  && !ProbeRules.StartsWithProbe(null) && rm.IsProbe, "probe rules v6: a hail scene starts with the probe", null);
            Check(td != null && td.IsProbe && PhraseTable.IsProbeScene(td), "probe rules v6: a touchdown row with a probe role is a probe scene", null);
            Check(ProbeRules.SceneFits(sa, false, true) && !ProbeRules.SceneFits(sa, true, true)
                  && !ProbeRules.SceneFits(sa, false, false), "probe rules: pet talk needs an awake probe and the link", null);
            ProbeShape crew = default(ProbeShape); crew.crew = 1;
            Check(!ProbeRules.SceneFits(crew, false, true), "probe rules: crew rows never fit", null);

            // weighted pick: exact core about twice as often as core any
            List<PhraseScene> pool = t.ProbeScenesFor("probe_chat", "okto", "orbit", false, null);
            Check(pool.Count == 2 && t.ProbeScenesFor("probe_chat", "hecs", "orbit", false, null).Count == 1,
                  "probe rules: pool holds exact-core and core-any scenes", pool.Count.ToString());
            Check(ProbeRules.CoreWeight("okto", "okto") == 2 && ProbeRules.CoreWeight("any", "okto") == 1 && ProbeRules.CoreWeight("hecs", "okto") == 1,
                  "probe rules: core weights", null);
            Random rng = new Random(7);
            int exact = 0, n = 6000;
            for (int i = 0; i < n; i++) if (ProbeRules.PickWeighted(pool, "okto", rng, null).Id == "pt_a") exact++;
            double frac = exact / (double)n;
            Check(Math.Abs(frac - 2.0 / 3.0) < 0.03, "probe rules: exact core picked about 2 of 3 times", frac.ToString("0.000"));
            bool avoids = true;
            for (int i = 0; i < 200; i++) if (ProbeRules.PickWeighted(pool, "okto", rng, "pt_a").Id != "pt_b") avoids = false;
            Check(avoids, "probe rules: the last scene is avoided when there is another", null);
            Check(ProbeRules.PickWeighted(new List<PhraseScene>(), "okto", rng, null) == null && ProbeRules.PickWeighted(null, "okto", rng, null) == null
                  && ProbeRules.PickWeighted(new List<PhraseScene> { pa }, "okto", rng, "pt_a") == pa,
                  "probe rules: empty pool gives null, a single scene is always picked", null);
        }
    }

    // ==== event max ages, debug topic cycle, expression override check, badass with effects ====
    // ---- probe cores, sleep hysteresis ----
    // line memory: a shuffle bag over the changing scene lists

    static void SceneMemoryTests()
    {
        var pool = new List<PhraseScene>();
        for (int i = 0; i < 20; i++) pool.Add(new PhraseScene("s" + i, "chatter", "calm", "chatter"));
        Random rng = new Random(3);

        // a fixed list of 20: all 20 before the first repeat, then never one of the last 15 again
        {
            SceneMemory m = new SceneMemory();
            var seen = new HashSet<string>();
            bool firstRound = true;
            for (int i = 0; i < 20; i++) firstRound &= seen.Add(m.Take(pool, rng, null).Id);
            var recent = new List<string>();
            int tooSoon = 0;
            for (int i = 0; i < 500; i++)
            {
                string id = m.Take(pool, rng, null).Id;
                int at = recent.LastIndexOf(id);
                if (at >= 0 && recent.Count - at < 15) tooSoon++;
                recent.Add(id);
            }
            Check(firstRound && tooSoon == 0, "line memory: 20 lines all play once before a repeat, then a line waits at least 15 picks", tooSoon + " too soon");
        }
        // plain random from the same list repeats much sooner (for comparison, informational)
        {
            int firstRepeat = 0, runs = 200;
            for (int r = 0; r < runs; r++)
            {
                var seen = new HashSet<string>();
                int n = 0;
                while (seen.Add(pool[rng.Next(pool.Count)].Id)) n++;
                firstRepeat += n + 1;
            }
            Check(true, "line memory: plain random from 20 lines repeats after this many picks on average", (firstRepeat / (double)runs).ToString("0.0"));
        }
        // a changing list: a new scene that joins the list comes first, the list order does not matter
        {
            SceneMemory m = new SceneMemory();
            foreach (PhraseScene sc in pool) m.Stamp(sc.Id);
            var grown = new List<PhraseScene>(pool) { new PhraseScene("new", "chatter", "calm", "chatter") };
            Check(m.Take(grown, rng, null).Id == "new", "line memory: a scene never heard comes first", null);
            List<PhraseScene> fresh = m.Freshest(pool);
            Check(fresh.Count == 5 && fresh.TrueForAll(x => m.LastSeq(x.Id) <= m.LastSeq("s4")), "line memory: after a full round, the oldest quarter is the choice", fresh.Count.ToString());
        }
        // avoid the last scene, a single scene, empty lists
        {
            SceneMemory m = new SceneMemory();
            var two = new List<PhraseScene> { pool[0], pool[1] };
            m.Stamp("s0"); m.Stamp("s1");
            bool avoided = true;
            for (int i = 0; i < 20; i++) avoided &= m.Take(two, rng, "s1").Id == "s0" && m.Take(two, rng, "s0").Id == "s1";
            Check(avoided, "line memory: with two scenes they take turns", null);
            var one = new List<PhraseScene> { pool[2] };
            Check(m.Take(one, rng, "s2") == pool[2] && m.Take(new List<PhraseScene>(), rng, null) == null && m.Take(null, rng, null) == null,
                  "line memory: one scene is returned as it is, empty gives null", null);
            SceneMemory a = new SceneMemory();
            a.Stamp("x"); a.Stamp("y");
            string pick = a.Take(new List<PhraseScene> { new PhraseScene("x", "chatter", "calm", "chatter"), new PhraseScene("y", "chatter", "calm", "chatter") }, rng, "x").Id;
            Check(pick == "y", "line memory: the last scene is left out when there is another choice", pick);
        }
        // file round trip and bad lines
        {
            SceneMemory m = new SceneMemory();
            m.Stamp("ct_pil_01"); m.Stamp("mj some id"); m.Stamp("ct_pil_01");
            SceneMemory back = SceneMemory.Parse(m.Serialize());
            Check(back.Count == 2 && back.LastSeq("ct_pil_01") == 3 && back.LastSeq("mj some id") == 2 && back.Sequence == 3,
                  "line memory: file round trip keeps ids (spaces too) and the counter", back.Count + " " + back.Sequence);
            back.Stamp("z");
            SceneMemory junk = SceneMemory.Parse(new[] { "# x", "", "noseq", "a -4", "b 12", " c 7 " });
            Check(back.LastSeq("z") == 4 && junk.Count == 2 && junk.LastSeq("b") == 12 && junk.LastSeq("c") == 7 && junk.Sequence == 12,
                  "line memory: the counter continues, bad lines are skipped", junk.Count.ToString());
            SceneMemory big = new SceneMemory();
            for (int i = 0; i < SceneMemory.MaxEntries + 10; i++) big.Stamp("id" + i);
            Check(big.Count <= SceneMemory.MaxEntries && big.LastSeq("id" + (SceneMemory.MaxEntries + 9)) > 0 && big.LastSeq("id0") < 0,
                  "line memory: past the cap the oldest scenes are forgotten", big.Count.ToString());
        }
    }

    // Experimental (runtime voice): names in loñapsE, the Spanish respelling, WAV reading and the build level shaping.
    static void RuntimeSpeechTests()
    {
        string[][] names = {
            new[] { "Jebediah Kerman", "Yebedia Kerman" }, new[] { "Bill", "Bil" }, new[] { "Bob", "Bob" },
            new[] { "Valentina", "Valentina" }, new[] { "Gilly", "Guili" }, new[] { "Jool", "Yul" }, new[] { "Eeloo", "Ilu" },
        };
        var bad = new List<string>();
        foreach (string[] n in names) if (RuntimeSpeech.Respell(n[0]) != n[1]) bad.Add(n[0] + " -> " + RuntimeSpeech.Respell(n[0]));
        Check(bad.Count == 0, "runtime voice: names are respelled for a Spanish voice", string.Join(", ", bad.ToArray()));
        string sp = RuntimeSpeech.Spoken(RuntimeSpeech.MarkName("Jebediah") + ", aquí Control.");
        Check(sp == "Yebedia, íuqa lortnoC.", "runtime voice: the name stays a name, the rest is loñapsE", sp);
        Check(RuntimeSpeech.Spoken("Hola.") == LonapseText.ReverseText("Hola.") && RuntimeSpeech.Spoken(null) == "",
              "runtime voice: without names it is ReverseText", null);
        Check(RuntimeSpeech.Json("a \"b\" \\ c\n") == "\"a \\\"b\\\" \\\\ c\\u000a\"", "runtime voice: JSON string escaping",
              RuntimeSpeech.Json("a \"b\" \\ c\n"));

        // a 1 s 220 Hz tone at 22050 Hz with 0.2 s silence on both ends, as a 16-bit WAV
        int sr = 22050, n0 = sr / 5, n1 = sr;
        var pcm = new short[n0 + n1 + n0];
        for (int i = 0; i < n1; i++) pcm[n0 + i] = (short)(8000 * Math.Sin(2 * Math.PI * 220 * i / sr));
        var ms = new System.IO.MemoryStream();
        var bw = new System.IO.BinaryWriter(ms);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + pcm.Length * 2);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(sr); bw.Write(sr * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(pcm.Length * 2);
        foreach (short v in pcm) bw.Write(v);
        int rate;
        float[] x = RuntimeSpeech.ReadWav(ms.ToArray(), out rate);
        Check(x != null && rate == sr && x.Length == pcm.Length && Math.Abs(x[n0 + 30] - pcm[n0 + 30] / 32768f) < 1e-6,
              "runtime voice: ReadWav gives the 16-bit samples and the rate", x == null ? "null" : x.Length + " @ " + rate);
        double ratio = Math.Pow(2, 3.0 / 12.0);
        float[] y = RuntimeSpeech.FromPiperWav(ms.ToArray(), 3.0);
        double secs = y == null ? 0 : y.Length / 44100.0, want = 1.0 / ratio;
        double rms = y == null ? -240 : RuntimeSpeech.GatedRmsDb(y), peak = 0;
        if (y != null) foreach (float v in y) peak = Math.Max(peak, Math.Abs(v));
        Check(y != null && Math.Abs(secs - want) < 0.06 && Math.Abs(rms + 20.0) < 1.0 && 20 * Math.Log10(peak) <= -1.85,
              "runtime voice: shift by +3 st shortens by the ratio, silence trimmed, -20 dB RMS, peak under -1.9 dBFS",
              string.Format("{0:0.000} s (want about {1:0.000}), rms {2:0.0} dB, peak {3:0.0} dBFS", secs, want, rms, 20 * Math.Log10(Math.Max(peak, 1e-9))));
        // pitch: count zero crossings, 220 Hz times the ratio
        // over the middle half second only, so the padding and fades do not count
        int zc = 0, a = y == null ? 0 : y.Length / 2 - 11025, b = a + 22050;
        if (y != null && a > 0 && b < y.Length) for (int i = a + 1; i < b; i++) if ((y[i - 1] < 0) != (y[i] < 0)) zc++;
        double hz = zc / 2.0 / 0.5;
        Check(Math.Abs(hz - 220 * ratio) < 6, "runtime voice: the shift raises the pitch by the ratio (220 Hz +3 st = 262 Hz)",
              hz.ToString("0.0") + " Hz");
        // crew names at the end of an MC line
        string[][] adds = {
            new[] { "Good work.", "Good work, Bob." }, new[] { "¿Todo bien?", "¿Todo bien, Bob?" },
            new[] { "Copy. Stand by!", "Copy. Stand by, Bob!" }, new[] { "Well...", "Well, Bob..." },
            new[] { "Hold on", "Hold on, Bob" }, new[] { "Copy, over.", null }, new[] { "Recibido. Cambio.", null },
            new[] { "Say \"cheese\".", null }, new[] { "Fuel at 5%.", null }, new[] { "Go {thing}.", null },
            new[] { "A | B", null }, new[] { "Bob, go.", null }, new[] { "", null }, new[] { "...", null },
        };
        var badAdd = new List<string>();
        foreach (string[] ad in adds) if (RuntimeSpeech.AddName(ad[0], "Bob") != ad[1]) badAdd.Add(ad[0] + " -> " + (RuntimeSpeech.AddName(ad[0], "Bob") ?? "null"));
        Check(badAdd.Count == 0, "runtime voice: a name goes at the end of the last sentence, not after a radio word or symbol",
              string.Join(" ; ", badAdd.ToArray()));
        Check(RuntimeSpeech.AddName("Bien hecho.", RuntimeSpeech.MarkName("Bob")) == "Bien hecho, ‹Bob›."
              && RuntimeSpeech.Spoken("Bien hecho, ‹Bob›.") == "neiB ohceh, Bob.", "runtime voice: a marked name in Spanish, spoken",
              RuntimeSpeech.Spoken("Bien hecho, ‹Bob›."));

        // every MC row of the built table: the runtime text is the pre-rendered loñapsE with the name added
        string here = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string builtPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/build/lonapse/phrases.tsv"));
        if (System.IO.File.Exists(builtPath))
        {
            PhraseTable built = PhraseTable.Parse(new List<string>(System.IO.File.ReadAllLines(builtPath, System.Text.Encoding.UTF8)), new List<string>());
            int mc = 0, named = 0, versionsNamed = 0, versionsAll = 0;
            var off = new List<string>();
            foreach (PhraseEntry pe in built.Entries)
            {
                if (pe.Role != "mc") continue;
                mc++;
                string sp2 = RuntimeSpeech.AddName(pe.Spanish, RuntimeSpeech.MarkName("Bob"));
                bool anyEn = false;
                foreach (string v in SubtitleSlots.Versions(pe.English))
                {
                    versionsAll++;
                    if (RuntimeSpeech.AddName(v, "Bob") != null || SubtitleSlots.NeedsFill(v)) { versionsNamed++; anyEn = true; }
                }
                if (sp2 == null || !anyEn) continue;
                named++;
                string wantSp = RuntimeSpeech.AddName(pe.LonapseText, "Bob");
                if (RuntimeSpeech.Spoken(sp2) != wantSp && off.Count < 5) off.Add(pe.ClipKey + ": " + RuntimeSpeech.Spoken(sp2) + " / " + wantSp);
            }
            Check(off.Count == 0 && named * 2 > mc, "runtime voice: " + named + " of " + mc + " MC lines take a name ("
                  + versionsNamed + " of " + versionsAll + " subtitle versions), spoken text = lonapse_text + name",
                  string.Join(" ; ", off.ToArray()));
        }
        // numbers in Spanish words
        object[][] nums = {
            new object[] { 0L, false, "cero" }, new object[] { 1L, true, "un" }, new object[] { 1L, false, "uno" },
            new object[] { 16L, false, "dieciséis" }, new object[] { 21L, true, "veintiún" }, new object[] { 22L, false, "veintidós" },
            new object[] { 31L, true, "treinta y un" }, new object[] { 45L, false, "cuarenta y cinco" }, new object[] { 100L, false, "cien" },
            new object[] { 101L, false, "ciento uno" }, new object[] { 250L, false, "doscientos cincuenta" },
            new object[] { 999L, false, "novecientos noventa y nueve" }, new object[] { 1000L, false, "mil" },
            new object[] { 2500L, false, "dos mil quinientos" }, new object[] { 21000L, false, "veintiún mil" },
            new object[] { 120000L, false, "ciento veinte mil" }, new object[] { 1000000L, false, "un millón" },
            new object[] { 3200001L, true, "tres millones doscientos mil un" }, new object[] { -5L, false, "menos cinco" },
        };
        var badNum = new List<string>();
        foreach (object[] nn in nums)
        {
            string got = RuntimeLines.Number((long)nn[0], (bool)nn[1]);
            if (got != (string)nn[2]) badNum.Add(nn[0] + " -> " + got);
        }
        Check(badNum.Count == 0, "runtime voice: numbers in Spanish words (apocope before a noun)", string.Join(" ; ", badNum.ToArray()));
        Check(RuntimeLines.Words("alt_km", 12.46) == "doce" && RuntimeLines.Digits("alt_km", 12.46) == "12"
              && RuntimeLines.Words("alt_km", 7.46) == "siete coma cinco" && RuntimeLines.Digits("alt_km", 7.46) == "7.5"
              && RuntimeLines.Words("funds", 43250) == "cuarenta y tres mil" && RuntimeLines.Digits("funds", 43250) == "43,000"
              && RuntimeLines.Words("day", 21) == "veintiuno" && RuntimeLines.Words("speed", 2281.4) == "dos mil doscientos ochenta y un",
              "runtime voice: spoken value and subtitle digits are rounded the same way",
              RuntimeLines.Words("alt_km", 12.46) + " / " + RuntimeLines.Digits("alt_km", 12.46) + " ; " + RuntimeLines.Words("alt_km", 7.46) + " / " + RuntimeLines.Digits("alt_km", 7.46));

        // big numbers stay short (15 000 000 km was a long string of words)
        object[][] big = {
            new object[] { "alt_km", 15000000.0, "quince millones", "15 million" },
            new object[] { "alt_km", 15340000.0, "quince millones", "15 million" },
            new object[] { "alt_km", 2480000.0, "dos coma cinco millones", "2.5 million" },
            new object[] { "funds_total", 1000400.0, "un millón", "1 million" },
            new object[] { "funds_total", 999700.0, "un millón", "1 million" },
            new object[] { "funds_total", 21000000.0, "veintiún millones", "21 million" },
            new object[] { "funds_total", 123456.0, "ciento veintitrés mil", "123,000" },
            new object[] { "speed", 12345.0, "doce mil", "12,000" },
            new object[] { "speed", 9876.0, "nueve mil ochocientos setenta y seis", "9,876" },
            new object[] { "rep", -15000.0, "menos quince mil", "-15,000" },
            new object[] { "alt_km", 7.46, "siete coma cinco", "7.5" },
            new object[] { "alt_km", 2368000000.0, "dos coma cuatro mil millones", "2.4 billion" },
            new object[] { "funds_total", 1000000000.0, "mil millones", "1 billion" },
            new object[] { "alt_km", 13600000000.0, "catorce mil millones", "14 billion" },
        };
        var badBig = new List<string>();
        foreach (object[] bg in big)
        {
            string w = RuntimeLines.Words((string)bg[0], (double)bg[1]), d = RuntimeLines.Digits((string)bg[0], (double)bg[1]);
            if (w != (string)bg[2] || d != (string)bg[3]) badBig.Add(bg[1] + " -> " + w + " / " + d);
        }
        Check(badBig.Count == 0, "runtime voice: big numbers are rounded to thousands and millions, the same in words and digits",
              string.Join(" ; ", badBig.ToArray()));
        // any value from 0 to 10 billion: at most 6 words (the longest below 10 000, like "nueve mil ochocientos setenta y seis")
        var longest = "";
        foreach (string k in new[] { "alt_km", "speed", "funds_total", "dist_m", "sci_total" })
            for (double xv = 0.37; xv < 1e10; xv = xv * 1.37 + 1)
            {
                string w = RuntimeLines.Words(k, xv);
                if (w.Split(' ').Length > longest.Split(' ').Length) longest = w;
            }
        Check(longest.Split(' ').Length <= 6, "runtime voice: no value takes more than 6 words to say", longest);
        // stock body names: the same spelling as bodies.tsv (read forwards), and not changed again by Respell
        string bodiesPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/lonapse/bodies.tsv"));
        var badBody = new List<string>();
        int bodyRows = 0;
        foreach (string line in System.IO.File.ReadAllLines(bodiesPath, System.Text.Encoding.UTF8))
        {
            if (line.StartsWith("#") || line.StartsWith("key\t") || line.Trim().Length == 0) continue;
            string[] f = line.Split('\t');
            if (f[0] == "generic") continue;
            bodyRows++;
            string want2 = LonapseText.ReverseText(f[1]);
            if (RuntimeLines.BodySpoken(f[0]) != want2) badBody.Add(f[0] + ": " + RuntimeLines.BodySpoken(f[0]) + " / " + want2);
            if (RuntimeLines.BodyEnglish(char.ToUpperInvariant(f[0][0]) + f[0].Substring(1)) != f[2]) badBody.Add(f[0] + " english " + f[2]);
        }
        Check(badBody.Count == 0 && bodyRows == 17, "runtime voice: stock body names are spoken like the pre-rendered clips (" + bodyRows + ")",
              string.Join(" ; ", badBody.ToArray()));
        Check(RuntimeLines.BodySpoken("Rhode^N") == "Rode" && RuntimeLines.BodyEnglish("Rhode^N") == "Rhode",
              "runtime voice: a modded body is respelled, the ^N tag goes", RuntimeLines.BodySpoken("Rhode^N"));
        Check(RuntimeSpeech.Spoken("Sobre " + RuntimeSpeech.MarkRaw("la Mun") + " y " + RuntimeSpeech.MarkName("Jool") + ".") == "erboS la Mun y Yul."
              && RuntimeSpeech.Unmarked("a ⟪b⟫ ‹c›") == "a b c", "runtime voice: a raw mark is spoken as it is, a name mark respelled",
              RuntimeSpeech.Spoken("Sobre " + RuntimeSpeech.MarkRaw("la Mun") + " y " + RuntimeSpeech.MarkName("Jool") + "."));

        // vessel names
        string[][] vessels = {
            new[] { "Kerbal X", "Kerbal X" }, new[] { "Mun Lander II", "Mun Lander 2" }, new[] { "Relay (Probe)", "Relay" },
            new[] { "Jumbo Debris", "Jumbo" }, new[] { "Untitled Space Craft", "Untitled Space Craft" },
            new[] { "A very long ship name with words", null }, new[] { "", null }, new[] { "I II", "I 2" },
        };
        var badV = new List<string>();
        foreach (string[] vv in vessels) if (RuntimeLines.VesselSpeakable(vv[0]) != vv[1]) badV.Add(vv[0] + " -> " + (RuntimeLines.VesselSpeakable(vv[0]) ?? "null"));
        Check(badV.Count == 0, "runtime voice: vessel names a voice can say (roman numbers, debris, brackets, too long)", string.Join(" ; ", badV.ToArray()));

        // body swap on the generic {body} rows of the built table
        string so, eo;
        Check(RuntimeSpeech.SwapBody("Saludos para el planeta.", "Say hi to the planet.", "⟪Rode⟫", "Rhode", out so, out eo)
              && so == "Saludos para ⟪Rode⟫." && eo == "Say hi to Rhode."
              && !RuntimeSpeech.SwapBody("El planetario.", "The planetarium.", "x", "y", out so, out eo),
              "runtime voice: the generic body words are swapped for a real name, whole words only", so + " / " + eo);
        if (System.IO.File.Exists(builtPath))
        {
            PhraseTable built2 = PhraseTable.Parse(new List<string>(System.IO.File.ReadAllLines(builtPath, System.Text.Encoding.UTF8)), new List<string>());
            int gen = 0, swapped = 0;
            foreach (PhraseEntry pe in built2.Entries)
            {
                if (pe.Body != "generic") continue;
                gen++;
                bool all = true;
                foreach (string v in SubtitleSlots.Versions(pe.English))
                    if (!RuntimeSpeech.SwapBody(pe.Spanish, v, "⟪Rode⟫", "Rhode", out so, out eo)) all = false;
                if (all) swapped++;
            }
            Check(gen > 0 && swapped == gen, "runtime voice: every generic {body} row takes a modded body name (" + swapped + " of " + gen + ")", null);
        }

        // runtime-only lines: every file in GameData/Lonapse/Phrases/Runtime
        string rtDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../GameData/Lonapse/Phrases/Runtime"));
        var rtErr = new List<string>();
        var rtFiles = new List<KeyValuePair<string, IList<string>>>();
        string[] rtPaths = System.IO.Directory.GetFiles(rtDir, "*.tsv");
        Array.Sort(rtPaths, StringComparer.Ordinal);
        foreach (string f in rtPaths)
            rtFiles.Add(new KeyValuePair<string, IList<string>>(System.IO.Path.GetFileName(f), System.IO.File.ReadAllLines(f, System.Text.Encoding.UTF8)));
        List<RuntimeTemplate> rtAll = RuntimeLines.ParseAll(rtFiles, rtErr);
        int rtVersions = 0;
        foreach (RuntimeTemplate t in rtAll) rtVersions += t.Versions.Count;
        var kindCount = new Dictionary<string, int>();
        foreach (RuntimeTemplate t in rtAll) { int c0; kindCount.TryGetValue(t.Kind, out c0); kindCount[t.Kind] = c0 + 1; }
        var kc = new List<string>();
        foreach (var kv in kindCount) kc.Add(kv.Key + " " + kv.Value);
        Check(rtErr.Count == 0 && rtAll.Count >= 40 && rtVersions > rtAll.Count && rtFiles.Count >= 5
              && System.Linq.Enumerable.All(RuntimeLines.Kinds, k => kindCount.ContainsKey(k) || Array.IndexOf(RuntimeLines.ModKinds, k) >= 0),
              "runtime voice: Phrases/Runtime parses (" + rtFiles.Count + " files, " + rtVersions + " versions), every kind has templates (" + rtAll.Count + ": " + string.Join(", ", kc.ToArray()) + ")",
              string.Join(" ; ", rtErr.ToArray()));
        {
            // a report that talks to one kerbal by name gets a reply of its own (to=), not only the generic ones
            var noReply = new List<string>();
            foreach (RuntimeTemplate t in rtAll)
            {
                if (t.Kind != "report" || !t.NamesKerbal) continue;
                bool has = false;
                foreach (RuntimeTemplate r in rtAll)
                    if (r.Kind == "reply")
                        foreach (string to in r.To)
                            if (to == t.Id || (to.EndsWith("*") && t.Id.StartsWith(to.Substring(0, to.Length - 1)))) has = true;
                if (!has) noReply.Add(t.Id);
            }
            Check(noReply.Count == 0, "runtime voice: every report that names a kerbal has its own reply", string.Join(" ", noReply.ToArray()));
            // tourists: the career lines are for crew who work here, tourists get their own lines, answers and chats
            var careerOpen = new List<string>();
            int tourReports = 0, tourReplies = 0, tourA = 0, tourB = 0, tourPair = 0;
            foreach (RuntimeTemplate t in rtAll)
            {
                if (t.Kind == "report" && t.Where.StartsWith("crew.tsv") && !t.Id.StartsWith("rt_crew_tourist"))
                {
                    bool gated = false;
                    foreach (var c in t.Checks) if (c.Key == "is_tourist" && c.Value.Value == 0) gated = true;
                    if (!gated) careerOpen.Add(t.Id);
                }
                if (t.Kind == "report" && RuntimeLines.ForTourist(t)) tourReports++;
                if (t.Kind == "reply" && RuntimeLines.ForTourist(t)) tourReplies++;
                if (t.Kind == "chat")
                {
                    bool sa = Persona.ChatSideFits(t.TraitA, "tourist") && t.TraitA != null, sb = Persona.ChatSideFits(t.TraitB, "tourist") && t.TraitB != null;
                    if (sa && sb) tourPair++; else if (sa) tourA++; else if (sb) tourB++;
                }
            }
            Check(careerOpen.Count == 0 && tourReports >= 5 && tourReplies >= 6 && tourA >= 24 && tourB >= 23 && tourPair >= 24,
                  "runtime voice: career lines skip tourists, tourists have own reports (" + tourReports + "), answers (" + tourReplies
                  + ") and chats (" + tourA + " tourist to crew, " + tourB + " crew to tourist, " + tourPair + " two tourists)",
                  string.Join(" ", careerOpen.ToArray()));
        }
        var rtBadErr = new List<string>();
        RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish", "rt_a\treport\tfuel<<2\tA {fuel}.\tA {fuel}.",
            "rt_b\tnope\t\tB.\tB.", "rt_c\treport\t\tC {fuel}.\tC.", "rt_d\treport\t\tD {nothing}.\tD {nothing}.", "x_e\treport\t\tE.\tE.",
            "rt_f\treport\torbit fuel>=2\tF {fuel}.\tF {fuel}.", "rt_f\treport\t\tF.\tF.",
            "rt_g\treport\t\tG. | G2.\tG.", "rt_h\treport\t\tH {fuel}. | H.\tH {fuel}. | H {ec}." }, rtBadErr);
        Check(rtBadErr.Count == 8, "runtime voice: bad template rows are reported (condition, kind, slots differ, unknown slot, id, duplicate, version count, version slots)",
              rtBadErr.Count + ": " + string.Join(" ; ", rtBadErr.ToArray()));

        var vals = new Dictionary<string, double> { { "alt_km", 85.2 }, { "pe_km", 80.4 }, { "ap_km", 91.0 }, { "speed", 2281 }, { "fuel", 63 }, { "day", 3 },
                                                    { "inc_deg", 0.4 }, { "ec", 95 } };
        var nms = new Dictionary<string, string[]> { { "body", new[] { "Kérbin", "Kerbin" } }, { "vessel", new[] { "Kerbal X", "Kerbal X" } } };
        var orbit = RuntimeLines.Fitting(rtAll, "report", "orbit", vals, nms);
        string rs, re;
        RuntimeTemplate t0 = rtAll.Find(t => t.Id == "rt_orbit");
        bool filled = t0 != null && RuntimeLines.Fill(t0, vals, nms, out rs, out re)
                      && re == "Stable orbit. Periapsis 80 km, apoapsis 91 km."
                      && RuntimeSpeech.Spoken(rs) == LonapseText.ReverseText("Órbita estable. Periapsis ochenta kilómetros, apoapsis noventa y un kilómetros.");
        Check(filled, "runtime voice: an orbit report, numbers in reversed Spanish words, digits in the subtitle",
              t0 == null ? "no rt_orbit" : (RuntimeLines.Fill(t0, vals, nms, out rs, out re) ? RuntimeSpeech.Spoken(rs) + " / " + re : "fill failed"));
        var ids = new List<string>();
        foreach (RuntimeTemplate t in orbit) ids.Add(t.Id);
        Check(!ids.Contains("rt_flight") && !ids.Contains("rt_fuel_low") && !ids.Contains("rt_orbit_inc") && !ids.Contains("rt_node")
              && ids.Contains("rt_fuel") && ids.Contains("rt_vessel") && ids.Contains("rt_orbit_eq") && ids.Contains("rt_ec_ok")
              && RuntimeLines.Fitting(rtAll, "report", "landed", new Dictionary<string, double>(), new Dictionary<string, string[]>()).Count == 0,
              "runtime voice: templates fit the situation, the checks and the values there are", string.Join(", ", ids.ToArray()));

        // every template fills with every value, the check bounds are respected by a value that fits them
        var full = new Dictionary<string, double>();
        foreach (string k in RuntimeLines.NumberSlots) full[k] = 50;
        var fullN = new Dictionary<string, string[]>();
        foreach (string k in RuntimeLines.NameSlots) fullN[k] = new[] { "Nombre", "Name" };
        fullN["x_frozen"] = new[] { "Jeb", "Jeb" };   // the DeepFreeze lines (deepfreeze.tsv)
        var badT = new List<string>();
        foreach (RuntimeTemplate t in rtAll)
        for (int v = 0; v < t.Versions.Count; v++)
        {
            string a1, a2;
            if (!RuntimeLines.Fill(t, v, full, fullN, out a1, out a2)) badT.Add(t.Id + " version " + (v + 1) + " does not fill");
            else if (a2.IndexOf('{') >= 0 || a1.IndexOf('{') >= 0) badT.Add(t.Id + " has a slot left");
            else if (PhraseTable.HasExplosionWord(a1) || PhraseTable.HasExplosionWord(a2)) badT.Add(t.Id + " has an explosion word");
            else if (RuntimeSpeech.Spoken(a1).IndexOfAny(new[] { '{', '}', '⟪', '⟫' }) >= 0) badT.Add(t.Id + " spoken text has marks left");
        }
        Check(badT.Count == 0, "runtime voice: every version of every template fills and speaks cleanly (" + rtVersions + ")", string.Join(" ; ", badT.ToArray()));
        {
            // versions: only the ones whose slots have a value are picked
            var vt = RuntimeLines.Parse(new List<string> { "kind\tid\twhen\tnote\tenglish\tspanish",
                "report\trt_v\t\tany note\tA {fuel}. | B {ec}. | C.\tA {fuel}. | B {ec}. | C." }, null);
            List<int> fv = vt.Count == 1 ? RuntimeLines.FillableVersions(vt[0], new Dictionary<string, double> { { "ec", 5 } }, null) : null;
            Check(fv != null && fv.Count == 2 && fv[0] == 1 && fv[1] == 2,
                  "runtime voice: columns by name, and a template only picks the versions it can fill", fv == null ? "parse failed" : string.Join(",", fv.ConvertAll(vx => vx.ToString()).ToArray()));
            var dupErr = new List<string>();
            RuntimeLines.ParseAll(new[] {
                new KeyValuePair<string, IList<string>>("a.tsv", new List<string> { "id\tkind\twhen\tspanish\tenglish", "rt_x\treport\t\tX.\tX.", "rt_chat_s_1\tchat\t\tS.\tS." }),
                new KeyValuePair<string, IList<string>>("b.tsv", new List<string> { "id\tkind\twhen\tspanish\tenglish", "rt_x\treport\t\tX.\tX.", "rt_chat_s_2\tchat\t\tS.\tS." }) }, dupErr);
            Check(dupErr.Count == 2, "runtime voice: an id is unique over all files, a chat stays in one file", string.Join(" ; ", dupErr.ToArray()));
        }
        Check(RuntimeLines.BiomeSpeakable("Highlands") == "Highlands" && RuntimeLines.BiomeSpeakable("Northern Basin") == "Northern Basin"
              && RuntimeLines.BiomeSpeakable("Côte Sauvage") == "Côte Sauvage" && RuntimeLines.BiomeSpeakable("#LOC_Pack_Biome3") == null
              && RuntimeLines.BiomeSpeakable("高地") == null && RuntimeLines.BiomeSpeakable("Высокогорье") == null
              && RuntimeLines.BiomeSpeakable("Northern Polar Highland Craters") == null && RuntimeLines.BiomeSpeakable("") == null,
              "runtime voice: biome names from mods are said, untranslated tags and other scripts are left out", null);
        Check(RuntimeLines.VesselSpeakable("Kerbal X") == "Kerbal X" && RuntimeLines.VesselSpeakable("Señora Brillitos") == "Señora Brillitos"
              && RuntimeLines.VesselSpeakable("Восток 1") == null && RuntimeLines.VesselSpeakable("はやぶさ") == null
              && RuntimeLines.VesselSpeakable("Mun Lander II") == "Mun Lander 2",
              "runtime voice: vessel names in another alphabet are not said", null);
        {
            // shadow: behind the body inside its cylinder is dark, in front or beside it is lit
            bool shadowOk = DcMath.InShadow(-700, 0, 0, 1e9, 0, 0, 600) && !DcMath.InShadow(700, 0, 0, 1e9, 0, 0, 600)
                            && !DcMath.InShadow(-700, 650, 0, 1e9, 0, 0, 600) && DcMath.InShadow(-5000, 100, 100, 1, 0, 0, 600)
                            && !DcMath.InShadow(-700, 0, 0, 0, 0, 0, 600);
            // personas in chats: a=dim b=brave, several keys for one kerbal must all hold
            var pErr = new List<string>();
            var pChats = RuntimeLines.Chats(RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish",
                "rt_chat_p_1\tchat\ta=dim a=pilot b=brave\tP.\tP.", "rt_chat_p_2\tchat\t\tP.\tP.",
                "rt_chat_q_1\tchat\ta=clever\tQ.\tQ.", "rt_chat_q_2\tchat\t\tQ.\tQ." }, pErr));
            var pc = pChats.Count == 1 ? pChats[0] : null;
            var pn = new Dictionary<string, double>();
            bool personaOk = pc != null && pErr.Count == 2
                && RuntimeLines.ChatFits(pc, "orbit", pn, fullN, fullN, "pilot dim nervous", "engineer brave")
                && !RuntimeLines.ChatFits(pc, "orbit", pn, fullN, fullN, "pilot nervous", "engineer brave")
                && !RuntimeLines.ChatFits(pc, "orbit", pn, fullN, fullN, "scientist dim", "engineer brave")
                && new Persona(0.9f, 0.9f, false, false, "Pilot").ChatKeys == "pilot brave dim";
            // sunrise lines and recorded MC lines get the general replies, a to= reply only its own lines
            var rec = new RuntimeTemplate("cm_001", RuntimeLines.RecordedKind, "", "");
            var rRec = RuntimeLines.Replies(rtAll, rec, "orbit", new Dictionary<string, double>(),
                new Dictionary<string, string[]> { { "self", new[] { "Yeb", "Jeb" } } }).ConvertAll(t => t.Id);
            RuntimeTemplate sunT = rtAll.Find(t => t.Id == "rt_sunrise_count");
            var rSun = RuntimeLines.Replies(rtAll, sunT, "orbit", new Dictionary<string, double>(),
                new Dictionary<string, string[]> { { "self", new[] { "Yeb", "Jeb" } } }).ConvertAll(t => t.Id);
            Check(shadowOk && personaOk && rRec.Contains("rt_re_any") && !rRec.Contains("rt_re_fuel") && !rRec.Contains("rt_re_sunrise")
                  && rSun.Contains("rt_re_sunrise") && rSun.Contains("rt_re_any"),
                  "runtime voice: sunrise shadow test, persona checks in chats, replies to sunrise and recorded MC lines",
                  "shadow " + shadowOk + " persona " + personaOk + " (" + string.Join(" ; ", pErr.ToArray()) + ") / " + string.Join(",", rRec.ToArray()) + " / " + string.Join(",", rSun.ToArray()));
        }
        {
            // crew replies: a reply answers its to= lines, or any report and warp line without one
            var rn = new Dictionary<string, string[]> { { "self", new[] { "Yeb", "Jeb" } }, { "vessel", new[] { "Kerbal X", "Kerbal X" } },
                                                        { "body", new[] { "Kérbin", "Kerbin" } } };
            var rv = new Dictionary<string, double> { { "fuel", 12 }, { "crew", 3 } };
            RuntimeTemplate fuelLow = rtAll.Find(t => t.Id == "rt_fuel_low"), dockFar = rtAll.Find(t => t.Id == "rt_dock_far"),
                            orbitInc = rtAll.Find(t => t.Id == "rt_orbit_inc"), sciPts = rtAll.Find(t => t.Id == "rt_sci_pts");
            var rFuel = RuntimeLines.Replies(rtAll, fuelLow, "orbit", rv, rn).ConvertAll(t => t.Id);
            var rDock = RuntimeLines.Replies(rtAll, dockFar, "orbit", rv, rn).ConvertAll(t => t.Id);
            var rOrbit = RuntimeLines.Replies(rtAll, orbitInc, "orbit", rv, rn).ConvertAll(t => t.Id);
            var rSci = RuntimeLines.Replies(rtAll, sciPts, "orbit", rv, rn).ConvertAll(t => t.Id);
            var toErr = new List<string>();
            RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish", "rt_r1\treport\tto=rt_fuel\tA.\tA.",
                "rt_r2\treply\tto=fuel\tB.\tB.", "rt_r3\treply\tto=rt_fuel* to=rt_ec\tC.\tC." }, toErr);
            Check(rFuel.Contains("rt_re_fuel_low") && rFuel.Contains("rt_re_any") && rFuel.Contains("rt_re_vessel") && !rFuel.Contains("rt_re_fuel")
                  && rDock.Count == 0 && rOrbit.Contains("rt_re_orbit") && rSci.Contains("rt_re_sci") && !rSci.Contains("rt_re_any")
                  && toErr.Count == 2,
                  "runtime voice: crew replies answer their own MC lines, the general ones any report",
                  string.Join(",", rFuel.ToArray()) + " / dock " + rDock.Count + " / " + string.Join(",", rOrbit.ToArray()) + " / " + string.Join(",", rSci.ToArray())
                  + " / " + string.Join(" ; ", toErr.ToArray()));
            {
                var en = new Dictionary<string, double> { { "jet", 80 }, { "ship_m", 120 }, { "eva_mins", 6 }, { "at_home", 0 }, { "day", 3 } };
                var enm = new Dictionary<string, string[]> { { "kerbal", new[] { "Yeb", "Jeb" } }, { "here", new[] { "Mun", "Mun" } },
                                                            { "biome", new[] { "Midlands", "Midlands" } }, { "vessel", new[] { "Kerbal X", "Kerbal X" } } };
                var evaFit = RuntimeLines.Fitting(rtAll, "eva", "landed", en, enm).ConvertAll(t => t.Id);
                RuntimeTemplate flagNamed = rtAll.Find(t => t.Id == "rt_flag_named");
                var fn = new Dictionary<string, string[]>(enm) { { "flag", new[] { "Bob", "Bob" } }, { "self", new[] { "Yeb", "Jeb" } } };
                var flagRe = flagNamed != null ? RuntimeLines.Replies(rtAll, flagNamed, "landed", en, fn).ConvertAll(t => t.Id) : new List<string>();
                var noFlag = RuntimeLines.Fitting(rtAll, "flag", "landed", en, enm).ConvertAll(t => t.Id);
                Check(evaFit.Contains("rt_eva_far") && evaFit.Contains("rt_eva_jet") && evaFit.Contains("rt_eva_away") && evaFit.Contains("rt_eva_time")
                      && !evaFit.Contains("rt_eva_near") && !evaFit.Contains("rt_eva_jet_low") && !evaFit.Contains("rt_eva_orbit")
                      && flagRe.Contains("rt_re_flag_named") && flagRe.Contains("rt_re_flag") && !noFlag.Contains("rt_flag_named") && noFlag.Contains("rt_flag_ship"),
                      "runtime voice: EVA lines fit the jetpack, the ship distance and the time outside. Flag lines get the planter's answer",
                      string.Join(",", evaFit.ToArray()) + " / " + string.Join(",", flagRe.ToArray()));
            }
            {
                var evaChats = RuntimeLines.Chats(rtAll).FindAll(c => c[0].AboutEva);
                var na = new Dictionary<string, string[]> { { "self", new[] { "Yeb", "Jeb" } }, { "other", new[] { "Bil", "Bill" } },
                                                           { "vessel", new[] { "Kerbal X", "Kerbal X" } }, { "biome", new[] { "Midlands", "Midlands" } } };
                var nb = new Dictionary<string, string[]>(na);
                nb["self"] = new[] { "Bil", "Bill" };
                nb["other"] = new[] { "Yeb", "Jeb" };
                var onEva = new Dictionary<string, double> { { "on_eva", 1 }, { "pair_m", 50 }, { "jet", 20 }, { "ship_m", 80 }, { "eva_mins", 12 } };
                var inside = new Dictionary<string, double> { { "fuel", 50 }, { "crew", 2 } };
                List<RuntimeTemplate> pair = evaChats.Find(c => c[0].Id == "rt_chat_evapair_1");
                bool evaFits = pair != null && RuntimeLines.ChatFits(pair, "landed", onEva, na, nb, "pilot", "scientist");
                bool insideFits = evaChats.Exists(c => RuntimeLines.ChatFits(c, "landed", inside, na, nb, "pilot brave", "scientist nervous"));
                Check(evaChats.Count >= 12 && evaFits && !insideFits,
                      "runtime voice: EVA suit radio chats fit on EVA with their values, never inside", evaChats.Count + " EVA chats");

                // radio chats between two vessels: only with apart, and the a_eva/b_eva checks pick the right side
                var allChats = RuntimeLines.Chats(rtAll);
                var apartChats = allChats.FindAll(c => c[0].AboutApart);
                var shipToEva = new Dictionary<string, double> { { "apart", 1 }, { "a_eva", 0 }, { "b_eva", 1 }, { "pair_m", 80 }, { "link", 1 }, { "eva_mins", 12 } };
                var evaToShip = new Dictionary<string, double> { { "apart", 1 }, { "a_eva", 1 }, { "b_eva", 0 }, { "pair_m", 80 }, { "jet", 20 }, { "link", 1 } };
                var na2 = new Dictionary<string, string[]>(na) { { "my_ship", new[] { "Kerbal X", "Kerbal X" } } };
                var nb2 = new Dictionary<string, string[]>(nb) { { "their_ship", new[] { "Kerbal X", "Kerbal X" } } };
                var se = apartChats.FindAll(c => RuntimeLines.ChatFits(c, "landed", shipToEva, na2, nb2)).ConvertAll(c => c[0].Id);
                var es = apartChats.FindAll(c => RuntimeLines.ChatFits(c, "landed", evaToShip, nb2, na2)).ConvertAll(c => c[0].Id);
                bool noApartInside = !apartChats.Exists(c => RuntimeLines.ChatFits(c, "landed", inside, na, nb));
                Check(apartChats.Count >= 12 && se.Contains("rt_chat_rshipeva_1") && !se.Contains("rt_chat_rjet_1")
                      && es.Contains("rt_chat_rjet_1") && !es.Contains("rt_chat_rshipeva_1") && !se.Contains("rt_chat_rnolink_1") && noApartInside
                      && !apartChats.Exists(c => c[0].AboutEva || c[0].IsEvent),
                      "runtime voice: radio chats between two vessels fit the side of each kerbal, never in one cabin",
                      apartChats.Count + " radio chats, ship to EVA " + string.Join(",", se.ToArray()) + " / EVA to ship " + string.Join(",", es.ToArray()));

                // event chats: only on their event, one line for a kerbal alone, link picks Control or not
                var ev = allChats.FindAll(c => c[0].IsEvent);
                Func<string, bool, double, List<string>> evFit = (e, alone, link) => ev.FindAll(c => RuntimeLines.OnFits(c[0], e) && alone == (c.Count == 1)
                    && RuntimeLines.ChatFits(c, "landed", new Dictionary<string, double> { { "link", link }, { "at_home", 0 } },
                        new Dictionary<string, string[]>(na) { { "here", new[] { "la Mun", "Mun" } }, { "exp", new[] { "x", "surface sample" } },
                                                               { "device", new[] { "x", "the antenna" } } }, nb)).ConvertAll(c => c[0].Id);
                List<string> sampleLink = evFit("surfacesample", true, 1), sampleNo = evFit("surfacesample", true, 0),
                             flagAloneLink = evFit("flag", true, 1), flagAloneNo = evFit("flag", true, 0), flagPair = evFit("flag", false, 1),
                             deploy = evFit("deploy", true, 1), tempNo = evFit("temperaturescan", true, 0);
                bool noControlWithoutLink = !ev.Exists(c => c[0].Checks.Exists(k => k.Key == "link" && k.Value.Value == 0)
                                                            && c.Exists(l => l.Versions.Exists(vv => System.Text.RegularExpressions.Regex.IsMatch(vv[1], @"\bControl\b"))));
                Check(ev.Count >= 15 && sampleLink.Contains("rt_chat_ssample_1") && !sampleLink.Contains("rt_chat_ssamplenolink_1")
                      && sampleNo.Contains("rt_chat_ssamplenolink_1") && flagAloneLink.Count == 0 && flagAloneNo.Contains("rt_chat_fsolo_1")
                      && flagPair.Contains("rt_chat_fflag_1") && deploy.Contains("rt_chat_sdeploy_1") && tempNo.Contains("rt_chat_snolink_1")
                      && !tempNo.Contains("rt_chat_ssamplenolink_1") && noControlWithoutLink
                      && !RuntimeLines.OnFits(ev.Find(c => c[0].Id == "rt_chat_snolink_1")[0], "flag")
                      && !RuntimeLines.OnFits(ev.Find(c => c[0].Id == "rt_chat_snolink_1")[0], "deploy"),
                      "runtime voice: event chats play on their event, alone one line, without a link never to Control",
                      ev.Count + " event chats; sample " + string.Join(",", sampleLink.ToArray()) + " / " + string.Join(",", sampleNo.ToArray())
                      + "; flag " + string.Join(",", flagAloneNo.ToArray()) + " / " + string.Join(",", flagPair.ToArray()));
                var sciOn = rtAll.FindAll(t => (t.Kind == "science" || t.Kind == "science_pts") && t.On.Count > 0).ConvertAll(t => t.Id);
                Check(sciOn.Contains("rt_sci_sample_pts") && sciOn.Contains("rt_sci_deploy") && sciOn.Contains("rt_sci_evarep"),
                      "runtime voice: Mission Control has science lines for the EVA experiments and deployed parts", string.Join(",", sciOn.ToArray()));
                var oneErr = new List<string>();
                var one = RuntimeLines.ParseAll(new[] { new KeyValuePair<string, IList<string>>("a.tsv", new List<string> {
                    "id\tkind\twhen\tspanish\tenglish", "rt_chat_ok_1\tchat\ton=flag\tA.\tA.", "rt_chat_bad_1\tchat\t\tB.\tB.",
                    "rt_x\treport\ton=flag\tX.\tX.", "rt_chat_ev_1\tchat\ton=nothing\tE.\tE." }) }, oneErr);
                Check(one.Count == 1 && one[0].Id == "rt_chat_ok_1" && oneErr.Count == 3,
                      "runtime voice: an event chat may have one line, a normal chat not, on= only on chats and science, known events only",
                      string.Join(" ; ", oneErr.ToArray()));
            }
            {
                // the API for other mods: a mod's own file next to the Lonapse files, with an example mod
                string exPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../docs/api/example_mod.tsv"));
                var modFiles = new List<KeyValuePair<string, IList<string>>>(rtFiles);
                modFiles.Add(new KeyValuePair<string, IList<string>>("ExampleMod/Lonapse/example_mod.tsv",
                    System.IO.File.ReadAllLines(exPath, System.Text.Encoding.UTF8)));
                var modErr = new List<string>();
                List<RuntimeTemplate> withMod = RuntimeLines.ParseAll(modFiles, modErr);
                var mine = withMod.FindAll(t => t.Where.StartsWith("ExampleMod/", StringComparison.Ordinal));
                ModEventInfo repair = ModEvents.Info(withMod, "examplemod.repair_done");
                ModEventInfo supplies = ModEvents.Info(withMod, "examplemod.supplies_low");
                ModEventInfo exp = ModEvents.Info(withMod, "examplemod.experiment_done");
                List<string> modIds = ModEvents.Ids(withMod).FindAll(i => i.StartsWith("examplemod.", StringComparison.Ordinal));
                Check(modErr.Count == 0 && mine.Count >= 10 && withMod.Count == rtAll.Count + mine.Count
                      && repair.HasChat && repair.HasMc && repair.Cooldown == 180 && repair.Priority == 1 && repair.Who == "any"
                      && supplies.Priority == 2 && supplies.Cooldown == 600 && exp.Who == "mc" && exp.Priority == 0 && exp.HasLines
                      && !ModEvents.Info(withMod, "examplemod.nothing").HasLines && modIds.Count == 3 && modIds[0] == "examplemod.experiment_done",
                      "mod API: the example mod file parses next to the Lonapse files, with its meta settings",
                      mine.Count + " mod templates; " + string.Join(" ; ", modErr.ToArray()));
                // DeepFreeze: Lonapse's own lines for the events the bridge sends
                {
                    var dfIds = new[] { "deepfreeze.frozen", "deepfreeze.thawed", "deepfreeze.comatose" };
                    var dfBad = new List<string>();
                    foreach (string id in dfIds)
                    {
                        ModEventInfo inf = ModEvents.Info(rtAll, id);
                        if (!inf.HasLines || !inf.HasChat || !inf.HasMc) dfBad.Add(id + " misses chat or Mission Control lines");
                        if (inf.Cooldown != 60) dfBad.Add(id + " cooldown " + inf.Cooldown);
                    }
                    foreach (RuntimeTemplate t in rtAll)
                        if (t.Where.StartsWith("deepfreeze.tsv") && t.Kind == "event"
                            && !rtAll.Exists(r => r.Kind == "reply" && r.To.Contains(t.Id))) dfBad.Add(t.Id + " has no reply");
                    Check(dfBad.Count == 0, "DeepFreeze: frozen, thawed and comatose have chats, Mission Control lines and replies",
                          string.Join(" ; ", dfBad.ToArray()));
                }

                // mod events never mix with the stock events, the stock lines never answer them
                RuntimeTemplate anySci = withMod.Find(t => t.Id == "rt_chat_snolink_1");
                RuntimeTemplate modChat = withMod.Find(t => t.Id == "rt_chat_examplemod_repair_1");
                Check(anySci != null && modChat != null && !RuntimeLines.OnFits(anySci, "examplemod.repair_done")
                      && RuntimeLines.OnFits(modChat, "examplemod.repair_done") && !RuntimeLines.OnFits(modChat, "examplemod.supplies_low")
                      && !RuntimeLines.OnFits(modChat, "flag") && !withMod.Exists(t => t.Kind == "reply" && t.To.Count == 0
                          && RuntimeLines.Answers(t, "rt_examplemod_repair", "event")),
                      "mod API: a mod event only fits its own lines, and only its own replies answer it", "");

                // a mod experiment through stock science: the experiment row names its topic and the words of {exp}
                RuntimeTemplate soilSci = withMod.Find(t => t.Id == "rt_sci_examplemod_soil");
                RuntimeTemplate stockSci = withMod.Find(t => t.On.Contains("science"));
                RuntimeTemplate soilChat = withMod.Find(t => t.Id == "rt_chat_examplemod_soil_1");
                string soilTopic = RuntimeLines.ScienceTopic(withMod, "exampleSoil@KerbinSrfLandedShores");
                string soilGen = RuntimeLines.GeneralOf(withMod, soilTopic);
                Check(soilTopic == "examplemod.soil" && RuntimeLines.ScienceTopic(withMod, "crewReport@KerbinFlyingLow") == "crewreport"
                      && RuntimeLines.ModExperiment(withMod, "examplesoil") != null
                      && RuntimeLines.ExperimentWords(withMod, "exampleSoil")[1] == "soil analysis"
                      && RuntimeLines.ExperimentWords(withMod, "otherModThing")[1] == "experiment"
                      && soilGen == "science" && RuntimeLines.GeneralOf(withMod, "examplemod.repair_done") == null
                      && soilSci != null && RuntimeLines.OnFits(soilSci, soilTopic, soilGen)
                      && !RuntimeLines.OnFits(soilSci, "crewreport", "science")
                      && stockSci != null && RuntimeLines.OnFits(stockSci, soilTopic, soilGen)
                      && !RuntimeLines.OnFits(stockSci, "examplemod.repair_done", null)
                      && soilChat != null && RuntimeLines.OnFits(soilChat, soilTopic, soilGen)
                      && !modIds.Contains("examplemod.soil") && ModEvents.IsRegisteredTopic(withMod, "examplemod.soil")
                      && !ModEvents.IsRegisteredTopic(withMod, "examplemod.repair_done"),
                      "mod API: an experiment row gives a mod experiment its topic and {exp} words, on=science fits it, Trigger never lists it",
                      soilTopic + " " + (stockSci != null ? stockSci.Id : "no on=science line"));

                // a mod's Breaking Ground part and contract type: the device and contract rows
                string drill = RuntimeLines.DeviceTopic(withMod, "exampleDrillRig");
                string drillGen = RuntimeLines.GeneralOf(withMod, drill);
                RuntimeTemplate deployChat = withMod.Find(t => t.Kind == "chat" && t.On.Contains("deploy"));
                RuntimeTemplate drillSci = withMod.Find(t => t.Id == "rt_sci_examplemod_drill");
                RuntimeTemplate salvage = withMod.Find(t => t.Id == "rt_con_examplemod_salvage");
                Check(drill == "examplemod.drill" && drillGen == "deploy" && RuntimeLines.DeviceTopic(withMod, "DeployedRTG") == "deploy"
                      && RuntimeLines.DeviceWords(withMod, "exampleDrillRig", "part")[1] == "the drill rig"
                      && RuntimeLines.DeviceWords(withMod, "DeployedRTG", "power")[1] == "the generator"
                      && deployChat != null && RuntimeLines.OnFits(deployChat, drill, drillGen)
                      && !RuntimeLines.OnFits(deployChat, soilTopic, soilGen)
                      && drillSci != null && RuntimeLines.OnFits(drillSci, drill, drillGen)
                      && RuntimeLines.ContractTopic(withMod, "ExampleSalvageContract") == "examplemod.salvage"
                      && RuntimeLines.ContractTopic(withMod, "PartTest") == null
                      && RuntimeLines.ContractWords(withMod, "examplesalvagecontract")[1] == "the salvage job"
                      && RuntimeLines.ContractWords(withMod, "PartTest") == null
                      && RuntimeLines.GeneralOf(withMod, "examplemod.salvage") == null
                      && salvage != null && salvage.On.Contains("examplemod.salvage")
                      && RuntimeLines.FillableVersions(salvage, new Dictionary<string, double> { { "funds", 5000 } },
                             new Dictionary<string, string[]> { { "contract", new[] { "x", "the salvage job" } } }).Count == 1
                      && RuntimeLines.FillableVersions(salvage, new Dictionary<string, double> { { "funds", 5000 } },
                             new Dictionary<string, string[]>()).Count == 0
                      && !modIds.Contains("examplemod.drill") && !modIds.Contains("examplemod.salvage"),
                      "mod API: device and contract rows give a mod's part and contract type a topic and words, on=deploy fits the part",
                      drill + " " + drillGen + " " + (deployChat != null ? deployChat.Id : "no on=deploy chat"));

                // x_ slots: numbers in Spanish words and digits, names as "spanish|english", checks on x_ values
                var xn = new Dictionary<string, double> { { "x_days", 4 } };
                var xs = new Dictionary<string, string[]> { { "other", new[] { "Bil", "Bill" } } };
                RuntimeTemplate sup = withMod.Find(t => t.Id == "rt_examplemod_supplies");
                RuntimeTemplate one = withMod.Find(t => t.Id == "rt_examplemod_supplies_one");
                string es0, en0;
                bool supFill = sup != null && RuntimeLines.Fill(sup, 0, xn, xs, out es0, out en0)
                               && en0 == "You have supplies for 4 days. Plan the trip home."
                               && RuntimeSpeech.Spoken(es0).Contains(LonapseText.ReverseText("cuatro"));
                string[] nm = ModEvents.SplitName("el grifo|the tap");
                var expNames = new Dictionary<string, string[]> { { "x_experiment", ModEvents.SplitName("plant growth") }, { "kerbal", new[] { "Yeb", "Jeb" } } };
                RuntimeTemplate et = withMod.Find(t => t.Id == "rt_examplemod_experiment");
                Check(supFill && RuntimeLines.Fitting(withMod, "event", "orbit", xn, xs).Contains(sup)
                      && !RuntimeLines.Fitting(withMod, "event", "orbit", xn, xs).Contains(one)
                      && RuntimeLines.Fitting(withMod, "event", "orbit", new Dictionary<string, double> { { "x_days", 1 } }, xs).Contains(one)
                      && nm[0] == "el grifo" && nm[1] == "the tap" && ModEvents.SplitName("") == null && ModEvents.SplitName("|x") == null
                      && et != null && RuntimeLines.FillableVersions(et, null, expNames).Count == 2,
                      "mod API: x_ slots fill from the mod's numbers and names, and checks work on them", "");

                // bad mod rows: each one only skips itself
                var badErr = new List<string>();
                var badFiles = new List<KeyValuePair<string, IList<string>>>(rtFiles);
                badFiles.Add(new KeyValuePair<string, IList<string>>("SomeMod/Lonapse/a.tsv", new List<string> {
                        "id\tkind\twhen\tspanish\tenglish",
                        "rt_somemod_ok\tevent\ton=somemod.thing\tVale {x_n}.\tOkay {x_n}.",
                        "rt_somemod_noon\tevent\t\tA.\tA.",
                        "rt_somemod_stock\tevent\ton=flag\tB.\tB.",
                        "rt_somemod_unknown\tchat\ton=nothing\tC.\tC.",
                        "rt_somemod_onelang\tevent\ton=somemod.thing\tD {x_n}.\tD.",
                        "rt_somemod_meta\tmeta\ton=somemod.thing cooldown=abc\t\t",
                        "rt_orbit\tevent\ton=somemod.thing\tE.\tE.",
                        "rt_somemod_check\tevent\ton=somemod.thing x_n>=2\tF.\tF.",
                        "rt_somemod_sci\tscience\ton=somemod.thing\tG.\tG.",
                        "rt_somemod_exp\texperiment\ton=somemod.scan id=someScan\tescaneo\tscan",
                        "rt_somemod_scan\tscience\ton=somemod.scan\tEl {exp}.\tThe {exp}.",
                        "rt_somemod_stockexp\texperiment\ton=somemod.crew id=crewReport\tinforme\treport",
                        "rt_somemod_noid\texperiment\ton=somemod.noid\tH\tH",
                        "rt_somemod_slotexp\texperiment\ton=somemod.slot id=slotScan\t{x_n}\t{x_n}",
                        "rt_somemod_twice\texperiment\ton=somemod.other id=someScan\tI\tI",
                        "rt_somemod_dev\tdevice\ton=somemod.rig id=someRig\tel equipo\tthe rig",
                        "rt_somemod_rigsci\tscience\ton=somemod.rig\tJ.\tJ.",
                        "rt_somemod_stockdev\tdevice\ton=somemod.rtg id=DeployedRTG\tK\tK",
                        "rt_somemod_con\tcontract\ton=somemod.job id=SomeJob\tel trabajo\tthe job",
                        "rt_somemod_jobdone\tcontract_done\ton=somemod.job\tHecho {contract}.\tDone: {contract}.",
                        "rt_somemod_jobsci\tscience\ton=somemod.job\tL.\tL.",
                        "rt_somemod_rigcon\tcontract_done\ton=somemod.rig\tM.\tM.",
                        "rt_somemod_stockcon\tcontract_done\ton=flag\tN.\tN." }));
                List<RuntimeTemplate> badRows = RuntimeLines.ParseAll(badFiles, badErr)
                    .FindAll(t => t.Where.StartsWith("SomeMod/", StringComparison.Ordinal));
                var badIds = badRows.ConvertAll(t => t.Id);
                Check(string.Join(",", badIds.ToArray()) == "rt_somemod_ok,rt_somemod_check,rt_somemod_exp,rt_somemod_scan,rt_somemod_dev,rt_somemod_rigsci,rt_somemod_con,rt_somemod_jobdone"
                      && badErr.Count == 15,
                      "mod API: bad mod rows are reported and skipped one by one (no on=, a stock event, unknown event, one language, bad meta, duplicate id, a science line on an unregistered topic, a stock experiment id, no id=, slots in the words, an id registered twice, a stock part, a science line on a contract topic, a contract line on a device topic or a stock event)",
                      badErr.Count + ": " + string.Join(" ; ", badErr.ToArray()));
                Check(ModEvents.ValidId("examplemod.repair_done") && ModEvents.ValidId("m2.e_1") && !ModEvents.ValidId("flag")
                      && !ModEvents.ValidId("a.b.c") && !ModEvents.ValidId(".x") && !ModEvents.ValidId("x.") && !ModEvents.ValidId("Mod.Event")
                      && !ModEvents.ValidId(null) && Slots.IsModSlot("x_days") && !Slots.IsModSlot("x_") && !Slots.IsModSlot("days")
                      && !Slots.IsModSlot("x_Days") && Slots.IsReserved("x_days"),
                      "mod API: event ids are mod.event, mod slots start with x_", "");

                // the pending list: cooldown per event, the global gap, high priority, max age, capacity
                var q = new ModEventQueue();
                bool okQ = q.Offer("m.a", 1, 120, 0, null) == null && q.Offer("m.a", 1, 120, 1, null) == "already waiting";
                var first = q.Take(2, 0);
                okQ &= first != null && first.Id == "m.a" && q.Offer("m.a", 1, 120, 50, null) == "cooldown";
                okQ &= q.Offer("m.b", 1, 120, 10, null) == null && q.Take(20, 0) == null;      // global gap of 30 s
                okQ &= q.Offer("m.c", 2, 120, 21, null) == null;
                var high = q.Take(22, 0);
                okQ &= high != null && high.Id == "m.c";                                        // high skips the gap
                okQ &= q.Take(60, 2) == null;                                                   // only high allowed now
                var qb = q.Take(60, 0);
                okQ &= qb != null && qb.Id == "m.b";
                okQ &= q.Offer("m.a", 1, 120, 130, null) == null;                               // its cooldown is over
                okQ &= q.Count == 1 && q.Take(300, 0) == null && q.Count == 0;                  // older than 90 s: gone
                for (int i = 0; i < 8; i++) q.Offer("m.e" + i, 0, 0, 400, null);
                okQ &= q.Count == ModEvents.MaxPending && q.Offer("m.z", 2, 0, 400, null) == "queue full";
                Check(okQ, "mod API: pending mod events keep their cooldowns, the gap between events, priorities, max age and the cap", "");
                var qc = new ModEventQueue();
                bool okC = qc.Offer("m.a", 1, 120, 0, null) == null && qc.Cancel("m.a") && !qc.Cancel("m.a") && qc.Count == 0
                           && !qc.Cancel("m.none") && qc.Offer("m.a", 1, 120, 1, null) == null;   // a cancelled event has no cooldown
                Check(okC, "mod API: Cancel drops a waiting event, without starting its cooldown", "");
            }
            Check(DcMath.WarpFactor(1f, 0.4f, 2.5f) == 1f && Math.Abs(DcMath.WarpFactor(100f, 0.4f, 2.5f) - 1.8f) < 1e-4f
                  && DcMath.WarpFactor(100000f, 0.4f, 2.5f) == 2.5f && DcMath.WarpFactor(1000f, 0f, 2.5f) == 1f
                  && Math.Abs(DcMath.WarpFactor(10f, 0.04f, 1.18f) - 1.04f) < 1e-4f && DcMath.WarpFactor(1e6f, 0.04f, 1.18f) == 1.18f,
                  "time warp: more talk and faster voices with the warp rate, capped", "");
            Check(RuntimeLines.DeviceWords("DeployedSeismicSensor", "experiment")[1] == "the seismometer"
                  && RuntimeLines.DeviceWords("SomeModdedThing", "comms")[1] == "the antenna"
                  && RuntimeLines.DeviceWords(null, "part")[1] == "the gadget",
                  "deployed parts: words by part name, else by kind", "");
            Check(Persona.JobFor("staging") == "pilot" && Persona.JobFor("touchdown") == "pilot" && Persona.JobFor("power_low") == "engineer"
                  && Persona.JobFor("science") == "scientist" && Persona.JobFor("chatter") == null && Persona.JobFor(null) == null
                  && Persona.JobForLine("rt_fuel_low") == "pilot" && Persona.JobForLine("rt_ec_ok") == "engineer"
                  && Persona.JobForLine("rt_sci_pts") == "scientist" && Persona.JobForLine("rt_funds") == null && Persona.JobForLine("rt_crew") == null,
                  "jobs: the pilot flies, the engineer watches power and parts, the scientist the science", "");
            Check(!rtAll.Exists(t => t.Kind == "reply" && t.Versions.Exists(vv0 => vv0[1].Contains("{self"))),
                  "runtime voice: no crew reply says its own name", "");
        }
        {
            // science and contracts: the extra values pick their own lines
            var sn = new Dictionary<string, string[]> { { "exp", new[] { "arutarepmet", "temperature" } }, { "body", new[] { "la Mun", "Mun" } },
                                                        { "vessel", new[] { "Kerbal X", "Kerbal X" } } };
            var sFirst = new Dictionary<string, double> { { "sci", 8.5 }, { "sci_first", 1 }, { "sci_left", 20 }, { "at_home", 0 } };
            var sDone = new Dictionary<string, double> { { "sci", 0.4 }, { "sci_first", 0 }, { "sci_left", 0.2 }, { "at_home", 0 } };
            var idsF = RuntimeLines.Fitting(rtAll, "science_pts", null, sFirst, sn).ConvertAll(t => t.Id);
            var idsD = RuntimeLines.Fitting(rtAll, "science_pts", null, sDone, sn).ConvertAll(t => t.Id);
            var cFail = new Dictionary<string, double> { { "funds", 12000 }, { "con_rep", 15 }, { "contracts", 0 } };
            var idsC = RuntimeLines.Fitting(rtAll, "contract_failed", null, cFail, sn).ConvertAll(t => t.Id);
            var idsP = RuntimeLines.Fitting(rtAll, "contract_funds", null, cFail, sn).ConvertAll(t => t.Id);
            Check(idsF.Contains("rt_sci_first") && idsF.Contains("rt_sci_left") && idsF.Contains("rt_sci_far") && !idsF.Contains("rt_sci_repeat")
                  && idsD.Contains("rt_sci_repeat") && idsD.Contains("rt_sci_small") && !idsD.Contains("rt_sci_first") && !idsD.Contains("rt_sci_pts_more")
                  && idsC.Contains("rt_con_fail_cost") && idsC.Contains("rt_con_fail_rep") && !idsC.Contains("rt_con_fail_left")
                  && idsP.Contains("rt_con_last") && !idsP.Contains("rt_con_left") && !idsP.Contains("rt_con_one"),
                  "runtime voice: science and contract lines fit the first data, what is left, the penalty and the open contracts",
                  string.Join(",", idsF.ToArray()) + " / " + string.Join(",", idsD.ToArray()) + " / " + string.Join(",", idsC.ToArray()) + " / " + string.Join(",", idsP.ToArray()));
        }
        {
            // time warp: warp chats only fit with in_warp, which the director only gives in warp
            var wv = new Dictionary<string, double> { { "in_warp", 1 }, { "node_h", 5.2 }, { "node_min", 312 }, { "soi_h", 12.4 }, { "day", 9 } };
            var wn = new Dictionary<string, string[]> { { "body", new[] { "Kérbin", "Kerbin" } }, { "next", new[] { "Duna", "Duna" } },
                                                        { "other", new[] { "Bil", "Bill" } } };
            var wIds = new List<string>();
            foreach (RuntimeTemplate t in RuntimeLines.Fitting(rtAll, "warp", "orbit", wv, wn)) wIds.Add(t.Id);
            var noWarp = new Dictionary<string, double>(wv);
            noWarp.Remove("in_warp");
            int chatsWarp = RuntimeLines.Fitting(rtAll, "chat", "orbit", wv, wn).FindAll(t => t.Id.StartsWith("rt_chat_warp", StringComparison.Ordinal) && t.Id.EndsWith("_1", StringComparison.Ordinal)).Count;
            int chatsNoWarp = RuntimeLines.Fitting(rtAll, "chat", "orbit", noWarp, wn).FindAll(t => t.Id.StartsWith("rt_chat_warp", StringComparison.Ordinal) && t.Id.EndsWith("_1", StringComparison.Ordinal)).Count;
            RuntimeTemplate soi = rtAll.Find(t => t.Id == "rt_warp_soi");
            string ws, we;
            bool soiOk = soi != null && RuntimeLines.Fill(soi, 0, wv, wn, out ws, out we) && we == "You reach Duna in 12 hours."
                         && RuntimeSpeech.Unmarked(ws) == "Llegáis a Duna en doce horas." && RuntimeSpeech.Spoken(ws).Contains(" Duna ");
            Check(wIds.Contains("rt_warp") && wIds.Contains("rt_warp_node_h") && wIds.Contains("rt_warp_soi") && wIds.Contains("rt_warp_orbit")
                  && !wIds.Contains("rt_warp_node") && !wIds.Contains("rt_warp_soi_soon") && !wIds.Contains("rt_warp_landed")
                  && chatsWarp >= 3 && chatsNoWarp == 0 && soiOk,
                  "runtime voice: warp lines fit the burn and the next body, warp chats only in warp",
                  string.Join(", ", wIds.ToArray()) + " / chats " + chatsWarp + " and " + chatsNoWarp + " without warp / soi "
                  + (soi != null && RuntimeLines.Fill(soi, 0, wv, wn, out ws, out we) ? RuntimeSpeech.Spoken(ws) + " / " + we : "no fill"));
        }
        // runtime chats: rt_chat_<name>_<seq>, two kerbals take turns, self and other per speaker
        var chats = RuntimeLines.Chats(rtAll);
        var nA = new Dictionary<string, string[]>(fullN);
        nA["self"] = new[] { "Bil", "Bill" }; nA["other"] = new[] { "Bob", "Bob" };
        var nB = new Dictionary<string, string[]>(fullN);
        nB["self"] = new[] { "Bob", "Bob" }; nB["other"] = new[] { "Bil", "Bill" };
        var badChat = new List<string>();
        foreach (var c in chats)
        {
            if (c.Count < 2 && !c[0].IsEvent) badChat.Add(c[0].Id + " has one line");
            string sit = c[0].Situations.Count > 0 ? c[0].Situations[0] : "orbit";
            // a value that meets each check of line 1
            var cv = new Dictionary<string, double>(full);
            foreach (var ck in c[0].Checks)
            {
                double n = ck.Value.Value;
                if (ck.Value.Key == "<" || ck.Value.Key == "<=") cv[ck.Key] = Math.Min(cv[ck.Key], n - 1 > 0 ? n - 1 : n / 2);
                else if (ck.Value.Key == ">" || ck.Value.Key == ">=") cv[ck.Key] = Math.Max(cv[ck.Key], n + 1);
                else cv[ck.Key] = n;
            }
            if (!RuntimeLines.ChatFits(c, sit, cv, nA, nB)) badChat.Add(c[0].Id + " does not fit with values that meet its checks");
        }
        string c1, c2;
        var dayChat = chats.Find(c => c[0].Id == "rt_chat_day_1");
        bool dayOk = dayChat != null && RuntimeLines.Fill(dayChat[2], new Dictionary<string, double> { { "day", 4 } }, nA, out c1, out c2)
                     && c2 == "That's the ship's paint, Bob." && RuntimeSpeech.Spoken(c1).EndsWith(", Bob.");
        Check(chats.Count >= 12 && badChat.Count == 0 && dayOk,
              "runtime voice: " + chats.Count + " runtime chats, each fits and fills with the names of its speakers", string.Join(" ; ", badChat.ToArray()));
        var chatErr = new List<string>();
        var badChats = RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish",
            "rt_chat_a_1\tchat\t\tA.\tA.", "rt_chat_b_1\tchat\t\tB.\tB.", "rt_chat_b_3\tchat\t\tB.\tB.",
            "rt_chat_c_1\tchat\t\tC.\tC.", "rt_chat_c_2\tchat\torbit\tC.\tC.", "rt_chat_ok_1\tchat\t\tD.\tD.", "rt_chat_ok_2\tchat\t\tD.\tD." }, chatErr);
        Check(chatErr.Count == 3 && RuntimeLines.Chats(badChats).Count == 1, "runtime voice: a bad chat is dropped (one line, a gap, a when after line 1)",
              string.Join(" ; ", chatErr.ToArray()));
        // jobs in a chat: a=pilot b=scientist on line 1 only, a known job, chats only
        var jobErr = new List<string>();
        var jobChats = RuntimeLines.Chats(RuntimeLines.Parse(new List<string> { "id\tkind\twhen\tspanish\tenglish",
            "rt_chat_j_1\tchat\ta=pilot b=scientist\tJ.\tJ.", "rt_chat_j_2\tchat\t\tJ.\tJ.",
            "rt_chat_x_1\tchat\ta=cook\tX.\tX.", "rt_chat_x_2\tchat\t\tX.\tX.",
            "rt_chat_y_1\tchat\t\tY.\tY.", "rt_chat_y_2\tchat\tb=pilot\tY.\tY.",
            "rt_jobreport\treport\ta=pilot\tR.\tR." }, jobErr));
        var jc = jobChats.Count == 1 ? jobChats[0] : null;
        var none = new Dictionary<string, double>();
        bool jobsOk = jc != null && jc[0].TraitA == "pilot" && jc[0].TraitB == "scientist"
            && RuntimeLines.ChatFits(jc, "orbit", none, nA, nB, "pilot", "scientist")
            && !RuntimeLines.ChatFits(jc, "orbit", none, nA, nB, "scientist", "pilot")
            && !RuntimeLines.ChatFits(jc, "orbit", none, nA, nB, "pilot", "pilot")
            && !RuntimeLines.ChatFits(jc, "orbit", none, nA, nB, Persona.Any, "scientist")
            && RuntimeLines.ChatFits(jc, "orbit", none, nA, nB)
            && RuntimeLines.ChatFits(chats.Find(c => c[0].TraitA == null && c[0].TraitB == null && c[0].Situations.Count == 0 && c[0].Checks.Count == 0),
                                     "orbit", full, nA, nB, Persona.Any, Persona.Any);
        Check(jobErr.Count == 4 && jobsOk && chats.Exists(c => c[0].TraitA != null),
              "runtime voice: a chat with a=/b= jobs only fits kerbals with those jobs (bad job, line 2 and a report are refused)",
              string.Join(" ; ", jobErr.ToArray()));
        Check(RuntimeLines.HasWord("Good to hear, Kerbal X.", "Kerbal X") && RuntimeLines.HasWord("bob, hi", "Bob")
              && !RuntimeLines.HasWord("Bobby is here.", "Bob") && !RuntimeLines.HasWord("", "Bob") && RuntimeLines.HasWord("Hi Bob", "Bob"),
              "runtime voice: a name in a subtitle is found as a whole word only", "");
        var hist = new Dictionary<string, double>();
        RuntimeLines.CrewHistory(new List<string[]> {
            new[] { "0", "Training1", "Kerbin" },
            new[] { "1", "Orbit", "Kerbin" }, new[] { "1", "Flyby", "Mun" }, new[] { "1", "Recover", "Kerbin" },
            new[] { "2", "Land", "Mun" }, new[] { "2", "Orbit", "Mun" }, new[] { "2", "Recover", "Kerbin" },
            new[] { "3", "Flyby", "Minmus" } }, "Mun", 2, hist);
        Check(hist["missions"] == 2 && hist["flight_no"] == 3 && hist["visits"] == 2 && hist["landings"] == 1 && hist["stars"] == 2,
              "runtime voice: crew history counts recovered flights, visits and landings per flight, without training",
              hist["missions"] + " " + hist["flight_no"] + " " + hist["visits"] + " " + hist["landings"] + " " + hist["stars"]);
        // spanish_rt (runtime Spanish next to the line) and the spanish column of pools.tsv
        {
            string dir = rtDir.Substring(0, rtDir.Length - "/Runtime".Length);
            var tErr = new List<string>();
            PhraseTable built = PhraseTable.Parse(new List<string>(System.IO.File.ReadAllLines(System.IO.Path.Combine(dir, "phrases.tsv"), System.Text.Encoding.UTF8)), tErr);
            var pErr = new List<string>();
            SlotPools realPools = SlotPools.Parse(new List<string>(System.IO.File.ReadAllLines(System.IO.Path.Combine(dir, "pools.tsv"), System.Text.Encoding.UTF8)), pErr);
            int slotted = 0, withRt = 0, versionsNoRt = 0, rtLines = 0;
            var noEs = new List<string>();
            foreach (PhraseEntry pe in built.Entries)
            {
                if (pe.SpanishRt != null) rtLines++;
                if (!SubtitleSlots.HasSlots(pe.English)) continue;
                slotted++;
                if (pe.SpanishRt != null) withRt++;
                List<string> vs = SubtitleSlots.Versions(pe.English);
                for (int k = 0; k < vs.Count; k++)
                {
                    if (!SubtitleSlots.HasSlots(vs[k])) continue;
                    string rtv = pe.SpanishRtVersion(k);
                    if (string.IsNullOrEmpty(rtv)) { versionsNoRt++; continue; }
                    // every pool the runtime Spanish uses has Spanish for each of its values
                    foreach (System.Text.RegularExpressions.Match m in SubtitleSlots.EsSlotRe.Matches(rtv))
                    {
                        string pool = m.Groups[2].Value;
                        IList<string> vals0 = realPools.Values(pool);
                        if (vals0 == null) continue;
                        foreach (string raw in vals0)
                        {
                            string text, g, es0;
                            SubtitleSlots.SplitConditions(raw, null, out text);
                            if (!realPools.Spanish(pool, text, out g, out es0) && !noEs.Contains(pool + "/" + text)) noEs.Add(pool + "/" + text);
                            else if (m.Groups[1].Success && g == null && !noEs.Contains(pool + "/" + text + " (no gender)")) noEs.Add(pool + "/" + text + " (no gender)");
                        }
                    }
                }
            }
            Check(tErr.Count == 0 && pErr.Count == 0 && rtLines >= 65 && realPools.SpanishCount >= 172 && noEs.Count == 0,
                  "spanish_rt: " + rtLines + " rows with runtime Spanish, " + realPools.SpanishCount + " pool values with Spanish, every pool value a runtime line uses has its Spanish",
                  string.Join(" ; ", tErr.ToArray()) + string.Join(" ; ", pErr.ToArray()) + string.Join(" ; ", noEs.ToArray()));
            // every runtime Spanish version fills, with every value there is, and speaks without marks or slots left
            {
                var allVals = new Dictionary<string, string> { { "day", "3" }, { "here", "the Mun" }, { "vessel", "Kerbal X" }, { "alt_km", "85" } };
                foreach (string k in Slots.Numbers) if (!allVals.ContainsKey(k)) allVals[k] = "42";
                var badRt = new List<string>();
                int spoken = 0;
                foreach (PhraseEntry pe in built.Entries)
                {
                    if (pe.SpanishRt == null) continue;
                    for (int k = 0; k < pe.SpanishRt.Count; k++)
                    {
                        if (string.IsNullOrEmpty(pe.SpanishRt[k])) continue;
                        int wantV = k;
                        // the conditions of this version, and every vessel part (pool values like [goo] need them)
                        var vc = new List<string>();
                        string vtext;
                        SubtitleSlots.SplitConditions(SubtitleSlots.Versions(pe.English)[k], vc, out vtext);
                        var phases = new List<string> { "prelaunch", "launch", "ascent", "orbit", "coast", "reentry", "descent", "landed", "blackout", "kerbin", "away" };
                        foreach (string c0 in SubtitleSlots.Conditions) if (!phases.Contains(c0) && !vc.Contains(c0)) vc.Add(c0);
                        var o = SubtitleSlots.Fill(new List<PhraseEntry> { pe }, realPools, allVals,
                            i => new Dictionary<string, string> { { "other", "Bob" }, { "self", "Bill" } },
                            (pool, vv) => pool.StartsWith(SceneMemory.VersionPool, StringComparison.Ordinal)
                                          ? (vv.Contains(wantV.ToString()) ? wantV.ToString() : vv[0]) : vv[0], null,
                            vc, true);
                        string said = o[0].RuntimeSpanish;
                        if (said == null) { badRt.Add(pe.ClipKey + " v" + (k + 1) + " gives no runtime Spanish"); continue; }
                        string spk = RuntimeSpeech.Spoken(said);
                        if (spk.IndexOfAny(new[] { '{', '}', '‹', '›', '⟪', '⟫' }) >= 0 || o[0].English.IndexOf('{') >= 0) badRt.Add(pe.ClipKey + " v" + (k + 1) + ": " + spk);
                        spoken++;
                    }
                }
                Check(badRt.Count == 0 && spoken > 300, "spanish_rt: every runtime Spanish version (" + spoken + ") fills and speaks cleanly",
                      string.Join(" ; ", badRt.GetRange(0, Math.Min(8, badRt.Count)).ToArray()) + (badRt.Count > 8 ? " ... " + badRt.Count : ""));
            }
            Console.WriteLine("INFO  spanish_rt coverage: " + slotted + " slotted rows, " + withRt + " with spanish_rt, " + versionsNoRt + " slotted versions without");

            var esPools = SlotPools.Parse(new List<string> { "pool\tenglish\tspanish", "thing\tlucky rock\tf:piedra de la suerte" }, null);
            var rowErr = new List<string>();
            var tbl = PhraseTable.Parse(new List<string> {
                "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish\tenglish\tspanish_rt",
                "t_1\tchatter\tchatter\tcalm\tcrew\tt\t1\tboth\tA.\tA. | {other}, where is my {thing}?\t | {other}, ¿dónde está {el thing}?",
                "t_2\tchatter\tchatter\tcalm\tcrew\tt\t2\tboth\tB.\tB. | B {thing}.\tB.",
                "t_3\tchatter\tchatter\tcalm\tcrew\tt\t3\tboth\tC.\tC. | Fuel at {fuel} percent.",
                "t_4\tchatter\tchatter\tcalm\tcrew\tt\t4\tboth\tD.\tD. | D.\t | D {vessel}.",
                "u_1\tchatter\tchatter\tcalm\tcrew\tu\t1\tboth\tE.\tE. | Fuel at {fuel} percent.\t | Combustible al {fuel} por ciento." }, rowErr);
            PhraseEntry row = new List<PhraseEntry>(tbl.Entries).Find(q0 => q0.ClipKey == "t_1");
            Check(rowErr.Count == 3 && row != null && new List<PhraseEntry>(tbl.Entries).Exists(q0 => q0.ClipKey == "u_1"),
                  "spanish_rt: a wrong version count, a runtime number without Spanish and a slot its english lacks are refused (a missing last cell is fine)",
                  rowErr.Count + ": " + string.Join(" ; ", rowErr.ToArray()));
            var outE = SubtitleSlots.Fill(new List<PhraseEntry> { row }, esPools, new Dictionary<string, string>(),
                i => new Dictionary<string, string> { { "other", "Bob" } }, (pool, vv) => pool.StartsWith("version:") ? "1" : vv[0], null, null, true);
            string esText = outE[0].RuntimeSpanish;
            Check(outE[0].English == "Bob, where is my lucky rock?" && esText == "‹Bob›, ¿dónde está la piedra de la suerte?"
                  && SubtitleSlots.Article("del", "mp") == "de los" && SubtitleSlots.Article("el", null) == null,
                  "spanish_rt: the filled version gets its Spanish with the same values (name mark, article by gender)", outE[0].English + " / " + esText);
            var outO = SubtitleSlots.Fill(new List<PhraseEntry> { row }, esPools, new Dictionary<string, string>(),
                i => new Dictionary<string, string> { { "other", "Bob" } }, (pool, vv) => pool.StartsWith("version:") ? "0" : vv[0], null, null, true);
            var outOff = SubtitleSlots.Fill(new List<PhraseEntry> { row }, esPools, new Dictionary<string, string>(),
                i => new Dictionary<string, string> { { "other", "Bob" } }, (pool, vv) => pool.StartsWith("version:") ? "1" : vv[0], null, null, false);
            Check(outO[0].RuntimeSpanish == null && outOff[0].RuntimeSpanish == null && outOff[0].English == "Bob, where is my lucky rock?",
                  "spanish_rt: an empty piece and the runtime voice off keep the recorded clip", outO[0].RuntimeSpanish + " / " + outOff[0].RuntimeSpanish);

            // names: one per scene, none when one was said a moment ago
            var nmTbl = PhraseTable.Parse(new List<string> {
                "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish\tenglish",
                "n_1\tchatter\tchatter\tcalm\tcrew\tn\t1\tboth\tA.\t{other}, hi. | Hi.",
                "n_2\tchatter\tchatter\tcalm\tcrew\tn\t2\tboth\tB.\t{other}, go. | Go." }, null);
            var nmLines = new List<PhraseEntry>(nmTbl.Entries);
            Func<int, IDictionary<string, string>> nmPer = i => new Dictionary<string, string> { { "other", i == 0 ? "Bob" : "Jeb" } };
            Func<string, IList<string>, string> nmPick = (pool, vv) => vv[0];
            bool nmNamed, nmNamedFew;
            var nmOut = SubtitleSlots.Fill(nmLines, esPools, new Dictionary<string, string>(), nmPer, nmPick, null, null, false, false, out nmNamed);
            var nmFew = SubtitleSlots.Fill(nmLines, esPools, new Dictionary<string, string>(), nmPer, nmPick, null, null, false, true, out nmNamedFew);
            Check(nmOut[0].English == "Bob, hi." && nmOut[1].English == "Go." && nmNamed
                  && nmFew[0].English == "Hi." && nmFew[1].English == "Go." && !nmNamedFew
                  && SubtitleSlots.NamesSomeone("{vessel} here.") && SubtitleSlots.NamesSomeone("Hi {other:2}.")
                  && !SubtitleSlots.NamesSomeone("Fuel at {fuel}.") && !SubtitleSlots.NamesSomeone("Over {here}."),
                  "names: a scene says one name, and none right after another name",
                  nmOut[0].English + " " + nmOut[1].English + " / " + nmFew[0].English + " " + nmFew[1].English);

            // a runtime number: only with its value (the runtime voice on), said in words, shown in digits
            PhraseEntry fuel = new List<PhraseEntry>(tbl.Entries).Find(q0 => q0.ClipKey == "u_1");
            var lines1 = new List<PhraseEntry> { fuel };
            var off = new Dictionary<string, string> { { "day", "2" } };
            var on = new Dictionary<string, string> { { "day", "2" }, { "fuel", "21.4" } };
            int picked1 = 0;
            for (int i = 0; i < 20; i++)
            {
                var o = SubtitleSlots.Fill(lines1, esPools, off, null, (pool, vv) => vv[vv.Count - 1], null, null, false);
                if (o[0].English.IndexOf("Fuel", StringComparison.Ordinal) >= 0) picked1++;
            }
            var onF = SubtitleSlots.Fill(lines1, esPools, on, null, (pool, vv) => vv[vv.Count - 1], null, null, true);
            Check(picked1 == 0 && onF[0].English == "Fuel at 21 percent." && onF[0].RuntimeSpanish == "Combustible al veintiún por ciento."
                  && SubtitleSlots.CanFill(lines1, esPools, off, null, null),
                  "spanish_rt: a {fuel} version is never picked without the value, with it the voice says the number in words",
                  onF[0].English + " / " + onF[0].RuntimeSpanish);
        }
        // the slot catalogue in src/Slots.cs and in audio/build_lonapse.py are the same
        {
            string py = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/build_lonapse.py")), System.Text.Encoding.UTF8);
            Func<string, string> list = name =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(py, "\n" + name + @" = \[([^\]]*)\]");
                if (!m.Success) return "(missing " + name + ")";
                var items = new List<string>();
                foreach (System.Text.RegularExpressions.Match q in System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, "\"([a-z_]+)\"")) items.Add(q.Groups[1].Value);
                return string.Join(" ", items.ToArray());
            };
            bool same = list("NUMBER_SLOTS") == string.Join(" ", Slots.Numbers) && list("NAME_SLOTS") == string.Join(" ", Slots.Names)
                        && list("RUNTIME_PERSONS") == string.Join(" ", Slots.Persons) && list("FLIGHT_SLOTS") == string.Join(" ", Slots.Flight);
            Check(same, "slots: src/Slots.cs and audio/build_lonapse.py list the same slots", list("NUMBER_SLOTS") + " | " + list("NAME_SLOTS"));
        }
        {
            float[] pitch = { 1.005f, 0.948f, 1.163f };
            float[] mul = DcMath.SpreadPitches(pitch, 0.09f, 0.82f, 1.28f);
            float pa = pitch[0] * mul[0], pb = pitch[1] * mul[1], pc = pitch[2] * mul[2];
            bool gaps = Math.Abs(pa - pb) >= 0.0899f && Math.Abs(pa - pc) >= 0.0899f && Math.Abs(pb - pc) >= 0.0899f;
            bool mean = Math.Abs((pa + pb + pc) - (pitch[0] + pitch[1] + pitch[2])) < 0.001f;
            bool order = pb < pa && pa < pc;
            float[] far = DcMath.SpreadPitches(new[] { 0.9f, 1.1f }, 0.09f, 0.82f, 1.28f);
            float[] one = DcMath.SpreadPitches(new[] { 1.0f }, 0.09f, 0.82f, 1.28f);
            Check(gaps && mean && order && far[0] == 1f && far[1] == 1f && one[0] == 1f,
                  "voices: a crew's pitches are pushed apart to the gap, the average and the order stay, far apart stays as it is",
                  pa.ToString("0.000") + " " + pb.ToString("0.000") + " " + pc.ToString("0.000"));
            RuntimeDelivery mcCalm = RuntimeDelivery.For("mc"), mcPanic = RuntimeDelivery.For(RuntimeDelivery.KindFor("mc", "panic", "chatter", "chatter"));
            RuntimeDelivery mT = RuntimeDelivery.For(RuntimeDelivery.KindFor("m", "tense", "chatter", "chatter"));
            RuntimeDelivery fP = RuntimeDelivery.For(RuntimeDelivery.KindFor("f", "panic", "chatter", "chatter"));
            RuntimeDelivery mC = RuntimeDelivery.For(RuntimeDelivery.KindFor("m", "calm", "callout", "staging"));
            bool same = RuntimeDelivery.KindFor("m", "calm", "chatter", "chatter") == "m" && RuntimeDelivery.KindFor("f", "none", "chatter", "x") == "f"
                && Math.Abs(mcCalm.Tempo - 1.04) < 1e-9 && mcCalm.NoiseScale == 0.80 && mcCalm.PitchSt == 3.0
                && Math.Abs(mcPanic.Tempo - 0.95) < 1e-9 && Math.Abs(mcPanic.PitchSt - 3.3) < 1e-9 && mcPanic.NoiseScale == 0.667
                && Math.Abs(mT.Tempo - 1.10) < 1e-9 && Math.Abs(mT.PitchSt - 5.7) < 1e-9
                && fP.NoiseScale == 0.90 && Math.Abs(fP.PitchSt - 5.0) < 1e-9
                && Math.Abs(mC.Tempo - 1.08) < 1e-9 && Math.Abs(mC.PitchSt - 5.5) < 1e-9
                && RuntimeDelivery.KindFor("mc", "calm", "chatter", "countdown") == null && RuntimeDelivery.BaseKind("m.tense") == "m";
            Check(same, "voices: runtime lines use the build's delivery per band (tempo, pitch, noise), the countdown is never runtime", "");
        }
        // a value of 1: the noun after it is singular, in both languages ("un kilómetro", "1 minute")
        var plural = new List<string>();
        var pluralRe = new System.Text.RegularExpressions.Regex(@"\b(un|1) (kilómetros|metros|minutos|grados|puntos|fondos|piezas|contratos|kerbals|meters|minutes|degrees|points|funds|parts|contracts)\b");
        foreach (RuntimeTemplate t in rtAll)
        for (int v = 0; v < t.Versions.Count; v++)
        {
            var one = new Dictionary<string, double>();
            foreach (string k in RuntimeLines.NumberSlots) one[k] = 1;
            string a1, a2;
            if (RuntimeLines.Fill(t, v, one, fullN, out a1, out a2) && (pluralRe.IsMatch(a1) || pluralRe.IsMatch(a2))) plural.Add(t.Id + ": " + a1 + " / " + a2);
        }
        string o1, o2;
        RuntimeLines.Fill(rtAll.Find(t => t.Id == "rt_orbit_period"), new Dictionary<string, double> { { "period_min", 1.04 } }, null, out o1, out o2);
        string m1, m2;
        RuntimeLines.Fill(rtAll.Find(t => t.Id == "rt_orbit"), 1, new Dictionary<string, double> { { "alt_km", 15e6 } },
                          new Dictionary<string, string[]> { { "body", new[] { "Kérbol", "Kerbol" } } }, out m1, out m2);
        Check(m1 == "Os vemos a quince millones de kilómetros sobre ⟪Kérbol⟫." && m2 == "We see you 15 million km above Kerbol.",
              "runtime voice: a value in millions: \"quince millones de kilómetros\", \"15 million km\"", m1 + " / " + m2);
        Check(plural.Count == 0 && o1 == "Una vuelta completa os lleva un minuto." && o2 == "One full orbit takes you 1 minute.",
              "runtime voice: a value of 1 takes the singular noun", string.Join(" ; ", plural.ToArray()) + " | " + o1 + " / " + o2);
        Check(RuntimeLines.ExperimentId("temperatureScan@KerbinSrfLanded") == "temperatureScan"
              && RuntimeLines.ExperimentWords("cometSample_long")[1] == "comet sample" && RuntimeLines.ExperimentWords("modThing")[0] == "experimento",
              "runtime voice: experiment words from the subject id", null);
        // extra rows (Phrases/Extra) in the source format after the built table: both parse, lonapse_text is made
        var mixed = new List<string> {
            "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tphase\tcause\ttrait\tpersona\ttopic\tcore\tbeep\tspanish\tlonapse_text\tfolder\tbody\tenglish",
            "a_1\tchatter\tchatter\tcalm\tmc\ta\t1\tm\tany\tany\tany\tany\tany\tany\t\tHola.\taloH.\tcontrol/calm\t\tHello.",
            "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tphase\tcause\tspanish\tenglish",
            "x_1\tchatter\tchatter\tcalm\tmc\tx\t1\tm\torbit\tany\tSaludos para {body}.\tSay hi to {body}.",
        };
        var mixErr = new List<string>();
        PhraseTable mt = PhraseTable.Parse(mixed, mixErr);
        PhraseEntry x1 = mt.Get("x_1");
        Check(mixErr.Count == 0 && mt.Count == 2 && x1 != null && x1.LonapseText == LonapseText.ReverseText(x1.Spanish) && x1.Folder == "control/calm",
              "runtime voice: extra rows in the source format parse after the built table", string.Join(" ; ", mixErr.ToArray()));
        Check(RuntimeDelivery.For("mc").Voice == "es_MX-ald-medium" && Math.Abs(RuntimeDelivery.For("m").LengthScale - Math.Pow(2, 5 / 12.0)) < 1e-9
              && RuntimeDelivery.For("x") == null, "runtime voice: delivery per kind like the build (voice, length scale)", null);
    }

    // Experimental (procedural talk): subtitle slots, filled in game. The spanish (the audio) stays general.
    static void SubtitleSlotTests()
    {
        var perr = new List<string>();
        SlotPools pools = SlotPools.Parse(new List<string> {
            "# comment", "pool\tenglish", "thing\thelmet", "thing\tsock", "thing\thelmet", "food\tcheese",
            "day\tmonday", "Bad\tx", "thing\t{X}", "thing\ta | b", "thing\tmy {body}", "line\tWhere is my {thing}?" }, perr);
        Check(pools.Count == 3 && pools.Values("thing").Count == 2 && pools.Values("food")[0] == "cheese",
              "slots: pools parse, a duplicate value is kept once", pools.Count + " pools");
        Check(perr.Count == 5 && !pools.Has("day") && pools.Values("line")[0] == "Where is my {thing}?",
              "slots: reserved names, bad slots, | and {body} in values are rejected, a value may hold a slot", string.Join(" | ", perr.ToArray()));

        // the table: slots only in the english
        const string H = "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish\tenglish";
        var e1 = new List<string>();
        PhraseTable t1 = PhraseTable.Parse(new List<string> { H,
            "s1\tchatter\tchatter\tcalm\tcrew\tss\t1\tboth\t¿Quién ha cogido mis cosas?\tWho took my {thing}?",
            "s2\tchatter\tchatter\tcalm\tcrew\tss\t2\tboth\tYo no.\tNot me, {other}. Ask about the {thing:2} on day {day}." }, e1);
        Check(e1.Count == 0 && t1.Entries.Count == 2, "slots: english slots are accepted", string.Join(" | ", e1.ToArray()));
        var e2 = new List<string>();
        PhraseTable.Parse(new List<string> { H,
            "b1\tchatter\tchatter\tcalm\tcrew\tsb\t1\tboth\tMi {thing}\tMy {thing}",
            "b2\tchatter\tchatter\tcalm\tcrew\tsc\t1\tboth\tHola\tHi {Thing}",
            "b3\tchatter\tchatter\tcalm\tcrew\tsd\t1\tboth\tHola\tHi {thing:0}" }, e2);
        Check(e2.Count == 3, "slots: a slot in the spanish, a capital name or :0 is an error", e2.Count.ToString());

        // Fill
        PhraseScene sc = null;
        foreach (PhraseScene x in t1.Scenes) if (x.Id == "ss") sc = x;
        Check(sc != null && sc.HasSlots, "slots: the scene knows it has slots", null);
        var flight = SubtitleSlots.FlightValues(6 * 3600.0 * 2 + 5, ProbeRules.KerbinDay, "Kerbin", "Lander II", 1234);
        Check(flight["day"] == "3" && flight["alt_km"] == "1.2" && SubtitleSlots.FlightValues(0, ProbeRules.EarthDay, null, "", 25600)["alt_km"] == "26"
              && !SubtitleSlots.FlightValues(0, ProbeRules.EarthDay, null, "", 0).ContainsKey("vessel"),
              "slots: flight values (mission day from 1, km, empty names left out)", flight["day"] + " " + flight["alt_km"]);
        var pp = SlotPools.Parse(new List<string> { "pool\tenglish", "thing\thelmet", "thing\tsock" }, null);
        var un = new List<string>();
        List<PhraseEntry> f = SubtitleSlots.Fill(sc.Lines, pp, flight,
            i => new Dictionary<string, string> { { "self", i % 2 == 0 ? "Jeb" : "Bill" }, { "other", i % 2 == 0 ? "Bill" : "Jeb" } },
            (pool, vals) => vals[0], un);
        Check(f[0].English == "Who took my helmet?" && f[1].English == "Not me, Jeb. Ask about the sock on day 3." && un.Count == 0,
              "slots: Fill gives pool values, {pool:2} another value, person and flight values", f[0].English + " / " + f[1].English);
        Check(f[0].ClipKey == "s1" && f[0].Spanish == sc.Lines[0].Spanish && sc.Lines[0].English == "Who took my {thing}?",
              "slots: Fill copies the row and keeps the clip key, the table row is unchanged", null);
        var cap = new List<PhraseEntry> { new PhraseEntry { ClipKey = "c", English = "{thing} is gone. {thing} again! Hm, {thing}." } };
        Check(SubtitleSlots.Fill(cap, pp, null, null, (pool, vals) => vals[1], null)[0].English == "Sock is gone. Sock again! Hm, sock.",
              "slots: a value at the start of a sentence gets a capital", null);
        var miss = new List<string>();
        var m2 = SubtitleSlots.Fill(new List<PhraseEntry> { new PhraseEntry { ClipKey = "m", English = "Hi {nobody} on {vessel}" } },
                                    pp, new Dictionary<string, string>(), null, null, miss);
        Check(m2[0].English == "Hi ... on ..." && miss.Count == 2, "slots: an unknown slot gives ... and is reported", m2[0].English);
        Check(SubtitleSlots.FirstName("Jebediah Kerman") == "Jebediah" && SubtitleSlots.FirstName("Val") == "Val",
              "slots: first name", null);

        // versions split by | and pool values with slots inside
        var np = SlotPools.Parse(new List<string> { "pool\tenglish", "thing\tsock", "thing\thelmet",
            "lost\t{thing} is gone", "lost\tsomeone took my {thing}", "loop\tagain {loop}" }, null);
        var vl = new List<PhraseEntry> {
            new PhraseEntry { ClipKey = "v1", English = "Who took my {thing}? | Has anyone seen my {thing}?" },
            new PhraseEntry { ClipKey = "v2", English = "{lost}. Ugh. Okay, {lost}." } };
        var picks = new List<string>();
        List<PhraseEntry> vf = SubtitleSlots.Fill(vl, np, null, null, (pool, vals) => { picks.Add(pool); return vals[vals.Count - 1]; }, null);
        Check(vf[0].English == "Has anyone seen my helmet?" && picks[0] == "version:v1",
              "slots: a version is picked per line, with its own memory key", vf[0].English + " / " + picks[0]);
        Check(vf[1].English == "Someone took my helmet. Ugh. Okay, someone took my helmet.",
              "slots: a pool value with a slot inside is filled, same text in the scene, capital at a sentence start", vf[1].English);
        var lu = new List<string>();
        string lf = SubtitleSlots.Fill(new List<PhraseEntry> { new PhraseEntry { ClipKey = "l", English = "{loop}" } },
                                       np, null, null, null, lu)[0].English;
        Check(lf == "..." && lu.Count == 1, "slots: a pool that refers to itself has no usable value", lf);
        var ve = new List<string>();
        PhraseTable.Parse(new List<string> { H,
            "q1\tchatter\tchatter\tcalm\tcrew\tsq\t1\tboth\tHola\tHi | ",
            "q2\tchatter\tchatter\tcalm\tcrew\tsr\t1\tboth\tEn {body}\tOn {body} | At {body}",
            "q3\tchatter\tchatter\tcalm\tcrew\tst\t1\tboth\tHola\tHi | Hello {other}" }, ve);
        Check(ve.Count == 2, "slots: an empty version or {body} with versions is an error", string.Join(" | ", ve.ToArray()));

        // [conditions] on versions: the matching ones when there are any, else the plain ones
        var cl = new List<PhraseEntry> {
            new PhraseEntry { ClipKey = "c1", English = "High up. | [landed] On the ground, dummy. | [landed kerbin] Home, dummy." } };
        Func<ICollection<string>, string> fillC = conds => SubtitleSlots.Fill(cl, null, null, null,
            (pool, vals) => vals[vals.Count - 1], null, conds)[0].English;
        Check(fillC(SubtitleSlots.ConditionsFor("orbit", "kerbin")) == "High up."
              && fillC(SubtitleSlots.ConditionsFor("landed", "mun")) == "On the ground, dummy."
              && fillC(SubtitleSlots.ConditionsFor("landed", "kerbin")) == "Home, dummy."
              && fillC(null) == "High up.",
              "slots: [conditions] pick the matching versions, else the plain ones",
              fillC(SubtitleSlots.ConditionsFor("landed", "mun")));
        var ce = new List<string>();
        PhraseTable.Parse(new List<string> { H,
            "k1\tchatter\tchatter\tcalm\tcrew\tsk\t1\tboth\tHola\t[landed] Hi | [orbit] Ho",
            "k2\tchatter\tchatter\tcalm\tcrew\tsl\t1\tboth\tHola\tHi | [moon] Ho",
            "k3\tchatter\tchatter\tcalm\tcrew\tsm\t1\tboth\tHola\tHi | [landed] Ho" }, ce);
        Check(ce.Count == 2, "slots: versions need a plain one and known conditions", string.Join(" | ", ce.ToArray()));

        // vessel conditions on pool values, and scenes that cannot be filled
        var vp = SlotPools.Parse(new List<string> { "pool\tenglish", "outside\t[solar] solar panel", "outside\t[legs] landing leg",
            "experiment\t[goo] Mystery Goo" }, null);
        var vl2 = new List<PhraseEntry> { new PhraseEntry { ClipKey = "w1", English = "The {outside} wobbles. | Something wobbles." } };
        var onlyLegs = new List<string> { "legs" };
        Check(SubtitleSlots.Fill(vl2, vp, null, null, (pool, vals) => vals[0], null, onlyLegs)[0].English == "The landing leg wobbles."
              || SubtitleSlots.Fill(vl2, vp, null, null, (pool, vals) => vals[0], null, onlyLegs)[0].English == "Something wobbles.",
              "slots: a pool value with [legs] is used only when the vessel has legs", null);
        bool neverSolar = true;
        for (int i = 0; i < 20; i++)
            neverSolar &= SubtitleSlots.Fill(vl2, vp, null, null, (pool, vals) => vals[i % vals.Count], null, onlyLegs)[0].English != "The solar panel wobbles.";
        Check(neverSolar, "slots: a part the vessel does not have is never named", null);
        Check(SubtitleSlots.Fill(vl2, vp, null, null, (pool, vals) => vals[0], null, new List<string>())[0].English == "Something wobbles.",
              "slots: a version whose pool has no usable value is skipped", null);
        var ex = new List<PhraseEntry> { new PhraseEntry { ClipKey = "x1", English = "The {experiment} glows. | The {experiment} hums." } };
        Check(!SubtitleSlots.CanFill(ex, vp, null, null, new List<string>()) && SubtitleSlots.CanFill(ex, vp, null, null, new List<string> { "goo" }),
              "slots: CanFill is false when no version can be filled (no Mystery Goo aboard)", null);
        var px = new List<PhraseEntry> { new PhraseEntry { ClipKey = "p1", English = "Hi {other}. | Hi there {other}." } };
        Check(!SubtitleSlots.CanFill(px, vp, null, null, null)
              && SubtitleSlots.CanFill(px, vp, null, i => new Dictionary<string, string> { { "other", "Bob" } }, null),
              "slots: {other} needs a person, so it only fills in a chat", null);
        var pe = new List<string>();
        SlotPools.Parse(new List<string> { "pool\tenglish", "a\t[moon] x", "a\t[solar]" }, pe);
        Check(pe.Count == 2, "slots: a pool value with an unknown condition or no text is rejected", string.Join(" | ", pe.ToArray()));

        // TakeValue goes through the pool before a value comes back
        SceneMemory mem = new SceneMemory();
        Random rng = new Random(3);
        var vals3 = new List<string> { "a", "b", "c" };
        var seen = new HashSet<string>();
        for (int i = 0; i < 3; i++) seen.Add(mem.TakeValue("p", vals3, rng));
        string fourth = mem.TakeValue("p", vals3, rng);
        Check(seen.Count == 3 && mem.LastSeq("slot:p:" + fourth) > 0 && mem.TakeValue("p", null, rng) == null,
              "slots: TakeValue uses every value once before one comes back", string.Join(" ", new List<string>(seen).ToArray()));
        {
            // subtitle versions: the original sentence ("0") comes after the new versions, then the oldest again
            bool origLast = true;
            for (int seed = 0; seed < 20; seed++)
            {
                var vm = new SceneMemory();
                var vr = new Random(seed);
                var order = new List<string>();
                var idx = new List<string> { "0", "1", "2" };
                for (int i = 0; i < 3; i++) order.Add(vm.TakeValue(SceneMemory.VersionPool + "k", idx, vr));
                if (order[2] != "0" || order[0] == order[1]) origLast = false;
                if (vm.TakeValue(SceneMemory.VersionPool + "k", idx, vr) != order[0]) origLast = false;
            }
            var pm = new SceneMemory();
            var pr = new Random(1);
            bool poolsKeep0 = false;
            for (int seed = 0; seed < 20 && !poolsKeep0; seed++)
                poolsKeep0 = new SceneMemory().TakeValue("p", new List<string> { "0", "1" }, new Random(seed)) == "0";
            var only0 = new List<string> { "0" };
            Check(origLast && poolsKeep0 && pm.TakeValue(SceneMemory.VersionPool + "k", only0, pr) == "0",
                  "versions: the original sentence comes after the new versions (other pools are unchanged)", null);
        }
    }

    // The C# probe beep synth must give the same samples as audio/build_clips.py
    static void ProbeSynthTests()
    {
        string here = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string refPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../tests/probe_reference.tsv"));
        string coresPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../audio/probe/cores.tsv"));
        Dictionary<string, ProbeVoiceSpec> stock = new Dictionary<string, ProbeVoiceSpec>();
        foreach (ProbeVoiceSpec v in ProbeVoiceSpec.Stock()) stock[v.Name] = v;

        // the stock voices in C# hold the same values as audio/probe/cores.tsv
        {
            int same = 0, rows = 0;
            string bad = null;
            foreach (string raw in System.IO.File.ReadAllLines(coresPath))
            {
                if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#") || raw.StartsWith("key\t")) continue;
                string[] f = raw.Split('\t');
                rows++;
                ProbeVoiceSpec v;
                if (f.Length == 5 && stock.TryGetValue(f[0].Trim(), out v)
                    && Math.Abs(v.BaseHz - double.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture)) < 1e-9
                    && v.Wave == f[2].Trim() && v.Rhythm == f[3].Trim()
                    && Math.Abs(v.Glide - double.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture)) < 1e-9) same++;
                else if (bad == null) bad = raw;
            }
            Check(rows == ProbeCores.Keys.Length && same == rows && stock.Count == rows,
                  "probe synth: ProbeVoiceSpec.Stock matches audio/probe/cores.tsv", bad ?? (same + "/" + rows));
        }

        // every stock clip against the Python fingerprint (midpoint jitter on both sides)
        {
            int clips = 0, lenOk = 0, ptsOk = 0;
            double worst = 0.0, worstRms = 0.0;
            string first = null;
            foreach (string raw in System.IO.File.ReadAllLines(refPath))
            {
                if (raw.StartsWith("#") || raw.Trim().Length == 0 || raw.StartsWith("core\t")) continue;
                string[] f = raw.Split('\t');
                clips++;
                ProbeVoiceSpec v = stock[f[0]];
                int variant = int.Parse(f[2]);
                int n = int.Parse(f[3]);
                double rms = double.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture);
                double[] y = ProbeSynth.Normalize(ProbeSynth.Beep(v, f[1], variant, null), ProbeSynth.TargetRmsDb);
                if (y.Length != n) { if (first == null) first = f[0] + "/" + f[1] + "/" + variant + " length " + y.Length + " vs " + n; continue; }
                lenOk++;
                int pts = f.Length - 5;
                double clipWorst = 0.0;
                for (int k = 0; k < pts; k++)
                {
                    double want = double.Parse(f[5 + k], System.Globalization.CultureInfo.InvariantCulture);
                    double got = y[(long)k * (n - 1) / (pts - 1)];
                    clipWorst = Math.Max(clipWorst, Math.Abs(got - want));
                }
                worst = Math.Max(worst, clipWorst);
                worstRms = Math.Max(worstRms, Math.Abs(ProbeSynth.GatedRmsDb(y) - rms));
                if (clipWorst < 1e-6) ptsOk++;
                else if (first == null) first = f[0] + "/" + f[1] + "/" + variant + " off by " + clipWorst.ToString("E2");
            }
            Check(clips == ProbeCores.Keys.Length * ProbeCores.Moods.Length * ProbeSynth.VariantsPerMood && lenOk == clips && ptsOk == clips && worstRms < 1e-4,
                  "probe synth: all " + clips + " stock clips match build_clips.py (length, 48 samples within 1e-6, gated RMS)",
                  first ?? ("max sample error " + worst.ToString("E2") + ", RMS " + worstRms.ToString("E2") + " dB"));
        }

        // with a real random jitter: loudness and peak stay in the build contract, the length stays near the reference
        {
            Random rng = new Random(7);
            ProbeSynth.Uniform u = (a, b) => a + (b - a) * rng.NextDouble();
            ProbeVoiceSpec custom = new ProbeVoiceSpec("test", 1600, "fm", "stutter", 0.8);
            int bad = 0, total = 0;
            double peakMax = 0.0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (string mood in ProbeCores.Moods)
                for (int i = 0; i < ProbeSynth.VariantsPerMood; i++)
                {
                    float[] y = ProbeSynth.Render(custom, mood, i, u);
                    double[] d = new double[y.Length];
                    double pk = 0.0;
                    for (int k = 0; k < y.Length; k++) { d[k] = y[k]; pk = Math.Max(pk, Math.Abs(y[k])); }
                    peakMax = Math.Max(peakMax, pk);
                    total++;
                    double sec = y.Length / (double)ProbeSynth.SampleRate;
                    if (Math.Abs(ProbeSynth.GatedRmsDb(d) - ProbeSynth.TargetRmsDb) > 1.0 || 20 * Math.Log10(pk) > -1.0 || sec < 0.08 || sec > 1.8) bad++;
                }
            sw.Stop();
            Check(bad == 0 && total == 21, "probe synth: a custom voice with real jitter gives 21 clips at -22 dB RMS +-1, peak under -1 dBFS, 0.08 to 1.8 s",
                  bad + " bad, peak " + (20 * Math.Log10(peakMax)).ToString("0.00") + " dBFS");
            // informational: how long one voice takes here (not the game's Mono)
            Check(true, "probe synth: 21 clips of one voice rendered", sw.ElapsedMilliseconds + " ms on this machine");
        }

        // extras and mood patterns of a voice's own
        {
            ProbeVoiceSpec baseV = ProbeVoiceSpec.Stock()[1];   // okto: triangle, swing
            int baseLen = ProbeSynth.Beep(baseV, "happy", 0, null).Length;
            ProbeVoiceSpec fast = baseV.Clone("fast"); fast.Speed = 2.0;
            ProbeVoiceSpec slow = baseV.Clone("slow"); slow.Speed = 0.5;
            int fastLen = ProbeSynth.Beep(fast, "happy", 0, null).Length, slowLen = ProbeSynth.Beep(slow, "happy", 0, null).Length;
            Check(Math.Abs(fastLen - baseLen / 2.0) < 20 && Math.Abs(slowLen - baseLen * 2.0) < 20,
                  "probe voice: speed 2 halves the phrase, 0.5 doubles it", baseLen + " / " + fastLen + " / " + slowLen);
            bool allDiffer = true;
            string which = null;
            ProbeVoiceSpec[] extras = { baseV.Clone("a"), baseV.Clone("b"), baseV.Clone("c") };
            extras[0].Spread = 1.5; extras[1].Vibrato = 0.5; extras[2].Buzz = 0.8;
            double[] plain = ProbeSynth.Beep(baseV, "curious", 0, null);
            for (int k = 0; k < extras.Length; k++)
            {
                double[] y = ProbeSynth.Beep(extras[k], "curious", 0, null);
                double diff = 0.0;
                for (int i = 0; i < Math.Min(y.Length, plain.Length); i++) diff = Math.Max(diff, Math.Abs(y[i] - plain[i]));
                if (diff < 0.05) { allDiffer = false; which = "extra " + k; }
                if (extras[k].Signature() == baseV.Signature()) { allDiffer = false; which = "signature " + k; }
            }
            Check(allDiffer, "probe voice: note jumps, vibrato and grit each change the sound and the signature", which);

            List<string> e = new List<string>();
            ProbeMoodSpec m = ProbeMoodSpec.FromValues(new Dictionary<string, string> { { "name", "Happy" }, { "length", "0.05" }, { "gap", "0.02" }, { "rhythm", "false" } },
                                                       new List<string> { "0>3 5 rest:0.1 7>12:0.2@0.8" }, "t", e);
            Check(e.Count == 0 && m != null && m.Name == "happy" && !m.UseRhythm && m.Variants.Count == 1 && m.Variants[0].Count == 3
                  && m.Variants[0][0].st1 == 3.0 && m.Variants[0][1].st0 == 5.0 && m.Variants[0][1].st1 == 5.0
                  && Math.Abs(m.Variants[0][1].gap - 0.12) < 1e-9 && m.Variants[0][2].dur == 0.2 && m.Variants[0][2].amp == 0.8
                  && m.Variants[0][0].dur == 0.05,
                  "probe mood: notes parse (glide, flat note, rest, length, level, mood defaults)", string.Join("; ", e.ToArray()));
            List<ProbeNote> v1 = m.NotesFor(1), v2 = m.NotesFor(2);
            Check(v1[0].st0 == 2.0 && v2[0].st0 == -2.0 && v1[v1.Count - 1].gap == 0.0, "probe mood: one notes line gives variants at 0, +2 and -2, no pause after the last note", null);
            ProbeMoodSpec m3 = ProbeMoodSpec.FromValues(new Dictionary<string, string> { { "name", "scared" } }, new List<string> { "0 1", "5 6 7", "-3" }, "t", null);
            Check(m3 != null && m3.NotesFor(0).Count == 2 && m3.NotesFor(1).Count == 3 && m3.NotesFor(2)[0].st0 == -3.0,
                  "probe mood: three notes lines are three variants, not transposed", null);
            List<string> eb = new List<string>();
            ProbeMoodSpec bad = ProbeMoodSpec.FromValues(new Dictionary<string, string> { { "name", "happy" } }, new List<string> { "0 x 99 1:5 2@3 rest:0.1x" }, "t", eb);
            Check(bad != null && bad.Variants[0].Count == 1 && eb.Count == 5, "probe mood: each bad note is reported and left out", eb.Count + ": " + string.Join("; ", eb.ToArray()));
            List<string> en = new List<string>();
            Check(ProbeMoodSpec.FromValues(new Dictionary<string, string> { { "name", "angry" } }, new List<string> { "0" }, "t", en) == null && en.Count == 1
                  && ProbeMoodSpec.FromValues(new Dictionary<string, string> { { "name", "happy" } }, new List<string> { "x" }, "t", en) == null,
                  "probe mood: an unknown mood or a mood without notes is rejected", null);

            ProbeVoiceSpec own = baseV.Clone("own");
            own.SetMood(m);
            double[] ownHappy = ProbeSynth.Beep(own, "happy", 0, null), ownSad = ProbeSynth.Beep(own, "sleepy", 0, null);
            double[] stockSad = ProbeSynth.Beep(baseV, "sleepy", 0, null);
            bool sadSame = ownSad.Length == stockSad.Length;
            for (int i = 0; sadSame && i < ownSad.Length; i++) sadSame = ownSad[i] == stockSad[i];
            int expect = (int)(0.05 * ProbeSynth.SampleRate) + (int)Math.Round(0.12 * ProbeSynth.SampleRate) + (int)(0.05 * ProbeSynth.SampleRate)
                         + (int)Math.Round(0.02 * ProbeSynth.SampleRate) + (int)(0.2 * ProbeSynth.SampleRate);
            Check(Math.Abs(ownHappy.Length - expect) <= 3 && sadSame && own.Signature() != baseV.Signature() && own.Clone("x").Moods.Count == 1,
                  "probe mood: an own mood replaces only that mood, it is in the signature and copies keep it", ownHappy.Length + " vs " + expect);
            float[] r = ProbeSynth.Render(own, "happy", 0, null);
            double pk = 0.0;
            foreach (float f in r) pk = Math.Max(pk, Math.Abs(f));
            Check(r.Length > 0 && 20 * Math.Log10(pk) <= -1.0, "probe mood: an own mood renders under the peak limit", (20 * Math.Log10(pk)).ToString("0.00"));
            var ev = new Dictionary<string, string>();
            foreach (var kv in own.ToValues()) ev[kv.Key] = kv.Value;
            ProbeVoiceSpec rt = ProbeVoiceSpec.FromValues(ev, null);
            var mv = new Dictionary<string, string>();
            foreach (var kv in m.ToValues()) mv[kv.Key] = kv.Value;
            rt.SetMood(ProbeMoodSpec.FromValues(mv, m.Notes, rt.Name, null));
            ProbeVoiceSpec ex = baseV.Clone("ex"); ex.Speed = 1.35; ex.Vibrato = 0.2; ex.Buzz = 0.1; ex.Spread = 0.75;
            var xv = new Dictionary<string, string>();
            foreach (var kv in ex.ToValues()) xv[kv.Key] = kv.Value;
            Check(rt.SameSound(own) && ProbeVoiceSpec.FromValues(xv, null).SameSound(ex),
                  "probe voice: extras and moods survive the settings round trip", null);
        }

        // registry and the pick per vessel
        {
            ProbeVoiceSpec cfgVoice = new ProbeVoiceSpec("modcore", 610, "saw", "even", 0.2);
            ProbeVoiceSpec cfgOkto = new ProbeVoiceSpec("okto", 900, "triangle", "swing", 0.35);   // a mod changes OKTO
            ProbeVoiceSpec mine = new ProbeVoiceSpec("custom1", 1500, "fm", "lazy", 0.5);
            ProbeVoiceRegistry reg = ProbeVoiceRegistry.Build(new[] { cfgVoice, cfgOkto }, new[] { mine });
            Check(reg.Get("OKTO ") == cfgOkto && reg.SourceOf("okto") == ProbeVoiceSource.Config && reg.SourceOf("custom1") == ProbeVoiceSource.Settings
                  && reg.Names.Count == 12 && reg.Names[0] == ProbeCores.Stayputnik && reg.Names[10] == "modcore" && reg.Names[11] == "custom1",
                  "probe voice: registry order stock, GameData, settings, a later voice replaces one with the same name", string.Join(" ", new List<string>(reg.Names).ToArray()));
            Check(!reg.UsesStockClips(cfgOkto) && reg.UsesStockClips(reg.Get("hecs")) && !reg.UsesStockClips(mine)
                  && reg.UsesStockClips(reg.Get("hecs").Clone(null)),
                  "probe voice: only a voice that sounds like its stock voice uses the prebuilt clips", null);
            Check(reg.FreeName("custom") == "custom2" && reg.Cycle("custom1", 1) == ProbeCores.Stayputnik && reg.Cycle(ProbeCores.Stayputnik, -1) == "custom1",
                  "probe voice: free name and cycling wrap around", null);

            ProbeVoicePick.Why why;
            int at;
            var parts = new List<ProbeCorePart> {
                new ProbeCorePart("probeCoreOcto.v2", null, 0),
                new ProbeCorePart("modProbe", "modcore", 0),
                new ProbeCorePart("otherMod", "custom1", 5),
                new ProbeCorePart("probeCoreHex.v2", null, 0) };
            ProbeVoiceSpec r1 = ProbeVoicePick.Resolve(parts, null, reg, out at, out why);
            Check(r1 == mine && at == 2 && why == ProbeVoicePick.Why.Module, "probe voice: the module voice with the highest priority wins over stock parts", r1 != null ? r1.Name : "null");
            var player = new Dictionary<string, string> { { ProbeCores.Normalise("probeCoreHex.v2"), "modcore" } };
            ProbeVoiceSpec r2 = ProbeVoicePick.Resolve(parts, player, reg, out at, out why);
            Check(r2 == cfgVoice && at == 3 && why == ProbeVoicePick.Why.Player, "probe voice: a voice the player gave a part wins over modules", r2 != null ? r2.Name : "null");
            var tie = new List<ProbeCorePart> { new ProbeCorePart("a", "modcore", 1), new ProbeCorePart("b", "custom1", 1) };
            Check(ProbeVoicePick.Resolve(tie, null, reg, out at, out why) == cfgVoice && at == 0, "probe voice: equal priority, the root-most core wins", null);
            var stockOnly = new List<ProbeCorePart> { new ProbeCorePart("someModCore", "nosuchvoice", 9), new ProbeCorePart("probeCoreOcto.v2", null, 0) };
            Check(ProbeVoicePick.Resolve(stockOnly, new Dictionary<string, string> { { "someModCore".ToLowerInvariant(), "missing" } }, reg, out at, out why) == cfgOkto
                  && at == 1 && why == ProbeVoicePick.Why.StockPart,
                  "probe voice: unknown voice names are skipped, then the first stock part decides", null);
            // Patches/Lonapse_StockCores.cfg: a stock core with its own voice from the patch (priority -1)
            var patched = new List<ProbeCorePart> { new ProbeCorePart("probeCoreOcto.v2", "okto", -1), new ProbeCorePart("modProbe", "modcore", 0) };
            ProbeVoiceSpec pm = ProbeVoicePick.Resolve(patched, null, reg, out at, out why);
            ProbeVoiceSpec ps = ProbeVoicePick.Resolve(new List<ProbeCorePart> { new ProbeCorePart("probeCoreOcto.v2", "okto", -1) }, null, reg, out at, out why);
            Check(pm == cfgVoice && ps == cfgOkto && why == ProbeVoicePick.Why.StockPart,
                  "probe voice: a patched stock core still says stock, and a modded core with priority 0 still wins over it", pm != null ? pm.Name : "null");
            {
                // every part in the patch is a known stock core and gets its own stock voice
                string cfgPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "../../GameData/Lonapse/Patches/Lonapse_StockCores.cfg"));
                string cfg = System.IO.File.ReadAllText(cfgPath);
                var badPatch = new List<string>();
                int patchedParts = 0;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(cfg,
                         @"@PART\[([^\]]+)\][^\n]*\n\{\s*MODULE \{ name = ModuleLonapse  voice = ([a-z0-9]+)  priority = (-?[0-9]+) \}"))
                    foreach (string pn in m.Groups[1].Value.Split('|'))
                    {
                        patchedParts++;
                        if (!ProbeCores.IsKnown(pn) || ProbeCores.KeyFor(pn) != m.Groups[2].Value) badPatch.Add(pn + " = " + m.Groups[2].Value);
                        if (m.Groups[3].Value != "-1") badPatch.Add(pn + " priority " + m.Groups[3].Value);
                    }
                Check(badPatch.Count == 0 && patchedParts == 17, "probe voice: the MM patch gives all " + patchedParts + " stock cores their own voice at priority -1",
                      string.Join(" ; ", badPatch.ToArray()));
            }
            ProbeVoiceSpec g = ProbeVoicePick.Resolve(new List<ProbeCorePart> { new ProbeCorePart("x", null, 0) }, null, reg, out at, out why);
            Check(g != null && g.Name == ProbeCores.Generic && at == 0 && why == ProbeVoicePick.Why.Generic
                  && ProbeVoicePick.Resolve(new List<ProbeCorePart>(), null, reg, out at, out why) == null && at == -1,
                  "probe voice: an unknown core gets generic, no core gives null", null);
            // ModuleLonapse displayName: two parts share one voice and keep their own subtitle name
            var named = new List<ProbeCorePart> {
                new ProbeCorePart("nfexSTP", "modcore", 1, " STP "),
                new ProbeCorePart("nfexPLTO", "modcore", 0, "PLTO"),
                new ProbeCorePart("probeCoreOcto.v2", null, 0, "   ") };
            ProbeVoiceSpec n1 = ProbeVoicePick.Resolve(named, null, reg, out at, out why);
            Check(n1 == cfgVoice && ProbeVoicePick.ModuleName(named, at) == "STP",
                  "probe voice: the deciding part's module displayName is the subtitle name, trimmed", ProbeVoicePick.ModuleName(named, at));
            var plto = new List<ProbeCorePart> { named[1] };
            Check(ProbeVoicePick.Resolve(plto, null, reg, out at, out why) == cfgVoice && ProbeVoicePick.ModuleName(plto, at) == "PLTO",
                  "probe voice: another part with the same voice has its own name", null);
            var plPick = new Dictionary<string, string> { { ProbeCores.Normalise("nfexPLTO"), "hecs" } };
            Check(ProbeVoicePick.Resolve(plto, plPick, reg, out at, out why).Name == "hecs" && why == ProbeVoicePick.Why.Player
                  && ProbeVoicePick.ModuleName(plto, at) == "PLTO",
                  "probe voice: the part name stays when the player picks another voice", null);
            Check(ProbeVoicePick.ModuleName(named, 2) == null && ProbeVoicePick.ModuleName(parts, 0) == null
                  && ProbeVoicePick.ModuleName(named, -1) == null && ProbeVoicePick.ModuleName(named, 9) == null && ProbeVoicePick.ModuleName(null, 0) == null,
                  "probe voice: no, blank or out of range module name gives null", null);
            // a voice written to the settings reads back the same
            var vals = new Dictionary<string, string>();
            foreach (var kv in mine.ToValues()) vals[kv.Key] = kv.Value;
            List<string> er = new List<string>();
            ProbeVoiceSpec back = ProbeVoiceSpec.FromValues(vals, er);
            Check(er.Count == 0 && back != null && back.SameSound(mine) && back.Name == mine.Name && back.Lines == mine.Lines && back.DisplayName == mine.DisplayName,
                  "probe voice: ToValues and FromValues round trip", string.Join("; ", er.ToArray()));
            Settings st = new Settings();
            st.probeVoices.Add(new ProbeVoiceSpec("dup", 500, "sine", "even", 0));
            ProbeVoiceSpec last = new ProbeVoiceSpec("dup", 700, "sine", "even", 0);
            st.probeVoices.Add(last);
            st.probeVoices.Add(null);
            st.Clamp();
            Check(st.probeVoices.Count == 1 && st.probeVoices[0] == last && st.SettingsVoice("dup") == last, "probe voice: Settings.Clamp keeps the last voice per name", st.probeVoices.Count.ToString());
        }

        // voice values from a config node or the settings
        {
            List<string> e = new List<string>();
            ProbeVoiceSpec v = ProbeVoiceSpec.FromValues(new Dictionary<string, string> {
                { "name", " MyProbe " }, { "displayName", "Kerbalism Core" }, { "lines", "OKTO" },
                { "baseHz", "760.5" }, { "wave", "FM" }, { "rhythm", "swing" }, { "glide", "0.4" } }, e);
            Check(e.Count == 0 && v != null && v.Name == "myprobe" && v.DisplayName == "Kerbalism Core" && v.Lines == "okto"
                  && Math.Abs(v.BaseHz - 760.5) < 1e-9 && v.Wave == "fm" && v.Rhythm == "swing" && Math.Abs(v.Glide - 0.4) < 1e-9,
                  "probe voice: values parse, case and spaces ignored, invariant decimal point", string.Join("; ", e.ToArray()));
            List<string> e2 = new List<string>();
            ProbeVoiceSpec w = ProbeVoiceSpec.FromValues(new Dictionary<string, string> {
                { "name", "bad" }, { "lines", "mycore" }, { "baseHz", "90" }, { "wave", "noise" }, { "rhythm", "fast" }, { "glide", "2" } }, e2);
            ProbeVoiceSpec def = new ProbeVoiceSpec();
            Check(e2.Count == 5 && w != null && w.Lines == ProbeCores.Generic && w.BaseHz == def.BaseHz && w.Wave == def.Wave
                  && w.Rhythm == def.Rhythm && w.Glide == def.Glide,
                  "probe voice: each bad value is reported and keeps its default", e2.Count + " errors");
            ProbeVoiceSpec okt = ProbeVoiceSpec.FromValues(new Dictionary<string, string> { { "name", "okto" }, { "baseHz", "900" } }, null);
            Check(okt != null && okt.Lines == "okto" && okt.DisplayName == "OKTO", "probe voice: a voice named after a stock core keeps its lines and name", okt != null ? okt.Lines : "null");
            List<string> e3 = new List<string>();
            Check(ProbeVoiceSpec.FromValues(new Dictionary<string, string> { { "wave", "sine" } }, e3) == null && e3.Count == 1,
                  "probe voice: a voice without a name is rejected", null);
        }
    }

    static void ProbeCoreTests()
    {
        string[,] stock =
        {
            { "probeCoreSphere", "stayputnik" }, { "probeCoreSphere_v2", "stayputnik" }, { "probeCoreCube", "qbe" },
            { "probeCoreOcto", "okto" }, { "probeCoreOcto_v2", "okto" }, { "probeCoreOcto2", "okto" }, { "probeCoreOcto2_v2", "okto" },
            { "probeCoreHex", "hecs" }, { "probeCoreHex_v2", "hecs" }, { "HECS2_ProbeCore", "hecs" },
            { "roverBody", "rovemate" }, { "roverBody_v2", "rovemate" },
            { "probeStackSmall", "snob" }, { "probeStackLarge", "snob" }, { "mk2DroneCore", "mk2drone" }, { "MpoProbe", "mpo" }, { "MtmStage", "mtm" },
        };
        string bad = "";
        for (int i = 0; i < stock.GetLength(0); i++)
        {
            string n = stock[i, 0], want = stock[i, 1];
            if (ProbeCores.KeyFor(n) != want) bad += n + "=" + ProbeCores.KeyFor(n) + " ";
            string dotted = n.Replace('_', '.');
            if (ProbeCores.KeyFor(dotted) != want) bad += dotted + "=" + ProbeCores.KeyFor(dotted) + " ";
            if (ProbeCores.KeyFor(n.ToUpperInvariant()) != want) bad += n.ToUpperInvariant() + " ";
            if (!ProbeCores.IsKnown(dotted)) bad += "!known " + dotted + " ";
        }
        Check(bad == "", "probe: KeyFor maps every stock core in the '_' and '.' forms", bad == "" ? stock.GetLength(0) + " names" : bad);
        Check(ProbeCores.KeyFor("mk1pod_v2") == "generic" && ProbeCores.KeyFor(null) == "generic" && ProbeCores.KeyFor("") == "generic"
              && ProbeCores.KeyFor("  probeCoreOcto.v2 ") == "okto", "probe: unknown, null and empty names are generic, whitespace trimmed", null);

        Check(ProbeCores.Pick(null) == null && ProbeCores.Pick(new List<string>()) == null, "probe: Pick of no cores is null", null);
        Check(ProbeCores.Pick(new List<string> { "someModCore", "probeCoreHex.v2", "probeCoreOcto" }) == "hecs",
              "probe: Pick skips unknown cores and takes the root-most known one", ProbeCores.Pick(new List<string> { "someModCore", "probeCoreHex.v2", "probeCoreOcto" }));
        Check(ProbeCores.Pick(new List<string> { "roverBody.v2", "probeCoreSphere" }) == "rovemate", "probe: Pick takes the root core first", null);
        Check(ProbeCores.Pick(new List<string> { "someModCore", null }) == "generic", "probe: Pick of only unknown cores is generic", null);

        string[] f = ProbeCores.Folders();
        bool shape = f.Length == 70 && f[0] == "beeps/probe_stayputnik_happy" && f[6] == "beeps/probe_stayputnik_bored"
                     && f[69] == "beeps/probe_generic_bored" && new HashSet<string>(f).Count == 70;
        Check(shape && ProbeCores.Keys.Length == 10 && ProbeCores.Moods.Length == 7, "probe: Folders is 10 cores x 7 moods", f.Length + " folders");
        Check(ProbeCores.DisplayName("okto") == "OKTO" && ProbeCores.DisplayName("hecs") == "HECS" && ProbeCores.DisplayName("rovemate") == "RoveMate"
              && ProbeCores.DisplayName("stayputnik") == "Stayputnik" && ProbeCores.DisplayName("snob") == "Probe" && ProbeCores.DisplayName("generic") == "Probe"
              && ProbeCores.DisplayName("qbe") == "QBE" && ProbeCores.DisplayName("mk2drone") == "Drone Core"
              && ProbeCores.DisplayName("mpo") == "MPO" && ProbeCores.DisplayName("mtm") == "MTM"
              && ProbeCores.DisplayName(null) == "Probe", "probe: display names", null);
        {
            bool own = true;
            foreach (string k in ProbeCores.Keys) own &= ProbeCores.DefaultLines(k) == k;
            Check(own && ProbeCores.BorrowGenericLines.Length == 0, "probe: every stock core uses its own lines", null);
        }
        {
            bool stockOk = true;
            foreach (ProbeVoiceSpec v in ProbeVoiceSpec.Stock()) stockOk &= v.Lines == ProbeCores.DefaultLines(v.Name);
            var vals = new Dictionary<string, string> { { "name", "qbe" }, { "baseHz", "1500" } };
            ProbeVoiceSpec cfgQbe = ProbeVoiceSpec.FromValues(vals, new List<string>());
            Check(stockOk && cfgQbe != null && cfgQbe.Lines == "qbe" && cfgQbe.DisplayName == "QBE",
                  "probe: stock voices and a config voice named qbe use DefaultLines and the core's display name", null);
        }

        ProbeSleep sl = new ProbeSleep();
        const float dr = 0.15f, sp = 0.03f;
        bool a1 = sl.Update(0.5, dr, sp) == ProbeSleepState.Awake;
        bool d1 = sl.Update(0.14, dr, sp) == ProbeSleepState.Drowsy;
        bool d2 = sl.Update(0.17, dr, sp) == ProbeSleepState.Drowsy;    // inside the margin: still drowsy
        bool a2 = sl.Update(0.21, dr, sp) == ProbeSleepState.Awake;     // above 0.15 + 0.05
        Check(a1 && d1 && d2 && a2, "probe sleep: drowsy below 0.15, awake again only above 0.20", null);
        bool z1 = sl.Update(0.02, dr, sp) == ProbeSleepState.Asleep;    // straight from awake to asleep
        bool z2 = sl.Update(0.10, dr, sp) == ProbeSleepState.Asleep && sl.Update(0.19, dr, sp) == ProbeSleepState.Asleep;
        bool z3 = sl.Update(0.25, dr, sp) == ProbeSleepState.Awake;
        Check(z1 && z2 && z3, "probe sleep: asleep below 0.03 stays asleep until the charge passes 0.20", null);
        sl.Update(0.12, dr, sp);
        bool z4 = sl.Update(0.01, dr, sp) == ProbeSleepState.Asleep && sl.Asleep && !sl.Drowsy;
        Check(z4, "probe sleep: drowsy falls asleep below 0.03", null);
        sl.Reset();
        bool f1 = sl.Update(0.9, dr, sp, ProbeCores.WakeMargin, true) == ProbeSleepState.Asleep;
        bool f2 = sl.Update(0.9, dr, sp, ProbeCores.WakeMargin, false) == ProbeSleepState.Awake;
        Check(f1 && f2, "probe sleep: forced sleep (no power, hibernating) is asleep at once and wakes when lifted", null);
        sl.Reset();
        Check(sl.Update(double.NaN, dr, sp) == ProbeSleepState.Awake, "probe sleep: NaN charge counts as full", null);

        FlightSample fs = new FlightSample();
        Check(fs.probeCoreKey == null && !fs.probeAsleep, "probe: FlightSample defaults mean no probe", null);
    }

    static void ProbeSettingsTests()
    {
        Settings d = new Settings();
        Tuning t = d.tuning;
        Check(d.probeMode && d.probePetTalk && d.probeHail && d.probeSleep,
              "settings v5: probe bools default true", null);
        Check(t.hailInterval == 360f && t.probeRateFactor == 1.0f
              && t.probeDrowsyFrac == 0.15f && t.probeSleepFrac == 0.03f && t.probeBeepPitchMax == 1.35f, "settings v5: TUNING defaults of the plan", null);
        Tuning t0 = new Tuning(); t0.Sanitize();
        Check(t0.hailInterval == 360f && t0.probeRateFactor == 1.0f
              && t0.probeDrowsyFrac == 0.15f && t0.probeSleepFrac == 0.03f && t0.probeBeepPitchMax == 1.35f, "settings v5: Sanitize keeps the defaults", null);

        // round trip of the TUNING node: Save and Load copy every public float by reflection under its own name
        Tuning src = new Tuning();
        src.hailInterval = 300f; src.probeRateFactor = 2f;
        src.probeDrowsyFrac = 0.2f; src.probeSleepFrac = 0.05f; src.probeBeepPitchMax = 1.5f;
        Dictionary<string, string> node = new Dictionary<string, string>();
        foreach (System.Reflection.FieldInfo fi in typeof(Tuning).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (fi.FieldType == typeof(float)) node[fi.Name] = ((float)fi.GetValue(src)).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        Tuning dst = new Tuning();
        foreach (System.Reflection.FieldInfo fi in typeof(Tuning).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (fi.FieldType == typeof(float) && node.ContainsKey(fi.Name))
                fi.SetValue(dst, float.Parse(node[fi.Name], System.Globalization.CultureInfo.InvariantCulture));
        dst.Sanitize();
        string[] v5 = { "hailInterval", "probeRateFactor", "probeDrowsyFrac", "probeSleepFrac", "probeBeepPitchMax" };
        bool all = true;
        for (int i = 0; i < v5.Length; i++) if (!node.ContainsKey(v5[i])) all = false;
        Check(all && dst.hailInterval == 300f && dst.probeRateFactor == 2f
              && dst.probeDrowsyFrac == 0.2f && dst.probeSleepFrac == 0.05f && dst.probeBeepPitchMax == 1.5f,
              "settings v5: probe tuning survives the TUNING round trip and Sanitize", null);

        Tuning bt = new Tuning();
        bt.hailInterval = 1e5f; bt.probeRateFactor = float.NaN; bt.probeDrowsyFrac = 0.05f; bt.probeSleepFrac = 0.1f;
        bt.probeBeepPitchMax = 0.5f; bt.Sanitize();
        Check(bt.hailInterval == 1800f && bt.probeRateFactor == 1.0f && bt.probeSleepFrac == 0.05f && bt.probeBeepPitchMax == 1f,
              "settings v5: Sanitize clamps probe tuning and keeps sleep at or below drowsy", bt.probeSleepFrac.ToString());
        Near(DcMath.StaticCurve(0f), 0.0, 1e-6, "radio static: 0 % is silent");
        Near(DcMath.StaticCurve(0.25f), 0.5, 1e-6, "radio static: 25 % is half the full level");
        Near(DcMath.StaticCurve(1f), 1.0, 1e-6, "radio static: 100 % is the full level");
        Near(DcMath.StaticCurve(float.NaN), 0.0, 1e-6, "radio static: NaN is silent");
        Check(new Settings().radioStatic == 0.5f, "radio static: on by default (50 %)", null);
        Settings old = new Settings(); old.tuningVersion = Tuning.CurrentVersion;
        Check(old.MigrateValues(Tuning.CurrentVersion).Count == 0 && Tuning.CurrentVersion == 12, "settings v5: no migration needed at the current version (12 since the 0.7.0 defaults)", null);
    }

    static void ReviewFixTests()
    {
        // event max age: science 600 s, soi 120 s at most, crew_lost keeps eventMaxAge
        Check(EventWatchers.MaxAgeFor(EventWatchers.Science, 600f, 600f) == 600f && EventWatchers.MaxAgeFor(EventWatchers.Science, 0f, 900f) == 600f,
              "events: science max age capped at 600 s", null);
        Check(EventWatchers.MaxAgeFor(EventWatchers.SoiEnter, 600f, 600f) == 120f && EventWatchers.MaxAgeFor(EventWatchers.SoiEnter, -1f, 600f) == 120f,
              "events: soi_enter max age capped at 120 s", null);
        Check(EventWatchers.MaxAgeFor(EventWatchers.Science, 30f, 600f) == 30f && EventWatchers.MaxAgeFor(EventWatchers.SoiEnter, 60f, 30f) == 60f,
              "events: a shorter requested max age stays", null);
        Check(EventWatchers.MaxAgeFor(EventWatchers.CrewLost, 600f, 600f) == 600f && EventWatchers.MaxAgeFor(EventWatchers.CrewLost, 0f, 900f) == 900f,
              "events: crew_lost keeps eventMaxAge", null);
        Check(EventWatchers.MaxAgeFor(EventWatchers.PartLost, EventWatchers.PartLostMaxAge, 600f) == EventWatchers.PartLostMaxAge
              && EventWatchers.MaxAgeFor(EventWatchers.SignalBack, EventWatchers.SignalMaxAge, 600f) == EventWatchers.SignalMaxAge,
              "events: part_lost and signal keep their own max age", null);
        SoiWatcherCheck();
        ScienceWatcherCheck();

        // debug topic cycle: "" first, every science topic and home once, never "any"
        string[] tc = PhraseTable.TopicCycle();
        bool allIn = true;
        foreach (string t in PhraseTable.ScienceTopics) if (t != "any" && Array.IndexOf(tc, t) < 0) allIn = false;
        var seen = new HashSet<string>(tc);
        Check(tc.Length > 0 && tc[0] == "" && allIn && Array.IndexOf(tc, "home") >= 0 && Array.IndexOf(tc, "any") < 0 && seen.Count == tc.Length,
              "debug: topic cycle has any, the science topics and home, each once", tc.Length + " entries");

        // expression override read-back
        Check(FaceMath.OverrideHeld(0.25f, 0.25f) && FaceMath.OverrideHeld(0.25f + 0.5f * FaceMath.OverrideEps, 0.25f),
              "face: override held when the read-back equals the write", null);
        Check(!FaceMath.OverrideHeld(0.1f, 0.25f) && !FaceMath.OverrideHeld(float.NaN, 0.25f) && !FaceMath.OverrideHeld(0.25f, float.NaN),
              "face: override lost on another value or NaN", null);

        // badass mood only with personalityEffects (the persona flag, not the raw isBadass)
        KerbalVoice b = KerbalVoice.Create("Badass Kerman", 0.5f, 0.5f, false);
        b.isBadass = true;
        b.ApplyPersona(new Persona(0.5f, 0.5f, true, false, "Pilot"), true);
        bool on = b.BadassEffective;
        b.ApplyPersona(new Persona(0.5f, 0.5f, true, false, "Pilot"), false);
        bool off = b.BadassEffective;
        KerbalVoice n = KerbalVoice.Create("Plain Kerman", 0.5f, 0.5f, false);
        n.isBadass = true; // raw flag set, but no badass persona applied
        n.ApplyPersona(new Persona(0.5f, 0.5f, false, false, "Pilot"), true);
        Check(on && !off && !n.BadassEffective, "face: badass mood needs the persona flag and personalityEffects", on + " " + off + " " + n.BadassEffective);
    }

    static void SoiWatcherCheck()
    {
        SoiWatcher w = new SoiWatcher();
        EventRequest r;
        bool ok = w.OnSoiChange(0.0, "Mun", "mun", false, 60.0, EventWatchers.MaxAgeFor(EventWatchers.SoiEnter, 600f, 600f), out r);
        Check(ok && r.maxAge == 120f, "events: soi_enter request carries the 120 s max age", ok ? r.maxAge.ToString() : "no request");
    }

    static void ScienceWatcherCheck()
    {
        ScienceWatcher w = new ScienceWatcher();
        EventRequest r;
        bool ok = w.OnExperiment(0.0, "crewReport@KerbinSrfLanded", "kerbin", 30.0, 300.0, EventWatchers.MaxAgeFor(EventWatchers.Science, 600f, 600f), out r);
        Check(ok && r.maxAge == 600f, "events: science request carries the 600 s max age", ok ? r.maxAge.ToString() : "no request");
    }

    // ==== pure core modules (Pacing, MixMath, SignalMath, RadioProfile, DescentSequence, ThrottleWatch, Audit) ====
    static class CoreModuleTests
    {
        static double MagDb(double[] c, double f, double sr)
        {
            double w = 2.0 * Math.PI * f / sr;
            double re1 = Math.Cos(w), im1 = -Math.Sin(w);
            double re2 = Math.Cos(2 * w), im2 = -Math.Sin(2 * w);
            double nr = c[0] + c[1] * re1 + c[2] * re2, ni = c[1] * im1 + c[2] * im2;
            double dr = 1 + c[3] * re1 + c[4] * re2, di = c[3] * im1 + c[4] * im2;
            return 20.0 * Math.Log10(Math.Sqrt(nr * nr + ni * ni) / Math.Sqrt(dr * dr + di * di));
        }

        static List<string> Feed(ThrottleWatch tw, float dt, int frames, float throttle)
        {
            List<string> events = new List<string>();
            for (int i = 0; i < frames; i++)
            {
                string e = tw.Update(dt, throttle, true);
                if (e != null) events.Add(e);
            }
            return events;
        }

        static bool Same(List<string> got, params string[] want)
        {
            if (got.Count != want.Length) return false;
            for (int i = 0; i < want.Length; i++) if (got[i] != want[i]) return false;
            return true;
        }

        public static void Run()
        {
            // ---- MixMath: dB conversion ----
            foreach (float db in new float[] { -60f, -20f, -6f, 0f, 3f, 12f })
            {
                float back = MixMath.LinToDb(MixMath.DbToLin(db));
                Check(Math.Abs(back - db) < 1e-3f, "core: dB round trip " + db, back.ToString("0.#####"));
            }
            Check(MixMath.LinToDb(0f) == MixMath.MinDb && MixMath.DbToLin(0f) == 1f, "core: 0 lin is floor, 0 dB is 1.0", null);

            // ---- MixMath: DuckStep attack and release are monotonic and do not overshoot ----
            {
                float cur = 1f; bool mono = true, noOvershoot = true;
                for (int i = 0; i < 200; i++)
                {
                    float next = MixMath.DuckStep(cur, 0.2f, 0.02f, 0.05f, 0.5f);
                    if (next > cur || next < 0.2f) mono = false;
                    cur = next;
                }
                if (cur < 0.2f) noOvershoot = false;
                Check(mono && noOvershoot, "core: duck attack monotonic down, no overshoot", cur.ToString("0.####"));

                cur = 0.2f; mono = true; noOvershoot = true;
                for (int i = 0; i < 400; i++)
                {
                    float next = MixMath.DuckStep(cur, 1f, 0.02f, 0.05f, 0.5f);
                    if (next < cur || next > 1f) mono = false;
                    cur = next;
                }
                if (cur > 1f) noOvershoot = false;
                Check(mono && noOvershoot, "core: duck release monotonic up, no overshoot", cur.ToString("0.####"));

                // One attack time constant covers about 63 percent of the step.
                float oneTau = MixMath.DuckStep(1f, 0.2f, 0.05f, 0.05f, 0.5f);
                Near(oneTau, 1.0 - 0.8 * (1 - Math.Exp(-1)), 1e-4, "core: duck attack after one tau");
            }

            // ---- MixMath: compressor ----
            Check(MixMath.CompressorGainDb(-30f, -20f, 4f) == 0f, "core: compressor below threshold no gain change", null);
            Near(MixMath.CompressorGainDb(-10f, -20f, 4f), -7.5, 1e-4, "core: compressor 10 dB over, ratio 4 gives -7.5 dB");
            Check(MixMath.CompressorGainDb(-10f, -20f, 4f) < 0f, "core: compressor above threshold reduces gain", null);

            // ---- MixMath: RBJ peaking EQ ----
            {
                double[] c = MixMath.PeakingBiquad(2500, 3.0, 1.0, 48000);
                Near(MagDb(c, 2500, 48000), 3.0, 0.05, "core: peaking EQ +3 dB at 2.5 kHz");
                Near(MagDb(c, 10, 48000), 0.0, 0.1, "core: peaking EQ flat far below the peak");
                double[] flat = MixMath.PeakingBiquad(2500, 0.0, 1.0, 48000);
                Near(MagDb(flat, 2500, 48000), 0.0, 1e-6, "core: peaking EQ at 0 dB is flat");
            }

            // ---- Pacing: TokenBucket ----
            {
                TokenBucket tb = new TokenBucket(4f, 8f);
                bool first4 = true;
                for (int i = 0; i < 4; i++) if (!tb.TryTake(0f)) first4 = false;
                Check(first4 && !tb.TryTake(0f), "core: token bucket burst of 4, then empty", null);
                Check(tb.TryTake(7.5f) && !tb.TryTake(7.5f), "core: token bucket 8 per minute refills 1 token in 7.5 s", null);
                bool capped = true;
                for (int i = 0; i < 4; i++) if (!tb.TryTake(1000f)) capped = false;
                Check(capped && !tb.TryTake(1000f), "core: token bucket caps at capacity", null);
            }

            // ---- Pacing: Cooldowns ----
            {
                Cooldowns cd = new Cooldowns();
                Check(cd.Ready("sk:a", 0f, 10f), "core: cooldown unknown key is ready", null);
                cd.Stamp("sk:a", 0f);
                Check(!cd.Ready("sk:a", 5f, 10f) && cd.Ready("sk:a", 10f, 10f), "core: cooldown holds until cd seconds passed", null);
                Check(cd.Ready("sk:b", 0f, 10f), "core: cooldown keys are independent", null);
                cd.Clear();
                Check(cd.Ready("sk:a", 1f, 10f), "core: cooldown clear resets", null);
            }

            // ---- Pacing: Escalation ----
            {
                Escalation es = new Escalation();
                es.Bump(2f); es.Bump(2f); es.Bump(2f);
                Check(es.Level == 2f, "core: escalation bump capped", es.Level.ToString());
                es.Decay(10f, 10f);
                Near(es.Level, 2.0 * Math.Exp(-1), 1e-4, "core: escalation decays with tau");
                Near(es.Offset(0.5f, 1.5f), 2.0 * Math.Exp(-1) * 0.5, 1e-4, "core: escalation offset after decay");
                es.Decay(1f, 0f);
                Check(es.Level == 0f, "core: escalation decay with tau 0 resets", null);
            }

            // ---- Pacing: RollingStat ----
            {
                RollingStat rs = new RollingStat(10f);
                rs.Add(1f, 5f); rs.Add(3f, 5f);
                Near(rs.StdDev, 1.0, 1e-4, "core: rolling stddev of 1 and 3 with equal weight");
                rs.Add(3f, 10f);
                Near(rs.StdDev, 0.0, 1e-4, "core: rolling stat forgets samples outside the window");
            }

            // ---- Pacing: Priority ordering ----
            Check(Priority.Callout > Priority.Warning && Priority.Warning > Priority.Chatter, "core: priority callout > warning > chatter", null);

            // ---- SignalMath: FromDistance ----
            Near(SignalMath.FromDistance(0, 100, 1000), 1.0, 1e-9, "core: FromDistance closer than full is 1");
            Near(SignalMath.FromDistance(100, 100, 1000), 1.0, 1e-9, "core: FromDistance at full is 1");
            Near(SignalMath.FromDistance(550, 100, 1000), 0.5, 1e-9, "core: FromDistance halfway is 0.5");
            Near(SignalMath.FromDistance(1000, 100, 1000), 0.0, 1e-9, "core: FromDistance at zero is 0");
            Near(SignalMath.FromDistance(5000, 100, 1000), 0.0, 1e-9, "core: FromDistance beyond zero clamps to 0");
            Near(SignalMath.FromDistance(-5, 100, 1000), 1.0, 1e-9, "core: FromDistance negative clamps to 1");
            Near(SignalMath.FromDistance(double.NaN, 100, 1000), 0.0, 1e-9, "core: FromDistance NaN gives 0");

            // ---- SignalMath: EVA relay ----
            Near(SignalMath.RelaySignal(0, 0.8, 0.5, 1.0, 2.3), 0.8, 1e-9, "core: EVA relay near the ship carries the ship signal");
            Near(SignalMath.RelaySignal(0, 0.8, 1.65, 1.0, 2.3), 0.4, 1e-9, "core: EVA relay halfway the suit range halves it");
            Near(SignalMath.RelaySignal(0, 0.8, 3.0, 1.0, 2.3), 0.0, 1e-9, "core: EVA relay out of suit range is 0");
            Near(SignalMath.RelaySignal(0, 0.0, 0.1, 1.0, 2.3), 0.0, 1e-9, "core: EVA relay without ship signal is 0");
            Near(SignalMath.RelaySignal(0.9, 0.2, 0.1, 1.0, 2.3), 0.9, 1e-9, "core: EVA own link wins when better");
            Near(SignalMath.RelaySignal(double.NaN, double.NaN, 0.1, 1.0, 2.3), 0.0, 1e-9, "core: EVA relay NaN gives 0");
            Near(SignalMath.RelaySignal(0, 1.5, 0.1, 1.0, 2.3), 1.0, 1e-9, "core: EVA relay caps at 1");
            Check(SignalMath.RelayConnected(false, true, 0.5, 1.0, 2.3) && !SignalMath.RelayConnected(false, false, 0.5, 1.0, 2.3)
                  && !SignalMath.RelayConnected(false, true, 2.3, 1.0, 2.3) && SignalMath.RelayConnected(true, false, 9.0, 1.0, 2.3),
                  "core: EVA link follows the ship link within suit range, own link always counts", null);
            {
                Tuning et = new Tuning();
                Check(et.evaRelayFullKm == 1.0f && et.evaRelayZeroKm == 2.3f && new Settings().evaRelay, "settings v6: EVA relay defaults", null);
                et.evaRelayFullKm = 5f; et.evaRelayZeroKm = 1f; et.Sanitize();
                Check(et.evaRelayZeroKm > et.evaRelayFullKm, "settings v6: Sanitize keeps the suit range ordered", null);
            }

            // ---- SignalMath: Occluded ----
            {
                double R = 600000;
                Check(SignalMath.Occluded(new double[] { 1e6, 0, 0 }, new double[] { -1e6, 0, 0 }, R), "core: occluded when segment crosses body", null);
                Check(!SignalMath.Occluded(new double[] { 1e6, 7e5, 0 }, new double[] { -1e6, 7e5, 0 }, R), "core: not occluded when segment passes beside body", null);
                Check(!SignalMath.Occluded(new double[] { 1e6, 0, 0 }, new double[] { 0, 1e6, 0 }, R), "core: not occluded at 707 km closest approach", null);
                Check(SignalMath.Occluded(new double[] { 1e6, 0, 0 }, new double[] { 0, 1e6, 0 }, 800000), "core: occluded with larger radius", null);
                Check(!SignalMath.Occluded(new double[] { 1e6, 0, 0 }, new double[] { 1e6, 0, 0 }, R), "core: zero-length segment outside body is not occluded", null);
            }

            // ---- SignalMath: Choose ordering ----
            Check(SignalMath.Choose(true, true, true, true, true) == SignalSource.Override, "core: choose override first", null);
            Check(SignalMath.Choose(false, true, true, true, true) == SignalSource.CommNet, "core: choose CommNet before RemoteTech", null);
            Check(SignalMath.Choose(false, true, false, true, true) == SignalSource.RemoteTech, "core: CommNet not ok falls to RemoteTech", null);
            Check(SignalMath.Choose(false, true, false, false, true) == SignalSource.Distance, "core: CommNet on but unreadable falls to distance", null);
            Check(SignalMath.Choose(false, false, false, false, true) == SignalSource.None, "core: CommNet off means full signal everywhere, no distance fallback", null);
            Check(SignalMath.Choose(false, false, false, true, true) == SignalSource.RemoteTech, "core: CommNet off with RemoteTech uses RemoteTech", null);
            Check(SignalMath.Choose(false, true, false, false, false) == SignalSource.None, "core: choose none when nothing valid", null);

            // ---- SignalMath: StaticLevel ----
            Near(SignalMath.StaticLevel(1, 0.5), 0.0, 1e-9, "core: static 0 at full signal");
            Near(SignalMath.StaticLevel(0, 0.5), 0.5, 1e-9, "core: static max at no signal");
            Near(SignalMath.StaticLevel(0.5, 0.5), 0.25, 1e-9, "core: static halfway");
            Near(SignalMath.StaticLevel(2, 0.5), 0.0, 1e-9, "core: static clamps signal above 1");
            Near(SignalMath.StaticLevel(-1, 0.5), 0.5, 1e-9, "core: static clamps signal below 0");

            // ---- RadioProfiles ----
            {
                RadioProfile st = RadioProfiles.Get(RadioContext.Station);
                RadioProfile ev = RadioProfiles.Get(RadioContext.Eva);
                RadioProfile sh = RadioProfiles.Get(RadioContext.Ship);
                Check(st.HighPassHz == 350f && st.LowPassHz == 2800f && st.Distortion == 0.30f && st.BedClip == null, "core: station profile values", null);
                Check(sh.HighPassHz == 300f && sh.LowPassHz == 3600f && sh.Distortion == 0.20f && sh.BedClip == null, "core: ship profile values", null);
                Check(ev.HighPassHz == 400f && ev.LowPassHz == 2600f && ev.Distortion == 0.25f && ev.BedClip == "static/breath_loop" && ev.BedGain == 0.6f, "core: EVA profile values", null);
                Check(RadioProfiles.Select(false, false, "auto") == sh, "core: select ship by default", null);
                Check(RadioProfiles.Select(true, false, "auto") == st, "core: select station", null);
                Check(RadioProfiles.Select(false, true, "auto") == ev, "core: select EVA", null);
                Check(RadioProfiles.Select(true, true, "auto") == st, "core: station beats EVA", null);
                Check(RadioProfiles.Select(true, false, "eva") == ev, "core: forced profile beats station", null);
                Check(RadioProfiles.Select(false, false, "Station") == st, "core: forced profile is case-insensitive", null);
                Check(RadioProfiles.Select(false, false, "bogus") == sh && RadioProfiles.Select(false, true, null) == ev, "core: unknown or null forced value is auto", null);
            }

            // ---- DescentSequence: ordering ----
            {
                DescentSequence ds = new DescentSequence(6f);
                Check(ds.Update(0.5f, false, 0, 0, false) == DescentPhase.Idle, "core: descent starts idle", null);
                Check(!ds.Touchdown(0f), "core: touchdown ignored while idle", null);
                Check(ds.Update(0.5f, true, 3000, -50, false) == DescentPhase.Descent && ds.BlipsActive, "core: idle to descent when descending", null);
                Check(ds.Update(0.5f, true, 0, -2, true) == DescentPhase.Touchdown && !ds.BlipsActive, "core: landing gives touchdown and stops blips", null);
                DescentPhase p = DescentPhase.Touchdown;
                for (int i = 0; i < 11; i++) p = ds.Update(0.5f, false, 0, 0, false);
                Check(p == DescentPhase.Touchdown, "core: touchdown still open just before 6 s", p.ToString());
                p = ds.Update(0.5f, false, 0, 0, false);
                Check(p == DescentPhase.Done, "core: touchdown to done after 6 s", p.ToString());
            }

            // ---- DescentSequence: debounce ----
            {
                DescentSequence ds = new DescentSequence(6f);
                ds.Update(0.5f, true, 3000, -50, false);
                Check(ds.Update(0.5f, true, 0, -2, true) == DescentPhase.Touchdown, "core: debounce first touchdown accepted", null);
                ds.Update(0.5f, true, 500, -5, false);
                Check(ds.Phase == DescentPhase.Descent, "core: bounce back to descent", ds.Phase.ToString());
                Check(ds.Update(0.5f, true, 0, -1, true) == DescentPhase.Touchdown && !ds.JustAccepted, "core: second touchdown within 6 s ends the descent quietly", null);
                Check(!ds.Touchdown(1f), "core: explicit touchdown within 6 s is debounced", null);
                // Standing still after the bounce: no second touchdown, also after the debounce window
                bool again = false;
                for (int i = 0; i < 20; i++) { ds.Update(0.5f, false, 0, 0, true); if (ds.JustAccepted) again = true; }
                Check(!again && ds.Phase == DescentPhase.Done, "core: no second touchdown while standing still after a bounce", ds.Phase.ToString());
                // a new descent later is accepted again
                ds.Update(0.5f, true, 500, -5, false);
                ds.Update(0.5f, true, 0, -1, true);
                Check(ds.JustAccepted, "core: a new descent after the debounce gives a new touchdown", null);
            }

            // ---- ThrottleWatch: full, cut and minimum gap ----
            {
                ThrottleWatch tw = new ThrottleWatch();
                Check(Feed(tw, 0.1f, 10, 0f).Count == 0, "core: throttle low at start gives no event", null);
                List<string> full1 = Feed(tw, 0.1f, 10, 1f);
                Check(Same(full1, "full"), "core: full throttle fires once after hold", full1.Count.ToString());
                List<string> cut1 = Feed(tw, 0.1f, 10, 0f);
                Check(Same(cut1, "cut"), "core: throttle cutoff fires once after hold", cut1.Count.ToString());
                List<string> blocked = Feed(tw, 0.1f, 10, 1f);
                Check(blocked.Count == 0, "core: full within min gap is dropped", blocked.Count.ToString());
                Feed(tw, 0.1f, 250, 0f);
                List<string> later = Feed(tw, 0.1f, 10, 1f);
                Check(Same(later, "full"), "core: full accepted again after min gap", later.Count.ToString());
                List<string> notAirborne = new List<string>();
                for (int i = 0; i < 20; i++) { string e = tw.Update(0.1f, 0f, false); if (e != null) notAirborne.Add(e); }
                Check(notAirborne.Count == 0, "core: no events when not airborne", null);
            }
            {
                // KSP puts a vessel on the pad at exactly 50 % throttle. 50 % to 100 % must give "full".
                ThrottleWatch tw = new ThrottleWatch();
                Feed(tw, 0.1f, 10, 0.5f);
                List<string> pad = Feed(tw, 0.1f, 10, 1f);
                Check(Same(pad, "full"), "core: full throttle from the 50 % pad default", pad.Count.ToString());
            }

            // ---- Audit: ring buffer ----
            {
                Check(Audit.Last == null && Audit.Recent.Length == 0, "core: audit empty at start", null);
                for (int i = 1; i <= 20; i++) Audit.Add("e" + i);
                Check(Audit.Last == "e20", "core: audit last is newest", Audit.Last);
                string[] rec = Audit.Recent;
                Check(rec.Length == 16 && rec[0] == "e20" && rec[15] == "e5", "core: audit keeps 16 newest, newest first", rec.Length.ToString());
            }
        }
    }

    // ==== stress and crew model (Rover, Eva, DockImpulse, launch cap, gender pitch) ====
    static class StressCrewTests
    {
        static readonly Band[] AllBands = { Band.Calm, Band.Tense, Band.Panic };

        public static void Run()
        {
            Tuning t = new Tuning();

            // ---- component indices and names ----
            Check(StressModel.Count == 10 && StressModel.Names.Length == 10, "v2 stress: Count 10 and 10 names", null);
            Check(StressModel.Spin == 0 && StressModel.Impulse == 7 && StressModel.Rover == 8 && StressModel.Eva == 9
                  && StressModel.Names[StressModel.Rover] == "rover" && StressModel.Names[StressModel.Eva] == "eva",
                  "v2 stress: old indices kept, Rover 8, Eva 9", null);

            // ---- rover ----
            Near(StressModel.RoverComponent(5f, 0f, 0f, 0f, t), 0, 1e-6, "rover: flat road at 5 m/s gives 0");
            Near(StressModel.RoverComponent(25f, 3f, 0f, 0f, t), 1, 1e-6, "rover: 25 m/s over rough ground (roughness 3) gives 1");
            Near(StressModel.RoverComponent(25f, 0.5f, 0f, 0f, t), 0, 1e-6, "rover: speed alone on smooth ground gives 0");
            Near(StressModel.RoverComponent(0f, 0f, 1.5f, 0f, t), 1, 1e-6, "rover: air time 1.5 s gives 1");
            Near(StressModel.RoverComponent(0f, 0f, 0.9f, 0f, t), 0.5, 1e-6, "rover: air time 0.9 s gives half");
            Near(StressModel.RoverComponent(0f, 0f, 0f, 45f, t), 1, 1e-6, "rover: tilt 45 deg gives 1");
            FlightSample rv = Calm(); rv.rover = true; rv.srfSpeed = 25; rv.roverRough = 3f;
            StressModel rm = Fresh(); Tests.Run(rm, rv, 1f, 0.1f);
            Near(rm.comp[StressModel.Rover], 1, 1e-6, "rover: Tick sets the Rover component for a crewed rover");
            StressModel rm2 = Fresh(); Tests.Run(rm2, Calm(), 1f, 0.1f);
            Check(rm2.comp[StressModel.Rover] == 0f && rm2.comp[StressModel.Eva] == 0f, "rover and eva: zero for a plain ship", null);

            // ---- docking impulse ----
            Near(StressModel.DockImpulse(0.2f, 0.2f, 0.5f, 0.35f), 0, 1e-6, "dock impulse: soft capture at 0.2 m/s gives 0");
            Near(StressModel.DockImpulse(0.1f, 0.2f, 0.5f, 0.35f), 0, 1e-6, "dock impulse: slower than soft gives 0");
            Near(StressModel.DockImpulse(0.5f, 0.2f, 0.5f, 0.35f), 0.35, 1e-6, "dock impulse: hard dock at 0.5 m/s gives the full impulse");
            Near(StressModel.DockImpulse(3f, 0.2f, 0.5f, 0.35f), 0.35, 1e-6, "dock impulse: faster stays at the full impulse");
            Near(StressModel.DockImpulse(0.35f, 0.2f, 0.5f, 0.35f), 0.175, 1e-5, "dock impulse: halfway gives half");
            StressModel dm = Fresh();
            dm.QuietMotion(2f);
            dm.AddImpulse(StressModel.DockImpulse(0.1f, 0.2f, 0.5f, 0.35f), false);
            Check(dm.impulse == 0f, "dock impulse: soft capture inside the quiet window adds nothing", null);
            dm.AddImpulse(StressModel.DockImpulse(0.5f, 0.2f, 0.5f, 0.35f), true);
            Check(dm.impulse > 0.34f, "dock impulse: hard dock inside the quiet window is critical and counts", "impulse " + dm.impulse.ToString("0.000"));

            // ---- EVA ----
            FlightSample jet = Calm();
            jet.eva = true; jet.omega = 1.0; jet.evaThrusting = true; jet.fuelFrac = 1; jet.evaPropFrac = 1; jet.evaEcFrac = 1; jet.crewDist = 50;
            StressModel jm = Fresh(); Tests.Run(jm, jet, 5f, 0.1f);
            Check(jm.weight[StressModel.Spin] == 0.3f, "eva: spin weight is evaSpinWeight (0.3) on EVA", null);
            Check(jm.raw < 0.2f && jm.comp[StressModel.Eva] == 0f, "eva: normal jetpack spin at 1 rad/s stays under 0.2", "raw " + jm.raw.ToString("0.000"));
            FlightSample low = Calm();
            low.eva = true; low.omega = 0.2; low.evaThrusting = true; low.fuelFrac = 0.05; low.evaPropFrac = 0.05; low.evaEcFrac = 1; low.crewDist = 50;
            StressModel lm = Fresh(); Tests.Run(lm, low, 2f, 0.1f);
            Check(lm.comp[StressModel.Eva] > 0.5f, "eva: low jetpack propellant while thrusting gives an EVA component above 0.5", "comp " + lm.comp[StressModel.Eva].ToString("0.000"));
            Check(lm.comp[StressModel.Fuel] == 0f, "eva: Fuel component stays 0 on EVA (no double count)", null);
            low.evaThrusting = false;
            StressModel lm2 = Fresh(); Tests.Run(lm2, low, 2f, 0.1f);
            Check(lm2.comp[StressModel.Eva] == 0f, "eva: low propellant but not thrusting gives 0", null);
            FlightSample rag = Calm();
            rag.eva = true; rag.evaRagdoll = true; rag.omega = 4.0; rag.g = 6; rag.evaPropFrac = 1; rag.evaEcFrac = 1; rag.crewDist = 50;
            StressModel rgm = Fresh(); Tests.Run(rgm, rag, 3f, 0.1f);
            Check(rgm.weight[StressModel.Spin] == 0f && rgm.weight[StressModel.GForce] == 0f && rgm.raw < 0.05f,
                  "eva: ragdoll tumble does not count as spin or G", "raw " + rgm.raw.ToString("0.000"));
            Check(jm.weight[StressModel.GForce] < t.wG, "eva: G weight is damped on EVA", null);
            FlightSample fall = Calm();
            fall.eva = true; fall.landed = true; fall.omega = 4.0; fall.g = 6; fall.evaPropFrac = 1; fall.evaEcFrac = 1; fall.crewDist = 50;
            StressModel fm = Fresh(); Tests.Run(fm, fall, 3f, 0.1f);
            Check(fm.weight[StressModel.Spin] == 0f && fm.raw < 0.05f, "eva: tumbling on the ground does not count as spin", "raw " + fm.raw.ToString("0.000"));
            StressModel sm = Fresh(); Tests.Run(sm, rag, 1f, 0.1f);
            FlightSample up = rag; up.evaRagdoll = false;
            Tests.Run(sm, up, 1f, 0.1f);
            Check(sm.weight[StressModel.Spin] == 0f, "eva: right after a ragdoll (settle time) spin still counts as nothing", null);
            Tests.Run(sm, up, 3f, 0.1f);
            Check(sm.weight[StressModel.Spin] == 0.3f, "eva: after the settle time the EVA spin weight is back", null);
            FlightSample hop = Calm();
            hop.eva = true; hop.evaPropFrac = 1; hop.evaEcFrac = 1; hop.crewDist = 50;
            StressModel hm = Fresh(); Tests.Run(hm, hop, 6f, 0.1f); hm.AddImpulse(0.5f, true); Tests.Run(hm, hop, 0.2f, 0.1f);
            Check(hm.comp[StressModel.Impulse] == 0f, "eva: an impulse never counts on EVA (nothing hit the kerbal)", null);
            Check(StressModel.EvaComponent(0.05, false, 1, double.PositiveInfinity, 0.15f, 0.1f, 200f) == 0f,
                  "eva: low propellant without thrust gives 0 (direct)", null);
            Near(StressModel.EvaComponent(1, false, 0.05, 500, 0.15f, 0.1f, 200f), 0.5, 1e-6, "eva: low EC with no crewed vessel within 200 m gives 0.5");
            Check(StressModel.EvaComponent(1, false, 0.05, 100, 0.15f, 0.1f, 200f) == 0f, "eva: low EC but a crewed vessel at 100 m gives 0", null);
            Check(StressModel.EvaComponent(1, false, 0.05, double.PositiveInfinity, 0.15f, 0.1f, 200f) > 0f,
                  "eva: low EC with no other crewed vessel at all counts as far", null);
            Near(FuelMath.EvaFraction(50, 100), 0.5, 1e-9, "eva fuel: mono 50 of 100 gives 0.5");
            Near(FuelMath.EvaFraction(0, 0), 1, 1e-9, "eva fuel: no mono max gives 1 (no stress)");
            Near(FuelMath.EvaFraction(200, 100), 1, 1e-9, "eva fuel: clamped to 1");

            // ---- launch cap ----
            Check(KerbalVoice.LaunchMaxBand(true, 5000, 10000, false) == Band.Tense, "launch cap: Tense below 10 km with no failure", null);
            Check(KerbalVoice.LaunchMaxBand(true, 5000, 10000, true) == Band.Panic, "launch cap: a real failure lifts the cap", null);
            Check(KerbalVoice.LaunchMaxBand(true, 12000, 10000, false) == Band.Panic, "launch cap: above 10 km there is no cap", null);
            Check(KerbalVoice.LaunchMaxBand(false, 5000, 10000, false) == Band.Panic, "launch cap: off means no cap", null);
            bool capHolds = true, unchanged = true;
            foreach (Band start in AllBands)
            {
                Band b = start;
                for (int i = 0; i <= 200; i++)
                {
                    float sk = i / 200f;
                    b = KerbalVoice.NextBand(b, sk, 0.3f, 0.68f, Band.Tense);
                    if (b == Band.Panic) capHolds = false;
                }
                for (int i = 0; i <= 100; i++)
                {
                    float sk = i / 100f;
                    if (KerbalVoice.NextBand(start, sk, 0.3f, 0.68f, Band.Panic) != KerbalVoice.NextBand(start, sk, 0.3f, 0.68f)) unchanged = false;
                }
            }
            Check(capHolds, "launch cap: never Panic under a Tense cap, even at sk 1 going up and down", null);
            Check(unchanged, "launch cap: maxBand Panic gives the same band as the plain NextBand", null);
            Check(KerbalVoice.NextBand(Band.Calm, 0.99f, 0.3f, 0.68f, Band.Panic) == Band.Panic, "launch cap: no cap, sk 0.99 gives Panic", null);
            Check(StressModel.RealFailure(0.92f, 0.92f, false, 0f), "failure: heat at heatHi is real", null);
            Check(!StressModel.RealFailure(0.91, 0.92f, false, 1.9f), "failure: heat just below heatHi and loss of control under 2 s is not", null);
            Check(StressModel.RealFailure(0.1, 0.92f, true, 0f), "failure: an engine flameout is real", null);
            Check(StressModel.RealFailure(0.1, 0.92f, false, 2f), "failure: loss of control for 2 s is real", null);
            StressModel lc = Fresh();
            FlightSample spin = Calm(); spin.omega = 4.5;
            Tests.Run(lc, spin, 1.5f, 0.1f);
            Check(!StressModel.RealFailure(0, 0.92f, false, lc.lossOfControlTime) && lc.lossOfControlTime > 1.4f,
                  "loss of control: 1.5 s at 4.5 rad/s is not yet a failure", "time " + lc.lossOfControlTime.ToString("0.00"));
            Tests.Run(lc, spin, 1f, 0.1f);
            Check(StressModel.RealFailure(0, 0.92f, false, lc.lossOfControlTime), "loss of control: 2.5 s at 4.5 rad/s is a failure", "time " + lc.lossOfControlTime.ToString("0.00"));

            // ---- gender pitch and cooldown key ----
            KerbalVoice fem = KerbalVoice.Create("Valentina Kerman", 0.5f, 0.5f, true, true);
            KerbalVoice mal = KerbalVoice.Create("Bill Kerman", 0.5f, 0.5f, false, false);
            KerbalVoice val4 = KerbalVoice.Create("Valentina Kerman", 0.5f, 0.5f, true);
            KerbalVoice bill4 = KerbalVoice.Create("Bill Kerman", 0.5f, 0.5f, false);
            Near(fem.basePitch, val4.basePitch * 1.06, 1e-5, "gender: female pitch is base x 1.06");
            Near(mal.basePitch, bill4.basePitch * 0.96, 1e-5, "gender: male pitch is base x 0.96");
            Check(fem.isFemale && !mal.isFemale && !val4.isFemale, "gender: flag set for female only", null);
            Check(fem.tremRate == val4.tremRate && fem.timbre == val4.timbre && fem.rngSeed == val4.rngSeed,
                  "gender: hash profile (tremolo, timbre, seed) unchanged by gender", null);
            Near(KerbalVoice.Create("Valentina Kerman", 0.5f, 0.5f, true, true, 1.2f, 0.8f).basePitch, val4.basePitch * 1.2, 1e-5, "gender: custom female factor is applied");
            Check(KerbalVoice.Create("Bob Kerman", 0.5f, 0.5f, false).basePitch == KerbalVoice.Create("Bob Kerman", 0.5f, 0.5f, false, false, 1f, 1f).basePitch,
                  "gender: factors 1 give the hash pitch", null);
            Check(bill4.CooldownKey == "sk:Bill Kerman", "cooldown key: sk:<name>", bill4.CooldownKey);
        }
    }

    // ==== persona, phrase table columns, event watchers, faces, subtitle layout, new settings ====
    static class V4Tests
    {
        static string Short(List<string> items, int max)
        {
            if (items.Count == 0) return "none";
            List<string> head = items.Count > max ? items.GetRange(0, max) : items;
            return string.Join(", ", head.ToArray()) + (items.Count > max ? " ... (" + items.Count + " in total)" : "");
        }

        public static void Run()
        {
            PersonaTests();
            TableTests();
            WatcherTests();
            FaceTests();
            SubtitleTests();
            SettingsTests();
        }

        static void PersonaTests()
        {
            Persona p = new Persona(0.65f, 0.6f, true, true, "Scientist");
            Check(p.badass && p.brave && p.dim && p.veteran && !p.nervous, "persona: flags at the edges (courage 0.65 brave, stupidity 0.6 dim)", p.ToString());
            Check(new Persona(0.35f, 0.59f, false, false, null).nervous && !new Persona(0.351f, 0.5f, false, false, null).nervous,
                  "persona: nervous at courage <= 0.35 only", null);
            Check(!new Persona(0.649f, 0.599f, false, false, null).brave && !new Persona(0.649f, 0.599f, false, false, null).dim,
                  "persona: just below brave and dim gives neither", null);
            Check(!Persona.None.AnyFlag && Persona.None.trait == "any" && Persona.None.FlagText() == "none", "persona: None has no flags and trait any", null);
            Check(new Persona(2f, -1f, false, false, null).courage == 1f && new Persona(2f, -1f, false, false, null).stupidity == 0f,
                  "persona: stats clamped to 0..1", null);
            Check(p.trait == "scientist" && Persona.TraitKey("Tourist") == "tourist" && Persona.TraitKey(" pilot ") == "pilot"
                  && Persona.TraitKey("ENGINEER") == "engineer" && Persona.TraitKey("Xyz") == "any" && Persona.TraitKey(null) == "any"
                  && Persona.TraitKey("") == "any", "persona: TraitKey maps the KSP trait, unknown or empty gives any", null);
            Check(p.Matches("any") && p.Matches("") && p.Matches(null) && p.Matches(" Badass ") && p.Matches("veteran")
                  && !p.Matches("nervous") && !p.Matches("foo"), "persona: Matches (any and empty match, unknown never)", null);
            Check(p.TraitMatches("scientist") && p.TraitMatches("any") && p.TraitMatches(null) && p.TraitMatches("Scientist") && !p.TraitMatches("pilot"),
                  "persona: TraitMatches", null);
            Check(!new Persona(0.5f, 0.5f, false, false, "Unknown").TraitMatches("pilot") && new Persona(0.5f, 0.5f, false, false, "Unknown").TraitMatches("any"),
                  "persona: an unknown trait matches only any rows", null);
            Near(new Persona(0f, 1f, false, false, null).IntercomWeight, 1.4, 1e-5, "persona: IntercomWeight 1 + 0.4 x stupidity (stupidity 1)");
            Near(new Persona(0f, 0f, false, false, null).IntercomWeight, 1.0, 1e-5, "persona: IntercomWeight 1 at stupidity 0");
            Near(Persona.VeteranPanicAt(0.8f, true), 0.84, 1e-5, "persona: veteran panicAt +0.04");
            Near(Persona.VeteranExponent(1.2f, true), 1.08, 1e-5, "persona: veteran exponent x0.9");
            Check(Persona.VeteranPanicAt(0.8f, false) == 0.8f && Persona.VeteranExponent(1.2f, false) == 1.2f, "persona: no tweak for a non-veteran", null);

            KerbalVoice k = KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, false);
            float pa = k.panicAt, e = k.exponent;
            k.ApplyPersona(new Persona(0.5f, 0.5f, false, true, "Pilot"), true);
            k.ApplyPersona(k.persona, true);
            k.ApplyPersona(k.persona, true);
            Check(Math.Abs(k.panicAt - (pa + 0.04f)) < 1e-5 && Math.Abs(k.exponent - e * 0.9f) < 1e-5,
                  "persona: veteran tweak applied once, ApplyPersona three times does not stack", k.panicAt.ToString("0.####") + " " + k.exponent.ToString("0.####"));
            k.ApplyPersona(k.persona, false);
            Check(k.panicAt == pa && k.exponent == e, "persona: personalityEffects off restores the stock panicAt and exponent", null);
            k.ApplyPersona(null, true);
            Check(k.persona == Persona.None && k.panicAt == pa, "persona: ApplyPersona(null) gives Persona.None", null);
            KerbalVoice dimK = KerbalVoice.Create("Bill Kerman", 0.5f, 1f, false);
            dimK.ApplyPersona(new Persona(0.5f, 1f, false, false, "Engineer"), true);
            Near(dimK.IntercomWeight(true), 1.4, 1e-5, "persona: KerbalVoice.IntercomWeight with effects on");
            Check(dimK.IntercomWeight(false) == 1f, "persona: KerbalVoice.IntercomWeight is 1 with effects off", null);

            // trait pick
            KerbalVoice jeb = KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, false);
            jeb.ApplyPersona(new Persona(0.5f, 0.5f, false, false, "Pilot"), true);
            KerbalVoice bob = KerbalVoice.Create("Bob Kerman", 0.5f, 0.5f, false);
            bob.ApplyPersona(new Persona(0.5f, 0.5f, false, false, "Scientist"), true);
            KerbalVoice bill = KerbalVoice.Create("Bill Kerman", 0.5f, 0.5f, false);
            bill.ApplyPersona(new Persona(0.5f, 0.5f, false, false, "Engineer"), true);
            List<KerbalVoice> crew = new List<KerbalVoice> { jeb, bob, bill };
            Random rng = new Random(1);
            bool allBob = true;
            for (int i = 0; i < 200; i++) if (KerbalVoice.PickWithTrait(crew, "scientist", null, rng, null) != bob) allBob = false;
            Check(allBob, "roster: PickWithTrait scientist picks only the scientist (200 picks)", null);
            Dictionary<string, int> seen = new Dictionary<string, int>();
            for (int i = 0; i < 300; i++)
            {
                KerbalVoice v = KerbalVoice.PickWithTrait(crew, "any", null, rng, null);
                string n = v == null ? "null" : v.name;
                seen[n] = seen.ContainsKey(n) ? seen[n] + 1 : 1;
            }
            Check(seen.Count == 3 && !seen.ContainsKey("null"), "roster: PickWithTrait any picks everyone", seen.Count + " names");
            Check(KerbalVoice.PickWithTrait(crew, "tourist", null, rng, null) == null, "roster: PickWithTrait gives null when nobody has the trait", null);
            Check(KerbalVoice.PickWithTrait(crew, "pilot", null, rng, v => v != jeb) == null && KerbalVoice.PickWithTrait(crew, "any", null, rng, v => v == bill) == bill,
                  "roster: PickWithTrait applies the extra predicate", null);
            bob.outDueToG = true;
            Check(KerbalVoice.PickWithTrait(crew, "scientist", null, rng, null) == null, "roster: a scientist out due to G is not picked", null);
            bool neverBob = true;
            for (int i = 0; i < 200; i++) if (KerbalVoice.PickWithTrait(crew, "any", null, rng, null) == bob) neverBob = false;
            Check(neverBob, "roster: an unconscious kerbal is never picked for any", null);
            Check(KerbalVoice.PickWithTrait(null, "any", null, rng, null) == null && KerbalVoice.PickWithTrait(new List<KerbalVoice>(), "any", null, rng, null) == null,
                  "roster: PickWithTrait on a null or empty list gives null", null);
        }

        static void TableTests()
        {
            string head9 = "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tspanish";
            List<string> e = new List<string>();
            PhraseTable t = PhraseTable.Parse(new List<string> { head9, "a_1\tchatter\tchatter\tcalm\tcrew\ta\t1\tboth\tHola" }, e);
            PhraseEntry a = t.Get("a_1");
            Check(e.Count == 0 && a != null && a.Trait == "any" && a.Persona == "any" && a.Topic == "any",
                  "v4 table: without the new columns trait, persona and topic are any", null);
            Check(t.Scenes.Count == 1 && t.Scenes[0].Trait == "any" && t.Scenes[0].Persona == "any" && t.Scenes[0].Topic == "any",
                  "v4 table: scene tags default to any", null);

            string head = "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\ttrait\tpersona\ttopic\tspanish\tenglish";
            List<string> good = new List<string> {
                head,
                "e_1\tchatter\tchatter\tcalm\tcrew\te\t1\tboth\t\t\t\tHola\tHi",
                "s_1\tscience\tchatter\tcalm\tcrew\ts\t1\tboth\tscientist\tany\tmysterygoo\tGoo\tGoo",
                "s_2\tscience\tchatter\tcalm\tmc\ts\t2\tm\tscientist\tany\tmysterygoo\tBien\tGood",
                "sa_1\tscience\tchatter\tcalm\tcrew\tsa\t1\tboth\tany\tany\tany\tCiencia\tScience",
                "h_1\tsoi_enter\tchatter\tcalm\tmc\th\t1\tm\tany\tany\thome\tCasa\tHome",
                "b_1\tchatter\tchatter\ttense\tcrew\tb\t1\tboth\tany\tbadass\tany\tVamos\tLet's go",
                "p_1\tpart_lost\twarning\ttense\tcrew\tp\t1\tboth\tany\tany\tany\t¡Boom!\tSomething blew up!",
                "l_1\tchatter\tchatter\tcalm\tcrew\tl\t1\tboth\tany\tany\tany\tDespegue\tReady for blast off" };
            List<string> eg = new List<string>();
            PhraseTable tg = PhraseTable.Parse(good, eg);
            Check(eg.Count == 0 && tg.Count == 8, "v4 table: good rows with trait, persona and topic parse", Short(eg, 3));
            Check(tg.Get("e_1") != null && tg.Get("e_1").Trait == "any" && tg.Get("e_1").Persona == "any" && tg.Get("e_1").Topic == "any",
                  "v4 table: empty trait, persona and topic cells are any", null);
            PhraseScene ss = tg.Scenes.Find(x => x.Id == "s");
            Check(ss != null && ss.Trait == "scientist" && ss.Topic == "mysterygoo" && ss.Persona == "any", "v4 table: scene carries trait and topic", null);
            Check(!PhraseTable.HasExplosionWord("Ready for blast off") && !PhraseTable.HasExplosionWord("Let's explore")
                  && PhraseTable.HasExplosionWord("Something blew up!") && PhraseTable.HasExplosionWord("Explosion")
                  && PhraseTable.HasExplosionWord("¡Ha estallado!") && PhraseTable.HasExplosionWord("explotó") && PhraseTable.HasExplosionWord("BOOM")
                  && !PhraseTable.HasExplosionWord(null), "v4 table: HasExplosionWord (blast off and explore are fine)", null);

            string[] bad = {
                "x_1\tchatter\tchatter\tcalm\tcrew\tx1\t1\tboth\twizard\tany\tany\tHola\tHi",          // unknown trait
                "x_2\tchatter\tchatter\tcalm\tcrew\tx2\t1\tboth\tany\tgrumpy\tany\tHola\tHi",          // unknown persona
                "x_3\tchatter\tchatter\tcalm\tcrew\tx3\t1\tboth\tany\tany\tmysterygoo\tHola\tHi",      // topic on chatter
                "x_4\tsoi_enter\tchatter\tcalm\tmc\tx4\t1\tm\tany\tany\tmysterygoo\tHola\tHi",         // science topic on soi
                "x_5\tscience\tchatter\tcalm\tcrew\tx5\t1\tboth\tany\tany\thome\tHola\tHi",            // soi topic on science
                "x_6\tchatter\tchatter\ttense\tcrew\tx6\t1\tboth\tany\tany\tany\t¡Explotó!\tIt exploded!",  // explosion outside part_lost
                "s_3\tscience\tchatter\tcalm\tcrew\ts\t3\tboth\tany\tany\tmysterygoo\tMas\tMore",       // trait differs from its scene
                "s_4\tscience\tchatter\tcalm\tcrew\ts\t4\tboth\tscientist\tany\tany\tMas\tMore",       // topic differs from its scene
                "s_5\tscience\tchatter\tcalm\tcrew\ts\t5\tboth\tscientist\tbrave\tmysterygoo\tMas\tMore" }; // persona differs
            List<string> lines = new List<string>(good);
            lines.AddRange(bad);
            List<string> eb = new List<string>();
            PhraseTable tb = PhraseTable.Parse(lines, eb);
            Check(tb.Count == 8 && eb.Count == bad.Length, "v4 table: bad trait, persona, topic, explosion word and mixed scene rows are rejected",
                  eb.Count + "/" + bad.Length + " errors");

            // topic only for science and soi_enter
            Check(Array.IndexOf(PhraseTable.TopicsFor("science"), "roc") >= 0 && Array.IndexOf(PhraseTable.TopicsFor("soi_enter"), "home") >= 0
                  && PhraseTable.TopicsFor("touchdown").Length == 1 && PhraseTable.TopicsFor("part_lost")[0] == "any",
                  "v4 table: TopicsFor (science topics, soi home, solar on chatter and power, any elsewhere)", null);
            List<string> missingTopic = new List<string>();
            foreach (string k in EventWatchers.KnownExperiments) if (Array.IndexOf(PhraseTable.ScienceTopics, k) < 0) missingTopic.Add(k);
            if (Array.IndexOf(PhraseTable.ScienceTopics, "roc") < 0) missingTopic.Add("roc");
            Check(missingTopic.Count == 0 && PhraseTable.ScienceTopics.Length == EventWatchers.KnownExperiments.Length + 2,
                  "v4 table: every ExperimentId topic is a table topic (plus roc and any)", Short(missingTopic, 4));
            List<string> evMissing = new List<string>();
            foreach (string ev in new string[] { EventWatchers.SoiEnter, EventWatchers.Science, EventWatchers.PartLost, EventWatchers.PowerLow,
                                                 EventWatchers.PowerOk, EventWatchers.SignalLost, EventWatchers.SignalBack, EventWatchers.CrewLost })
                if (Array.IndexOf(PhraseTable.Events, ev) < 0) evMissing.Add(ev);
            Check(evMissing.Count == 0, "v4 table: every watcher event is a table event", Short(evMissing, 4));
            // the table traits: any, the KSP jobs and the two intercom pair traits for tourists
            Check(Array.IndexOf(PhraseTable.Traits, "any") == 0 && Persona.TraitKeys.Length == PhraseTable.Traits.Length - 3
                  && Array.IndexOf(PhraseTable.Traits, Persona.ToTourist) > 0 && Array.IndexOf(PhraseTable.Traits, Persona.TouristPair) > 0
                  && Array.TrueForAll(Persona.TraitKeys, x => Array.IndexOf(PhraseTable.Traits, x) > 0)
                  && Array.TrueForAll(Persona.PersonaKeys, x => Array.IndexOf(PhraseTable.Personas, x) > 0)
                  && Persona.PersonaKeys.Length == PhraseTable.Personas.Length - 1,
                  "v4 table: Persona trait and persona keys equal the table values", null);

            // matching
            Check(PhraseTable.TopicMatches("any", "mysterygoo") && PhraseTable.TopicMatches("mysterygoo", "mysterygoo") && PhraseTable.TopicMatches("mysterygoo", null)
                  && PhraseTable.TopicMatches("mysterygoo", "any") && !PhraseTable.TopicMatches("mysterygoo", "crewreport"),
                  "v4 table: TopicMatches loose (any on either side)", null);
            Check(PhraseTable.TopicMatchesExact("mysterygoo", "mysterygoo") && !PhraseTable.TopicMatchesExact("any", "mysterygoo")
                  && !PhraseTable.TopicMatchesExact("mysterygoo", null) && PhraseTable.TopicMatchesExact("any", null) && PhraseTable.TopicMatchesExact("", "any")
                  && !PhraseTable.TopicMatchesExact("home", "any"), "v4 table: TopicMatchesExact (null means any, any only takes any)", null);
            Check(PhraseTable.TraitMatches("any", "pilot") && PhraseTable.TraitMatches("pilot", null) && !PhraseTable.TraitMatches("pilot", "scientist")
                  && PhraseTable.PersonaMatches("badass", "badass") && !PhraseTable.PersonaMatches("badass", "dim") && PhraseTable.PersonaMatches(null, "dim"),
                  "v4 table: TraitMatches and PersonaMatches", null);
            int exact = tg.ScenesFor("science", null, null, null, null, null, "mysterygoo", true, null).Count;
            int loose = tg.ScenesFor("science", null, null, null, null, null, "mysterygoo", false, null).Count;
            int exactAny = tg.ScenesFor("science", null, null, null, null, null, null, true, null).Count;
            int pilot = tg.ScenesFor("science", null, null, null, "pilot", null, "mysterygoo", false, null).Count;
            int bad2 = tg.ScenesFor("chatter", "tense", null, null, null, "dim", null, false, null).Count;
            int bad3 = tg.ScenesFor("chatter", "tense", null, null, null, "badass", null, false, null).Count;
            Check(exact == 1 && loose == 2 && exactAny == 1 && pilot == 1 && bad2 == 0 && bad3 == 1,
                  "v4 table: ScenesFor with trait, persona and topic (exact vs loose)",
                  exact + " " + loose + " " + exactAny + " " + pilot + " " + bad2 + " " + bad3);
            Check(!PhraseTable.SceneMatches(null, null, null, null, false), "v4 table: SceneMatches(null) is false", null);
        }

        static void WatcherTests()
        {
            EventRequest r;
            // ExperimentId
            object[][] ids = {
                new object[] { "crewReport@KerbinSrfLandedLaunchPad", "crewreport" }, new object[] { "evaReport@MunInSpaceLow", "evareport" },
                new object[] { "mysteryGoo@KerbinFlyingLow", "mysterygoo" }, new object[] { "surfaceSample@MunSrfLandedMidlands", "surfacesample" },
                new object[] { "mobileMaterialsLab@x", "mobilematerialslab" }, new object[] { "temperatureScan@x", "temperaturescan" },
                new object[] { "barometerScan@x", "barometerscan" }, new object[] { "seismicScan@x", "seismicscan" },
                new object[] { "gravityScan@x", "gravityscan" }, new object[] { "atmosphereAnalysis@x", "atmosphereanalysis" },
                new object[] { "asteroidSample@x", "asteroidsample" }, new object[] { "infraredTelescope@x", "infraredtelescope" },
                new object[] { "magnetometer@x", "magnetometer" }, new object[] { "ROCScience_MunStone@MunSrfLanded", "roc" },
                new object[] { "rocscience_x", "roc" }, new object[] { "cometSample_short@KerbolInSpaceHigh", "cometsample" },
                new object[] { "cometSample_long@x", "cometsample" }, new object[] { "cometSample@x", "cometsample" },
                new object[] { "deployedSeismicSensor@x", "other" }, new object[] { "@Kerbin", "other" }, new object[] { "crewReport", "crewreport" },
                new object[] { " CREWREPORT @x", "crewreport" }, new object[] { "", "other" }, new object[] { null, "other" } };
            List<string> wrong = new List<string>();
            foreach (object[] c in ids)
            {
                string got = EventWatchers.ExperimentId((string)c[0]);
                if (got != (string)c[1]) wrong.Add((c[0] ?? "null") + " -> " + got);
            }
            Check(wrong.Count == 0, "events: ExperimentId (" + ids.Length + " cases: stock ids, ROC, comet variants, unknown, null)", Short(wrong, 5));

            // SOI
            SoiWatcher so = new SoiWatcher();
            Check(so.OnSoiChange(0, "Mun", "mun", false, 60, 600, out r) && r.ev == "soi_enter" && r.topic == "any" && r.body == "mun"
                  && r.band == "calm" && !r.crewOnly && r.maxAge == 600f && r.priority == EventPriority.Soi, "events: SOI enter Mun", r.ToString());
            Check(!so.OnSoiChange(30, "Mun", "mun", false, 60, 600, out r), "events: SOI same body within soiCooldown is a duplicate", null);
            Check(so.OnSoiChange(40, "Kerbin", "kerbin", true, 60, 600, out r) && r.topic == "home" && r.body == "kerbin", "events: SOI home body gives topic home", null);
            Check(so.OnSoiChange(50, "Mun", "mun", false, 60, 600, out r), "events: SOI a different body always fires", null);
            Check(so.OnSoiChange(111, "Mun", "mun", false, 60, 600, out r), "events: SOI same body after the cooldown fires", null);
            Check(!so.OnSoiChange(200, null, "x", false, 60, 600, out r) && !so.OnSoiChange(200, "", "x", false, 60, 600, out r), "events: SOI without a body is ignored", null);
            so.Reset();
            Check(so.OnSoiChange(112, "Mun", "mun", false, 60, 600, out r), "events: SOI Reset forgets the last body", null);
            Check(so.OnSoiChange(500, "Jool", "jool", false, 60, -5, out r) && r.maxAge == 0f, "events: negative maxAge clamped to 0", null);

            // science
            ScienceWatcher sc = new ScienceWatcher();
            Check(sc.OnExperiment(0, "crewReport@a", "kerbin", 30, 300, 600, out r) && r.ev == "science" && r.topic == "crewreport" && r.body == "kerbin"
                  && r.priority == EventPriority.Science, "events: science crew report", r.ToString());
            Check(!sc.OnExperiment(10, "mysteryGoo@a", "kerbin", 30, 60, 600, out r) && sc.QueuedCount == 1, "events: science during the overall cooldown is queued", null);
            Check(!sc.OnExperiment(12, "mysteryGoo@b", "kerbin", 30, 60, 600, out r) && sc.QueuedCount == 1, "events: science topic already queued is not queued twice", null);
            Check(!sc.Tick(20, 30, out r), "events: science queue waits for the overall cooldown", null);
            Check(sc.Tick(31, 30, out r) && r.topic == "mysterygoo" && r.body == "kerbin" && sc.QueuedCount == 0, "events: science queue releases after the overall cooldown", null);
            Check(!sc.OnExperiment(40, "crewReport@b", "mun", 0, 60, 600, out r) && sc.QueuedCount == 0, "events: science per-topic cooldown (60 s)", null);
            Check(sc.OnExperiment(70, "crewReport@b", "mun", 30, 60, 600, out r), "events: science topic again after scienceTopicCooldown", null);
            // a burst of experiments: one now, the rest one per cooldown, at most MaxQueued wait
            ScienceWatcher bu = new ScienceWatcher();
            string[] sciIds = { "crewReport@x", "temperatureScan@x", "barometerScan@x", "mysteryGoo@x", "mobileMaterialsLab@x" };
            int now0 = 0, fired = 0; bool firstNow = bu.OnExperiment(0, sciIds[0], "kerbin", 30, 60, 600, out r);
            for (int k = 1; k < sciIds.Length; k++) bu.OnExperiment(k, sciIds[k], "kerbin", 30, 60, 600, out r);
            Check(firstNow && bu.QueuedCount == ScienceWatcher.MaxQueued, "events: science burst plays one and queues up to MaxQueued", bu.QueuedCount.ToString());
            for (int s2 = 1; s2 <= 200; s2++) if (bu.Tick(now0 + s2, 30, out r)) fired++;
            Check(fired == ScienceWatcher.MaxQueued, "events: science burst queue plays one per 30 s", fired.ToString());
            ScienceWatcher ol = new ScienceWatcher(); ol.OnExperiment(0, "crewReport@x", "kerbin", 1000, 60, 600, out r); ol.OnExperiment(1, "mysteryGoo@x", "kerbin", 1000, 60, 600, out r);
            Check(!ol.Tick(200, 1000, out r) && ol.QueuedCount == 0, "events: science queued too long is dropped", null);
            Check(sc.OnExperiment(400, "foo@b", "kerbin", 30, 300, 600, out r) && r.topic == "any", "events: unknown experiment uses the any rows", null);
            Check(!sc.OnExperiment(450, "bar@b", "kerbin", 0, 300, 600, out r), "events: unknown experiments share the topic cooldown", null);
            Check(sc.OnExperiment(460, null, "kerbin", 0, 0, 600, out r) && r.topic == "any", "events: null subject gives topic any", null);
            sc.Reset();
            Check(sc.OnExperiment(461, "mysteryGoo@a", "kerbin", 30, 300, 600, out r), "events: science Reset clears the cooldowns", null);

            // part loss
            PartLossWatcher pl = new PartLossWatcher();
            pl.OnPartLost(0, 1);
            Check(!pl.Tick(1, 1.5, 20, false, out r), "events: part loss waits for the window", null);
            pl.OnPartLost(1, 1);
            Check(pl.Tick(1.6, 1.5, 20, false, out r) && r.ev == "part_lost" && r.band == "tense" && pl.lastCount == 2 && r.priority == EventPriority.PartLost
                  && r.maxAge == EventWatchers.PartLostMaxAge, "events: two parts in the window give one tense call", r.ToString());
            Check(!pl.Tick(1.7, 1.5, 20, false, out r), "events: part loss fires once", null);
            pl.OnPartLost(5, 3);
            Check(pl.pending == 0 && !pl.Tick(7, 1.5, 20, false, out r), "events: parts lost in the cooldown are ignored", null);
            pl.OnPartLost(30, 2); pl.OnPartLost(30.5, 1);
            Check(pl.Tick(31.5, 1.5, 20, false, out r) && r.band == "panic" && pl.lastCount == 3, "events: 3 parts in the window give panic", null);
            pl.OnPartLost(60, 1);
            Check(pl.Tick(62, 1.5, 20, true, out r) && r.band == "panic", "events: one part while the crew panics gives panic", null);
            pl.OnPartLost(100, 0); pl.OnPartLost(100, -2);
            Check(pl.pending == 0, "events: zero or negative part counts are ignored", null);
            pl.OnPartLost(100, 1);
            Check(!pl.Tick(101.4, 1.5, 20, false, out r) && pl.Tick(101.5, 1.5, 20, false, out r), "events: the window starts at the first loss", null);

            // power
            PowerWatcher pw = new PowerWatcher();
            Check(!pw.Update(0.05, 3, false, .1, .25, 5, out r) && !pw.low, "events: power below powerMinMax capacity is ignored", null);
            Check(!pw.Update(0.05, 0, false, .1, 0.25, 0, out r), "events: power with zero capacity is ignored", null);
            Check(!pw.Update(double.NaN, 100, false, .1, .25, 5, out r) && !pw.Update(0.05, double.NaN, false, .1, .25, 5, out r), "events: power NaN is ignored", null);
            Check(pw.Update(0.05, 100, false, .1, .25, 5, out r) && r.ev == "power_low" && r.band == "tense" && r.priority == EventPriority.Power,
                  "events: power_low below powerLowFrac (first reading counts)", null);
            Check(!pw.Update(0.04, 100, false, .1, .25, 5, out r), "events: power_low once", null);
            Check(!pw.Update(0.2, 100, false, .1, .25, 5, out r) && !pw.Update(0.25, 100, false, .1, .25, 5, out r), "events: power hysteresis (no ok at 0.2 or 0.25)", null);
            Check(!pw.Update(0.9, 100, true, .1, .25, 5, out r) && pw.low, "events: power on EVA fires nothing and keeps the state", null);
            Check(pw.Update(0.3, 100, false, .1, .25, 5, out r) && r.ev == "power_ok" && r.band == "calm", "events: power_ok above powerOkFrac", null);
            Check(!pw.Update(0.3, 100, false, .1, .25, 5, out r) && !pw.Update(0.0, 100, true, .1, .25, 5, out r) && !pw.low, "events: power_ok once, EVA no low", null);
            int flips = 0;
            PowerWatcher pb = new PowerWatcher();
            for (int i = 0; i < 40; i++) if (pb.Update(i % 2 == 0 ? 0.09 : 0.15, 100, false, 0.2, 0.1, 5, out r)) flips++;
            Check(flips == 1, "events: power okFrac below lowFrac (broken setting) does not flap between them", flips + " events");

            // signal
            SignalWatcher sg = new SignalWatcher();
            Check(!sg.Update(0, false, true, 3, 45, out r) && !sg.hasState, "events: signal first reading during plasma is skipped", null);
            Check(!sg.Update(0, true, false, 3, 45, out r) && sg.hasState && sg.connected, "events: signal first reading sets the state silently", null);
            Check(!sg.Update(1, false, false, 3, 45, out r) && !sg.Update(3.9, false, false, 3, 45, out r), "events: signal loss must hold signalHold", null);
            Check(sg.Update(4, false, false, 3, 45, out r) && r.ev == "signal_lost" && r.crewOnly && r.band == "tense" && r.priority == EventPriority.Signal,
                  "events: signal_lost after the hold, crew only", r.ToString());
            Check(!sg.Update(5, true, false, 3, 45, out r) && sg.Update(8, true, false, 3, 45, out r) && r.ev == "signal_back" && !r.crewOnly,
                  "events: signal_back after an announced loss", null);
            Check(!sg.Update(9, false, false, 3, 45, out r) && !sg.Update(12, false, false, 3, 45, out r) && !sg.connected,
                  "events: signal_lost within signalCooldown is silent", null);
            Check(!sg.Update(13, true, false, 3, 45, out r) && !sg.Update(17, true, false, 3, 45, out r) && sg.connected,
                  "events: signal_back without an announced loss is silent (pairs match)", null);
            Check(!sg.Update(100, false, true, 3, 45, out r) && !sg.Update(110, false, true, 3, 45, out r) && sg.connected,
                  "events: signal loss during plasma blackout is skipped", null);
            Check(!sg.Update(111, false, false, 3, 45, out r) && sg.Update(114, false, false, 3, 45, out r), "events: the hold restarts after plasma", null);
            Check(!sg.Update(115, true, false, 3, 45, out r) && !sg.Update(116, false, false, 3, 45, out r) && !sg.Update(117.5, true, false, 3, 45, out r)
                  && !sg.Update(120, true, false, 3, 45, out r) && sg.Update(120.5, true, false, 3, 45, out r) && r.ev == "signal_back",
                  "events: a flicker restarts the hold", null);
            sg.Reset();
            Check(!sg.hasState && !sg.announcedLost && sg.connected, "events: signal Reset", null);

            // crew loss
            CrewLossWatcher cl = new CrewLossWatcher();
            cl.Know("Jebediah Kerman"); cl.Know("Bill Kerman"); cl.Know(null); cl.Know("");
            Check(cl.KnownCount == 2, "events: crew Know ignores empty names", cl.KnownCount.ToString());
            Check(!cl.OnLost(0, "Valentina Kerman") && !cl.OnLost(0, null), "events: crew loss of an unknown kerbal is ignored", null);
            Check(cl.OnLost(0, "Jebediah Kerman") && !cl.Tick(1, 3, 600, out r), "events: crew loss waits crewLostDelay", null);
            Check(cl.OnLost(2, "Bill Kerman") && !cl.OnLost(2.5, "Jebediah Kerman"), "events: second loss joins, a duplicate is refused", null);
            Check(cl.Tick(3, 3, 600, out r) && cl.lastNames.Count == 2 && r.ev == "crew_lost" && r.band == "tense" && r.priority == EventPriority.CrewLost
                  && r.maxAge == 600f && cl.pending.Count == 0, "events: one merged crew_lost call for two kerbals", r.ToString());
            Check(!cl.Tick(10, 3, 600, out r) && !cl.OnLost(10, "Bill Kerman"), "events: each kerbal reported once", null);
            cl.Reset();
            Check(cl.KnownCount == 0 && !cl.OnLost(11, "Bill Kerman"), "events: crew Reset forgets the flight", null);
            // DeepFreeze: a kerbal that is back (thawed) when the call is due is dropped and can be lost later
            cl.Know("Bill Kerman"); cl.Know("Jebediah Kerman");
            cl.OnLost(20, "Bill Kerman"); cl.OnLost(20, "Jebediah Kerman");
            Check(cl.Tick(24, 3, 600, n => n == "Jebediah Kerman", out r) && cl.lastNames.Count == 1 && cl.lastNames[0] == "Jebediah Kerman",
                  "events: crew_lost names only the kerbals still lost", string.Join(", ", cl.lastNames.ToArray()));
            Check(cl.OnLost(30, "Bill Kerman") && !cl.Tick(34, 3, 600, n => false, out r) && cl.pending.Count == 0,
                  "events: a dropped kerbal can be reported again, nobody still lost gives no call", null);

            // priorities
            Check(EventPriority.CrewLost > EventPriority.PartLost && EventPriority.PartLost > EventPriority.Power && EventPriority.Power > EventPriority.Signal
                  && EventPriority.Signal > EventPriority.Science && EventPriority.Science > EventPriority.Soi, "events: priority order of the plan", null);
        }

        static void FaceTests()
        {
            Check(FaceMath.Mood("panic", "chatter", "ascent", false, false) == -1f && FaceMath.Mood("panic", null, null, false, true) == FaceMath.MoodTense,
                  "face: panic is full fear, badass panic only tense", null);
            Check(FaceMath.Mood("tense", "chatter", "ascent", false, false) == -0.5f && FaceMath.Mood("tense", null, null, false, true) == 0.4f,
                  "face: tense -0.5, badass tense +0.4", null);
            Check(FaceMath.Mood("calm", "chatter", "ascent", false, false) == 0.8f && FaceMath.Mood("calm", "staging", "orbit", false, false) == 0.8f
                  && FaceMath.Mood("calm", "staging", "launch", true, false) == 0.8f && FaceMath.Mood("calm", "relief", null, false, false) == 0.8f,
                  "face: calm chatter, goofy phase, intercom and relief grin +0.8", null);
            Check(FaceMath.Mood("calm", "staging", "launch", false, false) == FaceMath.MoodCalm && FaceMath.Mood("none", "staging", "ascent", false, true) == 0.4f
                  && FaceMath.Mood(null, "touchdown", null, false, false) == FaceMath.MoodCalm, "face: plain calm callout a hint of a smile, badass +0.4", null);
            Check(FaceMath.IsGoofyPhase("landed") && !FaceMath.IsGoofyPhase("reentry")
                  && Array.TrueForAll(PhraseTable.Phases, ph => FaceMath.IsGoofyPhase(ph) == PhraseTable.IsGoofyPhase(ph)),
                  "face: IsGoofyPhase equals PhraseTable.IsGoofyPhase", null);
            Check(FaceMath.BandName(0) == "calm" && FaceMath.BandName(1) == "tense" && FaceMath.BandName(2) == "panic" && FaceMath.BandName(7) == "panic"
                  && FaceMath.BandName((int)Band.Panic) == "panic" && FaceMath.BandName((int)Band.Tense) == "tense", "face: BandName follows the Band enum", null);
            Check(FaceMath.Stronger(0.4f, -0.8f) == -0.8f && FaceMath.Stronger(-0.5f, 0.5f) == -0.5f, "face: Stronger by magnitude", null);

            Check(Math.Abs(FaceMath.Blend(0.3f, -1f, 0f, 1f) - 0.3f) < 1e-5f && Math.Abs(FaceMath.Blend(0.3f, -1f, 1f, 1f) + 1f) < 1e-5f
                  && Math.Abs(FaceMath.Blend(0.3f, -1f, 1f, -1f) - 1f) < 1e-5f && Math.Abs(FaceMath.Blend(0.3f, 0.8f, 5f, 1f) - 0.8f) < 1e-5f,
                  "face: Blend weight 0 keeps stock, 1 gives mood, sign -1 flips, weight clamped to 1", null);
            Near(FaceMath.Blend(0f, 0.8f, 0.5f, 1f), 0.4, 1e-6, "face: Blend half way");
            bool inRange = true;
            float[] vals = { -5f, -1f, -0.3f, 0f, 0.7f, 1f, 3f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
            foreach (float s in vals) foreach (float m in vals) foreach (float w in vals) foreach (float sign in new float[] { -1f, 1f })
            {
                float b = FaceMath.Blend(s, m, w, sign);
                if (float.IsNaN(b) || b < -1f || b > 1f) inRange = false;
            }
            Check(inRange, "face: Blend stays in -1..1 for any input (NaN and Infinity included)", null);
            float wr = 0f;
            for (int i = 0; i < 10; i++) wr = FaceMath.Ramp(wr, true, 0.02f, 0.2f, 0.4f);
            Near(wr, 1.0, 1e-5, "face: Ramp rises to 1 in the rise time");
            wr = FaceMath.Ramp(wr, false, 0.1f, 0.2f, 0.4f);
            Near(wr, 0.75, 1e-5, "face: Ramp falls at 1/fall per second");
            Check(FaceMath.Ramp(0.5f, true, 1f, 0f, 0f) == 1f && FaceMath.Ramp(0.5f, false, 10f, 0.2f, 0.4f) == 0f && FaceMath.Ramp(float.NaN, true, float.NaN, 1f, 1f) == 0f,
                  "face: Ramp limits 0..1, zero time jumps, NaN safe", null);
            Check(FaceMath.Smooth01(-1f) == 0f && FaceMath.Smooth01(2f) == 1f && FaceMath.Smooth01(0.5f) == 0.5f && FaceMath.Smooth01(float.NaN) == 0f, "face: Smooth01", null);
            float f = 0f;
            for (int i = 0; i < 200; i++) f = FaceMath.Follow(f, 20f, 0.02f, 4f);
            Check(Math.Abs(f - 20f) < 0.01f && FaceMath.Follow(5f, 20f, 0.02f, 0f) == 5f && FaceMath.Follow(5f, float.NaN, 0.02f, 4f) == 5f
                  && FaceMath.Follow(5f, 20f, -1f, 4f) == 5f, "face: Follow converges, rate 0, NaN target or negative dt hold", f.ToString("0.###"));
            Near(FaceMath.Follow(0f, 1f, 0.25f, 4f), 1 - Math.Exp(-1), 1e-5, "face: Follow one time constant");
            Check(FaceMath.WrapDeg(190f) == -170f && FaceMath.WrapDeg(-190f) == 170f && FaceMath.WrapDeg(720f) == 0f && FaceMath.WrapDeg(float.NaN) == 0f,
                  "face: WrapDeg to -180..180", null);
            Check(FaceMath.ClampAngle(40f, 25f) == 25f && FaceMath.ClampAngle(-40f, 25f) == -25f && FaceMath.ClampAngle(200f, 25f) == -25f
                  && FaceMath.ClampAngle(10f, 25f) == 10f && FaceMath.ClampAngle(150f, 500f) == 90f && FaceMath.ClampAngle(10f, -5f) == 0f
                  && FaceMath.ClampAngle(10f, float.NaN) == 0f, "face: ClampAngle limits to +-max, max clamped to 0..90", null);
            Near(FaceMath.SignedAngleAround(0, 1, 0, 0, 0, 1, 1, 0, 0), 90, 1e-3, "face: SignedAngleAround +Z to +X around +Y is +90 (Rodrigues)");
            Near(FaceMath.SignedAngleAround(0, 1, 0, 0, 0, 1, -1, 0, 0), -90, 1e-3, "face: SignedAngleAround +Z to -X around +Y is -90");
            Near(FaceMath.SignedAngleAround(0, 2, 0, 0, 0.3f, 1, 1, 5f, 1), 45, 1e-3, "face: SignedAngleAround ignores the axis component");
            Check(FaceMath.SignedAngleAround(0, 0, 0, 0, 0, 1, 1, 0, 0) == 0f && FaceMath.SignedAngleAround(0, 1, 0, 0, 0, 1, 0, 3, 0) == 0f
                  && FaceMath.SignedAngleAround(0, 1, 0, 0, 1, 0, 1, 0, 0) == 0f, "face: SignedAngleAround 0 for a zero axis or a target on the axis", null);
            Check(FaceMath.TurnTarget(60f, 25f, 0.5f) == 12.5f && FaceMath.TurnTarget(-10f, 25f, 1f) == -10f && FaceMath.TurnTarget(60f, 25f, 3f) == 25f,
                  "face: TurnTarget clamps, then scales (scale clamped to 0..1)", null);
            Check(FaceMath.AxisIndex(0f) == 0 && FaceMath.AxisIndex(1.4f) == 1 && FaceMath.AxisIndex(9f) == 2 && FaceMath.AxisIndex(-3f) == 0 && FaceMath.AxisIndex(float.NaN) == 1,
                  "face: AxisIndex rounds and clamps", null);
            bool testOk = true;
            for (float t = 0f; t < 10f; t += 0.05f)
            {
                float m = FaceMath.TestMood(t), y = FaceMath.TestYaw(t, 25f);
                if (m < -1f || m > 1f || y < -25f || y > 25f) testOk = false;
            }
            Check(testOk && Math.Abs(FaceMath.TestMood(1f) - 1f) < 1e-5, "face: test mood and test yaw stay in range", null);
        }

        static string HudName(int i) { return new string[] { "navball", "portraits", "staging", "time", "altimeter" }[i]; }

        static void SubtitleTests()
        {
            // clearance of the HUD for every preset, screen and scale
            int[][] screens = { new int[] { 1280, 720 }, new int[] { 1920, 1080 }, new int[] { 3840, 2160 } };
            float[] scales = { 0.8f, 1.0f, 1.2f, 1.5f };
            List<string> hits = new List<string>();
            List<string> narrow = new List<string>();
            List<string> oneOnly = new List<string>();
            int cases = 0;
            float maxH = SubtitleLayout.EntryHeight(true, 11f * 1.6f + 3f, 2f * (15f * 1.6f + 4f));
            foreach (int[] sc in screens)
                foreach (float ui in scales)
                    foreach (float nav in scales)
                        foreach (string pos in new string[] { SubtitleLayout.AboveNavball, SubtitleLayout.TopLeft, SubtitleLayout.BottomLeft })
                        {
                            cases++;
                            float W = sc[0], H = sc[1];
                            HudScales s = new HudScales(ui, nav, 1f, 1f, 1f);
                            SubColumn c = SubtitleLayout.Column(pos, W, H, s, 0.5f, 0.78f);
                            SubRect band = c.growUp ? new SubRect(c.x, c.minY, c.w, c.anchorY - c.minY) : new SubRect(c.x, c.anchorY, c.w, c.maxY - c.anchorY);
                            SubRect[] hud = { SubtitleLayout.Navball(W, H, s), SubtitleLayout.Portraits(W, H, s), SubtitleLayout.Staging(W, H, s),
                                              SubtitleLayout.TimeWarp(W, H, s), SubtitleLayout.Altimeter(W, H, s) };
                            string id = pos + " " + W + "x" + H + " ui " + ui + " nav " + nav;
                            if (!band.InsideScreen(W, H)) hits.Add(id + " off screen " + band);
                            for (int i = 0; i < hud.Length; i++) if (band.Overlaps(hud[i])) hits.Add(id + " hits " + HudName(i) + " " + band + " vs " + hud[i]);
                            if (c.w < SubtitleLayout.MinWidth) narrow.Add(id + " w " + c.w.ToString("0"));
                            // two entries of two lines at the largest size fit and stay in the band
                            float[] hs = { maxH, maxH };
                            SubRect[] rects = new SubRect[2];
                            int first = SubtitleLayout.Stack(c, hs, 2, rects);
                            for (int i = first; i < 2; i++)
                            {
                                if (!rects[i].InsideScreen(W, H)) hits.Add(id + " entry " + i + " off screen");
                                foreach (SubRect h in hud) if (rects[i].Overlaps(h)) { hits.Add(id + " entry " + i + " hits HUD " + rects[i]); break; }
                            }
                            if (first > 1 || rects[1].h < maxH - 0.01f) narrow.Add(id + " newest entry not placed in full");
                            if (first != 0) oneOnly.Add(W + "x" + H + " ui " + ui + " nav " + nav + " " + pos);
                            if (pos == SubtitleLayout.AboveNavball && ui * nav <= 1.5f * 1.5f + 1e-3f)
                            {
                                float want = H - 200f * ui * nav - 12f;
                                if (Math.Abs(c.anchorY - want) > 0.5f && c.anchorY > want) hits.Add(id + " anchor " + c.anchorY + " below " + want);
                            }
                        }
            Check(hits.Count == 0, "subtitle: column and 2 stacked entries clear navball, portraits, staging, time and altimeter (" + cases + " cases, 1280x720 to 3840x2160, UI and navball 0.8 to 1.5)", Short(hits, 4));
            Check(narrow.Count == 0, "subtitle: every column is at least " + SubtitleLayout.MinWidth + " px wide and holds the newest entry at size 1.6", Short(narrow, 4));
            // informational: where two 2-line entries at size 1.6 do not fit, Stack drops the older one
            Check(true, "subtitle: cases where only 1 entry of 2 lines at size 1.6 fits (the older one is dropped)", oneOnly.Count + " of " + cases + ": " + Short(oneOnly, 4));
            {
                SubColumn c = SubtitleLayout.Column(SubtitleLayout.AboveNavball, 1920, 1080, HudScales.One, 0.5f, 0.78f);
                SubColumn cc = SubtitleLayout.Column(SubtitleLayout.Custom, 1920, 1080, HudScales.One, 0.5f, 0.78f);
                Check(Math.Abs(c.anchorY - 1080 * 0.78f) < 0.01f && Math.Abs(c.w - 640f) < 0.01f && Math.Abs(c.x + c.w * 0.5f - 960f) < 0.01f && c.growUp
                      && Math.Abs(c.anchorY - cc.anchorY) < 0.01f && Math.Abs(c.x - cc.x) < 0.01f,
                      "subtitle: above_navball at 1920x1080 sits where custom X 0.5 / Y 0.78 sits (640 wide, centred, bottom edge at 0.78 H)", c.x + " " + c.w + " " + c.anchorY);
                SubColumn c2 = SubtitleLayout.Column(SubtitleLayout.AboveNavball, 1280, 720, new HudScales(1.2f, 1.1f, 1f, 1f, 1f), 0.5f, 0.78f);
                Check(Math.Abs(c2.anchorY - (720 - 200 * 1.2f * 1.1f - 12)) < 0.01f, "subtitle: above_navball stays above a large navball (UI_SCALE x UI_SCALE_NAVBALL)", c2.anchorY.ToString("0.#"));
            }
            {
                SubColumn cu = SubtitleLayout.Column(SubtitleLayout.Custom, 1920, 1080, HudScales.One, 0.5f, 0.78f);
                SubColumn cl = SubtitleLayout.Column(SubtitleLayout.Custom, 1920, 1080, HudScales.One, -3f, 9f);
                Check(Math.Abs(cu.anchorY - 1080 * 0.78f) < 0.01f && Math.Abs(cu.x + cu.w * 0.5f - 960f) < 0.01f
                      && cl.x >= SubtitleLayout.Margin && cl.anchorY <= 1080 - SubtitleLayout.Margin,
                      "subtitle: custom position from subtitleX/Y, clamped on screen", null);
                SubColumn bad = SubtitleLayout.Column("bogus", float.NaN, 0f, new HudScales(float.NaN, 0f, -1f, 99f, 1f), float.NaN, float.NaN);
                Check(!float.IsNaN(bad.x) && !float.IsNaN(bad.anchorY) && bad.w >= 1f, "subtitle: Column survives NaN and zero input", null);
                // subtitleWidth: a factor on the preset width, never past the HUD, the position stays
                bool widthOk = true;
                string widthBad = null;
                string[] wpos = { SubtitleLayout.AboveNavball, SubtitleLayout.Custom, SubtitleLayout.TopLeft, SubtitleLayout.BottomLeft };
                for (int pi = 0; pi < wpos.Length; pi++)
                {
                    SubColumn w1 = SubtitleLayout.Column(wpos[pi], 1920, 1080, HudScales.One, 0.5f, 0.78f, 1f);
                    SubColumn w0 = SubtitleLayout.Column(wpos[pi], 1920, 1080, HudScales.One, 0.5f, 0.78f);
                    SubColumn wn = SubtitleLayout.Column(wpos[pi], 1920, 1080, HudScales.One, 0.5f, 0.78f, 0.5f);
                    SubColumn ww = SubtitleLayout.Column(wpos[pi], 1920, 1080, HudScales.One, 0.5f, 0.78f, 1.6f);
                    SubColumn wx = SubtitleLayout.Column(wpos[pi], 1920, 1080, HudScales.One, 0.5f, 0.78f, 99f);
                    bool ok = Math.Abs(w1.w - w0.w) < 0.01f && Math.Abs(w1.x - w0.x) < 0.01f
                              && wn.w < w1.w - 1f && ww.w >= w1.w - 0.01f && Math.Abs(wx.w - ww.w) < 0.01f
                              && Math.Abs(wn.anchorY - w1.anchorY) < 0.01f && Math.Abs(ww.anchorY - w1.anchorY) < 0.01f
                              && ww.x >= 0f && ww.x + ww.w <= 1920f;
                    if (!ok) { widthOk = false; widthBad = wpos[pi] + ": " + wn.w + " / " + w1.w + " / " + ww.w; }
                }
                Check(widthOk, "subtitle: width scale 0.5 makes every preset narrower, 1.6 wider or the same, same anchor, clamped to 1.6", widthBad);
                SubColumn wc = SubtitleLayout.Column(SubtitleLayout.Custom, 1920, 1080, HudScales.One, 0.5f, 0.78f, 1.6f);
                SubColumn wa = SubtitleLayout.Column(SubtitleLayout.AboveNavball, 1920, 1080, HudScales.One, 0.5f, 0.78f, 1.6f);
                Check(Math.Abs(wc.w - 1024f) < 0.01f && Math.Abs(wc.x + wc.w * 0.5f - 960f) < 0.01f && wa.w <= 1920f - 2f * SubtitleLayout.Margin,
                      "subtitle: custom at 1.6 is 1024 px wide and centred, above_navball stays on screen", wc.w + " " + wa.w);
                SubColumn wNaN = SubtitleLayout.Column(SubtitleLayout.Custom, 1920, 1080, HudScales.One, 0.5f, 0.78f, float.NaN);
                Check(Math.Abs(wNaN.w - 640f) < 0.01f, "subtitle: width scale NaN counts as 1", null);
                HudScales hs = new HudScales(float.NaN, 0f, -1f, 99f, 0.1f).Sanitized();
                Check(hs.ui == 1f && hs.navball == 1f && hs.staging == 1f && hs.time == 3f && hs.crew == 0.3f, "subtitle: HudScales.Sanitized", null);
            }
            {
                // stacking: newest at the bottom, the oldest is dropped when it does not fit
                SubColumn c = new SubColumn { x = 10, w = 300, anchorY = 500, minY = 400, maxY = 500, growUp = true };
                float[] hs = { 40, 40, 40 };
                SubRect[] rects = new SubRect[3];
                int first = SubtitleLayout.Stack(c, hs, 3, rects);
                Check(first == 1 && rects[2].Bottom == 500f && rects[1].Bottom == 500f - 40f - SubtitleLayout.Gap, "subtitle: Stack newest at the bottom, oldest dropped when there is no room",
                      "first " + first);
                SubColumn d = new SubColumn { x = 10, w = 300, anchorY = 100, minY = 100, maxY = 120, growUp = false };
                Check(SubtitleLayout.Stack(d, new float[] { 50, 50 }, 2, new SubRect[2]) == 1, "subtitle: Stack always places the newest entry", null);
                Check(SubtitleLayout.Stack(d, hs, 0, rects) == 0, "subtitle: Stack with no entries", null);
            }
            {
                // Ellipsize with a fake measure: 8 px per char, 300 px wide, 20 px per line
                Func<string, float> h = s => (float)Math.Ceiling(Math.Max(1, s.Length) * 8f / 300f) * 20f;
                string shortT = "Copy that.";
                string longT = "Control, we have a small problem with the fuel lines and the left booster is making a very strange noise right now, please advise us soon.";
                string el = SubtitleLayout.Ellipsize(longT, h, 40f);
                Check(SubtitleLayout.Ellipsize(shortT, h, 40f) == shortT, "subtitle: Ellipsize leaves fitting text alone", null);
                Check(el.EndsWith("...") && h(el) <= 40f && el.Length > 40 && longT.StartsWith(el.Substring(0, el.Length - 3).TrimEnd())
                      && el[el.Length - 4] != ' ', "subtitle: Ellipsize cuts long text to 2 lines with ...", el);
                string cut = el.Substring(0, el.Length - 3);
                Check(longT.Length > cut.Length && (longT[cut.Length] == ' ' || longT[cut.Length] == ','), "subtitle: Ellipsize cuts at a word", cut);
                Check(SubtitleLayout.Ellipsize(null, h, 40f) == "" && SubtitleLayout.Ellipsize("abc", null, 40f) == "abc", "subtitle: Ellipsize null safe", null);
            }
            {
                int[] counts = new int[SubtitleLayout.Palette.Length];
                string[] first = { "Jebediah", "Bill", "Bob", "Valentina", "Jenmy", "Kirrim", "Dunford", "Tomgel", "Lodan", "Herbie", "Sigdred", "Mitzon",
                                   "Ludwig", "Gemory", "Samkin", "Wernher", "Gene", "Linus", "Mortimer", "Walt", "Bartbert", "Corgan", "Adeny", "Haldun", "Erlin" };
                int n = 0;
                foreach (string a in first) foreach (string b in new string[] { "Kerman", "Kermin", "Kerbal", "Kermy" }) { counts[SubtitleLayout.ColorIndex(a + " " + b)]++; n++; }
                int min = int.MaxValue, max = 0;
                foreach (int c in counts) { min = Math.Min(min, c); max = Math.Max(max, c); }
                Check(min > 0 && max <= 2.2 * n / counts.Length, "subtitle: ColorIndex spreads " + n + " names over all 8 colours",
                      string.Join(",", Array.ConvertAll(counts, x => x.ToString())));
                Check(SubtitleLayout.ColorIndex(" Jebediah Kerman ") == SubtitleLayout.ColorIndex("jebediah kerman") && SubtitleLayout.ColorIndex(null) == SubtitleLayout.ColorIndex(""),
                      "subtitle: ColorIndex ignores case and spaces, null safe", null);
                Check(SubtitleLayout.SpeakerColor("MC", true) == SubtitleLayout.McColor && SubtitleLayout.SpeakerColor("Mission Control", true) == SubtitleLayout.McColor
                      && SubtitleLayout.SpeakerColor(" capcom ", true) == SubtitleLayout.McColor && SubtitleLayout.SpeakerColor("MC", false) == SubtitleLayout.PlainColor
                      && SubtitleLayout.SpeakerColor("Bob Kerman", false) == SubtitleLayout.PlainColor
                      && SubtitleLayout.SpeakerColor("Bob Kerman", true) == SubtitleLayout.Palette[SubtitleLayout.ColorIndex("Bob Kerman")],
                      "subtitle: MC amber, crew palette, PlainColor with colours off", null);
                double minDist = 9;
                foreach (float[] p in SubtitleLayout.Palette)
                {
                    double d = Math.Sqrt(Math.Pow(p[0] - SubtitleLayout.McColor[0], 2) + Math.Pow(p[1] - SubtitleLayout.McColor[1], 2) + Math.Pow(p[2] - SubtitleLayout.McColor[2], 2));
                    minDist = Math.Min(minDist, d);
                }
                Check(minDist > 0.25, "subtitle: no palette colour close to the MC amber", "min RGB distance " + minDist.ToString("0.###"));
                Check(SubtitleLayout.SpeakerLabel("MC") == "Mission Control" && SubtitleLayout.SpeakerLabel(" Bob Kerman ") == "Bob Kerman" && SubtitleLayout.SpeakerLabel(null) == null,
                      "subtitle: SpeakerLabel", null);
            }
            {
                Check(SubtitleLayout.Alpha(10f, 10f, 13f) == 0f && Math.Abs(SubtitleLayout.Alpha(10.075f, 10f, 13f) - 0.5f) < 1e-3f && SubtitleLayout.Alpha(11.5f, 10f, 13f) == 1f
                      && Math.Abs(SubtitleLayout.Alpha(12.8f, 10f, 13f) - 0.5f) < 1e-3f && SubtitleLayout.Alpha(13f, 10f, 13f) == 0f && SubtitleLayout.Alpha(9f, 10f, 13f) == 0f
                      && SubtitleLayout.Alpha(float.NaN, 10f, 13f) == 0f, "subtitle: Alpha fades in 0.15 s and out 0.4 s", null);
                bool alphaRange = true;
                for (float now = 9f; now < 11f; now += 0.01f) { float a = SubtitleLayout.Alpha(now, 10f, 10.3f); if (a < 0f || a > 1f) alphaRange = false; }
                Check(alphaRange, "subtitle: Alpha in 0..1 for a very short entry", null);
                Check(SubtitleLayout.Duration(2f) == 2.6f && SubtitleLayout.Duration(0.1f) == 1.2f && SubtitleLayout.Duration(float.NaN) == 1.2f, "subtitle: Duration line + 0.6 s, min 1.2 s", null);
                Check(SubtitleLayout.Duration(2f, 3f) == 5f && SubtitleLayout.Duration(2f, 0f) == 2f && SubtitleLayout.Duration(0.1f, 0f) == 1.2f
                      && SubtitleLayout.Duration(2f, 99f) == 2f + SubtitleLayout.MaxHold && SubtitleLayout.Duration(2f, float.NaN) == 2.6f,
                      "subtitle: the hold setting adds its seconds after the line, 0 to 10 s, at least 1.2 s on screen", null);
                var sh = new Settings { subtitleHold = 25f };
                sh.Clamp();
                var sh2 = new Settings { subtitleHold = float.NaN };
                sh2.Clamp();
                Check(sh.subtitleHold == SubtitleLayout.MaxHold && sh2.subtitleHold == SubtitleLayout.DefaultHold && new Settings().subtitleHold == 0.6f,
                      "subtitle: the hold setting is clamped, the default keeps the old 0.6 s", sh.subtitleHold + " " + sh2.subtitleHold);
                Check(SubtitleLayout.FontSize(1f) == 15 && SubtitleLayout.FontSize(5f) == 24 && SubtitleLayout.FontSize(0.1f) == (int)Math.Round(15f * 0.7f) && SubtitleLayout.LabelFontSize(0.7f) >= 9,
                      "subtitle: font 15 px x size, size clamped 0.7..1.6", null);
            }
            {
                string p = SubtitleLayout.AboveNavball;
                List<string> seen = new List<string>();
                for (int i = 0; i < 4; i++) { seen.Add(p); p = SubtitleLayout.NextPreset(p); }
                Check(p == SubtitleLayout.AboveNavball && seen.Count == 4 && new HashSet<string>(seen).Count == 4, "subtitle: NextPreset cycles through all 4 presets", string.Join(",", seen.ToArray()));
                Check(SubtitleLayout.NextPreset("bogus") == SubtitleLayout.AboveNavball && SubtitleLayout.NextPreset(null) == SubtitleLayout.AboveNavball,
                      "subtitle: NextPreset of an unknown value gives the first preset", null);
                string[] ps = SubtitleLayout.Presets;
                Check(SubtitleLayout.StepPreset(ps[0], -1) == ps[ps.Length - 1] && SubtitleLayout.StepPreset(ps[ps.Length - 1], 1) == ps[0]
                      && SubtitleLayout.StepPreset(ps[1], -1) == ps[0] && SubtitleLayout.StepPreset(ps[0], 1) == SubtitleLayout.NextPreset(ps[0])
                      && SubtitleLayout.StepPreset("bogus", -1) == ps[ps.Length - 1] && SubtitleLayout.StepPreset(null, 1) == ps[0],
                      "subtitle: StepPreset goes forward and back, wrapping both ways", null);
                bool same = Settings.SubtitlePresets.Length == SubtitleLayout.Presets.Length;
                for (int i = 0; same && i < SubtitleLayout.Presets.Length; i++) if (Settings.SubtitlePresets[i] != SubtitleLayout.Presets[i]) same = false;
                Check(same && SubtitleLayout.IsPreset("custom") && !SubtitleLayout.IsPreset("Custom"), "subtitle: Settings and SubtitleLayout use the same preset strings", null);
                Check(SubtitleLayout.RoundedAlpha(0, 0, 16, 6f) < 0.1f && SubtitleLayout.RoundedAlpha(8, 8, 16, 6f) == 1f && SubtitleLayout.RoundedAlpha(8, 0, 16, 6f) == 1f,
                      "subtitle: rounded panel corners transparent, edges and centre opaque", null);
            }
        }

        static void SettingsTests()
        {
            {
                // EVA breathing speeds up gradually while running
                Check(DcMath.EvaRunLevel(0.5f, 1f, 3f) == 0f && Math.Abs(DcMath.EvaRunLevel(2f, 1f, 3f) - 0.5f) < 1e-5f && DcMath.EvaRunLevel(9f, 1f, 3f) == 1f
                      && DcMath.EvaRunLevel(float.NaN, 1f, 3f) == 0f && DcMath.EvaRunLevel(2.5f, 0f, 0f) == 1f,
                      "eva breath: run level 0 at walk speed, 1 at run speed, fallback speeds", null);
                float x = 0f;
                for (int i = 0; i < 100; i++) x = DcMath.Exertion(x, 1f, 0.1f, 20f, 30f); // 10 s of running
                float up10 = x;
                for (int i = 0; i < 500; i++) x = DcMath.Exertion(x, 1f, 0.1f, 20f, 30f); // 60 s in total
                float up60 = x;
                for (int i = 0; i < 300; i++) x = DcMath.Exertion(x, 0f, 0.1f, 20f, 30f); // 30 s standing
                Check(up10 > 0.3f && up10 < 0.5f && up60 > 0.9f && x > 0.3f && x < up60 * 0.45f && DcMath.Exertion(float.NaN, 1f, 0f, 20f, 30f) == 0f,
                      "eva breath: exertion builds over about 20 s of running and fades over about 30 s", up10.ToString("0.00") + " " + up60.ToString("0.00") + " " + x.ToString("0.00"));
                Check(Array.IndexOf(PhraseTable.TopicsFor("chatter"), PhraseTable.SolarTopic) >= 0 && Array.IndexOf(PhraseTable.TopicsFor("power_low"), PhraseTable.SolarTopic) >= 0
                      && Array.IndexOf(PhraseTable.TopicsFor("touchdown"), PhraseTable.SolarTopic) < 0,
                      "solar: chatter and power scenes may carry the solar topic", null);
            }
            Settings d = new Settings();
            Tuning t = d.tuning;
            Check(d.personalityEffects && d.faceExpressions && d.headTurn && d.subtitleSpeaker && d.subtitleColors && d.eventSoi && d.eventScience
                  && d.eventPartLoss && d.eventPower && d.eventSignal && d.eventCrewLoss, "settings v4: new bools default true", null);
            Check(d.subtitlePos == "above_navball" && d.subtitleX == 0.5f && d.subtitleY == 0.78f && d.subtitleSize == 1f && d.subtitleOpacity == 0.55f
                  && d.subtitleMaxEntries == 2, "settings v4: subtitle defaults", null);
            Check(t.traitPreference == 0.35f && t.personaPreference == 0.4f && t.powerLowFrac == 0.10f && t.powerOkFrac == 0.25f && t.powerMinMax == 5f
                  && t.scienceCooldown == 30f && t.scienceTopicCooldown == 60f && t.soiCooldown == 60f && t.partLostWindow == 1.5f && t.partLostCooldown == 20f
                  && t.signalHold == 3f && t.signalCooldown == 45f && t.crewLostDelay == 3f && t.somberTime == 180f && t.eventMaxAge == 600f
                  && t.headTurnMaxDeg == 25f && t.headTurnAxis == 1f && t.headTurnInvert == 0f && t.headTurnSpeed == 4f && t.faceWeight == 0.8f
                  && t.faceExpressionSign == 1f, "settings v4: TUNING defaults of the plan", null);
            Tuning t0 = new Tuning(); t0.Sanitize();
            Check(t0.traitPreference == 0.35f && t0.powerOkFrac == 0.25f && t0.eventMaxAge == 600f && t0.faceExpressionSign == 1f,
                  "settings v4: Sanitize keeps the defaults", null);

            Settings s = new Settings();
            s.subtitlePos = " TOP_LEFT ";
            s.Clamp();
            Check(s.subtitlePos == "top_left", "settings v4: subtitlePos trimmed and lower-cased", s.subtitlePos);
            s.subtitlePos = "middle"; s.Clamp();
            Check(s.subtitlePos == "above_navball", "settings v4: unknown subtitlePos falls back to above_navball", s.subtitlePos);
            s.subtitlePos = null; s.Clamp();
            Check(s.subtitlePos == "above_navball", "settings v4: null subtitlePos falls back to above_navball", null);
            {
                Settings m7 = new Settings(); m7.subtitlePos = "above_navball"; m7.MigrateValues(7);
                Settings k7 = new Settings(); k7.subtitlePos = "top_left"; k7.MigrateValues(7);
                Check(m7.subtitlePos == "above_navball" && k7.subtitlePos == "top_left", "settings v8 and v9: old above_navball ends at the new above_navball, a chosen preset stays", m7.subtitlePos + " " + k7.subtitlePos);
                Settings m8 = new Settings(); m8.subtitlePos = "custom"; m8.MigrateValues(8);
                Settings k8 = new Settings(); k8.subtitlePos = "custom"; k8.subtitleY = 0.6f; k8.MigrateValues(8);
                Settings p9 = new Settings(); p9.tuning.crewStressSemitones = 2f; p9.tuning.crewPitchMax = 1.2f; p9.MigrateValues(9);
                Settings q9 = new Settings(); q9.tuning.crewStressSemitones = 3f; q9.tuning.crewPitchMax = 1.3f; q9.MigrateValues(9);
                Check(p9.tuning.crewStressSemitones == 0.5f && p9.tuning.crewPitchMax == 1.12f && q9.tuning.crewStressSemitones == 3f && q9.tuning.crewPitchMax == 1.3f,
                      "settings v10: old stress pitch defaults move to 0.5 st and 1.12, own values stay", null);
                Check(m8.subtitlePos == "above_navball" && k8.subtitlePos == "custom", "settings v9: custom at the old default becomes above_navball, a moved custom stays", m8.subtitlePos + " " + k8.subtitlePos);
            }
            s.subtitleX = float.NaN; s.subtitleY = 7f; s.subtitleSize = 9f; s.subtitleOpacity = -1f; s.subtitleMaxEntries = 99; s.Clamp();
            Check(s.subtitleX == 0.5f && s.subtitleY == 1f && s.subtitleSize == 1.6f && s.subtitleOpacity == 0f && s.subtitleMaxEntries == 4,
                  "settings v4: subtitle numbers clamped (NaN to default)", null);
            s.subtitleSize = 0.1f; s.subtitleMaxEntries = 0; s.Clamp();
            Check(s.subtitleSize == 0.7f && s.subtitleMaxEntries == 1, "settings v4: subtitle size and entries lower limits", null);

            Tuning n = new Tuning();
            n.faceExpressionSign = -1f; n.Sanitize();
            Check(n.faceExpressionSign == -1f, "settings v4: faceExpressionSign -1 survives Sanitize", null);
            n.faceExpressionSign = -0.3f; n.Sanitize();
            Check(n.faceExpressionSign == -1f, "settings v4: a negative faceExpressionSign becomes -1", null);
            n.faceExpressionSign = float.NaN; n.Sanitize();
            Check(n.faceExpressionSign == 1f, "settings v4: NaN faceExpressionSign becomes +1", null);
            n.faceExpressionSign = 0f; n.Sanitize();
            Check(n.faceExpressionSign == 1f, "settings v4: zero faceExpressionSign becomes +1", null);
            n.headTurnMaxDeg = -5f; n.traitPreference = 3f; n.powerLowFrac = 0.5f; n.powerOkFrac = 0.2f; n.headTurnAxis = 1.7f; n.headTurnInvert = 0.6f;
            n.Sanitize();
            Check(n.headTurnMaxDeg == 25f && n.traitPreference == 1f && n.powerOkFrac > n.powerLowFrac && n.headTurnAxis == 2f && n.headTurnInvert == 1f,
                  "settings v4: negative to default, preferences clamped, power ok above low, axis rounded, invert 0 or 1", null);
            Check(Array.IndexOf(Tuning.SignedFields, "faceExpressionSign") >= 0 && Tuning.SignedFields.Length == 1, "settings v4: only faceExpressionSign may be negative", null);
        }
    }

    // ==== phrase source tables, flight phase, mouth math, new settings, body lines ====
    static class V3Tests
    {
        // Minimum counts the finished table (about 1000 source rows) must reach.
        const int MinSeriousCalmRadio = 15;   // per serious phase: calm radio chatter rows tagged with the phase or "serious"
        const int MinGoofyCalm = 25;          // per goofy phase: calm chatter rows tagged with the phase or "goofy"
        const int MinPerCause = 8;            // rows per cause (any event, any band)
        const int MinSourceRows = 900;

        sealed class SrcRow
        {
            public string File, Loc;
            public Dictionary<string, string> F = new Dictionary<string, string>();
            public string this[string c] { get { string v; return F.TryGetValue(c, out v) ? v : ""; } }
        }

        static readonly string[] SrcColumns = { "clip_key", "event", "severity", "band", "role", "scene", "seq", "gender",
                                                "phase", "cause", "spanish", "english" };

        // Reads every *.tsv in dir (sorted, like build_lonapse.py), columns by header name. Problems go to errs.
        static List<SrcRow> ReadSources(string dir, List<string> errs, List<string> allLines)
        {
            List<SrcRow> rows = new List<SrcRow>();
            string[] files = System.IO.Directory.GetFiles(dir, "*.tsv");
            Array.Sort(files, string.CompareOrdinal);
            foreach (string path in files)
            {
                string name = System.IO.Path.GetFileName(path);
                string[] lines = System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8);
                allLines.AddRange(lines);
                string[] header = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                    string[] f = line.Split('\t');
                    for (int k = 0; k < f.Length; k++) f[k] = f[k].Trim();
                    if (header == null)
                    {
                        header = f;
                        foreach (string c in SrcColumns)
                            if (Array.IndexOf(header, c) < 0) errs.Add(name + ": header lacks column " + c);
                        continue;
                    }
                    if (f.Length != header.Length) { errs.Add(name + ":" + (i + 1) + ": " + f.Length + " columns, header has " + header.Length); continue; }
                    SrcRow r = new SrcRow { File = name, Loc = name + ":" + (i + 1) };
                    for (int k = 0; k < f.Length; k++) r.F[header[k]] = f[k];
                    rows.Add(r);
                }
                if (header == null) errs.Add(name + ": no header line");
            }
            return rows;
        }

        static string Short(List<string> items, int max)
        {
            if (items.Count == 0) return "none";
            List<string> head = items.Count > max ? items.GetRange(0, max) : items;
            return string.Join(", ", head.ToArray()) + (items.Count > max ? " ... (" + items.Count + " in total)" : "");
        }

        static bool IsSeriousTag(string p) { return p == "serious" || Array.IndexOf(PhraseTable.SeriousPhases, p) >= 0; }

        static readonly System.Text.RegularExpressions.Regex FireEn = new System.Text.RegularExpressions.Regex(
            @"\b(fire|fires|flames?|smoke|smoking|burning)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // "llama" alone is also the verb "llamar" (Control llama), so only the plural and llamarada count as flame.
        // A flame line still has "flame" in its English text.
        static readonly System.Text.RegularExpressions.Regex FireEs = new System.Text.RegularExpressions.Regex(
            @"(fuego|humo|llamarada\w*|\bllamas\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Text.RegularExpressions.Regex ExplodeEn = new System.Text.RegularExpressions.Regex(
            @"\bexplo(d|s)\w*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Text.RegularExpressions.Regex ExplodeEs = new System.Text.RegularExpressions.Regex(
            @"(explot\w*|explosi\w*|estall\w*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly string[] FireOkEvents = { "staging", "throttle_full", "throttle_cut", "burn_soon", "countdown" };

        // Non-verbal panic lines (no cause needed): screams, or every word is an interjection or a stretched vowel.
        static readonly string[] Interjections = { "no", "ay", "ah", "oh", "uf", "uy", "eh", "mayday", "socorro", "ayuda",
                                                    "agarraos", "madre", "mía", "mia", "dios", "control", "vamos", "a", "ya", "sí", "si" };
        static bool NonVerbal(SrcRow r)
        {
            if (PacingRules.IsScream(r["scene"])) return true;
            string[] words = System.Text.RegularExpressions.Regex.Split(r["spanish"].ToLowerInvariant(), @"[^\p{L}]+");
            int n = 0;
            foreach (string w in words)
            {
                if (w.Length == 0) continue;
                n++;
                bool stretched = System.Text.RegularExpressions.Regex.IsMatch(w, @"(\p{L})\1\1");
                if (!stretched && Array.IndexOf(Interjections, w) < 0) return false;
            }
            return n > 0;
        }

        public static void SourceTables(string dir)
        {
            if (!System.IO.Directory.Exists(dir)) { Console.WriteLine("SKIP  v3 tables: no folder " + dir); return; }
            List<string> readErr = new List<string>();
            List<string> all = new List<string>();
            List<SrcRow> rows = ReadSources(dir, readErr, all);
            Check(readErr.Count == 0 && rows.Count > 0, "v3 tables: every phrases/*.tsv has the 12 columns by name (" + rows.Count + " rows)", Short(readErr, 5));

            // the C# loader on the concatenated files (a later header line starts a new file)
            List<string> pe = new List<string>();
            PhraseTable t = PhraseTable.Parse(all, pe);
            Check(pe.Count == 0, "v3 tables: PhraseTable.Parse reads the concatenated files without errors (" + t.Count + " rows)", Short(pe, 5));
            Check(rows.Count >= MinSourceRows, "v3 tables: at least " + MinSourceRows + " source rows", rows.Count + " rows");
            {
                // In launch and ascent Mission Control never talked to the probe (every probe_chat row was goofy)
                List<string> noChat = new List<string>();
                // the lines each stock core uses (a core without own lines yet borrows generic, ProbeCores.DefaultLines)
                var lineSets = new List<string>();
                foreach (string key in ProbeCores.Keys)
                    if (!lineSets.Contains(ProbeCores.DefaultLines(key))) lineSets.Add(ProbeCores.DefaultLines(key));
                foreach (string core in lineSets)
                    foreach (string ph in FlightPhase.All)
                    {
                        if (ph == FlightPhase.Blackout) continue;
                        List<PhraseScene> pool = t.ProbeScenesFor("probe_chat", core, ph, false,
                            sc => sc.McLines > 0 && ProbeRules.SceneFits(ProbeRules.ShapeOf(sc), false, true));
                        if (pool.Count == 0) noChat.Add(core + "/" + ph);
                    }
                Check(noChat.Count == 0, "v6 tables: every core has a probe_chat scene with MC in every phase outside blackout", Short(noChat, 6));
            }

            // unique keys and contiguous scene seq
            Dictionary<string, string> keyAt = new Dictionary<string, string>();
            List<string> dups = new List<string>();
            Dictionary<string, List<int>> seqs = new Dictionary<string, List<int>>();
            Dictionary<string, string> sceneFile = new Dictionary<string, string>();
            List<string> split = new List<string>();
            foreach (SrcRow r in rows)
            {
                string k = r["clip_key"];
                if (keyAt.ContainsKey(k)) dups.Add(k + " (" + r.Loc + ", first " + keyAt[k] + ")"); else keyAt[k] = r.Loc;
                int sq;
                if (!int.TryParse(r["seq"], out sq)) sq = -1;
                string sc = r["scene"];
                if (!seqs.ContainsKey(sc)) seqs[sc] = new List<int>();
                seqs[sc].Add(sq);
                string f0;
                if (sceneFile.TryGetValue(sc, out f0)) { if (f0 != r.File && !split.Contains(sc)) split.Add(sc + " (" + f0 + ", " + r.File + ")"); }
                else sceneFile[sc] = r.File;
            }
            Check(dups.Count == 0, "v3 tables: clip_key unique across all files", Short(dups, 5));
            List<string> gaps = new List<string>();
            foreach (KeyValuePair<string, List<int>> kv in seqs)
            {
                List<int> s = new List<int>(kv.Value); s.Sort();
                bool ok = true;
                for (int i = 0; i < s.Count; i++) if (s[i] != i + 1) ok = false;
                if (!ok) gaps.Add(kv.Key + " [" + string.Join(",", s.ConvertAll(x => x.ToString()).ToArray()) + "]");
            }
            Check(gaps.Count == 0 && split.Count == 0, "v3 tables: scene seq runs 1..n, each scene in one file (" + seqs.Count + " scenes)",
                  "gaps " + Short(gaps, 5) + "; split " + Short(split, 3));

            // scenes: intercom = crew only, 2 or more lines
            Dictionary<string, List<SrcRow>> byScene = new Dictionary<string, List<SrcRow>>();
            foreach (SrcRow r in rows)
            {
                if (!byScene.ContainsKey(r["scene"])) byScene[r["scene"]] = new List<SrcRow>();
                byScene[r["scene"]].Add(r);
            }
            Func<string, bool> isIntercom = sc => { List<SrcRow> l = byScene[sc]; return l.Count >= 2 && l.TrueForAll(x => x["role"] == "crew"); };
            List<string> seriousIc = new List<string>();
            foreach (string sc in byScene.Keys) if (isIntercom(sc) && IsSeriousTag(byScene[sc][0]["phase"])) seriousIc.Add(sc + " (" + byScene[sc][0]["phase"] + ")");
            Check(seriousIc.Count == 0, "v3 tables: no intercom scene in a serious phase", Short(seriousIc, 5));

            // minimum counts
            List<string> shortList = new List<string>();
            string have = "";
            foreach (string p in PhraseTable.SeriousPhases)
            {
                int n = 0;
                foreach (SrcRow r in rows)
                    if (r["event"] == "chatter" && r["band"] == "calm" && !isIntercom(r["scene"]) && (r["phase"] == p || r["phase"] == "serious")) n++;
                have += p + " " + n + " ";
                if (n < MinSeriousCalmRadio) shortList.Add(p + " " + n + "/" + MinSeriousCalmRadio);
            }
            Check(shortList.Count == 0, "v3 tables: calm radio chatter per serious phase >= " + MinSeriousCalmRadio + " (phase or serious)",
                  shortList.Count == 0 ? have.Trim() : "short: " + string.Join(", ", shortList.ToArray()));
            shortList.Clear(); have = "";
            foreach (string p in PhraseTable.GoofyPhases)
            {
                int n = 0;
                foreach (SrcRow r in rows)
                    if (r["event"] == "chatter" && r["band"] == "calm" && (r["phase"] == p || r["phase"] == "goofy")) n++;
                have += p + " " + n + " ";
                if (n < MinGoofyCalm) shortList.Add(p + " " + n + "/" + MinGoofyCalm);
            }
            Check(shortList.Count == 0, "v3 tables: calm chatter per goofy phase >= " + MinGoofyCalm + " (phase or goofy)",
                  shortList.Count == 0 ? have.Trim() : "short: " + string.Join(", ", shortList.ToArray()));
            shortList.Clear(); have = "";
            foreach (string c in PhraseTable.Causes)
            {
                if (c == "any") continue;
                int n = 0;
                foreach (SrcRow r in rows) if (r["cause"] == c) n++;
                have += c + " " + n + " ";
                if (n < MinPerCause) shortList.Add(c + " " + n + "/" + MinPerCause);
            }
            Check(shortList.Count == 0, "v3 tables: rows per cause >= " + MinPerCause,
                  shortList.Count == 0 ? have.Trim() : "short: " + string.Join(", ", shortList.ToArray()));

            // panic lines name their cause. Tense crew lines may be generic (cause any). With only one generic
            // tense scene (an MC line) that scene played every time the cause had just been talked about.
            List<string> noCause = new List<string>();
            foreach (SrcRow r in rows)
                if (r["role"] == "crew" && r["event"] == "chatter" && r["band"] == "panic"
                    && r["cause"] == "any" && !NonVerbal(r))
                    noCause.Add(r["clip_key"]);
            Check(noCause.Count == 0, "v3 tables: crew panic chatter has a cause (screams and interjections excepted)", Short(noCause, 8));
            // probe_crew scenes: a probe row and a crew row, no MC, never in a serious phase
            var pcScenes = new Dictionary<string, List<SrcRow>>();
            foreach (SrcRow r in rows)
                if (r["event"] == "probe_crew")
                {
                    List<SrcRow> l;
                    if (!pcScenes.TryGetValue(r["scene"], out l)) pcScenes[r["scene"]] = l = new List<SrcRow>();
                    l.Add(r);
                }
            List<string> pcBadScenes = new List<string>();
            foreach (var kv in pcScenes)
            {
                bool probe = false, crew = false, mc = false;
                foreach (SrcRow r in kv.Value) { probe |= r["role"] == "probe"; crew |= r["role"] == "crew"; mc |= r["role"] == "mc"; }
                string ph = kv.Value[0]["phase"];
                if (!probe || !crew || mc || ph == "serious" || ph == "launch" || ph == "ascent" || ph == "reentry" || ph == "descent")
                    pcBadScenes.Add(kv.Key);
            }
            Check(pcScenes.Count >= 20 && pcBadScenes.Count == 0, "0.6.0 tables: probe_crew scenes have probe and crew rows, no MC, no serious phase",
                  pcScenes.Count + " scenes" + (pcBadScenes.Count > 0 ? ", bad: " + Short(pcBadScenes, 6) : ""));
            int genericTense = 0;
            foreach (SrcRow r in rows)
                if (r["event"] == "chatter" && r["band"] == "tense" && r["cause"] == "any" && r["seq"] == "1") genericTense++;
            Check(genericTense >= 30, "0.6.0 tables: at least 30 generic (cause any) tense scenes", genericTense.ToString());

            // no fire outside fire-related contexts (the game measures heat, not fire)
            List<string> fire = new List<string>();
            foreach (SrcRow r in rows)
            {
                bool hit = FireEn.IsMatch(r["english"]) || FireEs.IsMatch(r["spanish"]);
                bool ok = Array.IndexOf(FireOkEvents, r["event"]) >= 0 || r["cause"] == "heat" || r["cause"] == "plasma";
                if (hit && !ok) fire.Add(r["clip_key"] + " (" + r.Loc + ")");
            }
            Check(fire.Count == 0, "v3 tables: fire, flame, smoke, burning only in staging, throttle, burn, countdown or heat/plasma rows", Short(fire, 6));

            // Explosion words (PhraseTable.HasExplosionWord, plus the old explod/explot patterns) only in part_lost
            List<string> boom = new List<string>();
            int boomOk = 0;
            foreach (SrcRow r in rows)
            {
                bool hit = PhraseTable.HasExplosionWord(r["english"]) || PhraseTable.HasExplosionWord(r["spanish"])
                           || ExplodeEn.IsMatch(r["english"]) || ExplodeEs.IsMatch(r["spanish"]);
                if (!hit) continue;
                if (r["event"] == PhraseTable.ExplosionEvent) boomOk++;
                else boom.Add(r["clip_key"] + " (" + r.Loc + ")");
            }
            Check(boom.Count == 0, "v4 tables: explosion words only in event part_lost (" + boomOk + " part_lost rows use one)", Short(boom, 6));

            V4Sources(rows, t);

            // {body} in both texts or in neither
            List<string> body = new List<string>();
            int bodyRows = 0;
            foreach (SrcRow r in rows)
            {
                bool es = r["spanish"].Contains("{body}"), en = r["english"].Contains("{body}");
                // A subtitle-only row (probe) has no spanish, {body} sits only in english
                if (PhraseTable.IsSubtitleOnlyRole(r["role"])) { if (en) bodyRows++; continue; }
                if (es != en) body.Add(r["clip_key"]);
                if (es && en) bodyRows++;
            }
            Check(body.Count == 0, "v3 tables: {body} in both spanish and english or in neither (" + bodyRows + " rows with {body})", Short(body, 6));

            // kept from the first source table test
            int screams = 0, mcPanic = 0, badScream = 0;
            foreach (PhraseEntry e in t.Entries)
            {
                if (PacingRules.IsScream(e.Scene)) { screams++; if (e.Role != "crew" || e.Band != "panic") badScream++; }
                if (e.Role == "mc" && e.Band == "panic") mcPanic++;
            }
            Check(screams >= 3 && badScream == 0 && mcPanic >= 3, "v3 tables: screams are crew panic lines, calm MC panic answers exist",
                  screams + " screams (" + badScream + " bad), " + mcPanic + " MC panic lines");
            int sep = 0, ign = 0, anyKind = 0;
            foreach (PhraseScene psc in t.ScenesFor("staging", null, null))
            {
                StageKind sk = PacingRules.StagingSceneKind(psc.Id);
                if (sk == StageKind.Separation) sep++; else if (sk == StageKind.Ignition) ign++; else anyKind++;
            }
            Check(sep >= 3 && ign >= 3 && anyKind >= 2, "v3 tables: separation, ignition and neutral staging scenes", sep + " / " + ign + " / " + anyKind);
        }

        // ---- source checks: new columns, scene agreement, topics and the minimum scene counts ----
        static string Col(SrcRow r, string c) { string v = r[c]; return string.IsNullOrEmpty(v) ? "any" : v; }

        static readonly System.Text.RegularExpressions.Regex JokeWords = new System.Text.RegularExpressions.Regex(
            @"\b(haha\w*|jaja\w*|lol|joke\w*|broma\w*|chiste\w*|funny|gracios\w*|snacks?|banana\w*|oops|whee+|yay|hooray|yupi)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static void V4Sources(List<SrcRow> rows, PhraseTable t)
        {
            // values of the new columns (a file without them reads as any)
            List<string> badVal = new List<string>();
            foreach (SrcRow r in rows)
            {
                if (Array.IndexOf(PhraseTable.Traits, Col(r, "trait")) < 0) badVal.Add(r["clip_key"] + " trait " + r["trait"]);
                if (Array.IndexOf(PhraseTable.Personas, Col(r, "persona")) < 0) badVal.Add(r["clip_key"] + " persona " + r["persona"]);
                if (Array.IndexOf(PhraseTable.TopicsFor(r["event"]), Col(r, "topic")) < 0) badVal.Add(r["clip_key"] + " topic " + r["topic"] + " on " + r["event"]);
            }
            Check(badVal.Count == 0, "v4 tables: trait, persona and topic values are known, topic only on science and soi_enter", Short(badVal, 6));

            Dictionary<string, List<SrcRow>> byScene = new Dictionary<string, List<SrcRow>>();
            List<string> order = new List<string>();
            foreach (SrcRow r in rows)
            {
                if (!byScene.ContainsKey(r["scene"])) { byScene[r["scene"]] = new List<SrcRow>(); order.Add(r["scene"]); }
                byScene[r["scene"]].Add(r);
            }
            List<string> mixed = new List<string>();
            foreach (string sc in order)
            {
                SrcRow h = byScene[sc][0];
                foreach (SrcRow r in byScene[sc])
                    if (Col(r, "trait") != Col(h, "trait") || Col(r, "persona") != Col(h, "persona") || Col(r, "topic") != Col(h, "topic"))
                    { mixed.Add(sc); break; }
            }
            Check(mixed.Count == 0, "v4 tables: all rows of a scene share trait, persona and topic", Short(mixed, 6));

            // scene list per event. A scene's tags come from its first row.
            Func<string, List<List<SrcRow>>> scenes = ev =>
            {
                List<List<SrcRow>> l = new List<List<SrcRow>>();
                foreach (string sc in order) if (byScene[sc][0]["event"] == ev) l.Add(byScene[sc]);
                return l;
            };
            Func<List<SrcRow>, bool> hasMc = l => l.Exists(x => x["role"] == "mc");
            Func<List<SrcRow>, bool> mcOnly = l => l.TrueForAll(x => x["role"] == "mc");

            // soi_enter: 10 scenes with {body}, 3 home scenes
            {
                List<List<SrcRow>> s = scenes("soi_enter");
                int any = 0, home = 0;
                List<string> noBody = new List<string>();
                foreach (List<SrcRow> l in s)
                {
                    if (Col(l[0], "topic") == "home") { home++; continue; }
                    any++;
                    if (!l.Exists(x => x["english"].Contains("{body}"))) noBody.Add(l[0]["scene"]);
                }
                Check(any >= 10 && home >= 3 && noBody.Count == 0, "v4 tables: soi_enter >= 10 {body} scenes and >= 3 home scenes",
                      any + " any, " + home + " home, without {body}: " + Short(noBody, 4));
            }
            // science: >= 2 scenes per topic, >= 8 any scenes, about half of the topic scenes for a scientist
            {
                List<List<SrcRow>> s = scenes("science");
                List<string> shortT = new List<string>();
                int sci = 0, topicScenes = 0;
                foreach (string topic in PhraseTable.ScienceTopics)
                {
                    int n = s.FindAll(l => Col(l[0], "topic") == topic).Count;
                    int need = topic == "any" ? 8 : 2;
                    if (n < need) shortT.Add(topic + " " + n + "/" + need);
                }
                foreach (List<SrcRow> l in s)
                    if (Col(l[0], "topic") != "any") { topicScenes++; if (Col(l[0], "trait") == "scientist") sci++; }
                Check(shortT.Count == 0, "v4 tables: science >= 2 scenes per topic and >= 8 any scenes (" + s.Count + " scenes)", Short(shortT, 6));
                Check(topicScenes > 0 && sci * 3 >= topicScenes && sci * 3 <= topicScenes * 2,
                      "v4 tables: about half of the science topic scenes need a scientist", sci + " of " + topicScenes);
            }
            // part_lost: 4 calm, 8 tense crew, 6 panic, 6 MC tense replies
            {
                List<List<SrcRow>> s = scenes("part_lost");
                int calm = s.FindAll(l => l[0]["band"] == "calm").Count;
                int tense = s.FindAll(l => l[0]["band"] == "tense" && !mcOnly(l)).Count;
                int panic = s.FindAll(l => l[0]["band"] == "panic").Count;
                int mc = s.FindAll(l => l[0]["band"] == "tense" && mcOnly(l)).Count;
                Check(calm >= 4 && tense >= 8 && panic >= 6 && mc >= 6, "v4 tables: part_lost >= 4 calm, 8 tense, 6 panic, 6 MC tense scenes",
                      calm + " / " + tense + " / " + panic + " / " + mc);
            }
            // power: power_low 6 (3 engineer), power_ok 3
            {
                List<List<SrcRow>> lo = scenes("power_low"), ok = scenes("power_ok");
                int eng = lo.FindAll(l => Col(l[0], "trait") == "engineer").Count;
                Check(lo.Count >= 6 && eng >= 3 && ok.Count >= 3, "v4 tables: power_low >= 6 scenes (>= 3 engineer), power_ok >= 3",
                      lo.Count + " (" + eng + " engineer), " + ok.Count);
            }
            // signal: lost 8 single + 3 two-line crew scenes and no MC row, back 6
            {
                List<List<SrcRow>> lost = scenes("signal_lost"), back = scenes("signal_back");
                int single = lost.FindAll(l => l.Count == 1).Count, multi = lost.FindAll(l => l.Count >= 2).Count;
                int mcRows = 0;
                foreach (List<SrcRow> l in lost) foreach (SrcRow r in l) if (r["role"] == "mc") mcRows++;
                Check(single >= 8 && multi >= 3, "v4 tables: signal_lost >= 8 single and >= 3 two-line scenes", single + " / " + multi);
                Check(lost.Count > 0 && mcRows == 0, "v4 tables: signal_lost has no MC rows (Control cannot hear the crew)", mcRows + " MC rows");
                Check(back.Count >= 6, "v4 tables: signal_back >= 6 scenes", back.Count.ToString());
            }
            // crew_lost: 6 MC single + 3 crew, no joke words, not tagged goofy
            {
                List<List<SrcRow>> s = scenes("crew_lost");
                int mc = s.FindAll(l => mcOnly(l)).Count, crew = s.FindAll(l => !hasMc(l) || !mcOnly(l)).Count;
                Check(mc >= 6 && crew >= 3, "v4 tables: crew_lost >= 6 MC and >= 3 crew scenes", mc + " / " + crew);
                List<string> joke = new List<string>();
                foreach (List<SrcRow> l in s)
                    foreach (SrcRow r in l)
                        if (JokeWords.IsMatch(r["english"]) || JokeWords.IsMatch(r["spanish"]) || PhraseTable.IsGoofyPhase(r["phase"])
                            || r["band"] == "calm")
                            joke.Add(r["clip_key"]);
                Check(s.Count > 0 && joke.Count == 0, "v4 tables: crew_lost rows have no joke words, no goofy phase, no calm band", Short(joke, 6));
            }
            // persona and trait groups: >= 4 scenes each
            {
                List<string> shortG = new List<string>();
                string have = "";
                foreach (string col in new string[] { "persona", "trait" })
                {
                    string[] vals = col == "persona" ? PhraseTable.Personas : PhraseTable.Traits;
                    foreach (string v in vals)
                    {
                        if (v == "any") continue;
                        int n = 0;
                        foreach (string sc in order) if (Col(byScene[sc][0], col) == v) n++;
                        have += v + " " + n + " ";
                        if (n < 4) shortG.Add(col + " " + v + " " + n + "/4");
                    }
                }
                Check(shortG.Count == 0, "v4 tables: every persona and every trait has >= 4 scenes",
                      shortG.Count == 0 ? have.Trim() : "short: " + string.Join(", ", shortG.ToArray()));
            }
            // tourists: enough intercom dialogues for each pair, calm and tense, and radio scenes with Mission Control
            {
                var need = new List<string>();
                foreach (string tr in new[] { "tourist", "to_tourist", "tourist_pair" })
                    foreach (string band in new[] { "calm", "tense" })
                    {
                        int n = 0;
                        foreach (string sc in order)
                        {
                            List<SrcRow> l = byScene[sc];
                            if (Col(l[0], "trait") == tr && l[0]["band"] == band && l.Count >= 2 && !hasMc(l)) n++;
                        }
                        int min = band == "calm" ? 14 : 5;
                        if (n < min) need.Add(tr + " " + band + " " + n + "/" + min);
                    }
                int radio = 0, tense = 0, panic = 0, withProbe = 0, stageJoke = 0;
                foreach (string sc in order)
                {
                    List<SrcRow> l = byScene[sc];
                    if (Col(l[0], "trait") != "tourist") continue;
                    bool probeRow = false;
                    foreach (SrcRow r in l) if (r["role"] == "probe") probeRow = true;
                    if (l[0]["event"] == "probe_crew" && probeRow) { if (Col(l[0], "topic") == "staging") stageJoke++; else withProbe++; continue; }
                    if (l[0]["event"] != "chatter" || (l.Count >= 2 && !hasMc(l))) continue; // radio only, not intercom
                    if (l[0]["band"] == "calm" && hasMc(l)) radio++;
                    if (l[0]["band"] == "tense") tense++;
                    if (l[0]["band"] == "panic") panic++;
                }
                if (radio < 12) need.Add("tourist radio " + radio + "/12");
                // under stress a tourist only says tourist lines on the radio, so there must be plenty
                if (tense < 20) need.Add("tourist tense radio " + tense + "/20");
                if (panic < 12) need.Add("tourist panic radio " + panic + "/12");
                if (withProbe < 12) need.Add("tourist with the probe " + withProbe + "/12");
                if (stageJoke < 6) need.Add("tourist calls the stage " + stageJoke + "/6");
                Check(need.Count == 0, "v4 tables: tourists have their own dialogues for every pair (tourist to crew, crew to tourist, two tourists) and radio scenes",
                      string.Join(", ", need.ToArray()));
            }
            // persona scenes are crew lines: a persona scene must have a crew line (the persona is the crew speaker's)
            {
                List<string> noCrew = new List<string>();
                foreach (string sc in order)
                {
                    List<SrcRow> l = byScene[sc];
                    if ((Col(l[0], "persona") != "any" || Col(l[0], "trait") != "any") && mcOnly(l)) noCrew.Add(sc);
                }
                Check(noCrew.Count == 0, "v4 tables: trait and persona scenes have a crew line", Short(noCrew, 6));
            }
            // the C# loader sees the same tags
            {
                int tagged = 0, agree = 0;
                foreach (PhraseScene ps in t.Scenes)
                {
                    List<SrcRow> l;
                    if (!byScene.TryGetValue(ps.Id, out l)) continue;
                    if (ps.Trait == "any" && ps.Persona == "any" && ps.Topic == "any") continue;
                    tagged++;
                    if (ps.Trait == Col(l[0], "trait") && ps.Persona == Col(l[0], "persona") && ps.Topic == Col(l[0], "topic")) agree++;
                }
                Check(tagged > 0 && agree == tagged, "v4 tables: PhraseTable scenes carry the trait, persona and topic of the source", agree + "/" + tagged);
            }
        }

        // feeds a constant input from t0 to t1 (step dt), returns the first time Current equals want (NaN if never)
        static double Feed(FlightPhaseClassifier c, PhaseInput i, double t0, double t1, double dt, string want)
        {
            double first = double.NaN;
            for (double t = t0; t < t1 - 1e-9; t += dt)
                if (c.Update(i, t) == want && double.IsNaN(first)) first = t;
            return first;
        }

        static PhaseInput In(PhaseSituation s, double vs, double alt, double h, double mach, double thr)
        {
            PhaseInput i = new PhaseInput();
            i.situation = s; i.verticalSpeed = vs; i.altitude = alt; i.height = h; i.atmosphereDepth = 70000;
            i.atmDensity = alt < 70000 ? 1.2 * Math.Exp(-alt / 5600.0) : 0; i.mach = mach; i.throttle = thr; i.srfSpeed = Math.Abs(vs);
            return i;
        }

        public static void Run()
        {
            // ---- FlightPhase.Matches and BodyKey ----
            object[][] cases = {
                new object[] { "any", "blackout", true }, new object[] { "", "coast", true }, new object[] { null, "orbit", true },
                new object[] { "serious", "launch", true }, new object[] { "serious", "descent", true }, new object[] { "serious", "orbit", false },
                new object[] { "serious", "blackout", false }, new object[] { "goofy", "landed", true }, new object[] { "goofy", "prelaunch", true },
                new object[] { "goofy", "reentry", false }, new object[] { "goofy", "blackout", false }, new object[] { "orbit", "orbit", true },
                new object[] { "orbit", "coast", false }, new object[] { "blackout", "blackout", true }, new object[] { "launch", null, false },
                new object[] { "any", null, true } };
            List<string> wrong = new List<string>();
            foreach (object[] c in cases)
                if (FlightPhase.Matches((string)c[0], (string)c[1]) != (bool)c[2]) wrong.Add((c[0] ?? "null") + "/" + (c[1] ?? "null"));
            Check(wrong.Count == 0, "phase: Matches table (" + cases.Length + " cases)", Short(wrong, 5));
            bool agree = true;
            foreach (string row in PhraseTable.Phases)
                foreach (string cur in FlightPhase.All)
                    if (FlightPhase.Matches(row, cur) != PhraseTable.PhaseMatches(row, cur)) agree = false;
            Check(agree, "phase: FlightPhase.Matches and PhraseTable.PhaseMatches agree for every concrete phase", null);
            Check(FlightPhase.IsSerious("reentry") && !FlightPhase.IsSerious("orbit") && FlightPhase.IsGoofy("coast") && !FlightPhase.IsGoofy("blackout"),
                  "phase: IsSerious and IsGoofy", null);
            Check(FlightPhase.BodyKey("Sun") == "kerbol" && FlightPhase.BodyKey("Mun") == "mun" && FlightPhase.BodyKey("Kerbin") == "kerbin"
                  && FlightPhase.BodyKey("Foo") == "generic" && FlightPhase.BodyKey(null) == "generic" && FlightPhase.BodyKey("") == "generic",
                  "phase: BodyKey Sun->kerbol, Mun->mun, Foo->generic, null->generic", FlightPhase.BodyKey("Sun") + " " + FlightPhase.BodyKey("Foo"));

            // ---- classifier: a whole flight on Kerbin (atmosphere 70 km), 0.5 s steps ----
            {
                FlightPhaseClassifier c = new FlightPhaseClassifier();
                double dt = 0.5;
                List<string> seen = new List<string>();
                Action<string> note = p => { if (seen.Count == 0 || seen[seen.Count - 1] != p) seen.Add(p); };
                double t = 0;
                for (; t < 10; t += dt) note(c.Update(In(PhaseSituation.PRELAUNCH, 0, 70, 0, 0, 0), t));
                bool padOk = c.Current == FlightPhase.Prelaunch;
                // KSP spawns a vessel at 50 % throttle: throttle on the pad is still prelaunch
                note(c.Update(In(PhaseSituation.PRELAUNCH, 0, 70, 0, 0, 1), t)); t += dt;
                padOk = padOk && c.Current == FlightPhase.Prelaunch;
                bool launchInstant = true;
                double liftoff = t, alt = 70, vs = 80;
                double ascentAt = double.NaN;
                for (; t < 150; t += dt)
                {
                    alt += vs * dt;
                    note(c.Update(In(PhaseSituation.FLYING, vs, alt, alt - 70, alt < 20000 ? 1.5 : 4, 1), t));
                    if (c.Current == FlightPhase.Ascent && double.IsNaN(ascentAt)) ascentAt = t;
                    if (t - liftoff < 1 && c.Current != FlightPhase.Launch) launchInstant = false;
                    vs += 4 * dt;
                }
                Check(padOk && launchInstant, "phase: pad gives prelaunch (also at throttle), liftoff gives launch at once", null);
                Check(!double.IsNaN(ascentAt) && ascentAt >= liftoff + 20, "phase: launch becomes ascent after 20 s and 1500 m (plus the 5 s hold)",
                      "ascent at +" + (ascentAt - liftoff).ToString("0.0") + " s");
                // above the atmosphere, still thrusting: ascent, then orbit
                note(c.Update(In(PhaseSituation.SUB_ORBITAL, 50, 75000, 75000, 0, 1), t)); t += dt;
                bool burnAscent = c.Current == FlightPhase.Ascent;
                double orbitAt = Feed(c, In(PhaseSituation.ORBITING, 0, 80000, 80000, 0, 0), t, t + 30, dt, FlightPhase.Orbit);
                note(c.Current);
                Check(burnAscent && Math.Abs(orbitAt - t - 5) < 0.01, "phase: circularisation stays ascent, orbit 5 s after ORBITING", (orbitAt - t).ToString("0.0"));
                t += 30;
                double reAt = Feed(c, In(PhaseSituation.SUB_ORBITAL, -600, 60000, 60000, 6, 0), t, t + 30, dt, FlightPhase.Reentry);
                note(c.Current);
                Check(Math.Abs(reAt - t - 5) < 0.01, "phase: falling in the atmosphere at mach 6 gives reentry after 5 s", (reAt - t).ToString("0.0"));
                t += 30;
                double keep = Feed(c, In(PhaseSituation.FLYING, -300, 30000, 30000, 2, 0), t, t + 20, dt, FlightPhase.Descent);
                Check(double.IsNaN(keep) && c.Current == FlightPhase.Reentry, "phase: reentry holds down to mach 1.5", c.Current);
                t += 20;
                double deAt = Feed(c, In(PhaseSituation.FLYING, -200, 15000, 15000, 0.8, 0), t, t + 30, dt, FlightPhase.Descent);
                note(c.Current);
                Check(Math.Abs(deAt - t - 5) < 0.01, "phase: subsonic fall gives descent after 5 s", (deAt - t).ToString("0.0"));
                t += 30;
                double laAt = Feed(c, In(PhaseSituation.LANDED, 0, 70, 0, 0, 0), t, t + 30, dt, FlightPhase.Landed);
                note(c.Current);
                Check(Math.Abs(laAt - t - 5) < 0.01, "phase: LANDED gives landed after 5 s", (laAt - t).ToString("0.0"));
                string path = string.Join(" ", seen.ToArray());
                Check(path == "prelaunch launch ascent orbit reentry descent landed", "phase: flight runs prelaunch, launch, ascent, orbit, reentry, descent, landed", path);
            }

            // ---- classifier: hysteresis holds a change for 5 s ----
            {
                FlightPhaseClassifier c = new FlightPhaseClassifier();
                PhaseInput orbit = In(PhaseSituation.ORBITING, 0, 80000, 80000, 0, 0);
                PhaseInput esc = In(PhaseSituation.ESCAPING, 0, 9e6, 9e6, 0, 0);
                c.Update(orbit, 0);
                bool first = c.Current == FlightPhase.Orbit;
                double t = 0.1;
                for (; t < 4.85; t += 0.1) c.Update(esc, t);
                bool held = c.Current == FlightPhase.Orbit;
                c.Update(orbit, t); t += 0.1;           // the blip ends, the hold restarts
                double startEsc = t;
                for (; t < startEsc + 4.95; t += 0.1) c.Update(esc, t);
                bool stillHeld = c.Current == FlightPhase.Orbit;
                c.Update(esc, startEsc + 5.0);
                Check(first && held && stillHeld && c.Current == FlightPhase.Coast,
                      "phase: first update is instant, a 4.8 s change is held, a 5 s change goes through", c.Current);
                c.Update(orbit, 100); c.Update(orbit, 50); // time goes back (quickload): restart the hold
                c.Update(orbit, 54.9);
                bool back = c.Current == FlightPhase.Coast;
                c.Update(orbit, 55.0);
                Check(back && c.Current == FlightPhase.Orbit, "phase: time going backwards restarts the 5 s hold", c.Current);
                FlightPhaseClassifier eva = new FlightPhaseClassifier();
                PhaseInput ev = In(PhaseSituation.PRELAUNCH, 0, 70, 0, 0, 1); ev.isEva = true;
                Check(eva.Update(ev, 0) == FlightPhase.Landed, "phase: a kerbal on EVA on the pad is landed, not launch", eva.Current);
                Check(new FlightPhaseClassifier().Candidate(In(PhaseSituation.FLYING, 100, 5000, 5000, 1, 1), 0) == FlightPhase.Ascent,
                      "phase: a vessel loaded in flight (no liftoff seen) is never launch", null);
            }

            // ---- MouthMath ----
            {
                // loudness track: 1 kHz stereo, 0.1 s quiet tone, 0.1 s silence, 0.1 s loud tone
                int fr = 1000;
                var data = new float[300 * 2];
                for (int i = 0; i < 300; i++)
                {
                    float amp = i < 100 ? 0.1f : (i < 200 ? 0f : 0.4f);
                    float x = amp * (i % 2 == 0 ? 1f : -1f);
                    data[2 * i] = x; data[2 * i + 1] = x;
                }
                float[] env = MouthMath.Envelope(data, 2, fr, MouthMath.EnvelopeFrame);
                Check(env != null && env.Length == 30 && Math.Abs(env[25] - 1f) < 1e-4 && env[15] == 0f && Math.Abs(env[5] - 0.25f) < 0.01f,
                      "mouth track: one frame per 10 ms, scaled so the loud part is 1, silence 0",
                      env == null ? "null" : env.Length + " " + env[5] + " " + env[15] + " " + env[25]);
                Check(MouthMath.EnvelopeAt(env, 0.01f, 0.255f) > 0.99f && MouthMath.EnvelopeAt(env, 0.01f, 0.5f) == 0f
                      && MouthMath.EnvelopeAt(env, 0.01f, -1f) == 0f && MouthMath.EnvelopeAt(null, 0.01f, 0.1f) == 0f
                      && MouthMath.Envelope(null, 1, 1000, 0.01f) == null,
                      "mouth track: read at a clip time, 0 outside the clip", "");
                Check(MouthMath.EnvelopeOpen(0.1f) == 0f && MouthMath.EnvelopeOpen(0.8f) == 1f && MouthMath.EnvelopeOpen(1f) == 1f
                      && Math.Abs(MouthMath.EnvelopeOpen(0.46f) - 0.5f) < 1e-4,
                      "mouth track: closed in pauses, wide on the loud syllables", "");
                Check(FaceMath.HeldMood(0.8f, -0.6f, 0f, 0.3f, 0.7f) == -0.6f && FaceMath.HeldMood(-1f, -0.6f, 0f, 0.3f, 0.7f) == -1f
                      && FaceMath.HeldMood(0.8f, 0f, 0.1f, 0.3f, 0.7f) == 0.8f && FaceMath.HeldMood(0.8f, 0f, 1f, 0.3f, 0.7f) == -1f
                      && Math.Abs(FaceMath.HeldMood(0.8f, 0f, 0.7f, 0.3f, 0.7f) - FaceMath.FaceAtPanic) < 1e-5
                      && FaceMath.HeldMood(-0.5f, 0.5f, 0f, 0.3f, 0.7f) == -0.5f,
                      "face: a calm line keeps a scared face scared, stress caps the grin, a fearful line still shows", "");
                // the thresholds of a real test crew (KSP.log 2026-11-06): Sidfrod 0.29/0.66, Mae (badass) 0.35/0.83
                Check(FaceMath.StressCap(0.2f, 0.29f, 0.66f) == 1f && FaceMath.StressCap(0.29f, 0.29f, 0.66f) == 1f
                      && Math.Abs(FaceMath.StressCap(0.66f, 0.29f, 0.66f) + 0.5f) < 1e-5 && FaceMath.StressCap(1f, 0.29f, 0.66f) == -1f
                      && Math.Abs(FaceMath.StressCap(0.83f, 0.35f, 0.83f) + 0.5f) < 1e-5,
                      "face: free below the own tenseAt, clearly scared at the own panicAt, full fear at 1", "");
                Check(FaceMath.StressCap(0.66f, 0.35f, 0.83f) > FaceMath.StressCap(0.66f, 0.29f, 0.66f) + 0.4f,
                      "face: at the same stress a braver kerbal (later thresholds) looks less scared", "");
                bool down = true; float prevCap = 2f;
                for (int i = 0; i <= 40; i++) { float c = FaceMath.StressCap(i / 40f, 0.31f, 0.69f); if (c > prevCap + 1e-6f) down = false; prevCap = c; }
                Check(down, "face: fear grows step by step with stress (never less scared at more stress)", "");
                Check(FaceMath.StressCap(0.5f, float.NaN, float.NaN) <= 1f && FaceMath.StressCap(float.NaN, 0.3f, 0.7f) == 1f
                      && FaceMath.StressCap(1f, 0.3f, 1.05f) >= -1f && FaceMath.StressCap(0.9f, 0.5f, 0.5f) < FaceMath.FaceAtPanic + 1e-5f,
                      "face: bad or crossed thresholds (escalated panicAt over 1, panicAt = tenseAt) stay sane", "");
                Check(FaceMath.ListenerMood(-1f, 0.1f, 0.35f, 0.83f) == 0f
                      && Math.Abs(FaceMath.ListenerMood(-1f, 0.83f, 0.35f, 0.83f) + 0.5f) < 1e-5
                      && Math.Abs(FaceMath.ListenerMood(-0.5f, 1f, 0.35f, 0.83f) + 0.25f) < 1e-5
                      && Math.Abs(FaceMath.ListenerMood(0.8f, 0.9f, 0.35f, 0.83f) - 0.4f) < 1e-5,
                      "face: a calm listener does not copy a panicking speaker's fear, a stressed one may, a grin still passes", "");
                Check(DcMath.CrewDistortion(1f, 0f, true) == 0f && Math.Abs(DcMath.CrewDistortion(1f, 1f, true) - 0.1f) < 1e-5
                      && Math.Abs(DcMath.CrewDistortion(1f, 0f, false) - 0.3f) < 1e-5 && DcMath.CrewDistortion(0.4f, 0f, false) == 0f,
                      "voice: a recorded panic clip gets no stress distortion on top, a calm line none, a stressed one at most 0.3", "");
                var tour = new Persona(0.5f, 0.5f, false, false, "Tourist");
                var pilot = new Persona(0.5f, 0.5f, false, false, "Pilot");
                Check(tour.IsTourist && !pilot.IsTourist && tour.TraitMatches("tourist_pair") && !pilot.TraitMatches("tourist_pair")
                      && pilot.TraitMatches("to_tourist") && !tour.TraitMatches("to_tourist") && !pilot.TraitMatches("tourist"),
                      "tourist: pair traits check speaker A (tourist_pair a tourist, to_tourist anyone else)", "");
                Check(Persona.PairFits("tourist_pair", true, true) && !Persona.PairFits("tourist", true, true) && !Persona.PairFits("any", true, true)
                      && Persona.PairFits("tourist", true, false) && !Persona.PairFits("any", true, false)
                      && Persona.PairFits("to_tourist", false, true) && !Persona.PairFits("any", false, true) && !Persona.PairFits("pilot", false, true)
                      && Persona.PairFits("any", false, false) && Persona.PairFits("pilot", false, false)
                      && !Persona.PairFits("tourist", false, false) && !Persona.PairFits("to_tourist", false, false) && !Persona.PairFits("tourist_pair", false, false),
                      "tourist: an intercom chat with a tourist only takes the dialogues written for that pair, one without never a tourist scene", "");
                Check(!Persona.RadioFits("any", "staging", "none", true) && Persona.RadioFits("tourist", "staging", "none", true)
                      && Persona.RadioFits("any", "staging", "none", false) && !Persona.RadioFits("any", "chatter", "calm", true)
                      && Persona.RadioFits("tourist", "chatter", "calm", true) && !Persona.RadioFits("any", "chatter", "panic", true)
                      && !Persona.RadioFits("any", "chatter", "tense", true) && Persona.RadioFits("any", "chatter", "panic", true, true)
                      && Persona.RadioFits("tourist", "chatter", "tense", true)
                      && !Persona.RadioFits("any", "power_low", "tense", true) && !Persona.RadioFits("to_tourist", "chatter", "calm", false)
                      && !Persona.RadioFits("tourist_pair", "chatter", "calm", true),
                      "tourist: on the radio a tourist only says tourist scenes (calm and stressed) and screams, pair scenes never on the radio", "");
                Check(Persona.ChatSideFits(null, "pilot brave") && !Persona.ChatSideFits(null, "tourist") && !Persona.ChatSideFits("pilot", "tourist")
                      && Persona.ChatSideFits("tourist", "tourist dim") && RuntimeLines.JobOk("tourist", "tourist dim"),
                      "tourist: a runtime chat side only takes a tourist when it asks for one", "");
                Check(Array.IndexOf(PhraseTable.TopicsFor("probe_crew"), "staging") > 0 && Array.IndexOf(PhraseTable.TopicsFor("chatter"), "staging") < 0,
                      "tourist: probe_crew has the staging topic (a tourist calls the stage, the probe reacts)", "");
                Check(PhraseTable.PartTopicOk("prechute", false, false, true, false) && !PhraseTable.PartTopicOk("prechute", false, false, true, true)
                      && !PhraseTable.PartTopicOk("prechute", false, false, false, false) && PhraseTable.PartTopicOk("chuteopen", false, false, false, true)
                      && !PhraseTable.PartTopicOk("chuteopen", false, false, true, false) && !PhraseTable.PartTopicOk("heatshield", true, false, true, true)
                      && PhraseTable.PartTopicOk("heatshield", false, true, false, false) && !PhraseTable.PartTopicOk("solar", false, true, true, true)
                      && PhraseTable.PartTopicOk("any", false, false, false, false),
                      "parts: arm-the-chutes talk only before any chute opens, chute-open talk only after, shield talk only with a shield", "");
                Check(FaceMath.IdleMood(0.8f, 0.1f, 0.3f, 0.7f) == 0.8f && FaceMath.IdleMood(0.8f, 1f, 0.3f, 0.7f) == -1f
                      && FaceMath.IdleMood(-0.8f, 0.4f, 0.3f, 0.7f) == -0.8f && Math.Abs(FaceMath.IdleMood(0.8f, 0.7f, 0.3f, 0.7f) + 0.5f) < 1e-5,
                      "face: between lines a grinning stock face is capped by stress, a scared one stays scared", "");
                Check(Math.Abs(FaceMath.FaceMood(-1f, 0f, 0.5f, 0.1f, 0.3f, 0.7f) - 0.5f) < 1e-5
                      && Math.Abs(FaceMath.FaceMood(-1f, 1f, 0.5f, 0.1f, 0.3f, 0.7f) + 1f) < 1e-5
                      && Math.Abs(FaceMath.FaceMood(-1f, 0.5f, 0.5f, 0.1f, 0.3f, 0.7f) + 0.25f) < 1e-5
                      && Math.Abs(FaceMath.FaceMood(0.8f, 0f, 0.8f, 1f, 0.3f, 0.7f) + 1f) < 1e-5,
                      "face: no stress is the old stock to line blend, high stress holds the fear between lines", "");
                float fa = 0f, sl = 0f, peak = 0f, steady = 0f;
                for (int i = 0; i < 30; i++) peak = Math.Max(peak, FaceMath.Accent(ref fa, ref sl, 1f, 0.01f));
                for (int i = 0; i < 400; i++) steady = FaceMath.Accent(ref fa, ref sl, 1f, 0.01f);
                Check(peak > 0.5f && steady < 0.05f && FaceMath.ListenerNod(0.25f, 0.5f) > 0.99f && FaceMath.ListenerNod(0.6f, 0.5f) == 0f
                      && FaceMath.ListenerNod(-0.1f, 0.5f) == 0f,
                      "head nod: on a louder syllable, not on a steady voice. A listener nods once", peak + " / " + steady);
            }
            Near(MouthMath.Rms(new float[] { 1f, -1f, 1f, -1f }), 1.0, 1e-6, "mouth: Rms of a full-scale square is 1");
            Near(MouthMath.Rms(new float[] { 3f, 4f }), Math.Sqrt(12.5), 1e-5, "mouth: Rms of 3, 4");
            Check(MouthMath.Rms(null) == 0f && MouthMath.Rms(new float[0]) == 0f && MouthMath.Rms(new float[] { 1f }, 0) == 0f,
                  "mouth: Rms of null, empty and n=0 is 0", null);
            Near(MouthMath.Rms(new float[] { 2f, 2f, 100f }, 2), 2.0, 1e-6, "mouth: Rms uses only the first n samples");
            Check(!float.IsNaN(MouthMath.Rms(new float[] { float.NaN, 1f })), "mouth: Rms ignores NaN samples", null);
            {
                float env = 0f;
                for (int i = 0; i < 100; i++) env = MouthMath.Follow(env, 1f, 0.01f, 0.03f, 0.10f);
                bool up = env > 0.99f && env <= 1f;
                float afterAttack = MouthMath.Follow(0f, 1f, 0.03f, 0.03f, 0.10f);
                for (int i = 0; i < 200; i++) env = MouthMath.Follow(env, 0f, 0.01f, 0.03f, 0.10f);
                Check(up && env < 0.01f && env >= 0f, "mouth: Follow converges up (attack) and down (release)", "env " + env.ToString("0.0000"));
                Near(afterAttack, 1 - Math.Exp(-1), 1e-5, "mouth: Follow covers 63 % of a step in one attack time");
                Check(MouthMath.Follow(0.5f, 1f, 0f, 0.03f, 0.1f) == 0.5f && MouthMath.Follow(float.NaN, 0f, 0.01f, 0.03f, 0.1f) == 0f,
                      "mouth: Follow keeps the value for dt 0 and resets NaN", null);
            }
            Check(MouthMath.Open(10f, 0.02f, 6f) == 1f && MouthMath.Open(0.01f, 0.02f, 6f) == 0f && MouthMath.Open(float.NaN, 0.02f, 6f) == 0f
                  && MouthMath.Open(-1f, 0.02f, 6f) == 0f, "mouth: Open clamps to 0..1 and gates", null);
            Near(MouthMath.Open(0.1f, 0.02f, 6f), 0.48, 1e-5, "mouth: Open is linear above the gate");
            {
                bool inRange = true; float mn = 1f, mx = 0f;
                foreach (uint seed in new uint[] { 0u, 1u, 12345u, 0xFFFFFFFFu })
                    for (int i = 0; i < 20000; i++)
                    {
                        float v = MouthMath.Flap(i * 0.0005f, seed);
                        if (v < 0f || v > 1f || float.IsNaN(v)) inRange = false;
                        mn = Math.Min(mn, v); mx = Math.Max(mx, v);
                    }
                Check(inRange && mn < 0.05f && mx > 0.5f, "mouth: Flap stays in 0..1 and opens and closes", mn.ToString("0.00") + ".." + mx.ToString("0.00"));
                Check(MouthMath.Flap(float.NaN, 1u) == 0f && MouthMath.FlapHz(0u) >= 6f && MouthMath.FlapHz(999u) <= 8f, "mouth: Flap NaN gives 0, rate 6..8 Hz", null);
                Check(!MouthMath.RefWarm(9, 1f, 10, 0.3f) && !MouthMath.RefWarm(10, 0.29f, 10, 0.3f) && MouthMath.RefWarm(10, 0.3f, 10, 0.3f)
                      && !MouthMath.RefWarm(50, float.NaN, 10, 0.3f), "mouth: closed reference tracked only after 10 frames and 0.3 s", null);
                Check(MouthMath.DecayAlpha(0f, 20f) == 0f && MouthMath.DecayAlpha(float.NaN, 20f) == 0f && MouthMath.DecayAlpha(0.02f, 0f) == 1f,
                      "mouth: DecayAlpha guards", null);
                {
                    // closed reference relax: 20 s time constant, about 63 % of the way after 20 s of 50 fps frames
                    double x = 0.0;
                    for (int i = 0; i < 1000; i++) x += (1.0 - x) * MouthMath.DecayAlpha(0.02f, 20f);
                    Near(x, 1.0 - Math.Exp(-1.0), 1e-3, "mouth: closed reference relaxes with a 20 s time constant");
                }
                Check(MouthMath.AxisIndex(0f) == 0 && MouthMath.AxisIndex(1.2f) == 1 && MouthMath.AxisIndex(2f) == 2 && MouthMath.AxisIndex(7f) == 2
                      && MouthMath.AxisIndex(float.NaN) == 2, "mouth: AxisIndex 0, 1, else 2", null);
            }

            // ---- Settings: new sliders and mouth tuning ----
            {
                Tuning d = new Tuning();
                Tuning s = new Tuning();
                s.Sanitize();
                Check(s.tQuiet == d.tQuiet && s.intercomInterval == d.intercomInterval && s.mouthOpenDeg == 18f && s.mouthAxis == 2f
                      && s.mouthInvert == 0f && s.mouthGain == 6f && s.mouthGate == 0.02f && s.mouthAttack == 0.03f && s.mouthRelease == 0.10f
                      && s.blackoutIntercomMaxStress == 0.9f && s.blackoutIntercomFirst == 4f && s.blackoutIntercomGap == 30f,
                      "settings: defaults survive Sanitize (tQuiet " + d.tQuiet + ", intercomInterval " + d.intercomInterval + ", mouth and blackout tuning)", null);
                Tuning lo = new Tuning(); lo.tQuiet = 1f; lo.intercomInterval = 2f; lo.Sanitize();
                Tuning hi = new Tuning(); hi.tQuiet = 5000f; hi.intercomInterval = 99999f; hi.Sanitize();
                Check(lo.tQuiet == 5f && hi.tQuiet == 1800f, "settings: Sanitize clamps tQuiet to 5..1800", lo.tQuiet + " / " + hi.tQuiet);
                Check(lo.intercomInterval == 10f && hi.intercomInterval == 3600f, "settings: Sanitize clamps intercomInterval to 10..3600", lo.intercomInterval + " / " + hi.intercomInterval);
                Tuning ax = new Tuning(); ax.mouthAxis = 0.6f; ax.mouthInvert = 0.7f; ax.Sanitize();
                Tuning ax2 = new Tuning(); ax2.mouthAxis = 1.4f; ax2.mouthInvert = 0.3f; ax2.Sanitize();
                Tuning ax3 = new Tuning(); ax3.mouthAxis = 7f; ax3.mouthInvert = 5f; ax3.Sanitize();
                Check(ax.mouthAxis == 1f && ax2.mouthAxis == 1f && ax3.mouthAxis == 2f, "settings: mouthAxis rounds and clamps to 0..2", ax.mouthAxis + " " + ax2.mouthAxis + " " + ax3.mouthAxis);
                Check(ax.mouthInvert == 1f && ax2.mouthInvert == 0f && ax3.mouthInvert == 1f, "settings: mouthInvert snaps to 0 or 1", ax.mouthInvert + " " + ax2.mouthInvert + " " + ax3.mouthInvert);
                Tuning neg = new Tuning(); neg.mouthGain = -3f; neg.blackoutIntercomGap = float.NaN; neg.Sanitize();
                Check(neg.mouthGain == 6f && neg.blackoutIntercomGap == 30f, "settings: negative or NaN new tuning floats reset to the default", null);
                Settings st = new Settings();
                Check(st.mouthAnimation && !st.mouthDebug, "settings: mouthAnimation on, mouthDebug off by default", null);
            }

            // ---- probe callouts, say again, milestones, rover moments, data beeps ----
            {
                Check(ProbeRules.CalloutMcAllowed(0.1, 0.35f, 30f, 20f), "v6: callout MC answer when the roll and the gap allow it", null);
                Check(!ProbeRules.CalloutMcAllowed(0.1, 0.35f, 10f, 20f), "v6: no callout MC answer inside the gap", null);
                Check(!ProbeRules.CalloutMcAllowed(0.5, 0.35f, 30f, 20f), "v6: no callout MC answer above the chance", null);
                Check(ProbeRules.SayAgainDue(0.2f, true, 0.35f, 0.1, 0.35f, 200f, 120f), "v6: say again on a weak link", null);
                Check(!ProbeRules.SayAgainDue(0.5f, true, 0.35f, 0.1, 0.35f, 200f, 120f), "v6: no say again on a good link", null);
                Check(!ProbeRules.SayAgainDue(0.2f, false, 0.35f, 0.1, 0.35f, 200f, 120f), "v6: no say again without a link", null);
                Check(!ProbeRules.SayAgainDue(0.2f, true, 0.35f, 0.1, 0.35f, 60f, 120f), "v6: no say again inside the cooldown", null);
                Check(!ProbeRules.SayAgainDue(float.NaN, true, 0.35f, 0.1, 0.35f, 200f, 120f), "v6: no say again on a NaN signal", null);
                Check(ProbeRules.RepeatMood("proud") == "offended" && ProbeRules.RepeatMood("bored") == "offended"
                      && ProbeRules.RepeatMood("scared") == "scared" && ProbeRules.RepeatMood(null) == "happy", "v6: RepeatMood", null);
                Check(ProbeRules.MissionDay(0, ProbeRules.KerbinDay) == 1 && ProbeRules.MissionDay(6 * 3600.0 - 1, ProbeRules.KerbinDay) == 1
                      && ProbeRules.MissionDay(6 * 3600.0, ProbeRules.KerbinDay) == 2 && ProbeRules.MissionDay(24 * 3600.0, ProbeRules.EarthDay) == 2
                      && ProbeRules.MissionDay(double.NaN, ProbeRules.KerbinDay) == 1, "v6: MissionDay with Kerbin and Earth days", null);
                // JNSQ with Kronometer: 12 h days, so 13 Kerbin days of MET is day 7
                Check(ProbeRules.MissionDay(13 * 6 * 3600.0, 12 * 3600.0) == 7 && ProbeRules.MissionDay(13 * 6 * 3600.0, double.NaN) == 14
                      && ProbeRules.MissionDay(100, 0) == 1, "MissionDay with a modded day length and a broken one", null);
                Check(SubtitleSlots.FlightValues(25 * 3600.0, 12 * 3600.0, null, null, double.NaN)["day"] == "3", "the {day} slot uses the game day length", null);
                Check(ProbeRules.BeepSubtitle("scared") == "[beeps nervously]" && PhraseTable.ProbeSubtitleOk(ProbeRules.DataSubtitle),
                      "v6: mood subtitle and data subtitle have the short form", null);

                double day = 6 * 3600.0;
                ProbeMilestones ms = new ProbeMilestones();
                Check(ms.Update("a", 2.5 * day, ProbeRules.KerbinDay, false, false, "Kerbin") == null, "v6: first sight of a vessel replays nothing", null);
                Check(ms.Update("a", 3 * day, ProbeRules.KerbinDay, false, false, "Kerbin") == null, "v6: day 1 passed before first sight stays quiet", null);
                Check(ms.Update("a", 10 * day, ProbeRules.KerbinDay, false, false, "Kerbin") == "day10", "v6: day 10 milestone", null);
                Check(ms.Update("a", 10.5 * day, ProbeRules.KerbinDay, false, false, "Kerbin") == null, "v6: a milestone plays once", null);
                Check(ms.Update("a", 10.6 * day, ProbeRules.KerbinDay, true, false, "Kerbin") == "orbit" && ms.Update("a", 10.7 * day, ProbeRules.KerbinDay, true, false, "Kerbin") == null,
                      "v6: first orbit of a body once", null);
                Check(ms.Update("a", 10.8 * day, ProbeRules.KerbinDay, true, false, "Mun") == "orbit", "v6: first orbit of another body", null);
                Check(ms.Update("a", 400 * day, ProbeRules.KerbinDay, true, false, "Mun") == "day365", "v6: several days at once give only the highest", null);
                Check(ms.Update("a", 401 * day, ProbeRules.KerbinDay, true, false, "Mun") == null, "v6: the skipped day 100 is marked done", null);
                Check(ms.TakeLanding("a", "Mun") && !ms.TakeLanding("a", "Mun") && ms.TakeLanding("a", "Minmus"), "v6: first landing per body once", null);
                ProbeMilestones ms2 = new ProbeMilestones();
                ms2.Update("b", 0.5 * day, ProbeRules.KerbinDay, false, true, "Kerbin");
                Check(!ms2.TakeLanding("b", "Kerbin") && ms2.Update("b", 1.2 * day, ProbeRules.KerbinDay, false, true, "Kerbin") == "day1",
                      "v6: landed at first sight is no first landing, day 1 still plays", null);
                Check(ms2.Update(null, day, ProbeRules.KerbinDay, false, false, "Kerbin") == null && !ms2.TakeLanding("", "Kerbin"), "v6: no vessel id, no milestone", null);

                Tuning tu = new Tuning();
                RoverMoments rm = new RoverMoments();
                string got = null;
                for (int i = 0; i < 60 && got == null; i++) got = rm.Update(0.1f, true, true, tu.roverFastSpeed + 1f, 0f, 0f, tu);
                Check(got == "fast", "v6: rover fast moment after roverFastSec", got ?? "null");
                Check(rm.Update(0.1f, true, false, 2f, tu.roverAirSec + 0.1f, 0f, tu) == null, "v6: rover moments share a cooldown", null);
                rm.Reset();
                Check(rm.Update(0.1f, true, false, 2f, tu.roverAirSec + 0.1f, 0f, tu) == "air", "v6: rover air moment", null);
                rm.Reset();
                Check(rm.Update(0.1f, false, false, 2f, tu.roverAirSec + 0.1f, 0f, tu) == null, "v6: no rover moment for a non rover", null);
                Check(rm.Update(0.1f, true, true, 1f, 0f, tu.roverTiltDeg + 5f, tu) == "tilt", "v6: rover tilt moment", null);
                rm.Reset();
                got = null;
                for (int i = 0; i < 2000 && got == null; i++) got = rm.Update(0.5f, true, true, 0f, 0f, 0f, tu);
                Check(got == "idle", "v6: rover idle moment after roverIdleSec", got ?? "null");

                DataBeeps db = new DataBeeps();
                bool first;
                int beeps = 0, firsts = 0;
                for (int i = 0; i < 400; i++) if (db.Update(0.5f, true, 12f, 4, out first)) { beeps++; if (first) firsts++; }
                Check(beeps == 4 && firsts == 1, "v6: data beeps at most max per send, one first", beeps + " beeps, " + firsts + " first");
                Check(!db.Update(0.5f, false, 12f, 4, out first) && db.Update(0.5f, true, 12f, 4, out first) && first,
                      "v6: a new send starts with a first beep", null);
            }

            // ---- PhraseScene.LinesFor: body variants and the generic fallback ----
            {
                List<string> e = new List<string>();
                PhraseTable bt = PhraseTable.Parse(new List<string> {
                    "clip_key\tevent\tseverity\tband\trole\tscene\tseq\tgender\tphase\tcause\tspanish\tbody\tenglish",
                    "v_1\tchatter\tchatter\tcalm\tmc\tv\t1\tm\tgoofy\tany\tQue tal la vista\t\tHow's the view",
                    "v_2__kerbin\tchatter\tchatter\tcalm\tcrew\tv\t2\tboth\tgoofy\tany\tVeo nibreK\tkerbin\tI see Kerbin",
                    "v_2__mun\tchatter\tchatter\tcalm\tcrew\tv\t2\tboth\tgoofy\tany\tVeo la nuM\tmun\tI see the Mun",
                    "v_2__generic\tchatter\tchatter\tcalm\tcrew\tv\t2\tboth\tgoofy\tany\tVeo el planeta\tgeneric\tI see the planet" }, e);
                PhraseScene sc = bt.Scenes.Count == 1 ? bt.Scenes[0] : null;
                Check(e.Count == 0 && sc != null && sc.HasBody && sc.Bodies.Count == 3 && sc.Lines.Count == 2,
                      "body: variants of one seq form one scene with 2 lines", e.Count + " errors");
                if (sc != null)
                {
                    List<PhraseEntry> mun = sc.LinesFor("mun"), duna = sc.LinesFor("duna"), none = sc.LinesFor(null);
                    Check(mun.Count == 2 && mun[0].ClipKey == "v_1" && mun[1].ClipKey == "v_2__mun", "body: LinesFor(mun) gives the plain line and the Mun variant", null);
                    Check(duna.Count == 2 && duna[1].ClipKey == "v_2__generic", "body: LinesFor of a body without a variant falls back to generic", duna.Count > 1 ? duna[1].ClipKey : "-");
                    Check(none.Count == 2 && none[1].ClipKey == "v_2__generic" && sc.Lines[1].ClipKey == "v_2__generic",
                          "body: LinesFor(null) and Lines are the generic version", null);
                    Check(sc.Phase == "goofy" && bt.ScenesFor("chatter", "calm", "orbit", "spin").Count == 1 && bt.ScenesFor("chatter", "calm", "launch", null).Count == 0,
                          "body: scene phase goofy plays in orbit, not in launch", null);
                }
            }
        }
    }
}
