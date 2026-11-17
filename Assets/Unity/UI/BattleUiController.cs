using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Entity;
using Game.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UnityBridge.UI
{
    /// <summary>
    /// 战斗交互:点空位弹建造菜单;点已建塔显示射程并弹出「详细数据 / 拆除 / 升级」;
    /// 详情与确认使用独立弹窗,无法升级时用飘字提示。UI 全部白底黑框,运行时生成。
    /// </summary>
    public sealed class BattleUiController : MonoBehaviour
    {
        [SerializeField] private BattleDriver driver;
        [SerializeField] private BattleViewRoot viewRoot;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Canvas canvas;

        private const int RangeSegments = 72;

        private static readonly Color BuffColor = new Color(0.05f, 0.55f, 0.2f);
        private static readonly Color CostColor = new Color(0.75f, 0.1f, 0.12f);

        private static readonly Dictionary<string, string> StatNames = new Dictionary<string, string>
        {
            { "Damage", "伤害" }, { "FireRate", "射速" }, { "Range", "射程" },
            { "TurnSpeed", "转向" }, { "BlockForce", "阻滞" }, { "Retention", "留存" },
            { "Ammo", "弹匣" }, { "ReloadTime", "装填" }, { "BeamWidth", "束宽" },
            { "BeamCount", "束数" }, { "ExplosionRadius", "爆炸半径" }, { "MinRange", "最小射程" },
            { "ProjectileSpeed", "弹速" }, { "Salvo", "齐射" }, { "ChainCount", "连锁次数" },
            { "ChainRange", "连锁距离" }, { "ChainFalloff", "连锁衰减" }, { "SubCount", "子弹数" },
            { "SubDamage", "子弹伤害" }, { "SubRadius", "子弹半径" }, { "SpreadRadius", "散布" },
            { "FirstStageDamage", "一段伤害" }, { "SecondStageDamage", "二段伤害" },
            { "SecondStageRadius", "二段半径" }, { "ZoneScale", "区域倍率" },
            { "DroneSquad", "编队规模" }, { "DroneCapacity", "机库容量" },
            { "DroneReload", "补充时间" }, { "DroneDeployInterval", "出击间隔" },
            { "DroneAmmo", "编队弹药" }, { "DroneFireRate", "编队射速" },
            { "DroneDamage", "编队伤害" }, { "DroneRange", "编队射程" },
            { "DroneSpeed", "编队速度" }, { "DroneSelfDestructRadius", "自毁半径" },
            { "DroneSelfDestructDamage", "自毁伤害" }, { "RadarRange", "雷达范围" },
            { "ChargeStacks", "充电层数" }, { "AuraRadius", "光环半径" },
            { "AuraInterval", "光环间隔" }, { "ProjectileLifetime", "弹体寿命" }
        };

        private static readonly HashSet<StatKey> LowerIsBetter = new HashSet<StatKey>
        {
            StatKey.ReloadTime, StatKey.MinRange, StatKey.SpreadRadius,
            StatKey.ChainFalloff, StatKey.DroneReload, StatKey.DroneDeployInterval,
            StatKey.AuraInterval
        };

        private static readonly Dictionary<string, string> MechanicTips =
            new Dictionary<string, string>
            {
                { "cluster", "空爆子母弹:落点抛撒 8 枚子弹,黄金角铺开覆盖整片区域。" },
                { "fragmentation", "高爆破片:中心高穿甲,外圈低穿甲大范围二次破片。" },
                { "incendiary", "贫铀穿甲燃烧弹:子弹附加火属性伤害,并施加燃烧。" },
                { "he_round", "高爆弹:近炸引信,命中点产生小范围爆炸伤害。" },
                { "cryo_core", "低温超导钢芯:子弹附加冰属性伤害,并施加低温。" },
                { "implosion", "贯穿内爆:命中大型时追加定额内爆(按剩余留存增伤),并施加碎甲易伤。" },
                { "thermal_melt", "热熔:持续照射同一目标叠加易伤至 ×2;能量衰减不再阻碍贯穿。" },
                { "scatter_prism", "散射棱镜:多条光束展开成 60° 扇形持续照射。" },
                { "enriched_plutonium", "富集钚激光器:辐射伤害,射程内全体敌人每次扫射 +1 层衰变。" },
                { "napalm", "凝固汽油弹:爆炸后留下燃烧区域,持续叠加燃烧层数。" },
                { "cryo", "液氮弹:爆炸后留下低温区域,降低敌人阻滞阈值。" },
                { "array_battery", "阵列炮:三管齐射,同帧覆盖三个目标。" },
                { "smart_implosion", "智能内爆:单段高穿甲,命中后附加碎甲。" },
                { "swarm", "蜂群弹幕:单次抛撒 8 枚小当量火箭。" },
                { "split_warhead", "分导弹头:齐射后每发再分裂 3 个子弹头。" },
                { "plasma_missile", "电浆导弹:元素爆炸并施加 2 层充电。" },
                { "thermobaric", "热压弹:大半径爆炸,并留下燃烧区域。" },
                { "hydrogen", "氢弹:核心爆炸 + 60 半径辐射冲击波。" },
                { "capacitor", "电容过载:无可跳目标时引爆,按剩余连锁放大单体伤害。" },
                { "rod", "引雷针:落点生成避雷针,吸引后续连锁并延长链长。" },
                { "ball_lightning", "球形闪电:爆炸半径随连锁次数增长,并施加充电。" },
                { "rolling_thunder", "雷电滚滚:命中后沿最近目标滚动,每跳都造成范围伤害。" },
                { "leak_current", "漏电:弹体驻留 3 秒,周期对范围内多个目标放电。" },
                { "mobile_factory", "移动工厂:本体在雷达范围内随敌群重心移动。" },
                { "hurricane", "飓风炮艇:单架重装编队,双管机炮 + 8 发火箭。" },
                { "aurora", "极光:单架重型激光机,高留存光束 + 中级穿甲,保留载弹与射速。" },
                { "thunderstorm", "雷暴空袭:矩形覆盖巡航,按距离间隔投弹。" },
                { "battleship", "大舰巨炮:移动工厂加装阳电子炮,8 发高穿甲能量点杀,整备时补弹。" },
                { "refined_hangar", "精装修机库:15 架编队,弹药耗尽后快速补充。" },
                { "missile_silo", "导弹发射井:单发重型钻地弹,巨大爆炸半径与双倍射程,超长重装。" },
                { "fission", "裂变闪电:每次连锁分裂成两根链条,链条数量指数增长,单根伤害降低。" }
            };

        private GameObject _menu;
        private GameObject _actionMenu;
        private int _actionTower = -1;
        private GameObject _modal;
        private RectTransform _modalPanel;
        private GameObject _detailPanel;
        private GameObject _tooltip;
        private GameObject _highlight;
        private LineRenderer _range;

        private int _selectedTower = -1;

        private void Start()
        {
            if (driver == null) driver = FindObjectOfType<BattleDriver>();
            if (viewRoot == null) viewRoot = FindObjectOfType<BattleViewRoot>();
            if (worldCamera == null) worldCamera = Camera.main;
            if (canvas == null) canvas = FindObjectOfType<Canvas>();

            CreateHighlight();
            CreateRangeLine();
        }

        private void Update()
        {
            // 环形菜单固定在炮塔上:每帧跟随塔的世界位置换算到屏幕坐标。
            if (_actionMenu != null)
            {
                TowerAgent selected = FindTower(_actionTower);
                if (selected == null)
                {
                    CloseMenu();
                    ClearSelection();
                    return;
                }

                ((RectTransform)_actionMenu.transform).anchoredPosition =
                    ToCanvasLocal(viewRoot.ToWorld(selected.Position));
            }

            if (_selectedTower < 0 || driver == null || driver.Simulation == null) return;

            TowerAgent tower = FindTower(_selectedTower);
            if (tower == null)
            {
                ClearSelection();
                return;
            }

            UpdateRange(_selectedTower);
        }

        // ---- 点击入口 ----------------------------------------------------------

        public void HandleSlotClicked(int slotIndex)
        {
            if (driver == null || driver.Simulation == null) return;

            CloseModal();
            CloseMenu();
            CloseDetail();

            if (driver.Simulation.IsSlotOccupied(slotIndex))
            {
                int towerIndex = driver.Simulation.TowerIndexAtSlot(slotIndex);
                if (towerIndex >= 0) SelectTower(slotIndex, towerIndex);
            }
            else
            {
                SelectEmptySlot(slotIndex);
            }
        }

        public void HandleBackgroundClicked()
        {
            if (_modal != null) return;
            ClearSelection();
            CloseMenu();
            CloseDetail();
        }

        // ---- 选择 --------------------------------------------------------------

        private void SelectEmptySlot(int slotIndex)
        {
            _selectedTower = -1;

            viewRoot.SelectTower(-1);
            ShowHighlight(slotIndex);
            HideRange();
            ShowBuildMenu(slotIndex);
        }

        private void SelectTower(int slotIndex, int towerIndex)
        {
            _selectedTower = towerIndex;

            viewRoot.SelectTower(towerIndex);
            ShowHighlight(slotIndex);
            ShowRange(towerIndex);
            ShowActionMenu(slotIndex, towerIndex);
        }

        private void ClearSelection()
        {
            _selectedTower = -1;
            if (viewRoot != null) viewRoot.SelectTower(-1);
            if (_highlight != null) _highlight.SetActive(false);
            HideRange();
            CloseDetail();
        }

        // ---- 建造菜单 ----------------------------------------------------------

        private void ShowBuildMenu(int slotIndex)
        {
            _menu = CreateMenuRoot(SlotWorld(slotIndex), 264f);
            AddHeader(_menu.transform, "建造 · 选择防御塔", 16);

            IReadOnlyList<TowerDef> towers = driver.Database.Towers;
            int gold = driver.Simulation.Gold;

            for (int i = 0; i < towers.Count; i++)
            {
                TowerDef def = towers[i];
                bool affordable = gold >= def.Price;
                string towerId = def.Id;
                int slot = slotIndex;

                AddRow(_menu.transform,
                    DisplayName(def.Id),
                    def.Price + " 金币",
                    affordable ? UiKit.Ink : UiKit.Muted,
                    affordable,
                    () => BuildTower(slot, towerId));
            }
        }

        private void BuildTower(int slotIndex, string towerId)
        {
            if (!driver.TryBuild(towerId, slotIndex))
            {
                Toast("金币不足", SlotWorld(slotIndex));
                return;
            }

            int towerIndex = driver.Simulation.TowerIndexAtSlot(slotIndex);
            CloseMenu();
            if (towerIndex >= 0) SelectTower(slotIndex, towerIndex);
        }

        // ---- 塔操作菜单 --------------------------------------------------------

        private void ShowActionMenu(int slotIndex, int towerIndex)
        {
            TowerAgent tower = FindTower(towerIndex);
            if (tower == null) return;

            CloseMenu();
            _actionTower = towerIndex;

            RectTransform root = UiKit.Rect("RadialMenu", canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(210f, 210f);
            root.anchoredPosition = ToCanvasLocal(viewRoot.ToWorld(tower.Position));

            AddSectorButton(root, 0, new Vector2(-71f, 41f), "detail", false,
                () => ShowDetail(towerIndex));
            AddSectorButton(root, 1, new Vector2(71f, 41f), "upgrade", false,
                () => OnUpgradeClicked(slotIndex, towerIndex));
            AddSectorButton(root, 2, new Vector2(0f, -82f), "demolish", true,
                () => ConfirmSell(slotIndex, towerIndex));

            _actionMenu = root.gameObject;
        }

        /// <summary>三等分圆环扇区按钮:高透明淡灰、边缘柔化,悬停变亮,拆除悬停变淡红。</summary>
        private void AddSectorButton(RectTransform parent, int index, Vector2 iconPosition,
            string icon, bool danger, Action onClick)
        {
            RectTransform rect = UiKit.Rect("Sector" + index, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(210f, 210f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiIconFactory.Sector(index);
            image.raycastTarget = true;
            image.alphaHitTestMinimumThreshold = 0.3f;

            Color normal = new Color(0.94f, 0.94f, 0.95f, 0.55f);
            Color hover = danger
                ? new Color(1f, 0.68f, 0.68f, 0.85f)
                : new Color(0.8f, 0.82f, 0.86f, 0.82f);
            image.color = normal;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());

            UiIconFactory.CreateIcon(icon, rect, iconPosition);

            var trigger = rect.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, () => image.color = hover);
            AddTrigger(trigger, EventTriggerType.PointerExit, () => image.color = normal);
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        private List<UpgradeNodeDef> NextNodes(TowerAgent tower)
        {
            var result = new List<UpgradeNodeDef>();
            if (tower == null) return result;

            int nextLevel = tower.Level + 1;
            IReadOnlyList<UpgradeNodeDef> all = driver.Database.Upgrades;
            for (int i = 0; i < all.Count; i++)
            {
                UpgradeNodeDef node = all[i];
                if (!string.Equals(node.TowerId, tower.Def.Id)) continue;
                if (node.Level != nextLevel) continue;
                if (!driver.Simulation.CanApplyUpgrade(tower.Id.Index, node.Id)) continue;
                result.Add(node);
            }
            return result;
        }

        private void OnUpgradeClicked(int slotIndex, int towerIndex)
        {
            TowerAgent tower = FindTower(towerIndex);
            if (tower == null) return;

            List<UpgradeNodeDef> nodes = NextNodes(tower);
            if (nodes.Count == 0)
            {
                Toast("已满级", SlotWorld(slotIndex));
                return;
            }

            int min = int.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                int cost = driver.Simulation.UpgradeCostOf(towerIndex, nodes[i].Id);
                if (cost < min) min = cost;
            }

            if (driver.Simulation.Gold < min)
            {
                Toast("金币不足,需 " + min, SlotWorld(slotIndex));
                return;
            }

            ShowUpgradeOptions(slotIndex, tower, nodes);
        }

        /// <summary>升级窗口:每个选项自身就是确认按钮,取消不消耗金币。</summary>
        private void ShowUpgradeOptions(int slotIndex, TowerAgent tower, List<UpgradeNodeDef> nodes)
        {
            CreateModalRoot(DisplayName(tower.Def.Id) + " · 升级至 Lv" + (tower.Level + 1), 560f);

            for (int i = 0; i < nodes.Count; i++)
            {
                UpgradeNodeDef node = nodes[i];
                int cost = driver.Simulation.UpgradeCostOf(tower.Id.Index, node.Id);
                AddUpgradeOption(_modalPanel, node, cost, slotIndex, tower.Id.Index);
            }

            AddCancelRow(_modalPanel);
            CenterModal();
        }

        private void AddUpgradeOption(Transform parent, UpgradeNodeDef node, int cost,
            int slotIndex, int towerIndex)
        {
            Button button = UiKit.Button("Option", parent, "", 16);
            UiKit.Height(button.gameObject, 80f);

            Text title = UiKit.Label("Name", button.transform,
                PrettyName(node.Id) + (node.Unique ? "  · 唯一" : ""), 18,
                TextAnchor.MiddleCenter, UiKit.Ink);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -7f), new Vector2(-18f, 26f));
            string buffs = string.Empty;
            string costs = string.Empty;
            for (int i = 0; i < node.Modifiers.Count; i++)
            {
                bool isBuff = FormatModifier(node.Modifiers[i], out string line);
                if (isBuff)
                    buffs += (buffs.Length > 0 ? "\n" : "") + line;
                else
                    costs += (costs.Length > 0 ? "\n" : "") + line;
            }
            costs += (costs.Length > 0 ? "\n" : "") + "金币 : -" + cost;

            Text left = UiKit.Label("Buff", button.transform, buffs, 14,
                TextAnchor.UpperLeft, BuffColor);
            SetRect(left.rectTransform, new Vector2(0f, 0f), new Vector2(0.52f, 0f),
                new Vector2(0f, 0f), new Vector2(12f, 6f), new Vector2(-12f, 44f));
            left.horizontalOverflow = HorizontalWrapMode.Wrap;

            Text right = UiKit.Label("Cost", button.transform, costs, 14,
                TextAnchor.UpperRight, CostColor);
            SetRect(right.rectTransform, new Vector2(0.52f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-12f, 6f), new Vector2(-12f, 44f));
            right.horizontalOverflow = HorizontalWrapMode.Wrap;

            if (node.Unique && !string.IsNullOrEmpty(node.Mechanic))
            {
                var trigger = button.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => ShowMechanicTip(node.Mechanic));
                trigger.triggers.Add(enter);

                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => HideMechanicTip());
                trigger.triggers.Add(exit);
            }

            string nodeId = node.Id;
            int slot = slotIndex;
            int index = towerIndex;
            button.onClick.AddListener(() => ApplyUpgrade(slot, index, nodeId));
        }

        private void AddCancelRow(Transform parent)
        {
            RectTransform row = UiKit.Rect("CancelRow", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            UiKit.Height(row.gameObject, 28f);

            Button cancel = UiKit.Button("Cancel", row, "取消", 14);
            var element = cancel.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 72f;
            element.preferredHeight = 24f;
            cancel.onClick.AddListener(CloseModal);
        }

        private void ApplyUpgrade(int slotIndex, int towerIndex, string nodeId)
        {
            HideMechanicTip();

            int cost;
            if (!driver.Simulation.TryUpgradeTower(towerIndex, nodeId, out cost))
            {
                Toast("升级失败", SlotWorld(slotIndex));
                CloseModal();
                return;
            }

            CloseModal();
            CloseMenu();
            SelectTower(slotIndex, towerIndex);
            Toast("升级完成  -" + cost, SlotWorld(slotIndex));
        }

        private void ConfirmSell(int slotIndex, int towerIndex)
        {
            int invested = driver.TowerInvested(towerIndex);
            int upgradeSpent = driver.TowerUpgradeSpent(towerIndex);
            int refund = driver.SellRefund(towerIndex);
            string body = "确定拆除该防御塔?\n" +
                          "建造与升级累计投入 " + invested + " 金币\n" +
                          "建造费用返还 100% + 升级费用返还 50%\n" +
                          "可得 " + refund + " 金币" + (upgradeSpent > 0 ? "（升级部分折价回收）" : "");

            ShowModal("拆除确认", body, "拆除", UiKit.Danger, () =>
            {
                int got;
                if (driver.TrySell(towerIndex, out got))
                {
                    // 拆除成功后同步关闭塔的操作菜单与选中态。
                    CloseMenu();
                    ClearSelection();
                    Toast("已拆除  +" + got, SlotWorld(slotIndex));
                }
                else
                {
                    ClearSelection();
                }
            });
        }

        // ---- 详情 --------------------------------------------------------------

        private void ShowDetail(int towerIndex)
        {
            TowerAgent tower = FindTower(towerIndex);
            if (tower == null) return;

            CloseDetail();

            RectTransform panel = UiKit.Rect("DetailPanel", canvas.transform);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.9f);
            UiKit.AddBorder(bg, 2f);
            UiKit.Vertical(panel, 8f, 14);

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-16f, 0f);
            panel.sizeDelta = new Vector2(360f, 0f);

            AddHeader(panel, "详细数据", 18);

            string text = BuildDetailText(tower);
            Text body = UiKit.Label("Body", panel, text, 16,
                TextAnchor.UpperLeft, UiKit.Ink);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;

            int lines = text.Split('\n').Length;
            UiKit.Height(body.gameObject, Mathf.Max(120f, lines * 23f + 10f));

            RectTransform row = UiKit.Rect("CloseRow", panel);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            UiKit.Height(row.gameObject, 34f);

            Button close = UiKit.Button("Close", row, "关闭", 15);
            var element = close.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 92f;
            element.preferredHeight = 32f;
            close.onClick.AddListener(CloseDetail);

            _detailPanel = panel.gameObject;
        }

        private void CloseDetail()
        {
            if (_detailPanel != null) Destroy(_detailPanel);
            _detailPanel = null;
        }

        private string BuildDetailText(TowerAgent tower)
        {
            TowerRuntimeStats s = tower.Stats;
            string text = s.DroneCapacity > 0
                ? BuildDroneDetail(tower, s)
                : BuildWeaponDetail(tower, s);

            if (tower.Upgrades.Count > 0)
            {
                text += "\n已选升级:\n";
                for (int i = 0; i < tower.Upgrades.Count; i++)
                {
                    text += "  Lv" + tower.Upgrades[i].Level + "  " +
                            PrettyName(tower.Upgrades[i].Id) + "\n";
                }
            }

            return text.TrimEnd();
        }

        private static string BuildWeaponDetail(TowerAgent tower, TowerRuntimeStats s)
        {
            string text = "等级  " + tower.Level + " / 7    机制  " +
                          (string.IsNullOrEmpty(s.Mechanic) ? "—" : s.Mechanic) + "\n";
            text += "伤害  " + s.Damage.ToString("0.#") +
                    "    射速  " + s.FireRate.ToString("0.##") + "\n";
            text += "对单 DPS  " + EstimateDps(s).ToString("0.#") + "\n";
            text += "射程  " + s.Range.ToString("0.#");
            if (s.MinRange > 0f) text += "(最小 " + s.MinRange.ToString("0.#") + ")";
            text += "\n穿甲  " + PierceName(s.Pierce) +
                    "    弹匣  " + (s.Ammo > 0 ? s.Ammo.ToString() : "无限");
            return text;
        }

        private static string BuildDroneDetail(TowerAgent tower, TowerRuntimeStats s)
        {
            bool factory = string.Equals(s.Mechanic, "mobile_factory");

            string text = "等级  " + tower.Level + " / 7    机制  " +
                          (string.IsNullOrEmpty(s.Mechanic) ? "—" : s.Mechanic) + "\n";
            text += "\n【机库】\n";
            text += "容量  " + s.DroneCapacity + "    每批  " + s.DroneSquad + "\n";
            text += "雷达  " + s.RadarRange.ToString("0.#") +
                    "    移动  " + s.DroneSpeed.ToString("0.#") + "\n";
            text += "补充  " + s.DroneReload.ToString("0.#") + "s" +
                    "    出击间隔  " + s.DroneDeployInterval.ToString("0.#") + "s\n";
            if (factory)
                text += "本体在雷达范围内随敌群重心缓慢移动\n";

            text += "\n【编队】\n";
            text += "单机伤害  " + s.DroneDamage.ToString("0.#") +
                    "    射速  " + s.DroneFireRate.ToString("0.##") + "\n";
            text += "编队 DPS  " + EstimateDps(s).ToString("0.#") + "\n";
            text += "射程  " + s.DroneRange.ToString("0.#") +
                    "    弹药  " + s.DroneAmmo.ToString("0") + "\n";
            if (s.DroneSelfDestructRadius > 0f)
            {
                text += "自毁  " + s.DroneSelfDestructDamage.ToString("0.#") +
                        " / 半径 " + s.DroneSelfDestructRadius.ToString("0.#");
            }
            return text;
        }

        private static float EstimateDps(TowerRuntimeStats s)
        {
            if (s.DroneCapacity > 0)
            {
                int squad = Mathf.Max(1, s.DroneSquad);
                return s.DroneDamage * s.DroneFireRate * squad;
            }
            return s.Damage * Mathf.Max(1, s.BeamCount) * s.FireRate * Mathf.Max(1, s.Salvo);
        }

        // ---- 通用 UI -----------------------------------------------------------

        private GameObject CreateMenuRoot(Vector3 worldPosition, float width)
        {
            RectTransform root = UiKit.Rect("Menu", canvas.transform);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.92f);
            UiKit.AddBorder(bg, 2f);
            UiKit.Vertical(root, 6f, 10);

            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(width, 0f);
            root.anchoredPosition = ToCanvasLocal(worldPosition) + new Vector2(0f, 26f);
            return root.gameObject;
        }

        private GameObject CreateModalRoot(string title, float width)
        {
            CloseModal();

            RectTransform blocker = UiKit.Rect("Modal", canvas.transform);
            UiKit.Stretch(blocker);
            var dim = blocker.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.28f);
            dim.raycastTarget = true;

            RectTransform panel = UiKit.Rect("Panel", blocker);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = UiKit.Paper;
            UiKit.AddBorder(bg, 2f);
            UiKit.Vertical(panel, 8f, 14);

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(width, 0f);

            AddHeader(panel, title, 18);
            _modal = blocker.gameObject;
            _modalPanel = panel;
            return blocker.gameObject;
        }

        private void CenterModal()
        {
            if (_modalPanel == null) return;
            Canvas.ForceUpdateCanvases();
            _modalPanel.anchoredPosition = Vector2.zero;
        }

        private void ShowModal(string title, string body, string confirmText, Color confirmColor,
            Action onConfirm)
        {
            _modal = CreateModalRoot(title, 500f);

            Text text = UiKit.Label("Body", _modalPanel, body, 15, TextAnchor.UpperLeft, UiKit.Ink);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Height(text.gameObject, 130f);

            RectTransform row = UiKit.Rect("Buttons", _modalPanel);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            UiKit.Height(row.gameObject, 44f);

            Button cancel = UiKit.Button("Cancel", row, "取消");
            cancel.onClick.AddListener(CloseModal);

            Button confirm = UiKit.Button("Confirm", row, confirmText);
            UiKit.SetText(confirm, confirmText, confirmColor);
            confirm.onClick.AddListener(() =>
            {
                onConfirm?.Invoke();
                CloseModal();
            });

            CenterModal();
        }

        private void AddHeader(Transform parent, string title, int size)
        {
            Text header = UiKit.Label("Header", parent, title, size, TextAnchor.MiddleLeft, UiKit.Ink);
            UiKit.Height(header.gameObject, 24f);
        }

        private void AddRow(Transform parent, string label, string right, Color labelColor,
            bool enabled, Action onClick)
        {
            Button button = UiKit.Button("Row", parent, "", 17);
            UiKit.Height(button.gameObject, 32f);

            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                text.alignment = TextAnchor.MiddleLeft;
                text.color = enabled ? labelColor : UiKit.Muted;
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                text.rectTransform.offsetMax = new Vector2(-96f, 0f);
            }

            if (!string.IsNullOrEmpty(right))
            {
                Text tail = UiKit.Label("Tail", button.transform, right, 15,
                    TextAnchor.MiddleRight, enabled ? UiKit.Gold : UiKit.Muted);
                UiKit.Stretch(tail.rectTransform);
                tail.rectTransform.offsetMax = new Vector2(-10f, 0f);
            }

            button.interactable = enabled;
            if (onClick != null) button.onClick.AddListener(() => onClick());
        }

        // ---- 高亮 / 射程 -------------------------------------------------------

        private void CreateHighlight()
        {
            _highlight = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _highlight.name = "SlotHighlight";
            Collider collider = _highlight.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            _highlight.transform.localScale = new Vector3(3.6f, 0.02f, 3.6f);
            Renderer renderer = _highlight.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Unlit/Color"));
                material.color = new Color(0.09f, 0.1f, 0.13f);
                renderer.sharedMaterial = material;
            }
            _highlight.SetActive(false);
        }

        private void ShowHighlight(int slotIndex)
        {
            if (_highlight == null || driver.Simulation == null) return;
            SlotDef slot = driver.Simulation.Slots[slotIndex];
            _highlight.transform.position = new Vector3(slot.X, 0.005f, slot.Y);
            _highlight.SetActive(true);
        }

        private void CreateRangeLine()
        {
            var go = new GameObject("RangeCircle");
            go.transform.SetParent(transform, false);

            _range = go.AddComponent<LineRenderer>();
            _range.useWorldSpace = true;
            _range.loop = true;
            _range.widthMultiplier = 0.16f;
            _range.positionCount = RangeSegments;
            _range.numCapVertices = 2;
            _range.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _range.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var rangeMaterial = new Material(shader);
            rangeMaterial.color = new Color(0.05f, 0.06f, 0.08f, 0.95f);
            _range.material = rangeMaterial;
            _range.startColor = new Color(0.05f, 0.06f, 0.08f, 0.95f);
            _range.endColor = new Color(0.05f, 0.06f, 0.08f, 0.95f);
            _range.enabled = false;
        }

        private void ShowRange(int towerIndex)
        {
            UpdateRange(towerIndex);
            if (_range != null) _range.enabled = true;
        }

        private void UpdateRange(int towerIndex)
        {
            TowerAgent tower = FindTower(towerIndex);
            if (tower == null || _range == null || viewRoot == null) return;

            Vector3 center = viewRoot.ToWorld(tower.Position);
            // 无人机库的"射程"是雷达范围;其余塔用武器射程。
            float radius = tower.Stats.DroneCapacity > 0 ? tower.Stats.RadarRange : tower.Stats.Range;

            for (int i = 0; i < RangeSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RangeSegments;
                Vector3 point = center + new Vector3(
                    Mathf.Cos(angle) * radius, 0.12f, Mathf.Sin(angle) * radius);
                _range.SetPosition(i, point);
            }
        }

        private void HideRange()
        {
            if (_range != null) _range.enabled = false;
        }

        // ---- 飘字 --------------------------------------------------------------

        private void Toast(string message, Vector3 worldPosition)
        {
            StartCoroutine(ToastRoutine(message, worldPosition));
        }

        private IEnumerator ToastRoutine(string message, Vector3 worldPosition)
        {
            RectTransform rect = UiKit.Rect("Toast", canvas.transform);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(290f, 42f);

            Vector2 origin = ToCanvasLocal(worldPosition) + new Vector2(0f, 44f);
            rect.anchoredPosition = origin;

            // 透明底飘字:靠白描边保证在白色地图与深色城墙上都能看清。
            Text label = UiKit.Label("Label", rect, message, 16, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.Stretch(label.rectTransform);
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(1.6f, -1.6f);
            outline.useGraphicAlpha = false;

            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;

            float elapsed = 0f;
            const float hold = 0.9f;
            const float fade = 0.7f;

            while (elapsed < hold + fade)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed > hold) group.alpha = Mathf.Clamp01(1f - (elapsed - hold) / fade);
                rect.anchoredPosition = origin + new Vector2(0f, elapsed * 26f);
                yield return null;
            }

            Destroy(rect.gameObject);
        }

        // ---- 工具 --------------------------------------------------------------

        private Vector2 ToCanvasLocal(Vector3 worldPosition)
        {
            Vector3 screen = worldCamera != null
                ? worldCamera.WorldToScreenPoint(worldPosition)
                : worldPosition;
            return ScreenToCanvas(screen);
        }

        private Vector2 ScreenToCanvas(Vector3 screen)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)canvas.transform, screen, null, out local);
            return local;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        /// <summary>把一条升级修饰格式化成"属性 : +20%";返回是否为增益。</summary>
        private static bool FormatModifier(StatModifier mod, out string line)
        {
            string name = StatNames.TryGetValue(mod.Key.ToString(), out string cn)
                ? cn
                : mod.Key.ToString();

            if (mod.Op == ModifierOp.Set)
            {
                line = name + " : " + mod.Value.ToString("0.##");
                return true;
            }

            bool lowerBetter = LowerIsBetter.Contains(mod.Key);

            if (mod.Op == ModifierOp.Add)
            {
                bool buff = lowerBetter ? mod.Value < 0f : mod.Value > 0f;
                line = name + " : " + (mod.Value > 0f ? "+" : "") + mod.Value.ToString("0.##");
                return buff;
            }

            float percent = (mod.Value - 1f) * 100f;
            bool isBuff = lowerBetter ? percent < 0f : percent > 0f;
            line = name + " : " + (percent >= 0f ? "+" : "") + percent.ToString("0.#") + "%";
            return isBuff;
        }

        private void ShowMechanicTip(string mechanic)
        {
            HideMechanicTip();

            string text;
            if (!MechanicTips.TryGetValue(mechanic, out text)) text = mechanic;

            RectTransform rect = UiKit.Rect("MechanicTip", canvas.transform);
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.82f);
            bg.raycastTarget = false;
            UiKit.AddBorder(bg, 1.5f);

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(320f, 92f);
            rect.anchoredPosition = ScreenToCanvas(Input.mousePosition) + new Vector2(18f, 18f);

            Text label = UiKit.Label("Text", rect, text, 14, TextAnchor.UpperLeft, UiKit.Ink);
            UiKit.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(10f, 8f);
            label.rectTransform.offsetMax = new Vector2(-10f, -8f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            _tooltip = rect.gameObject;
        }

        private void HideMechanicTip()
        {
            if (_tooltip != null) Destroy(_tooltip);
            _tooltip = null;
        }

        private Vector3 SlotWorld(int slotIndex)
        {
            if (driver == null || driver.Simulation == null) return Vector3.zero;
            SlotDef slot = driver.Simulation.Slots[slotIndex];
            return viewRoot.ToWorld(new Vec2(slot.X, slot.Y));
        }

        private TowerAgent FindTower(int towerIndex)
        {
            if (driver == null || driver.Simulation == null) return null;
            IReadOnlyList<TowerAgent> towers = driver.Simulation.Towers;
            for (int i = 0; i < towers.Count; i++)
            {
                if (towers[i].Id.Index == towerIndex) return towers[i];
            }
            return null;
        }

        private void CloseMenu()
        {
            if (_menu != null) Destroy(_menu);
            _menu = null;

            if (_actionMenu != null) Destroy(_actionMenu);
            _actionMenu = null;
            _actionTower = -1;
        }

        private void CloseModal()
        {
            HideMechanicTip();
            if (_modal != null) Destroy(_modal);
            _modal = null;
            _modalPanel = null;
        }

        private static string DisplayName(string towerId)
        {
            switch (towerId)
            {
                case "machine_gun": return "机枪塔";
                case "laser": return "激光塔";
                case "mortar": return "迫击炮";
                case "rocket": return "火箭塔";
                case "tesla": return "电弧塔";
                case "drone_hangar": return "无人机库";
                default: return towerId;
            }
        }

        private static string PierceName(ArmorPierce pierce)
        {
            switch (pierce)
            {
                case ArmorPierce.High: return "高";
                case ArmorPierce.Mid: return "中";
                default: return "低";
            }
        }

        private static string PrettyName(string nodeId)
        {
            string[] parts = nodeId.Split('_');
            int start = parts.Length > 2 ? 2 : 0;
            return string.Join(" ", parts, start, parts.Length - start);
        }
    }
}
