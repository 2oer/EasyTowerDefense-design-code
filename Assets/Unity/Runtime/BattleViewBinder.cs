using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 把 BattleDriver 与 BattleViewRoot 接起来:对局创建时自动绑定视图。
    /// 单独拆出来,避免场景里依赖脚本执行顺序(BattleDriver.Start 会立即开战)。
    /// </summary>
    public sealed class BattleViewBinder : MonoBehaviour
    {
        [SerializeField] private BattleDriver driver;
        [SerializeField] private BattleViewRoot viewRoot;

        private void Awake()
        {
            if (driver == null) driver = FindObjectOfType<BattleDriver>();
            if (viewRoot == null) viewRoot = FindObjectOfType<BattleViewRoot>();
            if (driver == null || viewRoot == null) return;

            driver.BattleStarted += viewRoot.Bind;

            // 如果对局已经在 Awake/Start 之前创建,直接补一次绑定。
            if (driver.Simulation != null) viewRoot.Bind(driver.Simulation);
        }

        private void OnDestroy()
        {
            if (driver != null && viewRoot != null) driver.BattleStarted -= viewRoot.Bind;
        }
    }
}
