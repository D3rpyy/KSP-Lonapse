// The one list of every slot a line can use, for the subtitle templates (SubtitleSlots) and the runtime voice
// (RuntimeLines). A slot that is not here is a pool from pools.tsv. Same lists as NUMBER_SLOTS, NAME_SLOTS and
// PERSON_SLOTS in audio/build_lonapse.py (the tests compare them).
//
//   number  a value from KSP: shown as digits in the subtitle, said in Spanish words by the runtime voice
//   name    a name from KSP: the body, the vessel, a docking target, a science experiment
//   person  a kerbal: other (the one spoken to), self (the speaker), kerbal (the one Mission Control talks to)
//
// day, alt_km, here, vessel and probe are there on every flight (probe: the name of the probe, only in probe mode), also without the runtime voice (FlightSlots). The other
// numbers only reach a recorded line when the runtime voice is on, because the recording cannot say them.
// Pure code: no Unity or KSP references, so it compiles under DC_OFFLINE_TEST.
using System;

namespace Lonapse
{
    public static class Slots
    {
        public static readonly string[] Numbers = { "alt_km", "pe_km", "ap_km", "speed", "fuel", "day", "sci", "funds",
            "inc_deg", "period_min", "t_ap_min", "t_pe_min", "ec", "mono", "parts", "crew", "node_dv", "node_min",
            "funds_total", "sci_total", "rep", "contracts", "dist_m", "close_ms", "height_m", "vspeed_ms",
            "at_home", "flight_no", "missions", "stars", "visits", "landings", "in_warp", "node_h", "soi_h",
            "sci_left", "sci_first", "con_sci", "con_rep", "sunrises", "eva_mins", "ship_m", "jet", "on_eva", "pair_m",
            "link", "apart", "a_eva", "b_eva", "has_solar", "has_rcs", "has_legs", "is_tourist",
            "temp_c", "press_kpa", "grav_g", "sci_flight", "exp_runs", "exp_count", "data_sent" };
        public static readonly string[] Names = { "here", "vessel", "body", "exp", "target", "next", "biome", "flag", "my_ship", "their_ship", "device", "contract", "exp_list", "probe" };
        public static readonly string[] Persons = { "other", "self", "kerbal" };
        /// <summary>Values the director gives every recorded line, with or without the runtime voice.</summary>
        public static readonly string[] Flight = { "day", "here", "vessel", "alt_km", "probe" };

        public static bool IsNumber(string name) { return Array.IndexOf(Numbers, name) >= 0; }
        public static bool IsName(string name) { return Array.IndexOf(Names, name) >= 0; }
        public static bool IsPerson(string name) { return Array.IndexOf(Persons, name) >= 0; }
        public static bool IsFlight(string name) { return Array.IndexOf(Flight, name) >= 0; }

        /// <summary>A number the recording cannot say: only filled with the runtime voice on.</summary>
        public static bool IsRuntimeNumber(string name) { return IsNumber(name) && !IsFlight(name); }

        /// <summary>A slot of another mod: x_ and a-z 0-9 _ (x_cups). Filled from the values the mod sends with its
        /// event, a number or a name. Never a pool.</summary>
        public static bool IsModSlot(string name)
        {
            if (name == null || name.Length < 3 || !name.StartsWith("x_", StringComparison.Ordinal)) return false;
            for (int i = 2; i < name.Length; i++)
            {
                char c = name[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }

        /// <summary>Not a pool: every number, name and person slot.</summary>
        public static bool IsReserved(string name) { return IsNumber(name) || IsName(name) || IsPerson(name) || IsModSlot(name); }

        /// <summary>Keys said with one decimal below 10: km, min, h, deg, m/s, kPa, g and science points (sci, sci_left,
        /// con_sci, sci_flight).</summary>
        public static bool HasDecimals(string key)
        {
            return key.EndsWith("_km", StringComparison.Ordinal) || key.EndsWith("_min", StringComparison.Ordinal) || key.EndsWith("_h", StringComparison.Ordinal)
                   || key.EndsWith("_deg", StringComparison.Ordinal) || key.EndsWith("_ms", StringComparison.Ordinal) || key.EndsWith("_kpa", StringComparison.Ordinal) || key.EndsWith("_g", StringComparison.Ordinal)
                   || key == "sci" || key == "sci_left" || key == "con_sci" || key == "sci_flight";
        }
    }
}
