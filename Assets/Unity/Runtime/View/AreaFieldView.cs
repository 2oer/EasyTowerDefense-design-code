using Game.Combat;
using Game.Model;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>地面区域视图(燃烧 / 低温):按半径缩放,颜色随类型切换。</summary>
    public sealed class AreaFieldView : MonoBehaviour
    {
        [SerializeField] private Renderer fieldRenderer;
        [SerializeField] private Color burnColor = new Color(1f, 0.45f, 0.15f, 0.55f);
        [SerializeField] private Color chillColor = new Color(0.42f, 0.85f, 1f, 0.5f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _block;

        public void Tick(AreaEffectField field, Vector3 worldPosition, float unitScale)
        {
            if (field == null) return;

            transform.position = worldPosition;
            transform.localScale = Vector3.one * Mathf.Max(0.1f, field.Radius * 2f * unitScale);

            if (fieldRenderer == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();

            // 内置管线用 _Color,URP 用 _BaseColor;两个都写,换管线时不用改预制体。
            Color color = field.Kind == StatusKind.Burn ? burnColor : chillColor;
            fieldRenderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, color);
            _block.SetColor(BaseColorId, color);
            fieldRenderer.SetPropertyBlock(_block);
        }

        public void Release()
        {
            gameObject.SetActive(false);
        }
    }
}
