// Full throttle and throttle cutoff detection (plan v2, req 4). Pure state machine, tested offline.
// Full: throttle at or above fullAt for holdFull seconds, after a rise from below midLevel.
// Cut: throttle below cutAt for holdCut seconds, after being above midLevel.
// A repeat of the same event within minGap seconds is dropped. The arm is reset in that case too.
using System;

namespace Lonapse
{
    public class ThrottleWatch
    {
        readonly float fullAt, cutAt, holdFull, holdCut, minGap, midLevel;
        float clock;
        float fullTimer, cutTimer;
        bool lowSeen, highSeen;
        float lastFull = float.NegativeInfinity;
        float lastCut = float.NegativeInfinity;

        public ThrottleWatch(float fullAt = 0.98f, float cutAt = 0.05f, float holdFull = 0.5f,
                             float holdCut = 0.3f, float minGap = 20f, float midLevel = 0.5f)
        {
            this.fullAt = fullAt;
            this.cutAt = cutAt;
            this.holdFull = holdFull;
            this.holdCut = holdCut;
            this.minGap = minGap;
            this.midLevel = midLevel;
        }

        // Returns "full", "cut" or null. Call once per frame with the throttle in 0 to 1.
        // `airborne` means "flying or about to": pass true for PRELAUNCH and any flight situation,
        // false only for LANDED and SPLASHED. Otherwise the full-throttle callout at launch never fires.
        public string Update(float dt, float throttle, bool airborne)
        {
            if (dt > 0f) clock += dt;

            if (!airborne)
            {
                Reset();
                return null;
            }

            // Inclusive on purpose: KSP puts a new vessel on the pad at exactly 50 % throttle,
            // and 50 % to 100 % at launch must count as a rise to full throttle.
            if (throttle <= midLevel) lowSeen = true;
            if (throttle >= midLevel) highSeen = true;

            // Full throttle timer.
            if (throttle >= fullAt && lowSeen) fullTimer += dt;
            else fullTimer = 0f;

            // Cutoff timer.
            if (throttle < cutAt && highSeen) cutTimer += dt;
            else cutTimer = 0f;

            if (fullTimer >= holdFull)
            {
                lowSeen = false;
                fullTimer = 0f;
                if (clock - lastFull >= minGap) { lastFull = clock; return "full"; }
                return null;
            }

            if (cutTimer >= holdCut)
            {
                highSeen = false;
                cutTimer = 0f;
                if (clock - lastCut >= minGap) { lastCut = clock; return "cut"; }
                return null;
            }

            return null;
        }

        public void Reset()
        {
            fullTimer = 0f;
            cutTimer = 0f;
            lowSeen = false;
            highSeen = false;
        }
    }
}
