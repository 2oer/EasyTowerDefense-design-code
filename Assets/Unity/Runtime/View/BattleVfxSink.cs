using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 一次性特效池:投射物落点时按机制 id 取预制体,寿命结束自动回收。
    /// 美术资源未到位前 fallback 留空即可,逻辑侧不会报错。
    /// </summary>
    public sealed class BattleVfxSink : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public GameObject prefab;
            public float lifetime = 1.2f;
            public float scalePerRadius = 0f;
        }

        [SerializeField] private Transform root;
        [SerializeField] private Entry[] entries;
        [SerializeField] private Entry fallback;
        [SerializeField] private float unitScale = 1f;

        private readonly Dictionary<string, List<ActiveVfx>> _pool =
            new Dictionary<string, List<ActiveVfx>>();

        private sealed class ActiveVfx
        {
            public GameObject Instance;
            public float Remaining;
        }

        private void Awake()
        {
            if (root == null) root = transform;
            for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(false);
        }

        public void Play(Vec2 position, string mechanic, float radius)
        {
            Entry entry = FindEntry(mechanic);
            if (entry == null || entry.prefab == null) return;

            Vector3 world = new Vector3(position.X, 0f, position.Y) * unitScale;
            GameObject instance = Rent(entry);
            instance.transform.position = world;

            float scale = 1f;
            if (entry.scalePerRadius > 0f) scale = Mathf.Max(1f, radius * entry.scalePerRadius);
            instance.transform.localScale = Vector3.one * scale;

            List<ActiveVfx> list = GetList(entry);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Instance == instance)
                {
                    list[i].Remaining = entry.lifetime;
                    return;
                }
            }
            list.Add(new ActiveVfx { Instance = instance, Remaining = entry.lifetime });
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            foreach (KeyValuePair<string, List<ActiveVfx>> pair in _pool)
            {
                List<ActiveVfx> list = pair.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    ActiveVfx vfx = list[i];
                    vfx.Remaining -= dt;
                    if (vfx.Remaining > 0f) continue;
                    vfx.Instance.SetActive(false);
                }
            }
        }

        private Entry FindEntry(string mechanic)
        {
            if (entries == null || string.IsNullOrEmpty(mechanic)) return fallback;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].id, mechanic, StringComparison.Ordinal)) return entries[i];
            }
            return fallback;
        }

        private List<ActiveVfx> GetList(Entry entry)
        {
            string key = entry.id ?? string.Empty;
            List<ActiveVfx> list;
            if (!_pool.TryGetValue(key, out list))
            {
                list = new List<ActiveVfx>();
                _pool[key] = list;
            }
            return list;
        }

        private GameObject Rent(Entry entry)
        {
            List<ActiveVfx> list = GetList(entry);
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Instance.activeSelf)
                {
                    list[i].Instance.SetActive(true);
                    return list[i].Instance;
                }
            }
            GameObject instance = Instantiate(entry.prefab, root);
            return instance;
        }
    }
}
