// Current crew of the active vessel and each kerbal's stable voice profile.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lonapse
{
    public enum Band { Calm = 0, Tense = 1, Panic = 2 }

    public class KerbalVoice
    {
        public const float Hysteresis = 0.04f;
        public static readonly string[] BandNames = { "calm", "tense", "panic" };

        public string name;           // raw name: the voice hash uses this, so voices stay stable
        public string displayName;    // localized for logs and the overlay (#autoLOC_... names of stock kerbals)
        public bool isPilot;
        /// <summary>Not on the active vessel: a kerbal of the EVA group (in voices) or of another vessel nearby
        /// (in CrewRoster.radio). Set by CrewRoster.Rebuild.</summary>
        public bool away;
        /// <summary>On EVA (its vessel is an EVA kerbal).</summary>
        public bool onEva;
        /// <summary>Its vessel. Two kerbals with the same id share a cabin (intercom), else they use the radio.</summary>
        public Guid vesselId;
        public float courage, stupidity;
        public bool isBadass;
        public bool outDueToG;

        public float basePitch;
        /// <summary>Set by CrewRoster.Rebuild: keeps this voice apart from the others aboard (DcMath.SpreadPitches).</summary>
        public float pitchSpread = 1f;
        /// <summary>The pitch to play with: basePitch spread from the rest of the crew.</summary>
        public float Pitch { get { return basePitch * pitchSpread; } }
        public float tremRate;
        public float timbre;
        public Random rng;
        public uint rngSeed;

        public float exponent;
        public float tenseAt, panicAt;

        public float sk;       // personal stress
        public Band band = Band.Calm;
        public bool isFemale;

        /// <summary>Persona flags and trait key (plan v4). Persona.None until ApplyPersona runs.</summary>
        public Persona persona = Persona.None;
        /// <summary>True when the veteran stress tweak is in exponent and panicAt (personalityEffects on).</summary>
        public bool personaEffects;

        /// <summary>
        /// Stores the persona and sets exponent and panicAt again from courage, stupidity and isBadass.
        /// With effects on, a veteran gets panicAt +0.04 and exponent x0.9. Safe to call again (no stacking).
        /// </summary>
        public void ApplyPersona(Persona p, bool effects)
        {
            persona = p ?? Persona.None;
            personaEffects = effects;
            bool vet = effects && persona.veteran;
            exponent = Persona.VeteranExponent(Exponent(courage, stupidity, isBadass), vet);
            panicAt = Persona.VeteranPanicAt(PanicAt(courage, stupidity, isBadass), vet);
        }

        /// <summary>The badass persona counts (faces, scene choice): the persona flag with personalityEffects on.</summary>
        public bool BadassEffective { get { return personaEffects && persona != null && persona.badass; } }

        /// <summary>Intercom speaker A weight: 1 + 0.4 x stupidity with effects on, else 1.</summary>
        public float IntercomWeight(bool effects)
        {
            return effects ? persona.IntercomWeight : 1f;
        }

        /// <summary>Key for per-speaker cooldowns in Pacing (plan 4.3: "sk:&lt;name&gt;").</summary>
        public string CooldownKey { get { return "sk:" + (name ?? "?"); } }

        /// <summary>
        /// Same as Create, plus gender (req 8). The gender pitch factor is applied after the hash, so the
        /// hash-based profile of a kerbal stays the same. The defaults match Tuning.genderPitchFemale/Male.
        /// </summary>
        public static KerbalVoice Create(string name, float courage, float stupidity, bool isBadass, bool female,
                                         float pitchFemale = 1.06f, float pitchMale = 0.96f)
        {
            KerbalVoice k = Create(name, courage, stupidity, isBadass);
            k.isFemale = female;
            k.basePitch *= female ? pitchFemale : pitchMale;
            return k;
        }

        public static KerbalVoice Create(string name, float courage, float stupidity, bool isBadass)
        {
            KerbalVoice k = new KerbalVoice();
            k.name = name ?? "?";
            k.displayName = k.name;
            k.courage = DcMath.Clamp01(courage);
            k.stupidity = DcMath.Clamp01(stupidity);
            k.isBadass = isBadass;
            double u0 = StableHash.Unit(k.name, 0);
            double u1 = StableHash.Unit(k.name, 1);
            double u2 = StableHash.Unit(k.name, 2);
            k.rngSeed = StableHash.Hash(k.name, 3);
            k.basePitch = DcMath.Lerp(0.90f, 1.12f, (float)u0);
            k.tremRate = 5.5f + DcMath.Lerp(-0.8f, 0.8f, (float)u1);
            k.timbre = DcMath.Lerp(0.85f, 1.15f, (float)u2);
            k.rng = new Random(unchecked((int)k.rngSeed));
            k.exponent = Exponent(k.courage, k.stupidity, isBadass);
            k.tenseAt = TenseAt(k.courage, k.stupidity);
            k.panicAt = PanicAt(k.courage, k.stupidity, isBadass);
            return k;
        }

        public static float Exponent(float courage, float stupidity, bool badass)
        {
            float e = DcMath.Clamp(1f + 1.0f * (0.5f - courage) + 0.6f * (stupidity - 0.5f), 0.4f, 1.8f);
            if (badass) e *= 0.75f;
            return e;
        }

        public static float TenseAt(float courage, float stupidity)
        {
            return DcMath.Clamp(0.30f + 0.15f * (courage - 0.5f) - 0.10f * (stupidity - 0.5f), 0.18f, 0.45f);
        }

        public static float PanicAt(float courage, float stupidity, bool badass)
        {
            return DcMath.Clamp(0.68f + 0.20f * (courage - 0.5f) - 0.15f * (stupidity - 0.5f) + (badass ? 0.08f : 0f), 0.50f, 0.90f);
        }

        /// <summary>sk = 1 - (1 - stress)^e. e &lt; 1 damps (brave), e &gt; 1 amplifies (nervous).</summary>
        public static float PersonalStress(float stress, float e)
        {
            return DcMath.Clamp01(1f - (float)Math.Pow(1.0 - DcMath.Clamp01(stress), e));
        }

        /// <summary>
        /// Band with a ceiling. maxBand Panic gives the plain NextBand. Tense as ceiling gives Tense at most (req 10).
        /// </summary>
        public static Band NextBand(Band current, float sk, float tenseAt, float panicAt, Band maxBand)
        {
            Band b = NextBand(current, sk, tenseAt, panicAt);
            return b > maxBand ? maxBand : b;
        }

        /// <summary>
        /// Launch cap (req 10): the highest band allowed now. Tense while the cap is on, the height is below capAlt,
        /// and there is no real failure. Otherwise Panic (no cap).
        /// </summary>
        public static Band LaunchMaxBand(bool capOn, double heightM, double capAlt, bool realFailure)
        {
            return (capOn && !realFailure && heightM < capAlt) ? Band.Tense : Band.Panic;
        }

        public static Band NextBand(Band current, float sk, float tenseAt, float panicAt)
        {
            float h = Hysteresis;
            switch (current)
            {
                case Band.Calm:
                    if (sk >= panicAt) return Band.Panic;
                    if (sk >= tenseAt) return Band.Tense;
                    return Band.Calm;
                case Band.Tense:
                    if (sk >= panicAt) return Band.Panic;
                    if (sk < tenseAt - h) return Band.Calm;
                    return Band.Tense;
                default:
                    if (sk >= panicAt - h) return Band.Panic;
                    if (sk < tenseAt - h) return Band.Calm;
                    return Band.Tense;
            }
        }

        public void UpdateStress(float vesselStress)
        {
            UpdateStress(vesselStress, Band.Panic);
        }

        /// <summary>maxBand is the launch cap from LaunchMaxBand. Panic means no cap.</summary>
        public void UpdateStress(float vesselStress, Band maxBand)
        {
            UpdateStress(vesselStress, maxBand, 0f);
        }

        /// <summary>panicOffset raises the panic threshold (escalation, req 11). 0 means no offset.</summary>
        public void UpdateStress(float vesselStress, Band maxBand, float panicOffset)
        {
            sk = PersonalStress(vesselStress, exponent);
            band = NextBand(band, sk, tenseAt, Math.Min(1.05f, panicAt + Math.Max(0f, panicOffset)), maxBand);
        }

        public string BandName { get { return BandNames[(int)band]; } }

        /// <summary>Clip folder for a band: crew/calm, crew/tense or crew/panic.</summary>
        public static string FolderFor(Band b) { return "crew/" + BandNames[(int)b]; }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} (courage {1:0.00}, stupidity {2:0.00}{3}, pitch {4:0.000}, trem {5:0.00} Hz, timbre {6:0.00}, e {7:0.00}, tense {8:0.00}, panic {9:0.00}, persona {10})",
                displayName + (isPilot ? " [pilot]" : ""), courage, stupidity, isBadass ? ", badass" : "", basePitch, tremRate, timbre, exponent, tenseAt, panicAt,
                persona + (personaEffects ? "" : " (effects off)"));
        }

        /// <summary>Weighted speaker choice: 0.3 + sk, previous speaker x0.4 when there are others.</summary>
        public static KerbalVoice Pick(List<KerbalVoice> list, KerbalVoice previous, Random rng)
        {
            return Pick(list, previous, rng, null);
        }

        /// <summary>Same as Pick, but never returns <paramref name="exclude"/>.</summary>
        public static KerbalVoice Pick(List<KerbalVoice> list, KerbalVoice previous, Random rng, KerbalVoice exclude)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (Eligible(list[i], exclude)) n++;
            if (n == 0) return null;
            double total = 0;
            for (int i = 0; i < list.Count; i++)
            {
                KerbalVoice k = list[i];
                if (!Eligible(k, exclude)) continue;
                total += Weight(k, previous, n);
            }
            double r = rng.NextDouble() * total;
            KerbalVoice last = null;
            for (int i = 0; i < list.Count; i++)
            {
                KerbalVoice k = list[i];
                if (!Eligible(k, exclude)) continue;
                last = k;
                r -= Weight(k, previous, n);
                if (r <= 0) return k;
            }
            return last;
        }

        private static bool Eligible(KerbalVoice k, KerbalVoice exclude)
        {
            return k != null && !k.outDueToG && !ReferenceEquals(k, exclude);
        }

        /// <summary>Two different conscious kerbals for an intercom chat. False when fewer than 2 can speak.</summary>
        public static bool PickPair(List<KerbalVoice> list, Random rng, out KerbalVoice a, out KerbalVoice b)
        {
            a = Pick(list, null, rng, null);
            b = a != null ? Pick(list, null, rng, a) : null;
            return a != null && b != null;
        }

        /// <summary>One-word callouts (staging, parachutes, docking): the first conscious pilot, else anyone conscious.</summary>
        /// <summary>At least one voice in the list is in the panic band.</summary>
        public static bool AnyPanic(List<KerbalVoice> list)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].band == Band.Panic) return true;
            return false;
        }

        public static KerbalVoice PickCallout(List<KerbalVoice> list, Random rng)
        {
            for (int i = 0; i < list.Count; i++)
            {
                KerbalVoice k = list[i];
                if (k != null && k.isPilot && !k.outDueToG) return k;
            }
            return Pick(list, null, rng, null);
        }

        public static int SpeakableCount(List<KerbalVoice> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && !list[i].outDueToG) n++;
            return n;
        }

        /// <summary>Station/base relaxed mix: one band calmer (panic -> tense, tense -> calm).</summary>
        public static Band Calmer(Band b)
        {
            return b == Band.Panic ? Band.Tense : Band.Calm;
        }

        private static double Weight(KerbalVoice k, KerbalVoice previous, int candidates)
        {
            double w = 0.3 + k.sk;
            if (candidates > 1 && ReferenceEquals(k, previous)) w *= 0.4;
            return w;
        }

        private static bool TraitOk(KerbalVoice k, string trait, Predicate<KerbalVoice> ok)
        {
            return k != null && !k.outDueToG && k.persona.TraitMatches(trait) && (ok == null || ok(k));
        }

        /// <summary>
        /// Weighted pick (same weights as Pick) among conscious kerbals with the trait (any or empty: everyone)
        /// that pass <paramref name="ok"/> (null: no extra filter). Null when nobody qualifies.
        /// </summary>
        public static KerbalVoice PickWithTrait(List<KerbalVoice> list, string trait, KerbalVoice previous, Random rng,
                                                Predicate<KerbalVoice> ok)
        {
            if (list == null) return null;
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (TraitOk(list[i], trait, ok)) n++;
            if (n == 0) return null;
            double total = 0;
            for (int i = 0; i < list.Count; i++) if (TraitOk(list[i], trait, ok)) total += Weight(list[i], previous, n);
            double r = rng.NextDouble() * total;
            KerbalVoice last = null;
            for (int i = 0; i < list.Count; i++)
            {
                KerbalVoice k = list[i];
                if (!TraitOk(k, trait, ok)) continue;
                last = k;
                r -= Weight(k, previous, n);
                if (r <= 0) return k;
            }
            return last;
        }

        /// <summary>True when a conscious kerbal with the trait passes ok. Any or empty trait: anyone conscious.</summary>
        public static bool AnyWithTrait(List<KerbalVoice> list, string trait, Predicate<KerbalVoice> ok)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (TraitOk(list[i], trait, ok)) return true;
            return false;
        }

        /// <summary>
        /// Intercom speaker A: weight (0.3 + sk) x IntercomWeight(effects), so dim kerbals start more chats.
        /// Skips unconscious kerbals, <paramref name="exclude"/> and those failing ok. Null when nobody qualifies.
        /// </summary>
        public static KerbalVoice PickIntercomA(List<KerbalVoice> list, Random rng, bool effects, KerbalVoice exclude,
                                                Predicate<KerbalVoice> ok)
        {
            if (list == null) return null;
            double total = 0;
            for (int i = 0; i < list.Count; i++)
            {
                KerbalVoice k = list[i];
                if (Eligible(k, exclude) && (ok == null || ok(k))) total += (0.3 + k.sk) * k.IntercomWeight(effects);
            }
            if (total <= 0) return null;
            double r = rng.NextDouble() * total;
            KerbalVoice last = null;
            for (int i = 0; i < list.Count; i++)
            {
                KerbalVoice k = list[i];
                if (!Eligible(k, exclude) || (ok != null && !ok(k))) continue;
                last = k;
                r -= (0.3 + k.sk) * k.IntercomWeight(effects);
                if (r <= 0) return k;
            }
            return last;
        }
    }

