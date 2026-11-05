// Pure layout, colour and timing maths for the subtitles.
// No UnityEngine here, so the offline tests (-define:DC_OFFLINE_TEST) can check that the
// subtitles stay clear of the stock HUD. HUD sizes are estimates in pixels at UI scale 1,
// taken from the stock layout by eye. They are not measured in game.
using System;

namespace Lonapse
{
    /// <summary>Small rectangle in IMGUI screen space (origin top left, y down).</summary>
    public struct SubRect
    {
        public float x, y, w, h;

        public SubRect(float x, float y, float w, float h) { this.x = x; this.y = y; this.w = w; this.h = h; }

        public float Right { get { return x + w; } }
        public float Bottom { get { return y + h; } }

        /// <summary>True when the two rectangles share area (touching edges do not count).</summary>
        public bool Overlaps(SubRect o)
        {
            return x < o.Right && o.x < Right && y < o.Bottom && o.y < Bottom;
        }

        /// <summary>True when this rectangle lies fully inside 0..W x 0..H.</summary>
        public bool InsideScreen(float W, float H)
        {
            return x >= 0f && y >= 0f && Right <= W && Bottom <= H;
        }

        public override string ToString()
        {
            return "(" + x.ToString("0") + "," + y.ToString("0") + " " + w.ToString("0") + "x" + h.ToString("0") + ")";
        }
    }

    /// <summary>The stock UI scale settings (GameSettings.UI_SCALE and the per-widget factors).</summary>
    public struct HudScales
    {
        public float ui, navball, staging, time, crew;

        public HudScales(float ui, float navball, float staging, float time, float crew)
        {
            this.ui = ui; this.navball = navball; this.staging = staging; this.time = time; this.crew = crew;
        }

        public static HudScales One { get { return new HudScales(1f, 1f, 1f, 1f, 1f); } }

        /// <summary>NaN, zero or absurd factors become 1, the rest is clamped to 0.3 .. 3.</summary>
        public HudScales Sanitized()
        {
            return new HudScales(Fix(ui), Fix(navball), Fix(staging), Fix(time), Fix(crew));
        }

