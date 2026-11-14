using UnityEngine;
using UnityEngine.UI;

namespace Game.UnityBridge.UI
{
    /// <summary>
    /// 运行时 UGUI 构造工具:白色面板 + 黑色描边 + 黑色文字。
    /// 供 BattleUiController 动态弹窗使用,不依赖场景里预先摆好的预制体。
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        public static readonly Color Muted = new Color(0.36f, 0.4f, 0.45f);
        public static readonly Color Gold = new Color(0.68f, 0.46f, 0.02f);
        public static readonly Color Danger = new Color(0.72f, 0.08f, 0.13f);
        public static readonly Color Paper = Color.white;
        public static readonly Color Disabled = new Color(0.78f, 0.8f, 0.82f);

        private static Font _font;

        public static Font Font()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        /// <summary>白色面板;内部再套一层黑色,形成 2px 描边。</summary>
        public static Image Panel(string name, Transform parent, Color color)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            AddBorder(image, 2f);
            return image;
        }

        public static void AddBorder(Graphic graphic, float width)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(width, -width);
            outline.useGraphicAlpha = false;
        }

        public static Text Label(string name, Transform parent, string content, int size,
            TextAnchor anchor, Color color)
        {
            RectTransform rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font();
            text.fontSize = size;
            text.text = content;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button Button(string name, Transform parent, string label, int fontSize = 16)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            AddBorder(image, 2f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.93f, 0.95f, 0.97f);
            colors.pressedColor = new Color(0.86f, 0.89f, 0.92f);
            colors.disabledColor = Disabled;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            Text text = Label("Label", rect, label, fontSize, TextAnchor.MiddleCenter, Ink);
            Stretch(text.rectTransform);
            return button;
        }

        public static void SetText(Button button, string content, Color color)
        {
            Text text = button.GetComponentInChildren<Text>();
            if (text == null) return;
            text.text = content;
            text.color = color;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        /// <summary>竖向自动高度容器(菜单 / 弹窗内容)。</summary>
        public static VerticalLayoutGroup Vertical(Transform parent, float spacing, int padding)
        {
            VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return layout;
        }

        public static LayoutElement Height(GameObject go, float height)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return element;
        }
    }
}
