using Game.Entity;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>防御塔视图:炮塔朝向、等级标记与选中高亮,不参与伤害结算。</summary>
    public sealed class TowerView : MonoBehaviour
    {
        [SerializeField] private Transform turret;
        [SerializeField] private Transform levelRoot;
        [SerializeField] private GameObject selectedMark;

        private TowerAgent _runtime;

        public TowerAgent Runtime { get { return _runtime; } }
        public int TowerIndex { get { return _runtime != null ? _runtime.Id.Index : -1; } }

        public void Bind(TowerAgent runtime)
        {
            _runtime = runtime;
            gameObject.SetActive(true);
        }

        public void Tick(Vector3 worldPosition)
        {
            if (_runtime == null) return;
            transform.position = worldPosition;

            if (turret != null)
            {
                // 逻辑角度:x 向右、y 向上;炮管沿 +Z,换算成绕 Y 的 yaw。
                float yaw = 90f - _runtime.Motor.AimAngle * Mathf.Rad2Deg;
                turret.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            if (levelRoot != null)
            {
                int level = _runtime.Level;
                for (int i = 0; i < levelRoot.childCount; i++)
                {
                    levelRoot.GetChild(i).gameObject.SetActive(i < level - 1);
                }
            }
        }

        public void SetSelected(bool selected)
        {
            if (selectedMark != null) selectedMark.SetActive(selected);
        }

        public void Release()
        {
            _runtime = null;
            gameObject.SetActive(false);
        }
    }
}
