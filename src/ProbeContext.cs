// Context choices are pure so the same rules can be checked without KSP.
namespace Lonapse
{
    public static class ProbeContext
    {
        public static string Topic(double ec, bool recovering, bool node, bool spinning, bool landed, bool hasBiome)
        {
            if (recovering && ec > 25) return "recovery";
            if (ec < 15) return "power";
            if (spinning) return "spin";
            if (node) return "node";
            if (landed && hasBiome) return "biome";
            return "orbit";
        }
        public static string Mood(string topic)
        {
            switch (topic) {
                case "power": return "sleepy";
                case "spin": return "scared";
                case "node": case "biome": return "curious";
                case "repeat": return "bored";
                default: return "proud";
            }
        }
        public static string StressCause(string cause)
        {
            switch (cause) {
                case "q": case "spin": case "heat": case "gforce": case "fuel": case "ground":
                case "plasma": case "impulse": case "engine_out": return cause;
                default: return "any";
            }
        }
        public static System.Collections.Generic.Dictionary<string, double> StressValues(FlightSample sample, float stress, bool panic)
        {
            var values = new System.Collections.Generic.Dictionary<string, double>();
            values["x_panic"] = panic ? 1 : 0;
            values["x_stress"] = DcMath.Clamp01(stress) * 100;
            if (!sample.valid) return values;
            Add(values, "x_q_kpa", sample.qKPa);
            Add(values, "x_spin_rpm", sample.omega * 60 / (2 * System.Math.PI));
            Add(values, "x_heat_pct", sample.heatRatio * 100);
            Add(values, "x_load_g", sample.g);
            Add(values, "x_height_m", sample.h);
            Add(values, "x_descent_ms", System.Math.Max(0, -sample.vs));
            Add(values, "x_fuel_pct", sample.fuelFrac * 100);
            return values;
        }
        static void Add(System.Collections.Generic.Dictionary<string, double> values, string key, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value)) values[key] = System.Math.Max(0, value);
        }
        public static bool StressReadingFresh(System.Collections.Generic.IDictionary<string, double> before,
            System.Collections.Generic.IDictionary<string, double> after, string cause)
        {
            string key;
            switch (cause) {
                case "q": key = "x_q_kpa"; break;
                case "spin": key = "x_spin_rpm"; break;
                case "heat": key = "x_heat_pct"; break;
                case "gforce": key = "x_load_g"; break;
                case "fuel": key = "x_fuel_pct"; break;
                case "ground": key = "x_height_m"; break;
                default: return true;
            }
            double a, b;
            return !before.TryGetValue(key, out a) || after.TryGetValue(key, out b)
                && System.Math.Abs(a - b) <= System.Math.Max(1, System.Math.Abs(a) * 0.35);
        }

        public static string Personality(string core)
        {
            switch (core) {
                case "okto": return "precision";
                case "stayputnik": case "qbe": return "brave";
                case "rovemate": return "rover";
                case "mk2drone": return "confident";
                default: return "neutral";
            }
        }
    }
}