#if !DC_OFFLINE_TEST
    /// <summary>Display names: stock scenario kerbals and vessels carry #autoLOC keys.</summary>
    public static class Names
    {
        public static string Localize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            if (s[0] != '#') return s;
            try
            {
                string f = KSP.Localization.Localizer.Format(s);
                return string.IsNullOrEmpty(f) ? s : f;
            }
            catch (Exception) { return s; }
        }

        public static string Of(Vessel v)
        {
            if (v == null) return "none";
            try { return Localize(v.vesselName); } catch (Exception) { return "?"; }
        }
    }

    public class CrewRoster
    {
        /// <summary>The kerbals who speak the scenes: the crew of the active vessel, and on EVA the EVA group.</summary>
        public readonly List<KerbalVoice> voices = new List<KerbalVoice>();
        private readonly List<ProtoCrewMember> members = new List<ProtoCrewMember>();
        /// <summary>Kerbals of other vessels nearby (a ship, a lander, an EVA kerbal further out). They only talk
        /// over the radio with the kerbals of voices, never in the scenes of the active vessel.</summary>
        public readonly List<KerbalVoice> radio = new List<KerbalVoice>();
        private readonly List<ProtoCrewMember> radioMembers = new List<ProtoCrewMember>();

        /// <summary>The kerbal is in voices or radio.</summary>
        public bool InGroup(KerbalVoice k) { return k != null && (voices.Contains(k) || radio.Contains(k)); }

        /// <summary>A kerbal of voices or radio by raw name, null when none.</summary>
        public KerbalVoice Find(string name)
        {
            if (name == null) return null;
            KerbalVoice k = voices.Find(x => x != null && x.name == name);
            return k ?? radio.Find(x => x != null && x.name == name);
        }

        /// <summary>voices and radio together.</summary>
        public int GroupCount { get { return voices.Count + radio.Count; } }
        public bool dirty = true;
        public KerbalVoice lastSpeaker;
        /// <summary>Band ceiling for UpdateStress (launch cap). Set it from LaunchMaxBand each tick. Panic means no cap.</summary>
        public Band maxBand = Band.Panic;
        /// <summary>Escalation offset on every panic threshold (req 11), set by the director. 0 means none.</summary>
        public float panicOffset;
        /// <summary>Gender pitch factors (req 8), set from Tuning by the addon. Defaults match Tuning.</summary>
        public float genderPitchFemale = 1.06f, genderPitchMale = 0.96f;
        /// <summary>
        /// Settings.personalityEffects (plan v4). Set it from the settings by the addon, like the gender pitch.
        /// Change it at runtime with SetPersonalityEffects so the voices update at once.
        /// </summary>
        public bool personalityEffects = true;

        /// <summary>Turns the persona effects on or off and applies the change to every voice now.</summary>
        public void SetPersonalityEffects(bool on)
        {
            if (on == personalityEffects) return;
            personalityEffects = on;
            for (int i = 0; i < voices.Count; i++) voices[i].ApplyPersona(voices[i].persona, on);
        }

        public int Count { get { return voices.Count; } }

        /// <summary>At least one kerbal is in the panic band (drives the soft master alarm).</summary>
        public bool AnyPanic
        {
            get { return KerbalVoice.AnyPanic(voices); }
        }

        public void Clear()
        {
            voices.Clear();
            members.Clear();
            radio.Clear();
            radioMembers.Clear();
            lastSpeaker = null;
            dirty = true;
        }

        /// <summary>Rebuild from the vessel crew. Returns true when the set of crew names changed.</summary>
        public bool Rebuild(Vessel v) { return Rebuild(v, null, null); }

        /// <summary>The kerbal in control: the first voice that is not a nearby EVA kerbal. Null without crew.</summary>
        public KerbalVoice Own
        {
            get
            {
                for (int i = 0; i < voices.Count; i++) if (!voices[i].away) return voices[i];
                return null;
            }
        }

        /// <summary>Rebuild from the vessel crew plus the kerbals of <paramref name="nearby"/> (other EVA kerbals close
        /// to an active EVA kerbal, they speak the scenes too) and of <paramref name="radioOnly"/> (other vessels
        /// nearby, they only use the radio). Both may be null. Returns true when the set of crew names changed.</summary>
        public bool Rebuild(Vessel v, IList<Vessel> nearby, IList<Vessel> radioOnly)
        {
            dirty = false;
            List<KerbalVoice> old = new List<KerbalVoice>(voices);
            old.AddRange(radio);
            voices.Clear();
            members.Clear();
            radio.Clear();
            radioMembers.Clear();
            if (v == null) return old.Count > 0;
            bool changed = false;
            if (!AddCrew(v, false, voices, members, old, ref changed)) return old.Count > 0;
            if (nearby != null) foreach (Vessel n in nearby) AddCrew(n, true, voices, members, old, ref changed);
            if (radioOnly != null) foreach (Vessel n in radioOnly) AddCrew(n, true, radio, radioMembers, old, ref changed);
            SpreadVoices();
            if (!voices.Contains(lastSpeaker)) lastSpeaker = null;
            // same count and no new name: the same set (names are unique)
            if (voices.Count + radio.Count != old.Count) changed = true;
            if (changed)
            {
                if (voices.Count == 0) Log.Info("Roster: no crew on " + Names.Of(v));
                else
                {
                    Log.Info("Roster for " + Names.Of(v) + ": " + voices.Count + " kerbal(s)");
                    for (int i = 0; i < voices.Count; i++)
                        Log.Info("  voice " + voices[i].Describe() + (voices[i].away ? ", nearby on EVA" : ", aboard") + " pitch " + voices[i].Pitch.ToString("0.000"));
                    for (int i = 0; i < radio.Count; i++)
                        Log.Info("  voice " + radio[i].Describe() + (radio[i].onEva ? ", on EVA" : ", in another vessel") + " nearby, radio only, pitch " + radio[i].Pitch.ToString("0.000"));
                }
            }
            return changed;
        }

        /// <summary>Adds the crew of vessel v to list (and its members). Reuses the voice of a kerbal from old, so its
        /// rng and stress stay. False when the crew cannot be read.</summary>
        private bool AddCrew(Vessel v, bool away, List<KerbalVoice> list, List<ProtoCrewMember> mem, List<KerbalVoice> old, ref bool changed)
        {
            if (v == null) return false;
            List<ProtoCrewMember> crew = null;
            bool eva = false;
            Guid id = Guid.Empty;
            try { crew = v.GetVesselCrew(); eva = v.isEVA; id = v.id; }
            catch (Exception e) { Log.Exception("CrewRoster.Rebuild", e); }
            if (crew == null) return false;
            for (int i = 0; i < crew.Count; i++)
            {
                ProtoCrewMember pcm = crew[i];
                if (pcm == null) continue;
                string nm = pcm.name;
                if (voices.Exists(x => x.name == nm) || radio.Exists(x => x.name == nm)) continue;
                KerbalVoice kv = null;
                for (int j = 0; j < old.Count; j++)
                    if (old[j].name == nm) { kv = old[j]; break; }
                if (kv == null)
                {
                    bool female = pcm.gender == ProtoCrewMember.Gender.Female;
                    kv = KerbalVoice.Create(nm, pcm.courage, pcm.stupidity, pcm.isBadass, female, genderPitchFemale, genderPitchMale);
                    kv.displayName = Names.Localize(nm);
                    changed = true;
                }
                try { kv.isPilot = pcm.trait == "Pilot"; } catch (Exception) { kv.isPilot = false; }
                // persona every rebuild: veteran and trait can change between flights, ApplyPersona does not stack
                Persona p;
                try { p = new Persona(pcm.courage, pcm.stupidity, pcm.isBadass, pcm.veteran, pcm.trait); }
                catch (Exception e) { Log.Exception("CrewRoster.Persona", e); p = new Persona(kv.courage, kv.stupidity, kv.isBadass, false, null); }
                kv.ApplyPersona(p, personalityEffects);
                kv.away = away;
                kv.onEva = eva;
                kv.vesselId = id;
                list.Add(kv);
                mem.Add(pcm);
            }
            return true;
        }

        /// <summary>Minimum pitch gap between two kerbals of the same gender aboard (about 1.5 semitones).</summary>
        public const float CrewPitchGap = 0.09f;

        /// <summary>Per gender (one voice model each): the pitches of the crew aboard pushed apart. Played the same on
        /// recorded and runtime lines, so a kerbal sounds the same in both.</summary>
        private void SpreadVoices()
        {
            foreach (bool f in new[] { false, true })
            {
                var group = voices.FindAll(k => k.isFemale == f);
                group.AddRange(radio.FindAll(k => k.isFemale == f));
                var pitch = new float[group.Count];
                for (int i = 0; i < group.Count; i++) pitch[i] = group[i].basePitch;
                float[] mul = DcMath.SpreadPitches(pitch, CrewPitchGap, 0.82f, 1.28f);
                for (int i = 0; i < group.Count; i++) group[i].pitchSpread = mul[i];
            }
        }

        public void UpdateStress(float stress)
        {
            for (int i = 0; i < voices.Count; i++)
            {
                voices[i].UpdateStress(stress, maxBand, panicOffset);
                try { voices[i].outDueToG = members[i] != null && members[i].outDueToG; }
                catch (Exception) { voices[i].outDueToG = false; }
            }
            for (int i = 0; i < radio.Count; i++)
            {
                radio[i].UpdateStress(stress, maxBand, panicOffset);
                try { radio[i].outDueToG = radioMembers[i] != null && radioMembers[i].outDueToG; }
                catch (Exception) { radio[i].outDueToG = false; }
            }
        }

        public int SpeakableCount() { return KerbalVoice.SpeakableCount(voices); }

        public bool AnyoneCanSpeak()
        {
            for (int i = 0; i < voices.Count; i++) if (!voices[i].outDueToG) return true;
            return false;
        }

        public KerbalVoice PickSpeaker(Random rng)
        {
            KerbalVoice k = KerbalVoice.Pick(voices, lastSpeaker, rng);
            if (k != null) lastSpeaker = k;
            return k;
        }

        /// <summary>
        /// Speaker for a trait scene: a conscious kerbal with the trait that passes ok (null: no extra filter).
        /// Trait any or empty: anyone. Null when nobody has it (skip the scene). Sets lastSpeaker like PickSpeaker.
        /// ok may be called twice per kerbal, so keep it free of side effects.
        /// </summary>
        public KerbalVoice PickWithTrait(string trait, Random rng, Predicate<KerbalVoice> ok)
        {
            KerbalVoice k = KerbalVoice.PickWithTrait(voices, trait, lastSpeaker, rng, ok);
            if (k != null) lastSpeaker = k;
            return k;
        }

        /// <summary>True when somebody conscious has the trait and passes ok. Does not touch lastSpeaker.</summary>
        public bool AnyWithTrait(string trait, Predicate<KerbalVoice> ok)
        {
            return KerbalVoice.AnyWithTrait(voices, trait, ok);
        }

        /// <summary>Intercom speaker A weight: 1 + 0.4 x stupidity when personalityEffects is on, else 1. Null gives 0.</summary>
        public float IntercomWeight(KerbalVoice v)
        {
            return v == null ? 0f : v.IntercomWeight(personalityEffects);
        }

        /// <summary>Intercom speaker A by (0.3 + sk) x IntercomWeight. Null when nobody qualifies. Does not touch lastSpeaker.</summary>
        public KerbalVoice PickIntercomA(Random rng, KerbalVoice exclude, Predicate<KerbalVoice> ok)
        {
            return KerbalVoice.PickIntercomA(voices, rng, personalityEffects, exclude, ok);
        }

        public float MaxSk()
        {
            float m = 0f;
            for (int i = 0; i < voices.Count; i++) if (voices[i].sk > m) m = voices[i].sk;
            return m;
        }
    }
#endif
}
