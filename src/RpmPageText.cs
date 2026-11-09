// Text for the Comms page on a RasterPropMonitor screen (pure, no KSP or Unity).
// RPM shows a page as plain text in a grid of characters, with [#rrggbbaa] colour tags.
// A "[" in the text has to be written as "[[]" and "$&$" starts a variable list, so both are escaped here.
// The newest lines sit at the bottom of the screen, older lines drop off the top, like a radio log.
using System;
using System.Collections.Generic;
using System.Text;

namespace Lonapse
{
    /// <summary>One line in the comms log, as the page gets it from Subtitles.</summary>
    public struct RpmLine
    {
        public string who;     // speaker display name or "MC"
        public string text;
        public bool live;      // still on screen as a subtitle, older lines are drawn dimmer
        public bool note;      // an event note without a speaker (roger beep, SOI change), one short row
    }

    public static class RpmPageText
    {
        public const int MaxRowsPerLine = 4;   // a long line gets at most this many rows, then it ends in dots
        public const string EmptyText = "No comms.";
        public const string OffText = "Subtitles are off in the Lonapse settings.";

        private static readonly float[] OldColor = { 0.55f, 0.58f, 0.64f };
        private static readonly float[] NoteColor = { 0.60f, 0.86f, 0.74f };
        private static readonly float[] FooterColor = { 0.81f, 0.89f, 0.96f };

        /// <summary>The page text for a screen of <paramref name="width"/> by <paramref name="height"/> characters.</summary>
        public static string Build(RpmLine[] lines, int count, int width, int height, string title, bool showSpeaker, bool colors)
        {
            return Build(lines, count, width, height, title, showSpeaker, colors, null);
        }

        /// <summary>With <paramref name="footer"/> (a button hint) on the bottom row of the screen.</summary>
        public static string Build(RpmLine[] lines, int count, int width, int height, string title, bool showSpeaker, bool colors,
                                   string footer)
        {
            bool olderHidden;
            return Build(lines, count, width, height, title, showSpeaker, colors, footer, out olderHidden);
        }

        /// <summary>
        /// <paramref name="olderHidden"/>: older lines did not fit above the ones shown. Scrolling up only goes on while
        /// it is true, so the oldest line never scrolls off the bottom of a half empty screen.
        /// </summary>
        public static string Build(RpmLine[] lines, int count, int width, int height, string title, bool showSpeaker, bool colors,
                                   string footer, out bool olderHidden)
        {
            if (width < 8) width = 8;
            if (height < 2) height = 2;
            if (lines == null) count = 0;
            if (count > (lines != null ? lines.Length : 0)) count = lines.Length;

            var rows = new List<string>(height);
            rows.Add(Escape(string.IsNullOrEmpty(title) ? Title("Comms", width) : Fit(title, width)));
            bool hasFooter = !string.IsNullOrEmpty(footer) && height >= 4;
            int body = height - 1 - (hasFooter ? 2 : 0);   // the footer keeps a blank row above it

            // blocks from the newest line back, until the screen is full
            var blocks = new List<List<string>>();
            int used = 0, oldestShown = count;
            for (int i = count - 1; i >= 0 && used < body; i--)
            {
                List<string> b = Block(lines[i], width, showSpeaker, colors);
                if (b.Count == 0) continue;
                int need = b.Count + (blocks.Count > 0 ? 1 : 0);   // a blank row between two blocks
                if (used + need > body)
                {
                    // the newest line always shows, cut to the rows that are left
                    if (blocks.Count == 0) { b.RemoveRange(body, b.Count - body); blocks.Add(b); used = body; oldestShown = i; }
                    break;
                }
                blocks.Add(b);
                used += need;
                oldestShown = i;
            }
            olderHidden = false;
            for (int j = 0; j < oldestShown && !olderHidden; j++) if (Clean(lines[j].text).Length > 0) olderHidden = true;

            if (blocks.Count == 0)
            {
                rows.Add(string.Empty);
                rows.Add(Tag(OldColor) + " " + Escape(Fit(EmptyText, width - 1)));
            }
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                rows.AddRange(blocks[i]);
                if (i > 0) rows.Add(string.Empty);
            }
            if (hasFooter)
            {
                while (rows.Count < height - 1) rows.Add(string.Empty);
                rows.Add(Tag(FooterColor) + Escape(Fit(footer, width)));
            }
            return string.Join(Environment.NewLine, rows.ToArray());
        }

        // ---- scrolling (RpmCommsPage keeps the position)

        /// <summary>0 .. count - 1: the oldest line always stays on the screen.</summary>
        public static int ClampScroll(int scroll, int count)
        {
            if (count <= 0 || scroll < 0) return 0;
            return scroll > count - 1 ? count - 1 : scroll;
        }

        /// <summary>A scrolled view moves up with the lines that came in, so it keeps showing the same lines.</summary>
        public static int KeepPlace(int scroll, int added, int count)
        {
            if (scroll <= 0 || added <= 0) return scroll;
            return ClampScroll(scroll + added, count);
        }

        /// <summary>Empty rows above the page, for a monitor whose overlay draws over the top rows.</summary>
        public static string Pad(string page, int topRows)
        {
            if (topRows <= 0) return page ?? string.Empty;
            var b = new StringBuilder();
            for (int i = 0; i < Math.Min(topRows, 10); i++) b.Append(Environment.NewLine);
            return b.Append(page).ToString();
        }

