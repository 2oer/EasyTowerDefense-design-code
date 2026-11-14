using UnityEngine;
using UnityEngine.UI;

namespace Game.UnityBridge.UI
{
    /// <summary>
    /// 程序化图标与底图:圆、环、三角、方块、径向渐变环。
    /// 统一白底黑线或纯黑线条,不需要外部美术资源。
    /// </summary>
    public static class UiIconFactory
    {
        private static Sprite _circle;
        private static Sprite _ring;
        private static Sprite _triangle;
        private static Sprite _square;
        private static readonly Sprite[] _sectors = new Sprite[3];

        public static Sprite Square()
        {
            if (_square != null) return _square;
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            _square = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _square;
        }

        public static Sprite Circle()
        {
            if (_circle != null) return _circle;
            _circle = BuildCircle(64, 30f, 0f);
            return _circle;
        }

        public static Sprite Ring()
        {
            if (_ring != null) return _ring;
            _ring = BuildCircle(64, 30f, 20f);
            return _ring;
        }

        public static Sprite Triangle()
        {
            if (_triangle != null) return _triangle;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var clear = new Color32(255, 255, 255, 0);
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            for (int y = 8; y < size - 8; y++)
            {
                float t = (y - 8f) / (size - 16f);
                int halfWidth = Mathf.RoundToInt(Mathf.Lerp(26f, 2f, t));
                for (int x = size / 2 - halfWidth; x <= size / 2 + halfWidth; x++)
                {
                    if (x >= 0 && x < size) pixels[y * size + x] = white;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            _triangle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _triangle;
        }

        /// <summary>
        /// 三等分圆环扇区(index 0 = 左上,1 = 右上,2 = 正下)。
        /// 内外边缘做柔化淡出,没有实线边框。
        /// </summary>
        public static Sprite Sector(int index)
        {
            if (index < 0 || index > 2) index = 0;
            if (_sectors[index] != null) return _sectors[index];

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float outer = size * 0.48f;
            float inner = size * 0.30f;
            float feather = 6f;

            // 0: 90..210(左上), 1: -30..90(右上), 2: 210..330(正下)
            float start = index == 0 ? 90f : (index == 1 ? -30f : 210f);
            float end = start + 120f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = 0f;

                    if (distance <= outer + feather && distance >= inner - feather)
                    {
                        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        float normalized = NormalizeAngle(angle);
                        float sectorStart = NormalizeAngle(start + 1.5f);
                        float sectorEnd = NormalizeAngle(end - 1.5f);
                        bool inSector = sectorStart <= sectorEnd
                            ? normalized >= sectorStart && normalized <= sectorEnd
                            : normalized >= sectorStart || normalized <= sectorEnd;

                        if (inSector)
                        {
                            float innerFade = Mathf.Clamp01((distance - (inner - feather)) / feather);
                            float outerFade = Mathf.Clamp01(((outer + feather) - distance) / feather);
                            alpha = Mathf.Min(innerFade, outerFade);
                        }
                    }

                    byte a = (byte)(Mathf.Clamp01(alpha) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _sectors[index] = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _sectors[index];
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle < 0f) angle += 360f;
            return angle;
        }

        private static Sprite BuildCircle(int size, float outer, float inner)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    bool inside = distance <= outer && distance >= inner;
                    pixels[y * size + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>在按钮中心拼一个简笔图标(offset 用于环形扇区)。</summary>
        public static void CreateIcon(string kind, Transform parent, Vector2 offset)
        {
            switch (kind)
            {
                case "detail":
                    BuildMagnifier(parent, offset);
                    break;
                case "upgrade":
                    BuildArrow(parent, offset);
                    break;
                default:
                    BuildCrane(parent, offset);
                    break;
            }
        }

        private static void BuildMagnifier(Transform parent, Vector2 offset)
        {
            Image lens = Part(parent, "Lens", UiKit.Ink, new Vector2(24f, 24f), offset + new Vector2(-2f, 2f));
            lens.sprite = Ring();
            Part(parent, "Handle", UiKit.Ink, new Vector2(5f, 16f), offset + new Vector2(11f, -10f))
                .rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }

        private static void BuildArrow(Transform parent, Vector2 offset)
        {
            Image head = Part(parent, "Head", UiKit.Ink, new Vector2(22f, 16f), offset + new Vector2(0f, 7f));
            head.sprite = Triangle();
            Part(parent, "Stem", UiKit.Ink, new Vector2(5f, 18f), offset + new Vector2(0f, -8f));
        }

        private static void BuildCrane(Transform parent, Vector2 offset)
        {
            Part(parent, "Mast", UiKit.Ink, new Vector2(4f, 26f), offset + new Vector2(-10f, -2f));
            Part(parent, "Jib", UiKit.Ink, new Vector2(22f, 4f), offset + new Vector2(0f, 11f));
            Part(parent, "Cable", UiKit.Ink, new Vector2(3f, 8f), offset + new Vector2(8f, 5f));
            Part(parent, "Hook", UiKit.Ink, new Vector2(7f, 7f), offset + new Vector2(8f, 0f)).sprite = Ring();
        }

        private static Image Part(Transform parent, string name, Color color, Vector2 size,
            Vector2 position)
        {
            RectTransform rect = UiKit.Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.sprite = Square();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return image;
        }
    }
}
