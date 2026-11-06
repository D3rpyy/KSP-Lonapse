// Optional subtitles for Lonapse lines.
// IMGUI. A rounded translucent panel per line with a small speaker label above the English text.
// Speaker colour from a stable hash of the name, MC amber. Placement and maths in SubtitleLayout (pure).
// Per frame this only draws: every GUIContent and measurement is cached per entry.
using System;
using UnityEngine;

namespace Lonapse
{
    public class Subtitles
    {
        private sealed class Entry
        {
            public string who, line;
            public GUIContent label;          // null when there is no speaker
            public GUIContent text = new GUIContent();
            public float[] color;             // speaker colour with subtitleColors on
            public float start, end;
            public float measuredW = -1f;
            public int measuredFont = -1;
            public float textH, labelH;
            public bool active;
        }

        private const int Cap = SubtitleLayout.MaxEntries;
        private readonly Entry[] entries = new Entry[Cap];      // oldest .. newest
        private readonly Entry[] visible = new Entry[Cap];
        private readonly float[] heights = new float[Cap];
        private readonly SubRect[] rects = new SubRect[Cap];

        /// <summary>Settings to read the subtitle keys from. Set once by the host (SettingsWindow does it too).</summary>
        public Settings settings;

        private GUIStyle textStyle, labelStyle, panelStyle;
        // one panel texture for the whole game session: a Subtitles object is made per flight scene, and a texture
        // with HideAndDontSave is never collected, so a per-instance texture leaked once per scene
        private static Texture2D panelTex;
        private int styledFont = -1;
        private readonly GUIContent probe = new GUIContent();
        private float probeWidth;
        private readonly Func<string, float> measure;

        public Subtitles()
        {
            for (int i = 0; i < Cap; i++) entries[i] = new Entry();
            measure = Measure;
        }

        /// <summary>Show a line. <paramref name="who"/> is the speaker's display name, or "MC" for Mission Control.</summary>
        public void Show(string who, string line, float seconds)
        {
            Show(who, line, seconds, settings != null ? settings.subtitleHold : SubtitleLayout.DefaultHold);
        }

        /// <summary>Show with its own hold time after the line (the countdown keeps the default, one number a second).</summary>
        public void Show(string who, string line, float seconds, float hold)
        {
            if (string.IsNullOrEmpty(line)) return;
            // newest at the end: rotate the oldest entry object to the end and reuse it
            Entry e = entries[0];
            for (int i = 0; i < Cap - 1; i++) entries[i] = entries[i + 1];
            entries[Cap - 1] = e;
            e.who = who;
            e.line = line;
            string lbl = SubtitleLayout.SpeakerLabel(who);
            e.label = lbl != null ? new GUIContent(lbl) : null;
            e.color = SubtitleLayout.SpeakerColor(who, true);
            e.measuredW = -1f;
            e.measuredFont = -1;
            e.start = Time.unscaledTime;
            e.end = e.start + SubtitleLayout.Duration(seconds, hold);
            e.active = true;
        }

        /// <summary>Two sample lines (crew, then MC) so the user can check position, size and colours.</summary>
        public void ShowTest(string crewName)
        {
            string name = string.IsNullOrEmpty(crewName) ? "Jebediah Kerman" : crewName;
            Show(name, "Houston, this is a test subtitle. Long lines wrap to two rows and then end in dots, so this one keeps going for a while.", 5f);
            Show("MC", "Copy that. Subtitles read fine down here.", 5f);
        }

        public void Clear()
        {
            for (int i = 0; i < Cap; i++) { entries[i].active = false; entries[i].end = 0f; }
        }

        /// <summary>Number of entries that are still on screen.</summary>
        public int ActiveCount
        {
            get
            {
                float now = Time.unscaledTime;
                int n = 0;
                for (int i = 0; i < Cap; i++) if (entries[i].active && entries[i].end > now) n++;
                return n;
            }
        }

        public void Draw()
        {
            Draw(settings);
        }

