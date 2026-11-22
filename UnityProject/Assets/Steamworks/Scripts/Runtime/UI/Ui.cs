using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SteamworksSim.UI
{
    /// <summary>
    /// Small helpers that build uGUI in code (placeholder look until the real art arrives). Everything is
    /// laid out with layout groups at a 1920 x 1080 reference size and is navigable with keyboard or gamepad.
    /// </summary>
    public static class Ui
    {
        public static readonly Color Backdrop = new Color(0.05f, 0.06f, 0.08f, 0.88f);
        public static readonly Color ButtonNormal = new Color(0.16f, 0.18f, 0.21f, 0.96f);
        public static readonly Color ButtonHover = new Color(0.26f, 0.29f, 0.33f, 1f);
        public static readonly Color ButtonSelected = new Color(0.62f, 0.45f, 0.06f, 1f);
        public static readonly Color ButtonPressed = new Color(0.78f, 0.58f, 0.1f, 1f);
        public static readonly Color TextColor = new Color(0.94f, 0.95f, 0.96f);
        public static readonly Color Muted = new Color(0.62f, 0.67f, 0.72f);
        public static readonly Color Gold = new Color(0.96f, 0.74f, 0.16f);
        public static readonly Color Red = new Color(0.86f, 0.2f, 0.18f);
        public static readonly Color Blue = new Color(0.2f, 0.45f, 0.9f);

        public static readonly Color Ink = new Color(0.11f, 0.12f, 0.14f);          // text on light panels
        public static readonly Color Paper = new Color(1f, 1f, 1f, 0.96f);          // white menu panel

        static Font legacy, body, bold, title;
        static Font Load(string name) { var f = Resources.Load<Font>(name); return f != null ? f : null; }
        static Font Legacy => legacy != null ? legacy : legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>Body text: Resources/Fonts/Body (Fira Sans), else Unity's built-in font.</summary>
        public static Font Font => body != null ? body : body = Load("Fonts/Body") ?? Legacy;
        /// <summary>Bold body text: Resources/Fonts/BodyBold, else Body.</summary>
        public static Font BoldFont => bold != null ? bold : bold = Load("Fonts/BodyBold") ?? Font;
        /// <summary>Menu and heading font: Resources/Fonts/Title (drop in Brim Narrow / Haymaker), else TitleDefault (Anton).</summary>
        public static Font TitleFont => title != null ? title : title = Load("Fonts/Title") ?? Load("Fonts/TitleDefault") ?? BoldFont;

        static Sprite ring, disc;
        /// <summary>A soft-edged ring (generated once), for radial fills such as the countdown.</summary>
        public static Sprite Ring => ring != null ? ring : ring = Circle("Ring", 0.80f);
        /// <summary>A soft-edged filled circle (generated once).</summary>
        public static Sprite Disc => disc != null ? disc : disc = Circle("Disc", -1f);

        static Sprite Circle(string name, float inner)
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float edge = 4f / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((r - inner) / edge + 0.5f) * Mathf.Clamp01((0.98f - r) / edge + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            sp.name = name;
            return sp;
        }

        static Sprite rounded;
        /// <summary>9-sliced rounded rectangle (24 px corners at multiplier 1); set Image.pixelsPerUnitMultiplier to shrink the corners.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int n = 64; const float r = 24f;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        float a = Mathf.Clamp01(r - d + 0.5f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                rounded = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                rounded.name = "Rounded";
                return rounded;
            }
        }

        /// <summary>Makes an Image a rounded rectangle with corners of about `radius` reference pixels.</summary>
        public static Image Round(Image img, float radius)
        {
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 24f / Mathf.Max(1f, radius);
            return img;
        }

        public static Canvas Canvas(string name, int sortOrder, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortOrder;
            var s = go.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            s.matchWidthOrHeight = 0.5f;
            return c;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform r, float inset = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        /// <summary>Anchors r at a point of its parent (0..1 each axis) with the same pivot.</summary>
        public static RectTransform Anchor(RectTransform r, Vector2 at, Vector2 size, Vector2 offset = default)
        {
            r.anchorMin = r.anchorMax = r.pivot = at;
            r.sizeDelta = size;
            r.anchoredPosition = offset;
            return r;
        }

        public static Image Fill(Transform parent, string name, Color color)
        {
            var r = Stretch(Rect(name, parent));
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>A vertical stack that sizes itself to its contents.</summary>
        public static RectTransform Column(Transform parent, string name, float width, float spacing, int padding = 0, bool fit = true)
        {
            var r = Rect(name, parent);
            r.sizeDelta = new Vector2(width, 0f);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            if (fit)
            {
                var f = r.gameObject.AddComponent<ContentSizeFitter>();
                f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            return r;
        }

        public static RectTransform Row(Transform parent, float height, float spacing)
        {
            var r = Rect("Row", parent);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            return r;
        }

        public static void Spacer(Transform parent, float height)
        {
            var le = Rect("Spacer", parent).gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft,
                                 FontStyle style = FontStyle.Normal, float height = -1f)
        {
            var r = Rect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            bool useBoldFont = style == FontStyle.Bold && BoldFont != Font;
            t.font = useBoldFont ? BoldFont : Font; t.text = text; t.fontSize = size; t.color = color;
            t.alignment = align; t.fontStyle = useBoldFont ? FontStyle.Normal : style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            var le = r.gameObject.AddComponent<LayoutElement>();
            if (height > 0f) { le.preferredHeight = height; le.minHeight = height; }
            return t;
        }

        /// <summary>A label in the menu / heading font.</summary>
        public static Text Title(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, float height = -1f)
        {
            var t = Label(parent, text, size, color, align, FontStyle.Normal, height);
            t.font = TitleFont;
            return t;
        }

        public static void SetWidth(Component c, float width, float flexible = 0f)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>(); // not ??: Unity's fake null in the editor
            le.preferredWidth = width; le.minWidth = Mathf.Min(width, 60f); le.flexibleWidth = flexible;
        }

        public static ColorBlock Colors()
        {
            var c = ColorBlock.defaultColorBlock;
            c.normalColor = ButtonNormal;
            c.highlightedColor = ButtonHover;
            c.selectedColor = ButtonSelected;
            c.pressedColor = ButtonPressed;
            c.disabledColor = new Color(0.12f, 0.13f, 0.15f, 0.6f);
            c.colorMultiplier = 1f;
            c.fadeDuration = 0.06f;
            return c;
        }

        /// <summary>Button colours for white panels (dark text).</summary>
        public static ColorBlock LightColors()
        {
            var c = Colors();
            c.normalColor = new Color(0.9f, 0.91f, 0.92f, 1f);
            c.highlightedColor = new Color(0.82f, 0.84f, 0.86f, 1f);
            c.selectedColor = new Color(0.97f, 0.76f, 0.2f, 1f);
            c.pressedColor = new Color(0.86f, 0.64f, 0.1f, 1f);
            return c;
        }

        /// <param name="light">styled for a white panel (dark text)</param>
        /// <param name="menuFont">label in the menu / heading font</param>
        public static Button Button(Transform parent, string text, UnityAction onClick, float height = 62f, int fontSize = 26,
                                    bool light = false, bool menuFont = false)
        {
            var r = Rect(text, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white; // tinted by the button colours
            Round(img, 10f);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = light ? LightColors() : Colors();
            if (onClick != null) b.onClick.AddListener(onClick);
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            var t = menuFont ? Title(r, text, fontSize, light ? Ink : TextColor) : Label(r, text, fontSize, light ? Ink : TextColor, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 4f);
            return b;
        }

        public static Text LabelOf(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>"Label    &lt; value &gt;" row: left/right (or click the arrows) changes the value.</summary>
        public static OptionRow Option(Transform parent, string label, string[] values, int index, Action<int> onChange,
                                       bool wrap = true, float height = 48f, float labelWidth = 380f)
        {
            var row = Row(parent, height, 12f);
            var l = Label(row, label, 22, TextColor);
            SetWidth(l, labelWidth);

            var r = Rect("Value", row);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white;
            Round(img, 10f);
            SetWidth(img, 300f, 1f);
            var opt = r.gameObject.AddComponent<OptionRow>();
            opt.targetGraphic = img;
            opt.colors = Colors();

            var left = Label(r, "<", 28, TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(left.rectTransform, new Vector2(0f, 0.5f), new Vector2(56f, height));
            left.raycastTarget = true;
            left.gameObject.AddComponent<ClickArea>().onClick = () => opt.Step(-1);

            var right = Label(r, ">", 28, TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(right.rectTransform, new Vector2(1f, 0.5f), new Vector2(56f, height));
            right.raycastTarget = true;
            right.gameObject.AddComponent<ClickArea>().onClick = () => opt.Step(1);

            var value = Label(r, "", 22, TextColor, TextAnchor.MiddleCenter);
            Stretch(value.rectTransform);
            value.rectTransform.offsetMin = new Vector2(60f, 0f);
            value.rectTransform.offsetMax = new Vector2(-60f, 0f);

            opt.Setup(value, values, index, onChange, wrap);
            return opt;
        }

        /// <summary>0%..100% in 5% steps, for volume rows.</summary>
        public static string[] Percentages()
        {
            var v = new string[21];
            for (int i = 0; i <= 20; i++) v[i] = (i * 5) + "%";
            return v;
        }
    }
}
