// Launch countdown in Lonapse (user idea): launch control counts "onU, soD ... zeiD" (uno to diez reversed), so it
// counts up, then calls liftoff. Pure state machine, tested offline. Time comes from the dt values passed to Update.
using System;

namespace Lonapse
{
    public class LaunchCountdown
    {
        public const int Numbers = 10;
        public const int Liftoff = Numbers + 1;
        public const string AbortKey = "countdown_abort";

        public readonly float leadIn, step;
        public bool Running { get; private set; }
        public int Said { get; private set; }   // last step said: 1..10 numbers, 11 liftoff, 0 none yet
        private float left;

        public LaunchCountdown(float leadIn = 0.6f, float step = 1f)
        {
            this.leadIn = Math.Max(0f, leadIn);
            this.step = Math.Max(0.2f, step);
        }

        public void Start()
        {
            Running = true;
            Said = 0;
            left = leadIn;
        }

        public void Abort()
        {
            Running = false;
        }

        /// <summary>Returns the step to say now (1..10, or Liftoff), or 0 when nothing is due. At most one per call.</summary>
        public int Update(float dt)
        {
            if (!Running) return 0;
            if (dt > 0f) left -= dt;
            if (left > 0f) return 0;
            Said++;
            left += step;
            if (Said >= Liftoff) { Said = Liftoff; Running = false; }
            return Said;
        }

        /// <summary>Clip key in the phrase table for a step.</summary>
        public static string ClipKey(int stepNo)
        {
            if (stepNo == Liftoff) return "countdown_liftoff";
            if (stepNo < 1 || stepNo > Numbers) return null;
            return "countdown_" + stepNo.ToString("00");
        }
    }
}
