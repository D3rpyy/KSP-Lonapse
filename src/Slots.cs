// The one list of every slot a line can use, for the subtitle templates (SubtitleSlots) and the runtime voice
// (RuntimeLines). A slot that is not here is a pool from pools.tsv. Same lists as NUMBER_SLOTS, NAME_SLOTS and
// PERSON_SLOTS in audio/build_lonapse.py (the tests compare them).
//
//   number  a value from KSP: shown as digits in the subtitle, said in Spanish words by the runtime voice
//   name    a name from KSP: the body, the vessel, a docking target, a science experiment
//   person  a kerbal: other (the one spoken to), self (the speaker), kerbal (the one Mission Control talks to)
//
// day, alt_km, here and vessel are there on every flight, also without the runtime voice (FlightSlots). The other
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
            "at_home", "flight_no", "missions", "stars", "visits", "landings" };
        public static readonly string[] Names = { "here", "vessel", "body", "exp", "target" };
        public static readonly string[] Persons = { "other", "self", "kerbal" };
        /// <summary>Values the director gives every recorded line, with or without the runtime voice.</summary>
        public static readonly string[] Flight = { "day", "here", "vessel", "alt_km" };

        public static bool IsNumber(string name) { return Array.IndexOf(Numbers, name) >= 0; }
        public static bool IsName(string name) { return Array.IndexOf(Names, name) >= 0; }
        public static bool IsPerson(string name) { return Array.IndexOf(Persons, name) >= 0; }
        public static bool IsFlight(string name) { return Array.IndexOf(Flight, name) >= 0; }

        /// <summary>A number the recording cannot say: only filled with the runtime voice on.</summary>
        public static bool IsRuntimeNumber(string name) { return IsNumber(name) && !IsFlight(name); }

        /// <summary>Not a pool: every number, name and person slot.</summary>
        public static bool IsReserved(string name) { return IsNumber(name) || IsName(name) || IsPerson(name); }

        /// <summary>Keys said with one decimal below 10: km, min, deg, m/s and science points.</summary>
        public static bool HasDecimals(string key)
        {
            return key.EndsWith("_km", StringComparison.Ordinal) || key.EndsWith("_min", StringComparison.Ordinal)
                   || key.EndsWith("_deg", StringComparison.Ordinal) || key.EndsWith("_ms", StringComparison.Ordinal) || key == "sci";
        }
    }
}
