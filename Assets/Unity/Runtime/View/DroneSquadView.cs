using Game.Combat;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 无人机编队视图:把编队内部数量映射成若干占位机体。
    /// 真正的机体动画 / 编队变换留给美术接入,这里只保证"看得见数量与位置"。
    /// </summary>
    public sealed class DroneSquadView : MonoBehaviour
    {
        [SerializeField] private Transform markersRoot;
        [SerializeField] private float spinSpeed = 1.2f;

        public void Tick(DroneSquad squad, Vector3 worldPosition, float unitScale)
        {
            if (squad == null || markersRoot == null) return;

            transform.position = worldPosition;
            markersRoot.Rotate(0f, spinSpeed * Time.deltaTime * 60f, 0f);

            int visible = Mathf.Clamp(squad.Alive, 0, markersRoot.childCount);
            for (int i = 0; i < markersRoot.childCount; i++)
            {
                Transform marker = markersRoot.GetChild(i);
                marker.gameObject.SetActive(i < visible);

                float angle = i * Mathf.PI * 2f / Mathf.Max(1, visible);
                float radius = Mathf.Lerp(0.6f, 1.4f, (i % 3) / 2f) * unitScale;
                marker.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }
        }

        public void Release()
        {
            gameObject.SetActive(false);
        }
    }
}