        /// <summary>The page when Lonapse shows no subtitles at all.</summary>
        public static string Off(int width, string title)
        {
            if (width < 8) width = 8;
            string head = Escape(string.IsNullOrEmpty(title) ? Title("Comms", width) : Fit(title, width));
            var b = new StringBuilder();
            b.Append(head).Append(Environment.NewLine).Append(Environment.NewLine);
            List<string> wrapped = Wrap(OffText, width - 1, MaxRowsPerLine);
            for (int i = 0; i < wrapped.Count; i++)
            {
                if (i > 0) b.Append(Environment.NewLine);
                b.Append(Tag(OldColor)).Append(' ').Append(Escape(wrapped[i]));
            }
            return b.ToString();
        }

        /// <summary>Speaker row (when shown) and the wrapped text rows of one line, with their colour tags.</summary>
        public static List<string> Block(RpmLine line, int width, bool showSpeaker, bool colors)
        {
            var rows = new List<string>(MaxRowsPerLine + 1);
            string text = Clean(line.text);
            if (text.Length == 0) return rows;
            if (line.note)
            {
                string nc = Tag(line.live ? NoteColor : OldColor);
                foreach (string r in Wrap("* " + text, width, 2)) rows.Add(nc + Escape(r));
                return rows;
            }
            string label = showSpeaker ? SubtitleLayout.SpeakerLabel(Clean(line.who)) : null;
            if (!string.IsNullOrEmpty(label))
            {
                float[] c = SubtitleLayout.SpeakerColor(line.who, colors);
                rows.Add(Tag(line.live ? c : Dim(c)) + Escape(Fit(label, width)));
            }
            string tc = Tag(line.live ? SubtitleLayout.TextColor : OldColor);
            foreach (string r in Wrap(text, width, MaxRowsPerLine)) rows.Add(tc + Escape(r));
            return rows;
        }

        /// <summary>
        /// Word wrap to <paramref name="width"/> characters. A word longer than a row is split. More than
        /// <paramref name="maxRows"/> rows: the last row ends in "...".
        /// </summary>
        public static List<string> Wrap(string text, int width, int maxRows)
        {
            var rows = new List<string>();
            if (string.IsNullOrEmpty(text) || width < 1 || maxRows < 1) return rows;
            var cur = new StringBuilder();
            bool cut = false;
            foreach (string raw in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string word = raw;
                while (word.Length > 0)
                {
                    int room = cur.Length == 0 ? width : width - cur.Length - 1;
                    if (word.Length <= room)
                    {
                        if (cur.Length > 0) cur.Append(' ');
                        cur.Append(word);
                        word = string.Empty;
                    }
                    else if (cur.Length > 0)
                    {
                        rows.Add(cur.ToString());
                        cur.Length = 0;
                    }
                    else
                    {
                        rows.Add(word.Substring(0, width));
                        word = word.Substring(width);
                    }
                    if (rows.Count > maxRows) { cut = true; break; }
                }
                if (cut) break;
            }
            if (!cut && cur.Length > 0) rows.Add(cur.ToString());
            if (rows.Count > maxRows) { rows.RemoveRange(maxRows, rows.Count - maxRows); cut = true; }
            if (cut) rows[maxRows - 1] = Dots(rows[maxRows - 1], width);
            return rows;
        }

        /// <summary>A row that ends in "..." within <paramref name="width"/>, cut at a word when one is near.</summary>
        private static string Dots(string row, int width)
        {
            int keep = Math.Max(0, Math.Min(row.Length, width - 3));
            string head = row.Substring(0, keep);
            int space = head.LastIndexOf(' ');
            if (space > (int)(keep * 0.6f)) head = head.Substring(0, space);
            return head.TrimEnd(' ', ',', ';', ':', '-') + "...";
        }

        /// <summary>"----- Comms -----" across the whole width, like the stock Flight Log page.</summary>
        public static string Title(string name, int width)
        {
            string mid = " " + name + " ";
            if (mid.Length >= width) return Fit(name, width);
            int left = (width - mid.Length) / 2;
            return new string('-', left) + mid + new string('-', width - left - mid.Length);
        }

        /// <summary>Cut to <paramref name="width"/> characters with dots.</summary>
        public static string Fit(string s, int width)
        {
            if (s == null) return string.Empty;
            if (s.Length <= width) return s;
            return width <= 3 ? s.Substring(0, width) : s.Substring(0, width - 3) + "...";
        }

        /// <summary>One line of plain text: no line breaks, typographic quotes and dashes as ASCII, trimmed.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var b = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\r': case '\n': case '\t': b.Append(' '); break;
                    case '‘': case '’': b.Append('\''); break;
                    case '“': case '”': b.Append('"'); break;
                    case '–': case '—': b.Append('-'); break;
                    case '…': b.Append("..."); break;
                    case ' ': b.Append(' '); break;
                    default: b.Append(ch); break;
                }
            }
            return b.ToString().Trim();
        }

        /// <summary>RPM escapes: "[" becomes "[[]", and "$&amp;$" is broken up so RPM never reads it as a variable list.</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.IndexOf('[') >= 0) s = s.Replace("[", "[[]");
            while (s.IndexOf("$&$", StringComparison.Ordinal) >= 0) s = s.Replace("$&$", "$&");
            return s;
        }

        /// <summary>[#rrggbbff] for an RGB triple of 0..1.</summary>
        public static string Tag(float[] rgb)
        {
            return "[#" + Hex(rgb, 0) + Hex(rgb, 1) + Hex(rgb, 2) + "ff]";
        }

        private static string Hex(float[] rgb, int i)
        {
            float v = rgb != null && rgb.Length > i ? rgb[i] : 1f;
            if (float.IsNaN(v)) v = 1f;
            int n = (int)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);
            return n.ToString("x2");
        }

        private static float[] Dim(float[] c)
        {
            return new[] { c[0] * 0.6f, c[1] * 0.6f, c[2] * 0.6f };
        }
    }
}
