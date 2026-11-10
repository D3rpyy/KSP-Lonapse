// Unicode cell positions for RPM's bitmap-font overlay. Pure code for offline layout tests.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lonapse
{
    public struct RpmUnicodeGlyph
    {
        public int code, column, row, cells;
        public uint color;
    }

    public static class RpmUnicodeText
    {
        public static int CodeAt(string text, int index)
        {
            return char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
        }

        public static int CellWidth(int code)
        {
            return ChinesePronunciation.IsHan(code) || (code >= 0x3000 && code <= 0x303F)
                   || (code >= 0xFF01 && code <= 0xFF60) || (code >= 0xFFE0 && code <= 0xFFE6) ? 2 : 1;
        }

        public static int Cells(string text)
        {
            int cells = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int code = CodeAt(text, i);
                cells += CellWidth(code);
                if (code > 0xFFFF) i++;
            }
            return cells;
        }

        public static int PrefixLength(string text, int cells)
        {
            int i = 0;
            while (i < text.Length)
            {
                int code = CodeAt(text, i), width = CellWidth(code);
                if (width > cells) break;
                cells -= width;
                i += code > 0xFFFF ? 2 : 1;
            }
            return i;
        }

        /// <summary>Keep RPM colour tags and escapes. Reserve blank cells where the Unicode overlay draws glyphs.</summary>
        public static string Mask(string page, List<RpmUnicodeGlyph> glyphs)
        {
            glyphs.Clear();
            var result = new StringBuilder(page.Length);
            int column = 0, row = 0;
            uint color = 0xFFFFFFFF;
            for (int i = 0; i < page.Length;)
            {
                if (page[i] == '\r') { result.Append(page[i++]); continue; }
                if (page[i] == '\n') { result.Append(page[i++]); column = 0; row++; color = 0xFFFFFFFF; continue; }
                if (page[i] == '[' && i + 2 < page.Length && page.Substring(i, 3) == "[[]")
                { result.Append("[[]"); column++; i += 3; continue; }
                uint parsed;
                if (page[i] == '[' && i + 10 < page.Length && page[i + 1] == '#' && page[i + 10] == ']'
                    && uint.TryParse(page.Substring(i + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                { result.Append(page, i, 11); color = parsed; i += 11; continue; }
                int code = CodeAt(page, i), cells = CellWidth(code), length = code > 0xFFFF ? 2 : 1;
                if (code > 0x024F)
                {
                    glyphs.Add(new RpmUnicodeGlyph { code = code, column = column, row = row, cells = cells, color = color });
                    result.Append(' ', cells);
                }
                else result.Append(page, i, length);
                column += cells;
                i += length;
            }
            return result.ToString();
        }
    }
}
