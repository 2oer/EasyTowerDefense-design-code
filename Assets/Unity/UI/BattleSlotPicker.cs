using System.Collections.Generic;
using Game.Core;
using Game.Model;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.UnityBridge.UI
{
    /// <summary>
    /// 槽位拾取:把鼠标射线投到 y = 0 平面,取最近的建造槽位。
    /// 不依赖碰撞体,槽位数据直接来自 maps.json,场景里摆放更自由。
    /// </summary>
    public sealed class BattleSlotPicker : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private BattleDriver driver;
        [SerializeField] private BattleViewRoot viewRoot;
        [SerializeField] private BuildController controller;
        [SerializeField] private float pickRadius = 5f;

        private void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (driver == null) driver = FindObjectOfType<BattleDriver>();
            if (viewRoot == null) viewRoot = FindObjectOfType<BattleViewRoot>();
            if (controller == null) controller = FindObjectOfType<BuildController>();
        }

        private void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (driver == null || driver.Simulation == null || viewRoot == null || controller == null) return;
            if (worldCamera == null) return;

            Ray ray = worldCamera.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return;

            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f) return;

            Vec2 logical = viewRoot.ToLogical(ray.origin + ray.direction * t);

            IReadOnlyList<SlotDef> slots = driver.Simulation.Slots;
            int bestIndex = -1;
            float bestDistance = pickRadius;

            for (int i = 0; i < slots.Count; i++)
            {
                float dx = slots[i].X - logical.X;
                float dy = slots[i].Y - logical.Y;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                bestIndex = i;
            }

            if (bestIndex >= 0) controller.HandleSlotClicked(bestIndex);
        }
    }
}