        private static float Fix(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) return 1f;
            return v < 0.3f ? 0.3f : (v > 3f ? 3f : v);
        }
    }

    /// <summary>Where the subtitle stack goes: a column x..x+w, anchored at anchorY, growing up or down within minY..maxY.</summary>
    public struct SubColumn
    {
        public float x, w, anchorY, minY, maxY;
        public bool growUp;
    }

    public static class SubtitleLayout
    {
        public const string AboveNavball = "above_navball", TopLeft = "top_left", BottomLeft = "bottom_left", Custom = "custom";
        public static readonly string[] Presets = { AboveNavball, Custom, TopLeft, BottomLeft };   // first = default
        /// <summary>above_navball: bottom edge of the newest entry as a fraction of the screen height (the old custom default).</summary>
        public const float AboveNavballY = 0.78f;

        public const int MaxEntries = 4;     // hard cap of the entry buffer (subtitleMaxEntries is 1 .. 4)
        public const int MaxLines = 2;       // per entry, longer text ends in "..."
        public const float BaseFont = 15f, BaseLabelFont = 11f;
        public const float Margin = 12f, Gap = 4f, PadX = 10f, PadY = 6f, LabelGap = 1f;
        public const float MinWidth = 200f;
        /// <summary>subtitleWidth range: a factor on the preset width. The column never grows past the free space.</summary>
        public const float MinWidthScale = 0.5f, MaxWidthScale = 1.6f;
        public const float FadeIn = 0.15f, FadeOut = 0.4f;
        public const int PanelTexSize = 16, PanelRadius = 6;

        // stock HUD estimates at scale 1 (pixels). Not verified in game.
        public const float NavballH = 200f, NavballHalfW = 160f;      // navball plus the side gauges, bottom centre
        public const float PortraitW = 400f, PortraitH = 180f;        // crew portraits, bottom right
        public const float BottomLeftExtraX = 80f;   // bottom_left sits this much further right (user: too close to the stack)
        public const float StagingW = 160f, StagingTop = 90f;         // staging stack, left column from below the time widget
        public const float TimeW = 270f, TimeH = 90f;                 // time warp and MET, top left
        public const float AltimeterW = 320f, AltimeterH = 90f;       // altimeter, top centre

        // ------------------------------------------------ presets

        public static bool IsPreset(string pos)
        {
            return Array.IndexOf(Presets, pos) >= 0;
        }

        /// <summary>The preset after <paramref name="pos"/>, wrapping around. Unknown values give the first preset.</summary>
        public static string NextPreset(string pos)
        {
            int i = Array.IndexOf(Presets, pos);
            return Presets[(i + 1) % Presets.Length];
        }

        /// <summary>Short caption for the settings button.</summary>
        public static string PresetCaption(string pos)
        {
            switch (pos)
            {
                case TopLeft: return "Top left";
                case BottomLeft: return "Bottom left";
                case Custom: return "Custom";
                default: return "Above navball";
            }
        }

        // ------------------------------------------------ stock HUD rectangles

        public static SubRect Navball(float W, float H, HudScales s)
        {
            s = s.Sanitized();
            float k = s.ui * s.navball;
            float h = NavballH * k, hw = NavballHalfW * k;
            return new SubRect(W * 0.5f - hw, H - h, 2f * hw, h);
        }

        public static SubRect Portraits(float W, float H, HudScales s)
        {
            s = s.Sanitized();
            float k = s.ui * s.crew;
            float w = Math.Min(PortraitW * k, W * 0.5f), h = PortraitH * k;
            return new SubRect(W - w, H - h, w, h);
        }

        public static SubRect TimeWarp(float W, float H, HudScales s)
        {
            s = s.Sanitized();
            float k = s.ui * s.time;
            return new SubRect(0f, 0f, Math.Min(TimeW * k, W * 0.5f), TimeH * k);
        }

        public static SubRect Altimeter(float W, float H, HudScales s)
        {
            s = s.Sanitized();
            float w = AltimeterW * s.ui, h = AltimeterH * s.ui;
            return new SubRect(W * 0.5f - w * 0.5f, 0f, w, h);
        }

        /// <summary>The staging stack column on the left. It can be tall, so it reaches from below the time widget to the bottom.</summary>
        public static SubRect Staging(float W, float H, HudScales s)
        {
            s = s.Sanitized();
            float w = Math.Min(StagingW * s.ui * s.staging, W * 0.3f);
            float top = Math.Min(StagingTop * s.ui * s.time, H);
            return new SubRect(0f, top, w, H - top);
        }

        // ------------------------------------------------ column per preset

        /// <summary>
        /// Column for the given preset. above_navball: width min(640, 0.42 W), centred, bottom edge at
        /// 0.78 H (never below H - 200 x UI_SCALE x UI_SCALE_NAVBALL - 12), growing up, narrowed when it would reach the portraits
        /// or the staging stack. top_left: right of the staging stack, below the time widget, growing down.
        /// bottom_left: right of the staging stack, left of the navball, growing up from the bottom.
        /// custom: centre at customX x W, bottom edge at customY x H, growing up.
        /// </summary>
        public static SubColumn Column(string pos, float W, float H, HudScales s, float customX, float customY)
        {
            return Column(pos, W, H, s, customX, customY, 1f);
        }

        /// <summary>
        /// Column with <paramref name="widthScale"/> (subtitleWidth) applied to the preset width. A wider column is
        /// still narrowed by the stock HUD and the screen edge, so the scale only changes the width, not the position.
        /// </summary>
        public static SubColumn Column(string pos, float W, float H, HudScales s, float customX, float customY, float widthScale)
        {
            float k = float.IsNaN(widthScale) || float.IsInfinity(widthScale) ? 1f : Math.Max(MinWidthScale, Math.Min(MaxWidthScale, widthScale));
            s = s.Sanitized();
            if (float.IsNaN(W) || W < 1f) W = 1f;
            if (float.IsNaN(H) || H < 1f) H = 1f;
            SubRect nav = Navball(W, H, s), por = Portraits(W, H, s), stg = Staging(W, H, s);
            SubRect tw = TimeWarp(W, H, s), alt = Altimeter(W, H, s);
            float topLimit = Math.Max(tw.Bottom, alt.Bottom) + Margin;
            SubColumn c = new SubColumn();
            switch (pos)
            {
                case TopLeft:
                {
                    c.x = stg.Right + Margin;
                    c.w = Math.Min(Math.Min(520f, W * 0.34f) * k, W - c.x - Margin);
                    c.growUp = false;
                    c.anchorY = topLimit;
                    c.minY = topLimit;
                    float bottom = nav.y - Margin;
                    if (por.x < c.x + c.w) bottom = Math.Min(bottom, por.y - Margin);   // wide column on a small screen
                    c.maxY = Math.Max(topLimit, bottom);
                    break;
                }
                case BottomLeft:
                {
                    c.x = stg.Right + Margin + BottomLeftExtraX * s.ui;
                    float right = nav.x - Margin;
                    // the room beside the navball decides the place, the width scale only the width
                    bool room = right - c.x >= MinWidth;
                    c.w = Math.Min(560f * k, right - c.x);
                    c.anchorY = H - Margin;
                    if (!room)
                    {
                        // no room beside the navball (small screen, large UI): sit above the navball top instead
                        c.anchorY = nav.y - Margin;
                        right = W - Margin;
                        if (por.y < c.anchorY) right = Math.Min(right, por.x - Margin);
                        c.w = Math.Max(1f, Math.Min(560f * k, right - c.x));
                    }
                    c.growUp = true;
                    c.minY = topLimit;
                    c.maxY = c.anchorY;
                    break;
                }
                case Custom:
                {
                    float x = Clamp01(customX), y = Clamp01(customY);
                    c.w = Math.Min(Math.Min(640f, W * 0.42f) * k, W - 2f * Margin);
                    c.x = W * x - c.w * 0.5f;
                    if (c.x > W - Margin - c.w) c.x = W - Margin - c.w;
                    if (c.x < Margin) c.x = Margin;
                    c.growUp = true;
                    c.anchorY = Math.Min(Math.Max(H * y, Margin + 40f), H - Margin);
                    c.minY = Math.Min(Margin, c.anchorY);
                    c.maxY = c.anchorY;
                    break;
                }
                default: // above_navball
                {
                    float half = Math.Min(Math.Min(640f, W * 0.42f) * k, W - 2f * Margin) * 0.5f, cx = W * 0.5f;
                    // where custom sits by default (X 0.5, Y 0.78), but always above the navball top
                    c.anchorY = Math.Min(H * AboveNavballY, nav.y - Margin);
                    c.anchorY = Math.Max(c.anchorY, Math.Min(Margin + 40f, nav.y - Margin));
                    c.growUp = true;
                    c.minY = Math.Min(topLimit, c.anchorY);
                    c.maxY = c.anchorY;
                    // the band minY..anchorY must stay clear of the portraits (right) and the staging stack (left)
                    if (stg.y < c.anchorY) half = Math.Min(half, cx - stg.Right - Margin);
                    if (por.y < c.anchorY && por.x - Margin - cx < half)
                    {
                        // portraits reach above the anchor: narrow the column, or when that leaves too little, start above them
                        if (por.x - Margin - cx >= MinWidth * 0.5f) half = por.x - Margin - cx;
                        else { c.anchorY = por.y - Margin; c.minY = Math.Min(c.minY, c.anchorY); c.maxY = c.anchorY; }
                    }
                    if (half < 1f) half = 1f;
                    c.x = cx - half;
                    c.w = 2f * half;
                    break;
                }
            }
            if (c.w < 1f) c.w = 1f;
            return c;
        }

        // ------------------------------------------------ stacking

        /// <summary>Panel height of one entry: padding, optional speaker label, text.</summary>
        public static float EntryHeight(bool hasLabel, float labelH, float textH)
        {
            return PadY + (hasLabel ? labelH + LabelGap : 0f) + textH + PadY;
        }

        /// <summary>
        /// Place <paramref name="count"/> entries (heights ordered oldest to newest) in the column, newest at the bottom.
        /// Older entries that do not fit between minY and maxY are dropped; the newest is always placed.
        /// Fills rects[i] for the placed entries and returns the index of the oldest placed entry
        /// (count when nothing was placed).
        /// </summary>
        public static int Stack(SubColumn c, float[] heights, int count, SubRect[] rects)
        {
            if (count <= 0) return 0;
            if (count > heights.Length) count = heights.Length;
            if (count > rects.Length) count = rects.Length;
            // how many of the newest fit
            float room = c.growUp ? c.anchorY - c.minY : c.maxY - c.anchorY;
            float used = 0f;
            int first = count;
            for (int i = count - 1; i >= 0; i--)
            {
                float need = used + heights[i] + (first < count ? Gap : 0f);
                if (first < count && need > room) break;
                used = need;
                first = i;
            }
            if (c.growUp)
            {
                float y = c.anchorY;
                for (int i = count - 1; i >= first; i--)
                {
                    y -= heights[i];
                    rects[i] = new SubRect(c.x, y, c.w, heights[i]);
                    y -= Gap;
                }
            }
            else
            {
                float y = c.anchorY;
                for (int i = first; i < count; i++)
                {
                    rects[i] = new SubRect(c.x, y, c.w, heights[i]);
                    y += heights[i] + Gap;
                }
            }
            return first;
        }

        // ------------------------------------------------ colour

        /// <summary>Eight light tints that read well on a dark translucent panel. None is close to the MC amber.</summary>
        public static readonly float[][] Palette =
        {
            new float[] { 0.55f, 0.80f, 1.00f },   // sky blue
            new float[] { 0.55f, 0.95f, 0.68f },   // mint
            new float[] { 1.00f, 0.62f, 0.78f },   // pink
            new float[] { 0.78f, 0.68f, 1.00f },   // lavender
            new float[] { 0.45f, 0.92f, 0.92f },   // aqua
            new float[] { 0.80f, 0.95f, 0.45f },   // lime
            new float[] { 1.00f, 0.58f, 0.50f },   // coral
            new float[] { 0.86f, 0.89f, 0.95f },   // silver
        };
        public static readonly float[] McColor = { 1.00f, 0.74f, 0.25f };      // amber
        public static readonly float[] PlainColor = { 0.80f, 0.84f, 0.90f };   // label colour with subtitleColors off
        public static readonly float[] TextColor = { 0.94f, 0.96f, 1.00f };
        public static readonly float[] PanelColor = { 0.03f, 0.05f, 0.08f };

        public static bool IsMc(string who)
        {
            if (string.IsNullOrEmpty(who)) return false;
            string w = who.Trim();
            return string.Equals(w, "MC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "Mission Control", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "CAPCOM", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Label shown above the text. MC lines read "Mission Control".</summary>
        public static string SpeakerLabel(string who)
        {
            if (string.IsNullOrEmpty(who)) return null;
            return IsMc(who) ? "Mission Control" : who.Trim();
        }

        /// <summary>Palette index from a stable FNV-1a hash of the trimmed, lower-case name.</summary>
        public static int ColorIndex(string who)
        {
            string key = (who ?? string.Empty).Trim().ToLowerInvariant();
            return (int)(StableHash.Fnv1a(key) % (uint)Palette.Length);
        }

        /// <summary>RGB of the speaker colour: amber for MC, the palette entry otherwise, PlainColor with colours off.</summary>
        public static float[] SpeakerColor(string who, bool colors)
        {
            if (!colors) return PlainColor;
            if (IsMc(who)) return McColor;
            return Palette[ColorIndex(who)];
        }

        // ------------------------------------------------ timing and sizes

        /// <summary>How long an entry stays: the line plus 0.6 s, at least 1.2 s.</summary>
        public static float Duration(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0f;
            return Math.Max(1.2f, seconds + 0.6f);
        }

        /// <summary>Opacity factor: fades in over FadeIn after start and out over FadeOut before end.</summary>
        public static float Alpha(float now, float start, float end)
        {
            if (float.IsNaN(now) || float.IsNaN(start) || float.IsNaN(end) || now >= end || now < start) return 0f;
            float a = Math.Min((now - start) / FadeIn, (end - now) / FadeOut);
            return a > 1f ? 1f : (a < 0f ? 0f : a);
        }

        public static float ClampSize(float size)
        {
            if (float.IsNaN(size) || float.IsInfinity(size)) return 1f;
            return size < 0.7f ? 0.7f : (size > 1.6f ? 1.6f : size);
        }

        public static int FontSize(float size) { return (int)Math.Round(BaseFont * ClampSize(size)); }

        public static int LabelFontSize(float size) { return Math.Max(9, (int)Math.Round(BaseLabelFont * ClampSize(size))); }

        // ------------------------------------------------ text

        /// <summary>
        /// Longest start of <paramref name="text"/> that fits in <paramref name="maxHeight"/> with "..." added, cut at a word
        /// when one is near. <paramref name="height"/> measures a string at the target width. Returns the text unchanged when it fits.
        /// </summary>
        public static string Ellipsize(string text, Func<string, float> height, float maxHeight)
        {
            if (string.IsNullOrEmpty(text) || height == null) return text ?? string.Empty;
            if (height(text) <= maxHeight) return text;
            const string dots = "...";
            int lo = 0, hi = text.Length - 1; // largest n with height(prefix(n) + dots) <= maxHeight
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (height(text.Substring(0, mid).TrimEnd() + dots) <= maxHeight) lo = mid;
                else hi = mid - 1;
            }
            int n = lo;
            int space = n > 0 ? text.LastIndexOf(' ', n - 1) : -1;
            if (n < text.Length && text[n] != ' ' && space > (int)(n * 0.6f)) n = space;
            return text.Substring(0, n).TrimEnd(' ', ',', ';', ':', '-') + dots;
        }

        // ------------------------------------------------ panel texture

        /// <summary>Coverage 0..1 of pixel (px, py) in a size x size square with rounded corners of the given radius (1 px anti-aliasing).</summary>
        public static float RoundedAlpha(int px, int py, int size, float radius)
        {
            float x = px + 0.5f, y = py + 0.5f;
            float cx = x < radius ? radius : (x > size - radius ? size - radius : x);
            float cy = y < radius ? radius : (y > size - radius ? size - radius : y);
            float dx = x - cx, dy = y - cy;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            float a = radius - d + 0.5f;
            return a > 1f ? 1f : (a < 0f ? 0f : a);
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0.5f;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
