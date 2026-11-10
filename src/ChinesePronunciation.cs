// Mandarin character readings respelled for the same Spanish voices as other names.
// Pure offline code. The generated Unihan data is loaded only when a Han character is encountered.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    static partial class ChinesePronunciation
    {
        static class Data
        {
            internal static readonly Dictionary<int, string> Readings = BuildReadings();
        }

        internal static bool IsHan(int code)
        {
            return code == 0x3007 || (code >= 0x3400 && code <= 0x4DBF)
                   || (code >= 0x4E00 && code <= 0x9FFF) || (code >= 0xF900 && code <= 0xFAFF)
                   || (code >= 0x20000 && code <= 0x3347F);
        }

        internal static string Spell(int code)
        {
            string pinyin;
            if (!IsHan(code) || !Data.Readings.TryGetValue(code, out pinyin)) return null;
            // Rare syllabic consonants need a vowel so the Spanish voice does not spell letter names.
            if (pinyin == "m") return "Em";
            if (pinyin == "n") return "En";
            if (pinyin == "ng") return "Eng";
            if (pinyin == "hm") return "Jem";
            // Final Spanish spellings, never passed through the English name rules.
            string initial = "", final = pinyin;
            if (pinyin.StartsWith("zh") || pinyin.StartsWith("ch") || pinyin.StartsWith("sh"))
            { initial = "ch"; final = pinyin.Substring(2); }
            else if ("bpmfdtnlgkhjqxzcsryw".IndexOf(pinyin[0]) >= 0)
            {
                final = pinyin.Substring(1);
                switch (pinyin[0])
                {
                    case 'j': case 'q': case 'x': initial = "ch"; break;
                    case 'h': initial = "j"; break;
                    case 'z': case 'c': initial = "ts"; break;
                    case 'w': initial = "u"; break;
                    default: initial = pinyin.Substring(0, 1); break;
                }
            }
            // Pinyin abbreviates these finals. Spanish needs their intervening vowel.
            if (final == "iu") final = "iou";
            if (final == "ui") final = "uei";
            if (final == "ian") final = "ien";
            if (initial == "g" && final.Length > 0 && "ei".IndexOf(final[0]) >= 0) initial = "gu";
            string result = initial + final;
            return char.ToUpperInvariant(result[0]) + result.Substring(1);
        }

        static void Add(Dictionary<int, string> readings, string pinyin, string characters)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                int code = char.ConvertToUtf32(characters, i);
                readings.Add(code, pinyin);
                if (char.IsHighSurrogate(characters[i])) i++;
            }
        }
    }
}
