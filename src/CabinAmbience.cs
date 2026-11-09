// Cabin ambience in IVA (pure, no KSP or Unity): when the fan loop plays and when the power down plays.
// The loop plays while the player is in IVA and the ship has power. When the power goes while the loop plays,
// the power down takes over once. Back in IVA without power the cabin stays quiet: nothing to spin down.
// When the power comes back the loop fades in slowly, as if the fans spin up.
// Power uses the same capacity rule as the power events: a ship with less than minMax EC capacity counts as
// powered, so a part without batteries never goes silent.
namespace Lonapse
{
    public class CabinAmbience
    {
        public const float FadeInIva = 1.5f;       // into IVA
        public const float FadeInPowerBack = 3f;   // the power came back
        public const double OffFrac = 0.001;       // power gone below this fraction of the capacity
        public const double OnFrac = 0.01;         // and back above this one

        public bool Powered { get; private set; }
        /// <summary>The fan loop should play.</summary>
        public bool LoopWanted { get; private set; }
        /// <summary>True for one step: the power just went while the loop played.</summary>
        public bool PowerDownNow { get; private set; }
        /// <summary>Fade-in for the next start of the loop.</summary>
        public float FadeIn { get; private set; }

        private bool quietInIva;   // in IVA without power: the next loop start is the power coming back

        public CabinAmbience()
        {
            Powered = true;
            FadeIn = FadeInIva;
        }

        /// <summary>Power with hysteresis. A sample that is not valid keeps the last state.</summary>
        public static bool NextPowered(bool was, bool valid, double ecFrac, double ecMax, double minMax)
        {
            if (!valid || double.IsNaN(ecFrac) || double.IsNaN(ecMax)) return was;
            if (ecMax < minMax || ecMax <= 0) return true;
            return was ? ecFrac > OffFrac : ecFrac > OnFrac;
        }

        /// <summary>One step. <paramref name="inCabin"/>: IVA, the setting on and sound allowed (gate open, not paused).</summary>
        public void Step(bool inCabin, bool valid, double ecFrac, double ecMax, double minMax)
        {
            PowerDownNow = false;
            Powered = NextPowered(Powered, valid, ecFrac, ecMax, minMax);
            if (!inCabin)
            {
                LoopWanted = false;
                quietInIva = false;
                FadeIn = FadeInIva;
                return;
            }
            if (Powered)
            {
                if (!LoopWanted) FadeIn = quietInIva ? FadeInPowerBack : FadeInIva;
                LoopWanted = true;
                quietInIva = false;
                return;
            }
            if (LoopWanted) PowerDownNow = true;
            LoopWanted = false;
            quietInIva = true;
        }
    }
}
