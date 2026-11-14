using System;
using Game.Model;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UnityBridge.UI
{
    /// <summary>升级树上的单个节点。唯一升级会额外标注。</summary>
    public sealed class UpgradeNodeView : MonoBehaviour
    {
        [SerializeField] private Text nameText;
        [SerializeField] private Text levelText;
        [SerializeField] private Text modifierText;
        [SerializeField] private Text costText;
        [SerializeField] private GameObject uniqueBadge;
        [SerializeField] private Button button;

        private string _nodeId;

        public event Action<string> Clicked;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        public void Bind(UpgradeNodeDef node, int cost, bool available)
        {
            _nodeId = node.Id;
            if (nameText != null) nameText.text = PrettyName(node.Id);
            if (levelText != null) levelText.text = "Lv" + node.Level;
            if (costText != null) costText.text = cost + " 金币";
            if (modifierText != null) modifierText.text = DescribeModifiers(node);
            if (uniqueBadge != null) uniqueBadge.SetActive(node.Unique);
            if (button != null) button.interactable = available;
        }

        public void SetAffordable(bool affordable)
        {
            if (button != null && button.interactable) button.interactable = affordable;
        }

        private void HandleClick()
        {
            if (!string.IsNullOrEmpty(_nodeId)) Clicked?.Invoke(_nodeId);
        }

        private static string PrettyName(string id)
        {
            // mortar_lv2_heavy_charge → "heavy charge"
            string[] parts = id.Split('_');
            int start = parts.Length > 2 ? 2 : 0;
            return string.Join(" ", parts, start, parts.Length - start);
        }

        private static string DescribeModifiers(UpgradeNodeDef node)
        {
            if (node.Modifiers == null || node.Modifiers.Count == 0) return string.Empty;

            string result = string.Empty;
            for (int i = 0; i < node.Modifiers.Count; i++)
            {
                if (i > 0) result += " · ";
                StatModifier mod = node.Modifiers[i];
                if (mod.Op == ModifierOp.Set) result += mod.Key + " = " + mod.Value;
                else if (mod.Op == ModifierOp.Add) result += mod.Key + " +" + mod.Value;
                else result += mod.Key + " ×" + mod.Value;
            }
            return result;
        }
    }
}
