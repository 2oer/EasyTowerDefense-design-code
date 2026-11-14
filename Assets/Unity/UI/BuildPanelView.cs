using System;
using System.Collections.Generic;
using Game.Model;
using UnityEngine;

namespace Game.UnityBridge.UI
{
    /// <summary>
    /// 建造面板:用 ModelDatabase 自动生成塔卡,点击后把 towerId 抛给上层。
    /// 槽位选择由战斗视图 / 输入层负责,这里不关心。
    /// </summary>
    public sealed class BuildPanelView : MonoBehaviour
    {
        [SerializeField] private BattleDriver driver;
        [SerializeField] private Transform content;
        [SerializeField] private TowerCardView cardPrefab;
        [SerializeField] private AssetCatalog catalog;

        private readonly List<TowerCardView> _cards = new List<TowerCardView>();

        /// <summary>请求建造:towerId。</summary>
        public event Action<string> BuildRequested;

        private void Start()
        {
            if (driver == null) driver = FindObjectOfType<BattleDriver>();
            Rebuild();
        }

        public void Rebuild()
        {
            if (driver == null || driver.Database == null || content == null || cardPrefab == null) return;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null) Destroy(_cards[i].gameObject);
            }
            _cards.Clear();

            IReadOnlyList<TowerDef> towers = driver.Database.Towers;
            for (int i = 0; i < towers.Count; i++)
            {
                TowerDef def = towers[i];
                TowerCardView card = Instantiate(cardPrefab, content);
                AssetCatalog.TowerArt art = catalog != null ? catalog.FindTower(def.Id) : null;
                card.Bind(def, art != null ? art.icon : null, RoleOf(def.Id), EstimateDps(def));
                card.Clicked += HandleCardClicked;
                _cards.Add(card);
            }

            RefreshAffordability();
        }

        /// <summary>金币变化后刷新可点击状态(由 HUD 每帧调用即可)。</summary>
        public void RefreshAffordability()
        {
            if (driver == null || driver.Simulation == null) return;

            int gold = driver.Simulation.Gold;
            IReadOnlyList<TowerDef> towers = driver.Database.Towers;
            for (int i = 0; i < _cards.Count && i < towers.Count; i++)
            {
                _cards[i].SetAffordable(gold >= towers[i].Price);
            }
        }

        private void HandleCardClicked(string towerId)
        {
            BuildRequested?.Invoke(towerId);
        }

        private static float EstimateDps(TowerDef def)
        {
            if (def.DroneCapacity > 0)
            {
                int squad = Mathf.Max(1, def.DroneSquad);
                return def.DroneDamage * def.DroneFireRate * squad;
            }
            return def.Damage * def.FireRate * Mathf.Max(1, def.Salvo);
        }

        private static string RoleOf(string id)
        {
            switch (id)
            {
                case "machine_gun": return "单体压制";
                case "laser": return "长射程光束";
                case "mortar": return "曲射范围 · 最小射程";
                case "rocket": return "追踪齐射";
                case "tesla": return "连锁电场";
                case "drone_hangar": return "编队部署";
                default: return string.Empty;
            }
        }
    }
}
