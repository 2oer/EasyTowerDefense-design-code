using Game.Combat;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>投射物视图。迫击炮用弧线插值,火箭直接跟随运行时位置。</summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        [SerializeField] private float arcHeight = 3.5f;

        public void Tick(ProjectileRuntime projectile, Vector3 start, Vector3 current, Vector3 target)
        {
            if (projectile == null) return;

            if (projectile.Homing)
            {
                transform.position = current;
                Vector3 delta = target - current;
                if (delta.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(delta);
                return;
            }

            // 抛射:逻辑插值 + 正弦抬升,只是表现层近似。
            float t = projectile.FlightTime > 0f
                ? Mathf.Clamp01(projectile.Elapsed / projectile.FlightTime)
                : 1f;
            Vector3 flat = Vector3.Lerp(start, target, t);
            flat.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
            transform.position = flat;
        }
    }
}
