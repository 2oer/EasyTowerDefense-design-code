using System;
using Game.Model;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UnityBridge.UI
{
    /// <summary>建造面板里的单张塔卡。只负责展示与派发点击,不直接改经济。</summary>
    public sealed class TowerCardView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private Text nameText;
        [SerializeField] private Text roleText;
        [SerializeField] private Text priceText;
        [SerializeField] private Text dpsText;
        [SerializeField] private Button button;
        [SerializeField] private Image affordabilityMask;

        private string _towerId;

        public event Action<string> Clicked;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        public void Bind(TowerDef def, Sprite iconSprite, string role, float dps)
        {
            _towerId = def.Id;
            if (icon != null)
            {
                icon.sprite = iconSprite;
                icon.enabled = iconSprite != null;
            }
            if (nameText != null) nameText.text = DisplayName(def.Id);
            if (roleText != null) roleText.text = role;
            if (priceText != null) priceText.text = def.Price.ToString();
            if (dpsText != null) dpsText.text = "DPS " + dps.ToString("0.0");
        }

        public void SetAffordable(bool affordable)
        {
            if (button != null) button.interactable = affordable;
            if (affordabilityMask != null)
                affordabilityMask.color = affordable
                    ? new Color(0f, 0f, 0f, 0f)
                    : new Color(0f, 0f, 0f, 0.45f);
        }

        private void HandleClick()
        {
            if (!string.IsNullOrEmpty(_towerId)) Clicked?.Invoke(_towerId);
        }

        private static string DisplayName(string id)
        {
            switch (id)
            {
                case "machine_gun": return "机枪塔";
                case "laser": return "激光塔";
                case "mortar": return "迫击炮";
                case "rocket": return "火箭塔";
                case "tesla": return "电弧塔";
                case "drone_hangar": return "无人机库";
                default: return id;
            }
        }
    }
}
