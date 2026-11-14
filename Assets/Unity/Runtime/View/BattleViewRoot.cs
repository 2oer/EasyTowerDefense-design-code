using System.Collections.Generic;
using Game.Battle;
using Game.Combat;
using Game.Core;
using Game.Entity;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 表现层总同步:每帧把逻辑世界(敌人 / 塔 / 投射物 / 区域 / 编队 / 避雷针)
    /// 映射成场景里的视图对象。逻辑坐标 (x, y) → 世界坐标 (x, 0, y) × unitScale。
    /// 视图只读取运行时数据,不反向写入。
    /// </summary>
    public sealed class BattleViewRoot : MonoBehaviour
    {
        [Header("坐标")]
        [SerializeField] private float unitScale = 1f;
        [SerializeField] private Vector3 worldOrigin = Vector3.zero;

        [Header("资源索引(按 id 取预制体,缺失时回落到下面的默认预制体)")]
        [SerializeField] private AssetCatalog catalog;

        [Header("默认预制体")]
        [SerializeField] private EnemyView enemyPrefab;
        [SerializeField] private TowerView towerPrefab;
        [SerializeField] private ProjectileView projectilePrefab;
        [SerializeField] private DroneSquadView droneSquadPrefab;
        [SerializeField] private AreaFieldView areaFieldPrefab;
        [SerializeField] private Transform rodPrefab;

        [Header("特效")]
        [SerializeField] private BattleVfxSink vfxSink;

        private BattleSimulation _sim;

        private readonly Dictionary<int, EnemyView> _activeEnemies = new Dictionary<int, EnemyView>();
        private readonly Dictionary<string, List<EnemyView>> _enemyPools =
            new Dictionary<string, List<EnemyView>>();

        private readonly Dictionary<int, TowerView> _towers = new Dictionary<int, TowerView>();

        private readonly Dictionary<string, List<ProjectileView>> _projectilePools =
            new Dictionary<string, List<ProjectileView>>();

        private readonly Dictionary<AreaEffectField, AreaFieldView> _areas =
            new Dictionary<AreaEffectField, AreaFieldView>();
        private readonly List<AreaFieldView> _areaPool = new List<AreaFieldView>();

        private readonly Dictionary<DroneSquad, DroneSquadView> _drones =
            new Dictionary<DroneSquad, DroneSquadView>();
        private readonly List<DroneSquadView> _dronePool = new List<DroneSquadView>();

        private readonly Dictionary<LightningRod, Transform> _rods =
            new Dictionary<LightningRod, Transform>();

        private int _selectedTowerIndex = -1;

        public void Bind(BattleSimulation simulation)
        {
            if (_sim != null) _sim.Combat.Impacted -= HandleImpact;

            _sim = simulation;
            ClearAll();

            if (_sim == null) return;
            _sim.Combat.Impacted += HandleImpact;
        }

        private void OnDestroy()
        {
            if (_sim != null) _sim.Combat.Impacted -= HandleImpact;
        }

        public void SelectTower(int towerIndex)
        {
            _selectedTowerIndex = towerIndex;
        }

        private void LateUpdate()
        {
            if (_sim == null) return;

            SyncEnemies();
            SyncTowers();
            SyncProjectiles();
            SyncAreas();
            SyncDrones();
            SyncRods();
        }

        // ---- 敌人 --------------------------------------------------------------

        private void SyncEnemies()
        {
            IReadOnlyList<EnemyRuntime> enemies = _sim.Enemies;

            var stale = new List<int>();
            foreach (KeyValuePair<int, EnemyView> pair in _activeEnemies)
            {
                bool found = false;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].Id.Index == pair.Key) { found = true; break; }
                }
                if (!found) stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++)
            {
                EnemyView view = _activeEnemies[stale[i]];
                view.Release();
                _activeEnemies.Remove(stale[i]);
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyRuntime enemy = enemies[i];
                if (!enemy.Alive) continue;

                EnemyView view;
                if (!_activeEnemies.TryGetValue(enemy.Id.Index, out view))
                {
                    view = RentEnemy(enemy.Def.Id);
                    if (view == null) continue;
                    view.Bind(enemy);
                    _activeEnemies[enemy.Id.Index] = view;
                }

                view.Tick(ToWorld(enemy.Position), enemy.Radius, unitScale);
            }
        }

        private EnemyView RentEnemy(string enemyId)
        {
            List<EnemyView> pool;
            if (!_enemyPools.TryGetValue(enemyId, out pool))
            {
                pool = new List<EnemyView>();
                _enemyPools[enemyId] = pool;
            }

            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].gameObject.activeSelf) return pool[i];
            }

            GameObject prefab = ResolveEnemyPrefab(enemyId);
            if (prefab == null) return null;

            GameObject instance = Instantiate(prefab, transform);
            EnemyView view = instance.GetComponent<EnemyView>();
            if (view == null) view = instance.AddComponent<EnemyView>();
            pool.Add(view);
            return view;
        }

        private GameObject ResolveEnemyPrefab(string enemyId)
        {
            if (catalog != null)
            {
                AssetCatalog.EnemyArt art = catalog.FindEnemy(enemyId);
                if (art != null && art.prefab != null) return art.prefab;
            }
            return enemyPrefab != null ? enemyPrefab.gameObject : null;
        }

        // ---- 塔 ----------------------------------------------------------------

        private void SyncTowers()
        {
            IReadOnlyList<TowerAgent> towers = _sim.Towers;

            for (int i = 0; i < towers.Count; i++)
            {
                TowerAgent tower = towers[i];
                TowerView view;
                if (!_towers.TryGetValue(tower.Id.Index, out view))
                {
                    view = CreateTowerView(tower.Def.Id);
                    if (view == null) continue;
                    view.Bind(tower);
                    _towers[tower.Id.Index] = view;
                }

                view.Tick(ToWorld(tower.Position));
                view.SetSelected(tower.Id.Index == _selectedTowerIndex);
            }
        }

        private TowerView CreateTowerView(string towerId)
        {
            GameObject prefab = ResolveTowerPrefab(towerId);
            if (prefab == null) return null;
            GameObject instance = Instantiate(prefab, transform);
            TowerView view = instance.GetComponent<TowerView>();
            if (view == null) view = instance.AddComponent<TowerView>();
            return view;
        }

        private GameObject ResolveTowerPrefab(string towerId)
        {
            if (catalog != null)
            {
                AssetCatalog.TowerArt art = catalog.FindTower(towerId);
                if (art != null && art.prefab != null) return art.prefab;
            }
            return towerPrefab != null ? towerPrefab.gameObject : null;
        }

        // ---- 投射物 ------------------------------------------------------------

        private void SyncProjectiles()
        {
            List<ProjectileRuntime> projectiles = _sim.Combat.Projectiles;

            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileRuntime projectile = projectiles[i];
                string key = projectile.Spec.Mechanic ?? string.Empty;

                List<ProjectileView> pool;
                if (!_projectilePools.TryGetValue(key, out pool))
                {
                    pool = new List<ProjectileView>();
                    _projectilePools[key] = pool;
                }

                ProjectileView view = null;
                for (int p = 0; p < pool.Count; p++)
                {
                    if (!pool[p].gameObject.activeSelf) { view = pool[p]; break; }
                }
                if (view == null)
                {
                    GameObject prefab = ResolveProjectilePrefab(key);
                    if (prefab == null) continue;
                    GameObject instance = Instantiate(prefab, transform);
                    view = instance.GetComponent<ProjectileView>();
                    if (view == null) view = instance.AddComponent<ProjectileView>();
                    pool.Add(view);
                }

                view.gameObject.SetActive(true);
                view.Tick(
                    projectile,
                    ToWorld(projectile.Start),
                    ToWorld(projectile.Position),
                    ToWorld(projectile.TargetPosition));
            }
        }

        private GameObject ResolveProjectilePrefab(string mechanic)
        {
            if (catalog != null)
            {
                AssetCatalog.ProjectileArt art = catalog.FindProjectile(mechanic);
                if (art != null && art.prefab != null) return art.prefab;
            }
            return projectilePrefab != null ? projectilePrefab.gameObject : null;
        }

        // ---- 区域 --------------------------------------------------------------

        private void SyncAreas()
        {
            IReadOnlyList<AreaEffectField> fields = _sim.Combat.Areas.Fields;

            var stale = new List<AreaEffectField>();
            foreach (KeyValuePair<AreaEffectField, AreaFieldView> pair in _areas)
            {
                bool found = false;
                for (int i = 0; i < fields.Count; i++)
                {
                    if (ReferenceEquals(fields[i], pair.Key)) { found = true; break; }
                }
                if (!found) stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                AreaFieldView view = _areas[stale[i]];
                view.Release();
                _areaPool.Add(view);
                _areas.Remove(stale[i]);
            }

            for (int i = 0; i < fields.Count; i++)
            {
                AreaEffectField field = fields[i];
                AreaFieldView view;
                if (!_areas.TryGetValue(field, out view))
                {
                    view = RentArea();
                    if (view == null) continue;
                    _areas[field] = view;
                    view.gameObject.SetActive(true);
                }
                view.Tick(field, ToWorld(field.Center), unitScale);
            }
        }

        private AreaFieldView RentArea()
        {
            for (int i = 0; i < _areaPool.Count; i++)
            {
                if (!_areaPool[i].gameObject.activeSelf) return _areaPool[i];
            }
            if (areaFieldPrefab == null) return null;
            AreaFieldView view = Instantiate(areaFieldPrefab, transform);
            _areaPool.Add(view);
            return view;
        }

        // ---- 无人机编队 --------------------------------------------------------

        private void SyncDrones()
        {
            IReadOnlyList<DroneSquad> squads = _sim.Combat.Drones.Squads;

            var stale = new List<DroneSquad>();
            foreach (KeyValuePair<DroneSquad, DroneSquadView> pair in _drones)
            {
                bool found = false;
                for (int i = 0; i < squads.Count; i++)
                {
                    if (ReferenceEquals(squads[i], pair.Key)) { found = true; break; }
                }
                if (!found) stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                DroneSquadView view = _drones[stale[i]];
                view.Release();
                _dronePool.Add(view);
                _drones.Remove(stale[i]);
            }

            for (int i = 0; i < squads.Count; i++)
            {
                DroneSquad squad = squads[i];
                DroneSquadView view;
                if (!_drones.TryGetValue(squad, out view))
                {
                    view = RentDroneSquad();
                    if (view == null) continue;
                    _drones[squad] = view;
                    view.gameObject.SetActive(true);
                }
                view.Tick(squad, ToWorld(squad.Position), unitScale);
            }
        }

        private DroneSquadView RentDroneSquad()
        {
            for (int i = 0; i < _dronePool.Count; i++)
            {
                if (!_dronePool[i].gameObject.activeSelf) return _dronePool[i];
            }
            if (droneSquadPrefab == null) return null;
            DroneSquadView view = Instantiate(droneSquadPrefab, transform);
            _dronePool.Add(view);
            return view;
        }

        // ---- 避雷针 ------------------------------------------------------------

        private void SyncRods()
        {
            IReadOnlyList<LightningRod> rods = _sim.Combat.Rods.Rods;

            var stale = new List<LightningRod>();
            foreach (KeyValuePair<LightningRod, Transform> pair in _rods)
            {
                bool found = false;
                for (int i = 0; i < rods.Count; i++)
                {
                    if (ReferenceEquals(rods[i], pair.Key)) { found = true; break; }
                }
                if (!found) stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                Destroy(_rods[stale[i]].gameObject);
                _rods.Remove(stale[i]);
            }

            if (rodPrefab == null) return;

            for (int i = 0; i < rods.Count; i++)
            {
                LightningRod rod = rods[i];
                Transform view;
                if (!_rods.TryGetValue(rod, out view))
                {
                    view = Instantiate(rodPrefab, transform);
                    _rods[rod] = view;
                }
                view.position = ToWorld(rod.Position);
            }
        }

        // ---- 工具 --------------------------------------------------------------

        private void HandleImpact(ProjectileRuntime projectile)
        {
            if (vfxSink != null)
                vfxSink.Play(projectile.Position, projectile.Spec.Mechanic, projectile.Spec.ExplosionRadius);
        }

        public Vector3 ToWorld(Vec2 position)
        {
            return worldOrigin + new Vector3(position.X, 0f, position.Y) * unitScale;
        }

        /// <summary>世界坐标 → 逻辑坐标(取 x/z 平面),供点击拾取使用。</summary>
        public Vec2 ToLogical(Vector3 worldPosition)
        {
            Vector3 local = (worldPosition - worldOrigin) / Mathf.Max(0.0001f, unitScale);
            return new Vec2(local.x, local.z);
        }

        private void ClearAll()
        {
            foreach (KeyValuePair<int, EnemyView> pair in _activeEnemies) pair.Value.Release();
            _activeEnemies.Clear();

            foreach (KeyValuePair<int, TowerView> pair in _towers) pair.Value.Release();
            _towers.Clear();

            foreach (KeyValuePair<string, List<ProjectileView>> pair in _projectilePools)
            {
                for (int i = 0; i < pair.Value.Count; i++) pair.Value[i].gameObject.SetActive(false);
            }

            foreach (KeyValuePair<AreaEffectField, AreaFieldView> pair in _areas) pair.Value.Release();
            _areas.Clear();

            foreach (KeyValuePair<DroneSquad, DroneSquadView> pair in _drones) pair.Value.Release();
            _drones.Clear();

            foreach (KeyValuePair<LightningRod, Transform> pair in _rods)
                Destroy(pair.Value.gameObject);
            _rods.Clear();
        }
    }
}
