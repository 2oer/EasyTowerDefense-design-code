using Game.Battle;
using Game.Combat;
using Game.Core;
using Game.Entity;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>敌人视图:只负责跟随逻辑坐标、朝向与血条,不含任何战斗判定。</summary>
    public sealed class EnemyView : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform hpFill;
        [SerializeField] private Transform statusRoot;

        private EnemyRuntime _runtime;

        public EnemyRuntime Runtime { get { return _runtime; } }

        public void Bind(EnemyRuntime runtime)
        {
            _runtime = runtime;
            gameObject.SetActive(true);
        }

        /// <summary>由 BattleViewRoot 每帧调用;worldPosition 已完成逻辑→世界坐标换算。</summary>
        public void Tick(Vector3 worldPosition, float logicalRadius, float unitScale)
        {
            if (_runtime == null) return;

            transform.position = worldPosition;
            // 球体原始直径为 1,乘 2 让缩放后的直径等于逻辑直径;同时抬到贴地高度。
            if (body != null)
            {
                body.localScale = Vector3.one * Mathf.Max(0.1f, logicalRadius * 2f * unitScale);
                body.localPosition = new Vector3(0f, logicalRadius * unitScale, 0f);
            }

            if (hpFill != null)
            {
                float ratio = _runtime.MaxHp > 0f ? Mathf.Clamp01(_runtime.Hp / _runtime.MaxHp) : 0f;
                float width = Mathf.Max(1f, logicalRadius * 2.2f);
                Vector3 scale = hpFill.localScale;
                scale.x = width * ratio;
                hpFill.localScale = scale;

                Vector3 position = hpFill.localPosition;
                position.y = logicalRadius * 2f + 0.6f;
                hpFill.localPosition = position;
            }

            if (statusRoot != null)
            {
                // 状态图标(燃烧 / 低温 / 碎甲)由表现层挂点;这里只做显隐。
                statusRoot.gameObject.SetActive(_runtime.Status.HasAny);
            }
        }

        public void Release()
        {
            _runtime = null;
            gameObject.SetActive(false);
        }
    }
}
