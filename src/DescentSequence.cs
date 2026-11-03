// Descent and landing as one sequence: Idle, Descent, Touchdown, Done. Pure C#, tested offline.
// Touchdown is debounced: a second touchdown within `debounce` seconds of the last accepted one is ignored.
// Time comes from the dt values passed to Update. Touchdown(now) expects the same clock as those dt values.
using System;

namespace Lonapse
{
    public enum DescentPhase { Idle, Descent, Touchdown, Done }

    public class DescentSequence
    {
        readonly float debounce;
        float clock;
        float lastTouchdown = float.NegativeInfinity;

        public DescentPhase Phase { get; private set; }
        // Kept for the debug overlay. They do not drive transitions.
        public float LastHeight { get; private set; }
        public float LastVs { get; private set; }
        // True only for the Update call that accepted a touchdown (the touchdown sequence plays once).
        public bool JustAccepted { get; private set; }

        public DescentSequence(float debounce = 6f)
        {
            this.debounce = Math.Max(0f, debounce);
            Phase = DescentPhase.Idle;
        }

        // Descent blips run only in the Descent phase.
        public bool BlipsActive { get { return Phase == DescentPhase.Descent; } }

        public DescentPhase Update(float dt, bool descending, float h, float vs, bool landed)
        {
            if (dt > 0f) clock += dt;
            LastHeight = h;
            LastVs = vs;

            // Landing counts as touchdown only while a descent is running. A landing inside the debounce window
            // (a bounce) ends the descent quietly, so standing still later never gives a second touchdown.
            JustAccepted = false;
            if (landed && Phase == DescentPhase.Descent)
            {
                if (Touchdown(clock)) JustAccepted = true;
                else Phase = DescentPhase.Touchdown;
            }

            switch (Phase)
            {
                case DescentPhase.Idle:
                    if (descending) Phase = DescentPhase.Descent;
                    break;

                case DescentPhase.Descent:
                    if (!descending && !landed) Phase = DescentPhase.Idle;
                    break;

                case DescentPhase.Touchdown:
                    if (clock - lastTouchdown >= debounce) Phase = DescentPhase.Done;
                    else if (descending && !landed) Phase = DescentPhase.Descent;
                    break;

                case DescentPhase.Done:
                    if (descending && !landed) Phase = DescentPhase.Descent;
                    break;
            }
            return Phase;
        }

        // Accepts a touchdown only during a descent and outside the debounce window.
        // Returns true when the touchdown was accepted.
        public bool Touchdown(float now)
        {
            if (Phase != DescentPhase.Descent) return false;
            if (now - lastTouchdown < debounce) return false;
            lastTouchdown = now;
            Phase = DescentPhase.Touchdown;
            return true;
        }
    }
}
