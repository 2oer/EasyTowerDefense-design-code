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
        private readonly List<AoeRing> _rings = new List<AoeRing>();
        private Material _ringMaterial;

        private sealed class AoeRing
        {
            public LineRenderer Line;
            public float Age;
        }

        private sealed class BulletTracer
        {
            public LineRenderer Line;
            public Vector3 Start;
            public Vector3 End;
            public int Age;
        }

        private sealed class BeamTracer
        {
            public GameObject Root;
            public MeshRenderer FillRenderer;
            public LineRenderer Border;
            public int Age;
        }

        private readonly List<BulletTracer> _bulletTracers = new List<BulletTracer>();
        private readonly List<BeamTracer> _beamTracers = new List<BeamTracer>();
        private Material _tracerMaterial;
        private Material _beamFillMaterial;
        private static Mesh _beamQuadMesh;

        private const int BulletExtendFrames = 3;
        private const int BulletHoldFrames = 8;
        private const int BulletFadeFrames = 8;
        private const int BeamDelayFrames = 8;
        private const int BeamFadeFrames = 8;

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
            if (_sim != null)
            {
                _sim.Combat.Impacted -= HandleImpact;
                _sim.ShotFired -= HandleShot;
            }

            _sim = simulation;
            ClearAll();

            if (_sim == null) return;
            _sim.Combat.Impacted += HandleImpact;
            _sim.ShotFired += HandleShot;
        }

        private void OnDestroy()
        {
            if (_sim != null)
            {
                _sim.Combat.Impacted -= HandleImpact;
                _sim.ShotFired -= HandleShot;
            }
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
            UpdateRings();
            UpdateTracers();
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

            // 拆除后清掉对应视图(塔索引由 EntityId 分配,不复用)。
            var stale = new List<int>();
            foreach (KeyValuePair<int, TowerView> pair in _towers)
            {
                bool found = false;
                for (int i = 0; i < towers.Count; i++)
                {
                    if (towers[i].Id.Index == pair.Key) { found = true; break; }
                }
                if (!found) stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                TowerView view = _towers[stale[i]];
                view.Release();
                Destroy(view.gameObject);
                _towers.Remove(stale[i]);
                if (_selectedTowerIndex == stale[i]) _selectedTowerIndex = -1;
            }

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
            var used = new Dictionary<string, int>();

            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileRuntime projectile = projectiles[i];
                string key = projectile.Spec.Mechanic ?? string.Empty;

                int index;
                used.TryGetValue(key, out index);

                ProjectileView view = GetProjectileView(key, index);
                if (view == null) continue;

                view.gameObject.SetActive(true);
                view.Tick(
                    projectile,
                    ToWorld(projectile.Start),
                    ToWorld(projectile.Position),
                    ToWorld(projectile.TargetPosition));

                used[key] = index + 1;
            }

            // 逻辑层已移除的弹体必须回收,否则会留下"不消失的尾迹"。
            foreach (KeyValuePair<string, List<ProjectileView>> pair in _projectilePools)
            {
                int count;
                used.TryGetValue(pair.Key, out count);
                for (int i = count; i < pair.Value.Count; i++)
                {
                    pair.Value[i].gameObject.SetActive(false);
                }
            }
        }

        private ProjectileView GetProjectileView(string key, int index)
        {
            List<ProjectileView> pool;
            if (!_projectilePools.TryGetValue(key, out pool))
            {
                pool = new List<ProjectileView>();
                _projectilePools[key] = pool;
            }

            while (pool.Count <= index)
            {
                GameObject prefab = ResolveProjectilePrefab(key);
                if (prefab == null) return null;
                GameObject instance = Instantiate(prefab, transform);
                ProjectileView view = instance.GetComponent<ProjectileView>();
                if (view == null) view = instance.AddComponent<ProjectileView>();
                pool.Add(view);
            }

            return pool[index];
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

            SpawnAoeRing(projectile.Position, projectile.Spec.ExplosionRadius);
        }

        /// <summary>范围伤害标识:空心圆,短暂停留后淡出。</summary>
        private void SpawnAoeRing(Vec2 center, float radius)
        {
            if (radius < 0.2f) return;

            var go = new GameObject("AoeRing");
            go.transform.SetParent(transform, false);
            go.transform.position = ToWorld(center);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = 0.16f;
            line.positionCount = 48;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = RingMaterial();

            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius, 0.12f, Mathf.Sin(angle) * radius));
            }

            var color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
            line.startColor = color;
            line.endColor = color;

            _rings.Add(new AoeRing { Line = line, Age = 0f });
        }

        private void UpdateRings()
        {
            const float hold = 0.08f;
            const float fade = 0.16f;

            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                AoeRing ring = _rings[i];
                ring.Age += Time.deltaTime;

                float alpha = ring.Age <= hold
                    ? 0.9f
                    : Mathf.Clamp01(0.9f * (1f - (ring.Age - hold) / fade));

                var color = new Color(0.05f, 0.06f, 0.08f, alpha);
                ring.Line.startColor = color;
                ring.Line.endColor = color;

                if (ring.Age >= hold + fade)
                {
                    Destroy(ring.Line.gameObject);
                    _rings.RemoveAt(i);
                }
            }
        }

        private Material RingMaterial()
        {
            if (_ringMaterial != null) return _ringMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _ringMaterial = new Material(shader);
            return _ringMaterial;
        }

        // ---- 弹道 / 光束 ---------------------------------------------------------

        private void HandleShot(ShotTrace trace)
        {
            if (trace.Rectangle) SpawnBeamTracer(trace);
            else SpawnBulletTracer(trace);
        }

        /// <summary>实弹曳光:从塔外 1 单位处出发,3 帧内拉伸到射程端点,随后整条渐隐。</summary>
        private void SpawnBulletTracer(ShotTrace trace)
        {
            Vector3 origin = ToWorld(trace.Origin);
            Vector3 direction = new Vector3(trace.Direction.X, 0f, trace.Direction.Y);
            if (direction.sqrMagnitude < 1e-6f) return;
            direction.Normalize();

            Vector3 start = origin + direction * 1f + Vector3.up * 0.05f;
            Vector3 end = origin + direction * trace.Range + Vector3.up * 0.05f;

            var go = new GameObject("BulletTracer");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 2;
            line.numCapVertices = 2;
            line.widthMultiplier = Mathf.Max(0.06f, trace.HalfWidth * 0.6f);
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = TracerMaterial();
            line.SetPosition(0, start);
            line.SetPosition(1, start);
            line.startColor = Color.black;
            line.endColor = Color.black;

            _bulletTracers.Add(new BulletTracer
            {
                Line = line,
                Start = start,
                End = end,
                Age = 0
            });
        }

        /// <summary>激光束:与判定矩形同宽同长,白底 + 细黑边,延迟 2 帧后渐隐。</summary>
        private void SpawnBeamTracer(ShotTrace trace)
        {
            Vector3 origin = ToWorld(trace.Origin);
            Vector3 direction = new Vector3(trace.Direction.X, 0f, trace.Direction.Y);
            if (direction.sqrMagnitude < 1e-6f) return;
            direction.Normalize();

            float width = Mathf.Max(0.05f, trace.HalfWidth * 2f);
            float length = Mathf.Max(0.1f, trace.Range);

            var root = new GameObject("LaserBeam");
            root.transform.SetParent(transform, false);
            root.transform.position = origin + Vector3.up * 0.03f;
            root.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(root.transform, false);
            fillGo.transform.localPosition = Vector3.zero;
            fillGo.transform.localScale = new Vector3(width, 1f, length);

            var filter = fillGo.AddComponent<MeshFilter>();
            filter.sharedMesh = BeamQuadMesh();

            var renderer = fillGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BeamFillMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(root.transform, false);
            var border = borderGo.AddComponent<LineRenderer>();
            border.useWorldSpace = false;
            border.loop = true;
            border.positionCount = 4;
            border.numCapVertices = 0;
            border.widthMultiplier = 0.04f;
            border.alignment = LineAlignment.View;
            border.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            border.receiveShadows = false;
            border.material = TracerMaterial();

            float half = width * 0.5f;
            float lift = 0.01f;
            border.SetPosition(0, new Vector3(-half, lift, 0f));
            border.SetPosition(1, new Vector3(half, lift, 0f));
            border.SetPosition(2, new Vector3(half, lift, length));
            border.SetPosition(3, new Vector3(-half, lift, length));
            border.startColor = new Color(0.62f, 0.64f, 0.68f, 1f);
            border.endColor = new Color(0.62f, 0.64f, 0.68f, 1f);

            _beamTracers.Add(new BeamTracer
            {
                Root = root,
                FillRenderer = renderer,
                Border = border,
                Age = 0
            });
        }

        private void UpdateTracers()
        {
            for (int i = _bulletTracers.Count - 1; i >= 0; i--)
            {
                BulletTracer tracer = _bulletTracers[i];
                tracer.Age++;

                float grow = Mathf.Clamp01((float)tracer.Age / BulletExtendFrames);
                Vector3 tail = Vector3.Lerp(tracer.Start, tracer.End, grow);
                tracer.Line.SetPosition(0, tracer.Start);
                tracer.Line.SetPosition(1, tail);

                float alpha;
                if (tracer.Age <= BulletExtendFrames + BulletHoldFrames)
                {
                    alpha = 1f;
                }
                else
                {
                    alpha = 1f - (tracer.Age - BulletExtendFrames - BulletHoldFrames)
                        / (float)BulletFadeFrames;
                }

                if (alpha <= 0f)
                {
                    Destroy(tracer.Line.gameObject);
                    _bulletTracers.RemoveAt(i);
                    continue;
                }

                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.black, 0f),
                        new GradientColorKey(Color.black, 1f)
                    },
                    new[]
                    {
                        // 出发点先淡出,弹着点最后消失。
                        new GradientAlphaKey(alpha * 0.05f, 0f),
                        new GradientAlphaKey(alpha, 1f)
                    });
                tracer.Line.colorGradient = gradient;
            }

            for (int i = _beamTracers.Count - 1; i >= 0; i--)
            {
                BeamTracer beam = _beamTracers[i];
                beam.Age++;

                float alpha;
                if (beam.Age <= BeamDelayFrames)
                {
                    alpha = 1f;
                }
                else
                {
                    alpha = 1f - (beam.Age - BeamDelayFrames) / (float)BeamFadeFrames;
                }

                if (alpha <= 0f)
                {
                    Destroy(beam.Root);
                    _beamTracers.RemoveAt(i);
                    continue;
                }

                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", new Color(1f, 1f, 1f, alpha * 0.92f));
                beam.FillRenderer.SetPropertyBlock(block);

                Color borderColor = new Color(0.62f, 0.64f, 0.68f, alpha);
                beam.Border.startColor = borderColor;
                beam.Border.endColor = borderColor;
            }
        }

        private Material TracerMaterial()
        {
            if (_tracerMaterial != null) return _tracerMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _tracerMaterial = new Material(shader);
            return _tracerMaterial;
        }

        private Material BeamFillMaterial()
        {
            if (_beamFillMaterial != null) return _beamFillMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _beamFillMaterial = new Material(shader);
            return _beamFillMaterial;
        }

        /// <summary>单位矩形:宽 1(X)、长 1(+Z),运行时缩放到光束宽度与射程。</summary>
        private static Mesh BeamQuadMesh()
        {
            if (_beamQuadMesh != null) return _beamQuadMesh;

            var mesh = new Mesh { name = "BeamQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 1f),
                new Vector3(-0.5f, 0f, 1f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            _beamQuadMesh = mesh;
            return mesh;
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

            foreach (KeyValuePair<int, TowerView> pair in _towers)
            {
                pair.Value.Release();
                Destroy(pair.Value.gameObject);
            }
            _towers.Clear();

            foreach (KeyValuePair<string, List<ProjectileView>> pair in _projectilePools)
            {
                for (int i = 0; i < pair.Value.Count; i++) pair.Value[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < _rings.Count; i++)
            {
                if (_rings[i].Line != null) Destroy(_rings[i].Line.gameObject);
            }
            _rings.Clear();

            for (int i = 0; i < _bulletTracers.Count; i++)
            {
                if (_bulletTracers[i].Line != null) Destroy(_bulletTracers[i].Line.gameObject);
            }
            _bulletTracers.Clear();

            for (int i = 0; i < _beamTracers.Count; i++)
            {
                if (_beamTracers[i].Root != null) Destroy(_beamTracers[i].Root);
            }
            _beamTracers.Clear();

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
