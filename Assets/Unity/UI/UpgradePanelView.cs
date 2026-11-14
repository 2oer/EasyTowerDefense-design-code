using System.Collections.Generic;
using Game.Entity;
using Game.Model;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UnityBridge.UI
{
    /// <summary>
    /// 塔详情 / 升级面板:展示当前等级、唯一机制与"下一级可选节点"。
    /// 选择哪座塔由战场点击决定(调用 ShowTower),本类不处理射线检测。
    /// </summary>
    public sealed class UpgradePanelView : MonoBehaviour
    {
        [SerializeField] private BattleDriver driver;
        [SerializeField] private BattleViewRoot viewRoot;
        [SerializeField] private Text towerNameText;
        [SerializeField] private Text levelText;
        [SerializeField] private Text mechanicText;
        [SerializeField] private Transform content;
        [SerializeField] private UpgradeNodeView nodePrefab;

        private readonly List<UpgradeNodeView> _nodes = new List<UpgradeNodeView>();
        private int _towerIndex = -1;
        private int _lastLevel = -1;

        private static readonly Dictionary<string, string> MechanicText = new Dictionary<string, string>
        {
            { "cluster", "空爆子母弹：落点抛撒多枚子弹，覆盖整片区域。" },
            { "fragmentation", "高爆破片：中心高穿甲，外圈低穿甲二次破片。" },
            { "napalm", "凝固汽油弹：爆炸后留下燃烧区域。" },
            { "cryo", "液氮弹：爆炸后留下低温区域。" },
            { "array_battery", "阵列炮：三管齐射，覆盖三个目标。" },
            { "smart_implosion", "智能内爆：单段高穿甲 + 碎甲。" },
            { "swarm", "蜂群弹幕：单次抛撒 8 枚小当量火箭。" },
            { "split_warhead", "分导弹头：齐射后再次分裂。" },
            { "plasma_missile", "电浆导弹：元素爆炸并施加充电。" },
            { "thermobaric", "热压弹：大半径爆炸 + 燃烧区域。" },
            { "hydrogen", "氢弹：核心 + 辐射冲击波。" },
            { "capacitor", "电容过载：无可跳目标时引爆。" },
            { "rod", "引雷针：落点生成避雷针，延长链长。" },
            { "ball_lightning", "球形闪电：半径随连锁增长。" },
            { "rolling_thunder", "雷电滚滚：连续滚动范围伤害。" },
            { "leak_current", "漏电：弹体驻留，周期范围放电。" },
            { "mobile_factory", "移动工厂：自动拟合敌群重心。" },
            { "hurricane", "飓风炮艇：机炮 + 火箭重装编队。" },
            { "thunderstorm", "雷暴空袭：矩形覆盖巡航投弹。" },
            { "refined_hangar", "精装修机库：大规模编队快速补充。" }
        };

        private void Start()
        {
            if (driver == null) driver = FindObjectOfType<BattleDriver>();
            if (viewRoot == null) viewRoot = FindObjectOfType<BattleViewRoot>();
            Clear();
        }

        private void Update()
        {
            if (_towerIndex < 0 || driver == null || driver.Simulation == null) return;

            // 等级变化时重建(升级成功 / 回合切换)。
            IReadOnlyList<TowerAgent> towers = driver.Simulation.Towers;
            if (_towerIndex >= towers.Count) { Clear(); return; }
            if (towers[_towerIndex].Level != _lastLevel) Rebuild();
        }

        public void ShowTower(int towerIndex)
        {
            _towerIndex = towerIndex;
            _lastLevel = -1;
            if (viewRoot != null) viewRoot.SelectTower(towerIndex);
            Rebuild();
        }

        public void Rebuild()
        {
            if (driver == null || driver.Simulation == null) { Clear(); return; }

            IReadOnlyList<TowerAgent> towers = driver.Simulation.Towers;
            if (_towerIndex < 0 || _towerIndex >= towers.Count) { Clear(); return; }

            TowerAgent tower = towers[_towerIndex];
            _lastLevel = tower.Level;

            if (towerNameText != null) towerNameText.text = DisplayName(tower.Def.Id);
            if (levelText != null) levelText.text = "Lv" + tower.Level;

            TowerRuntimeStats stats = tower.Stats;
            string mechanic = stats.Mechanic;
            if (mechanicText != null)
            {
                if (string.IsNullOrEmpty(mechanic))
                    mechanicText.text = "尚未解锁唯一机制";
                else
                {
                    string description;
                    mechanicText.text = MechanicText.TryGetValue(mechanic, out description)
                        ? mechanic + " · " + description
                        : mechanic;
                }
            }

            ClearNodes();
            if (content == null || nodePrefab == null) return;

            int nextLevel = tower.Level + 1;
            int gold = driver.Simulation.Gold;
            IReadOnlyList<UpgradeNodeDef> upgrades = driver.Database.Upgrades;

            for (int i = 0; i < upgrades.Count; i++)
            {
                UpgradeNodeDef node = upgrades[i];
                if (!string.Equals(node.TowerId, tower.Def.Id)) continue;
                if (node.Level != nextLevel) continue;

                int cost = driver.Simulation.UpgradeCostOf(_towerIndex, node.Id);
                bool available = driver.Simulation.CanApplyUpgrade(_towerIndex, node.Id) && gold >= cost;

                UpgradeNodeView view = Instantiate(nodePrefab, content);
                view.Bind(node, cost, available);
                view.Clicked += HandleNodeClicked;
                _nodes.Add(view);
            }
        }

        private void HandleNodeClicked(string nodeId)
        {
            if (driver == null || _towerIndex < 0) return;
            if (!driver.TryUpgrade(_towerIndex, nodeId)) return;

            AudioService service = AudioService.Instance;
            if (service != null) service.PlayUi("ui_upgrade");
            Rebuild();
        }

        private void Clear()
        {
            _towerIndex = -1;
            _lastLevel = -1;
            if (towerNameText != null) towerNameText.text = "未选中";
            if (levelText != null) levelText.text = string.Empty;
            if (mechanicText != null) mechanicText.text = "选择战场上的防御塔查看升级树。";
            ClearNodes();
        }

        private void ClearNodes()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] != null) Destroy(_nodes[i].gameObject);
            }
            _nodes.Clear();
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
