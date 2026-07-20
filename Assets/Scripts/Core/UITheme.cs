using UnityEngine;

namespace ClutchFPS.Core
{
    /// Shared visual language for every IMGUI screen: dark tactical panels,
    /// amber accent, flat borders. Built procedurally so there are no UI
    /// texture assets to manage.
    public static class UITheme
    {
        public static readonly Color Accent = new(0.95f, 0.71f, 0.22f);
        public static readonly Color AccentDim = new(0.62f, 0.47f, 0.16f);
        public static readonly Color Panel = new(0.07f, 0.08f, 0.09f, 0.94f);
        public static readonly Color PanelLight = new(0.13f, 0.14f, 0.16f, 0.96f);
        public static readonly Color Line = new(0.28f, 0.30f, 0.33f, 1f);
        public static readonly Color TextBright = new(0.93f, 0.94f, 0.95f);
        public static readonly Color TextDim = new(0.58f, 0.61f, 0.65f);
        public static readonly Color Danger = new(0.85f, 0.28f, 0.24f);
        public static readonly Color Success = new(0.42f, 0.82f, 0.45f);

        private static Texture2D _white;
        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }
                return _white;
            }
        }

        public static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = previous;
        }

        /// Panel with a 1px border and an accent bar down the left edge.
        public static void Panel3D(Rect rect, bool accentEdge = true)
        {
            Fill(rect, Panel);
            Fill(new Rect(rect.x, rect.y, rect.width, 1), Line);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Line);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), Line);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Line);
            if (accentEdge) Fill(new Rect(rect.x, rect.y, 3, rect.height), Accent);
        }

        /// Section header: label in accent with a rule across the remainder.
        public static void Header(Rect rect, string text)
        {
            var style = Style(13, FontStyle.Bold, TextAnchor.MiddleLeft, Accent);
            GUI.Label(rect, text.ToUpper(), style);
            float textWidth = style.CalcSize(new GUIContent(text.ToUpper())).x;
            float lineX = rect.x + textWidth + 10f;
            if (lineX < rect.xMax)
            {
                Fill(new Rect(lineX, rect.y + rect.height / 2f, rect.xMax - lineX, 1), AccentDim);
            }
        }

        /// Flat button that lights up on hover; returns true on click.
        public static bool Button(Rect rect, string label, bool primary = false)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Fill(rect, primary
                ? (hover ? Accent : AccentDim)
                : (hover ? PanelLight : new Color(0.10f, 0.11f, 0.12f, 0.96f)));
            if (!primary)
            {
                Fill(new Rect(rect.x, rect.y, rect.width, 1), hover ? Accent : Line);
                Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), hover ? Accent : Line);
                Fill(new Rect(rect.x, rect.y, 1, rect.height), hover ? Accent : Line);
                Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), hover ? Accent : Line);
            }
            var color = primary ? new Color(0.06f, 0.06f, 0.07f) : (hover ? Accent : TextBright);
            GUI.Label(rect, label.ToUpper(), Style(13, FontStyle.Bold, TextAnchor.MiddleCenter, color));
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// Horizontal meter (health, extraction progress, etc).
        public static void Bar(Rect rect, float fill01, Color color)
        {
            Fill(rect, new Color(0.05f, 0.05f, 0.06f, 0.9f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill01), rect.height), color);
            Fill(new Rect(rect.x, rect.y, rect.width, 1), Line);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Line);
        }

        private static readonly System.Collections.Generic.Dictionary<int, GUIStyle> StyleCache = new();

        public static GUIStyle Style(int size, FontStyle fontStyle, TextAnchor anchor, Color color)
        {
            int key = size * 1000 + (int)fontStyle * 100 + (int)anchor;
            if (!StyleCache.TryGetValue(key, out var style))
            {
                style = new GUIStyle { fontSize = size, fontStyle = fontStyle, alignment = anchor };
                StyleCache[key] = style;
            }
            style.normal.textColor = color;
            return style;
        }
    }
}
