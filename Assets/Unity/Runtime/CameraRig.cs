using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 俯视相机:默认只显示基地与下半张地图,右键(或中键)拖动平移,滚轮缩放。
    /// 与点击建造互不冲突:建造使用左键。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Vector3 defaultPivot = new Vector3(40f, 0f, 26f);
        [SerializeField] private float defaultSize = 34f;
        [SerializeField] private float minSize = 14f;
        [SerializeField] private float maxSize = 90f;
        [SerializeField] private float zoomStep = 6f;
        [SerializeField] private float smooth = 12f;

        [Header("可移动地图范围(世界坐标)")]
        [SerializeField] private Vector2 mapMin = new Vector2(0f, 0f);
        [SerializeField] private Vector2 mapMax = new Vector2(64f, 120f);

        private Vector3 _pivot;
        private float _targetSize;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = GetComponent<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
            _pivot = defaultPivot;
            _targetSize = defaultSize;
            Apply(1f);
        }

        private void Update()
        {
            if (targetCamera == null) return;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetSize = Mathf.Clamp(_targetSize - scroll * zoomStep, minSize, maxSize);
            }

            if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                // 屏幕像素 → 世界单位,保证不同缩放级别下手感一致。
                float unitsPerPixel = 2f * _targetSize / Mathf.Max(1f, Screen.height);
                float dx = -Input.GetAxis("Mouse X") * unitsPerPixel * 24f;
                float dz = -Input.GetAxis("Mouse Y") * unitsPerPixel * 24f;
                Vector2 clamped = ClampPivot(new Vector2(_pivot.x + dx, _pivot.z + dz), _targetSize);
                _pivot.x = clamped.x;
                _pivot.z = clamped.y;
            }

            Apply(1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime));
        }

        private void Apply(float t)
        {
            if (targetCamera == null) return;

            targetCamera.orthographic = true;
            targetCamera.orthographicSize = Mathf.Lerp(targetCamera.orthographicSize, _targetSize, t);

            Vector2 clamped = ClampPivot(new Vector2(_pivot.x, _pivot.z), targetCamera.orthographicSize);
            _pivot.x = clamped.x;
            _pivot.z = clamped.y;

            Vector3 desired = new Vector3(_pivot.x, 100f, _pivot.z);
            targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, desired, t);
            targetCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>
        /// 限制视野不越出地图:视野半宽 = 正交尺寸 × 宽高比,半高 = 正交尺寸。
        /// 当地图比视野窄(或矮)时锁定该轴中心,避免露出地图外的空白。
        /// </summary>
        private Vector2 ClampPivot(Vector2 pivot, float size)
        {
            float aspect = targetCamera != null && targetCamera.aspect > 0.01f
                ? targetCamera.aspect
                : 16f / 9f;
            float halfWidth = size * aspect;
            float halfHeight = size;

            float minX = mapMin.x;
            float maxX = mapMax.x;
            float minZ = mapMin.y;
            float maxZ = mapMax.y;

            float x = (maxX - minX) <= halfWidth * 2f
                ? (minX + maxX) * 0.5f
                : Mathf.Clamp(pivot.x, minX + halfWidth, maxX - halfWidth);
            float z = (maxZ - minZ) <= halfHeight * 2f
                ? (minZ + maxZ) * 0.5f
                : Mathf.Clamp(pivot.y, minZ + halfHeight, maxZ - halfHeight);

            return new Vector2(x, z);
        }

        /// <summary>把视图聚焦到某个世界坐标(不改变缩放)。</summary>
        public void FocusOn(Vector3 world)
        {
            Vector2 clamped = ClampPivot(new Vector2(world.x, world.z), _targetSize);
            _pivot.x = clamped.x;
            _pivot.z = clamped.y;
        }

        public void ResetView()
        {
            _pivot = defaultPivot;
            _targetSize = defaultSize;
        }
    }
}
