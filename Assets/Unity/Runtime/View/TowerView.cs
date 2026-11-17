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
        private LineRenderer _aimIndicator;

        public TowerAgent Runtime { get { return _runtime; } }
        public int TowerIndex { get { return _runtime != null ? _runtime.Id.Index : -1; } }

        private void Awake()
        {
            CreateAimIndicator();
        }

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

            UpdateAimIndicator();
        }

        /// <summary>
        /// 朝向指示线:所有需要转向的塔在炮口方向画一条短黑线(射程 20%,最长 10);
        /// 持续照射型激光与免转向塔不显示。
        /// </summary>
        private void UpdateAimIndicator()
        {
            if (_aimIndicator == null) return;

            if (_runtime == null || _runtime.Stats.NoTurn || _runtime.Stats.Continuous)
            {
                _aimIndicator.enabled = false;
                return;
            }

            _aimIndicator.enabled = true;
            float angle = _runtime.Motor.AimAngle;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float length = Mathf.Min(_runtime.Stats.Range * 0.2f, 10f);
            Vector3 start = transform.position + Vector3.up * 0.06f;
            _aimIndicator.SetPosition(0, start);
            _aimIndicator.SetPosition(1, start + direction * length);
        }

        private void CreateAimIndicator()
        {
            var go = new GameObject("AimIndicator");
            go.transform.SetParent(transform, false);

            _aimIndicator = go.AddComponent<LineRenderer>();
            _aimIndicator.useWorldSpace = true;
            _aimIndicator.loop = false;
            _aimIndicator.positionCount = 2;
            _aimIndicator.numCapVertices = 2;
            _aimIndicator.widthMultiplier = 0.06f;
            _aimIndicator.alignment = LineAlignment.View;
            _aimIndicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _aimIndicator.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _aimIndicator.material = new Material(shader);

            Color color = new Color(0.05f, 0.06f, 0.08f, 0.65f);
            _aimIndicator.startColor = color;
            _aimIndicator.endColor = color;
            _aimIndicator.enabled = false;
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
