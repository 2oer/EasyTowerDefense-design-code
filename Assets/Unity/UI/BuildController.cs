using System;
using Game.Battle;
using Game.Core;
using Game.Model;
using UnityEngine;

namespace Game.UnityBridge.UI
{
    /// <summary>
    /// 建造流程胶水层:记录当前选中槽位,把建造面板的请求落到 BattleSimulation。
    /// 详细的交互反馈(高亮、预览、禁用态)留给后续 UI 迭代。
    /// </summary>
    public sealed class BuildController : MonoBehaviour
    {
        [SerializeField] private BattleDriver driver;
        [SerializeField] private BuildPanelView buildPanel;
        [SerializeField] private UpgradePanelView upgradePanel;
        [SerializeField] private BattleViewRoot viewRoot;
        [SerializeField] private Transform slotHighlight;

        public int SelectedSlot { get; private set; } = -1;

        public event Action<int> SlotSelected;

        private void OnEnable()
        {
            if (buildPanel != null) buildPanel.BuildRequested += HandleBuildRequested;
        }

        private void OnDisable()
        {
            if (buildPanel != null) buildPanel.BuildRequested -= HandleBuildRequested;
        }

        private void Start()
        {
            if (driver == null) driver = FindObjectOfType<BattleDriver>();
            if (buildPanel == null) buildPanel = FindObjectOfType<BuildPanelView>();
            if (upgradePanel == null) upgradePanel = FindObjectOfType<UpgradePanelView>();
            if (viewRoot == null) viewRoot = FindObjectOfType<BattleViewRoot>();
        }

        /// <summary>点击空槽位时调用(由 BattleSlotPicker 转发)。</summary>
        public void SelectSlot(int slotIndex)
        {
            SelectedSlot = slotIndex;
            if (slotHighlight != null && driver != null && driver.Simulation != null &&
                slotIndex >= 0 && slotIndex < driver.Simulation.Slots.Count)
            {
                MapDefSlot(slotIndex);
            }
            SlotSelected?.Invoke(slotIndex);
        }

        /// <summary>点击战场槽位:已有塔则打开升级面板,否则选中等待建造。</summary>
        public void HandleSlotClicked(int slotIndex)
        {
            if (driver == null || driver.Simulation == null) return;

            BattleSimulation sim = driver.Simulation;
            if (sim.IsSlotOccupied(slotIndex))
            {
                int towerIndex = sim.TowerIndexAtSlot(slotIndex);
                if (upgradePanel != null && towerIndex >= 0) upgradePanel.ShowTower(towerIndex);
                return;
            }

            SelectSlot(slotIndex);
            AudioService service = AudioService.Instance;
            if (service != null) service.PlayUi("ui_slot");
        }

        private void HandleBuildRequested(string towerId)
        {
            if (driver == null || SelectedSlot < 0) return;
            if (!driver.TryBuild(towerId, SelectedSlot)) return;

            AudioService service = AudioService.Instance;
            if (service != null) service.PlayUi("ui_build");

            SelectedSlot = -1;
            if (buildPanel != null) buildPanel.RefreshAffordability();
        }

        private void MapDefSlot(int slotIndex)
        {
            if (slotHighlight == null || viewRoot == null) return;
            SlotDef slot = driver.Simulation.Slots[slotIndex];
            slotHighlight.position = viewRoot.ToWorld(new Vec2(slot.X, slot.Y));
        }
    }
}