        public void Draw(Settings s)
        {
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;   // no controls, so only Repaint matters
            float now = Time.unscaledTime;

            int maxEntries = s != null ? s.subtitleMaxEntries : 2;
            if (maxEntries < 1) maxEntries = 1;
            if (maxEntries > Cap) maxEntries = Cap;
            // the newest maxEntries active entries, then reversed to oldest first. Older active entries beyond
            // maxEntries are switched off, so they do not come back when a newer one ends.
            int count = 0;
            for (int i = Cap - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                if (!e.active) continue;
                if (e.end <= now || count >= maxEntries) { e.active = false; continue; }
                visible[count++] = e;
            }
            if (count == 0) return;
            for (int a = 0, b = count - 1; a < b; a++, b--) { Entry t = visible[a]; visible[a] = visible[b]; visible[b] = t; }

            float size = s != null ? s.subtitleSize : 1f;
            float opacity = s != null ? s.subtitleOpacity : 0.55f;
            bool showSpeaker = s == null || s.subtitleSpeaker;
            bool colors = s == null || s.subtitleColors;
            string pos = s != null ? s.subtitlePos : SubtitleLayout.Custom;
            float cx = s != null ? s.subtitleX : 0.5f, cy = s != null ? s.subtitleY : 0.78f;

            EnsureStyles(SubtitleLayout.FontSize(size), SubtitleLayout.LabelFontSize(size));

            float widthScale = s != null ? s.subtitleWidth : 1f;
            SubColumn col = SubtitleLayout.Column(pos, Screen.width, Screen.height, ReadScales(), cx, cy, widthScale);
            bool stripe = colors && !showSpeaker;
            float textW = Math.Max(20f, col.w - 2f * SubtitleLayout.PadX - (stripe ? 6f : 0f));
            float maxTextH = textStyle.lineHeight * SubtitleLayout.MaxLines + 1f;

            for (int i = 0; i < count; i++)
            {
                Entry e = visible[i];
                if (e.measuredW != textW || e.measuredFont != styledFont)
                {
                    probeWidth = textW;
                    e.text.text = SubtitleLayout.Ellipsize(e.line, measure, maxTextH);
                    e.textH = textStyle.CalcHeight(e.text, textW);
                    e.labelH = e.label != null ? labelStyle.CalcHeight(e.label, textW) : 0f;
                    e.measuredW = textW;
                    e.measuredFont = styledFont;
                }
                heights[i] = SubtitleLayout.EntryHeight(showSpeaker && e.label != null, e.labelH, e.textH);
            }

            int first = SubtitleLayout.Stack(col, heights, count, rects);
            Color old = GUI.color;
            float[] tc = SubtitleLayout.TextColor, pc = SubtitleLayout.PanelColor;
            for (int i = first; i < count; i++)
            {
                Entry e = visible[i];
                float[] sc = colors ? e.color : SubtitleLayout.PlainColor;
                float a = SubtitleLayout.Alpha(now, e.start, e.end);
                if (a <= 0f) continue;
                SubRect r = rects[i];
                Rect panel = new Rect(r.x, r.y, r.w, r.h);
                if (opacity > 0f)
                {
                    GUI.color = new Color(pc[0], pc[1], pc[2], opacity * a);
                    panelStyle.Draw(panel, false, false, false, false);
                }
                float x = r.x + SubtitleLayout.PadX, y = r.y + SubtitleLayout.PadY;
                if (stripe)
                {
                    GUI.color = new Color(sc[0], sc[1], sc[2], a);
                    GUI.DrawTexture(new Rect(r.x + 4f, y, 3f, r.h - 2f * SubtitleLayout.PadY), Texture2D.whiteTexture);
                    x += 6f;
                }
                if (showSpeaker && e.label != null)
                {
                    Rect lr = new Rect(x, y, textW, e.labelH);
                    GUI.color = new Color(0f, 0f, 0f, 0.7f * a);
                    labelStyle.Draw(new Rect(lr.x + 1f, lr.y + 1f, lr.width, lr.height), e.label, false, false, false, false);
                    GUI.color = new Color(sc[0], sc[1], sc[2], a);
                    labelStyle.Draw(lr, e.label, false, false, false, false);
                    y += e.labelH + SubtitleLayout.LabelGap;
                }
                Rect tr = new Rect(x, y, textW, e.textH);
                GUI.color = new Color(0f, 0f, 0f, 0.8f * a);
                textStyle.Draw(new Rect(tr.x + 1.5f, tr.y + 1.5f, tr.width, tr.height), e.text, false, false, false, false);
                GUI.color = new Color(tc[0], tc[1], tc[2], a);
                textStyle.Draw(tr, e.text, false, false, false, false);
            }
            GUI.color = old;
        }

        private float Measure(string str)
        {
            probe.text = str;
            return textStyle.CalcHeight(probe, probeWidth);
        }

        private static HudScales ReadScales()
        {
#if !DC_OFFLINE_TEST
            try
            {
                return new HudScales(GameSettings.UI_SCALE, GameSettings.UI_SCALE_NAVBALL, GameSettings.UI_SCALE_STAGINGSTACK,
                                     GameSettings.UI_SCALE_TIME, GameSettings.UI_SCALE_CREW);
            }
            catch (Exception) { return HudScales.One; }
#else
            return HudScales.One;
#endif
        }

        private void EnsureStyles(int font, int labelFont)
        {
            if (panelTex == null) // Unity null: also true when the texture was destroyed
            {
                int n = SubtitleLayout.PanelTexSize;
                panelTex = new Texture2D(n, n, TextureFormat.ARGB32, false);
                panelTex.hideFlags = HideFlags.HideAndDontSave;
                panelTex.wrapMode = TextureWrapMode.Clamp;
                panelTex.filterMode = FilterMode.Bilinear;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        panelTex.SetPixel(x, y, new Color(1f, 1f, 1f, SubtitleLayout.RoundedAlpha(x, y, n, SubtitleLayout.PanelRadius)));
                panelTex.Apply();
                panelStyle = null;
            }
            if (panelStyle == null || panelStyle.normal.background != panelTex)
            {
                panelStyle = new GUIStyle();
                panelStyle.normal.background = panelTex;
                int b = SubtitleLayout.PanelRadius;
                panelStyle.border = new RectOffset(b, b, b, b);
            }
            if (textStyle == null || styledFont != font)
            {
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperLeft, fontSize = font, wordWrap = true, richText = false,
                    padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                };
                textStyle.normal.textColor = Color.white;   // tinted with GUI.color
                labelStyle = new GUIStyle(textStyle) { fontSize = labelFont, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                styledFont = font;
            }
        }
    }
}
