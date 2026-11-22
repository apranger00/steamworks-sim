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

        static Font font;
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
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
            t.font = Font; t.text = text; t.fontSize = size; t.color = color;
            t.alignment = align; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            var le = r.gameObject.AddComponent<LayoutElement>();
            if (height > 0f) { le.preferredHeight = height; le.minHeight = height; }
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

        public static Button Button(Transform parent, string text, UnityAction onClick, float height = 62f, int fontSize = 26)
        {
            var r = Rect(text, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white; // tinted by the button colours
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = Colors();
            if (onClick != null) b.onClick.AddListener(onClick);
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            var t = Label(r, text, fontSize, TextColor, TextAnchor.MiddleCenter);
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
