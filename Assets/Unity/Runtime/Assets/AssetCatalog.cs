using System;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 美术 / 音效索引表:把配置表里的字符串 id 映射到具体资源。
    /// 约定:一个 id 一份图标 + 一份预制体;缺失时留空,运行时自动退化为占位。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Asset Catalog", fileName = "AssetCatalog")]
    public sealed class AssetCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class TowerArt
        {
            public string id;
            public Sprite icon;
            public GameObject prefab;
            public AudioClip fireSfx;
            public AudioClip upgradeSfx;
        }

        [Serializable]
        public sealed class EnemyArt
        {
            public string id;
            public Sprite icon;
            public GameObject prefab;
            public AudioClip deathSfx;
        }

        [Serializable]
        public sealed class ProjectileArt
        {
            [Tooltip("对应 ProjectileSpec.Mechanic;留空表示常规弹体")]
            public string mechanic;
            public GameObject prefab;
            public AudioClip impactSfx;
        }

        [SerializeField] private TowerArt[] towers;
        [SerializeField] private EnemyArt[] enemies;
        [SerializeField] private ProjectileArt[] projectiles;
        [SerializeField] private Sprite[] uiIcons;

        public TowerArt FindTower(string id)
        {
            if (towers == null) return null;
            for (int i = 0; i < towers.Length; i++)
            {
                if (string.Equals(towers[i].id, id, StringComparison.Ordinal)) return towers[i];
            }
            return null;
        }

        public EnemyArt FindEnemy(string id)
        {
            if (enemies == null) return null;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (string.Equals(enemies[i].id, id, StringComparison.Ordinal)) return enemies[i];
            }
            return null;
        }

        public ProjectileArt FindProjectile(string mechanic)
        {
            if (projectiles == null) return null;
            string key = mechanic ?? string.Empty;
            for (int i = 0; i < projectiles.Length; i++)
            {
                if (string.Equals(projectiles[i].mechanic ?? string.Empty, key, StringComparison.Ordinal))
                    return projectiles[i];
            }
            return null;
        }
    }
}
