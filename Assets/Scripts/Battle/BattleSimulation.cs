using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Entity;
using Game.Model;

namespace Game.Battle
{
    /// <summary>
    /// 逻辑主循环:固定 30 Hz,顺序为「波次 → 开火 → 移动 / 攻基 → 结算击杀 → 波次完成」。
    /// 不产生任何 GameObject,可脱离 Unity 运行。
    /// </summary>
    public sealed class BattleSimulation
    {
        public const float Dt = 1f / 30f;

        private readonly ModelDatabase _db;
        private readonly DifficultyDef _difficulty;
        private readonly MapDef _map;
        private readonly Rng _rng;
        private readonly EntityWorld _world = new EntityWorld();
        private readonly WaveDirector _director;
        private readonly EconomySystem _economy;
        private readonly UpgradeService _upgrades;
        private readonly List<EnemyRuntime> _hitBuffer = new List<EnemyRuntime>();
        private readonly CombatWorld _combat;
        private readonly List<IAoETarget> _targetBuffer = new List<IAoETarget>();

        private readonly List<string> _buildOrder = new List<string>
        {
            "laser", "machine_gun", "machine_gun", "laser", "machine_gun", "laser"
        };

        /// <summary>槽位 → 塔索引。玩家接管建造时也走这张表,避免重复占位。</summary>
        private readonly Dictionary<int, int> _slotToTower = new Dictionary<int, int>();

        /// <summary>机枪塔路线:快速供弹 → 高速枪机 → 电磁反器材 → 重型弹芯 → 快速充能 → 低温超导钢芯。</summary>
        private static readonly string[] MgUpgradePath =
        {
            "mg_lv2_quick_feed", "mg_lv3_high_speed_bolt", "mg_lv4_antimateriel",
            "mg_lv5_heavy_core", "mg_lv6_fast_charge", "mg_lv7_cryo_core"
        };

        /// <summary>激光聚焦路线:高效冷却 → 稳压回路 → 聚焦透镜 → 高压泵浦 → 散热矩阵 → 热熔。</summary>
        private static readonly string[] LaserFocusPath =
        {
            "laser_lv2_efficient_cool", "laser_lv3_stable_circuit", "laser_lv4_focus_lens",
            "laser_lv5_high_pressure", "laser_lv6_heat_matrix", "laser_lv7_thermal_melt"
        };

        /// <summary>激光扫荡路线:高效冷却 → 稳压回路 → 扫荡透镜 → 分光棱镜组 → 轮转打击 → 散射棱镜。</summary>
        private static readonly string[] LaserSweepPath =
        {
            "laser_lv2_efficient_cool", "laser_lv3_stable_circuit", "laser_lv4_sweep_lens",
            "laser_lv5_split_prism", "laser_lv6_rotate_strike", "laser_lv7_scatter_prism"
        };

        private readonly string[] _laserPath;

        private int _builtCount;

        public BattleSimulation(ModelDatabase db, string difficultyId, string mapId, ulong seed,
            float baseMaxHpOverride = 0f, string laserBranch = "focus", bool autoPlay = true,
            bool manualWaveStart = false)
        {
            AutoPlay = autoPlay;
            ManualWaveStart = manualWaveStart;
            _laserPath = string.Equals(laserBranch, "sweep", StringComparison.OrdinalIgnoreCase)
                ? LaserSweepPath
                : LaserFocusPath;
            if (db == null) throw new ArgumentNullException(nameof(db));

            DifficultyDef difficulty = db.FindDifficulty(difficultyId)
                ?? throw new ArgumentException("未找到难度:" + difficultyId);
            MapDef map = db.FindMap(mapId)
                ?? throw new ArgumentException("未找到地图:" + mapId);

            _db = db;
            _difficulty = difficulty;
            _map = map;
            _rng = new Rng(seed);
            _director = new WaveDirector(db, difficulty, map, _rng);
            _director.ManualStart = manualWaveStart;
            _economy = new EconomySystem(difficulty);
            _upgrades = new UpgradeService(db, _economy);
            _combat = new CombatWorld(db.Effects);
            _combat.SquadConsumed = OnSquadConsumed;

            BaseMaxHp = baseMaxHpOverride > 0f ? baseMaxHpOverride : map.BaseMaxHp;
            BaseHp = BaseMaxHp;

            if (AutoPlay) TryBuild();
        }

        public Action<int>? OnWaveStart;
        public Action<int, int, int, int>? OnWaveEnd;
        public Action<int, string, int>? OnUpgrade;
        public Action? OnFinished;

        public int Tick { get; private set; }
        public float Time => Tick * Dt;
        public int Wave => _director.Wave;
        public int Gold => _economy.Gold;
        public int Meta => _economy.Meta;
        public float BaseHp { get; private set; }
        public float BaseMaxHp { get; }
        public int Kills { get; private set; }
        public int Leaks { get; private set; }
        public int Spawned { get; private set; }
        public int WaveKills { get; private set; }
        public int WaveLeaks { get; private set; }
        public int WaveGold { get; private set; }
        public int WaveSpawned { get; private set; }
        public bool BaseDestroyed { get; private set; }
        public bool Finished => BaseDestroyed || _director.AllWavesDone;

        /// <summary>
        /// true = 旧的自 build / 自升级(AI 演示);false = 由 UI 调用 TryBuildTower / TryUpgradeTower。
        /// 默认保持 true,不影响既有的确定性回归。
        /// </summary>
        public bool AutoPlay { get; set; } = true;

        /// <summary>true = 每波开始前停在布防阶段,由玩家调用 StartNextWave()。</summary>
        public bool ManualWaveStart { get; }

        /// <summary>当前是否在等待玩家点击"开始交战"。</summary>
        public bool AwaitingWaveStart => _director.AwaitingStart;

        public IReadOnlyList<SlotDef> Slots => _map.Slots;
        public bool InPrep => _director.InPrep;

        /// <summary>指定波次是否为大型敌人波(每 5 波)。</summary>
        public bool IsLargeWave(int wave) => _director.IsLargeWave(wave);

        public IReadOnlyList<EnemyRuntime> Enemies => _world.Enemies;
        public IReadOnlyList<TowerAgent> Towers => _world.Towers;
        public CombatWorld Combat => _combat;

        private void OnSquadConsumed(int towerIndex, int drones)
        {
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower != null) tower.OnSquadConsumed(drones);
        }

        private TowerAgent? FindTowerById(int towerIndex)
        {
            for (int i = 0; i < _world.Towers.Count; i++)
            {
                if (_world.Towers[i].Id.Index == towerIndex) return _world.Towers[i];
            }
            return null;
        }

        public void Begin(int maxWaves)
        {
            if (maxWaves <= 0) throw new ArgumentOutOfRangeException(nameof(maxWaves));
            _director.Begin(maxWaves);
            ResetForDeployment();
        }

        /// <summary>布防阶段:清掉场上临时物,重装所有塔并收回无人机编队。</summary>
        private void ResetForDeployment()
        {
            _combat.ClearTransient();
            for (int i = 0; i < _world.Towers.Count; i++)
            {
                _world.Towers[i].ResetForDeployment();
            }
        }

        public void Step()
        {
            if (Finished) return;
            Tick++;

            _director.Tick(Dt, _world.AliveCount, ResolveSpawnY());

            if (_director.ConsumeWaveStarted(out int wave))
            {
                WaveKills = 0;
                WaveLeaks = 0;
                WaveGold = 0;
                WaveSpawned = 0;
                if (AutoPlay)
                {
                    TryBuild();
                    TryUpgrade();
                }
                OnWaveStart?.Invoke(wave);
            }

            for (int i = 0; i < _director.PendingSpawns.Count; i++)
            {
                SpawnRequest request = _director.PendingSpawns[i];
                float hp = request.Def.Hp * HpScale(_director.Wave);
                _world.SpawnEnemy(request.Def, request.Position, hp, _db.Effects);
                Spawned++;
                WaveSpawned++;
            }
            _director.PendingSpawns.Clear();

            SyncCombatTargets();

            for (int i = 0; i < _world.Towers.Count; i++)
            {
                _world.Towers[i].Tick(Dt, _world.Enemies, _hitBuffer, _combat);
            }

            _combat.Tick(Dt);

            for (int i = _world.Enemies.Count - 1; i >= 0; i--)
            {
                EnemyRuntime enemy = _world.Enemies[i];
                if (!enemy.Alive) continue;

                enemy.TickBlock(Dt);

                // 直线下行:每只敌人朝"自己正下方的城墙"走,不再向中心点汇集。
                if (!enemy.ReachedBase)
                {
                    Vec2 wallTarget = new Vec2(enemy.Position.X, _map.BaseY);
                    enemy.TickMovement(Dt, wallTarget);
                }

                if (!enemy.ReachedBase) continue;

                // 撞墙即被城墙吸收:一次性扣血后离场(不给赏金),避免贴墙敌人无限啃基地。
                Leaks++;
                WaveLeaks++;
                float damage = enemy.Hp * enemy.Def.BodyCoef;
                float cap = BaseMaxHp * 0.25f;
                if (damage > cap) damage = cap;
                BaseHp -= damage;
                _world.Enemies.RemoveAt(i);
            }

            if (BaseHp < 0f) BaseHp = 0f;

            CollectKills();

            if (_director.ConsumeWaveCompleted(out int completedWave))
            {
                WaveGold += _economy.AwardWaveClear(_db.Waves.WaveClearGold);
                _economy.AwardWaveMeta(completedWave, _director.IsLargeWave(completedWave));
                OnWaveEnd?.Invoke(completedWave, WaveKills, WaveLeaks, WaveGold);

                // 进入下一波布防:重装防御塔、回收无人机编队。
                ResetForDeployment();
            }

            if (BaseHp <= 0f)
            {
                BaseDestroyed = true;
                OnFinished?.Invoke();
            }
            else if (_director.AllWavesDone)
            {
                OnFinished?.Invoke();
            }
        }

        /// <summary>构建状态哈希,用于两次同种子运行的一致性校验。</summary>
        public ulong ComputeStateHash()
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (ulong)Tick);
            h = Mix(h, (ulong)Math.Round((double)BaseHp * 100.0, MidpointRounding.AwayFromZero));
            h = Mix(h, (ulong)Gold);
            h = Mix(h, (ulong)Kills);
            h = Mix(h, (ulong)Leaks);

            for (int i = 0; i < _world.Towers.Count; i++)
            {
                TowerAgent tower = _world.Towers[i];
                h = Mix(h, (ulong)Math.Round((double)tower.Cooldown * 1000.0, MidpointRounding.AwayFromZero));
                h = Mix(h, (ulong)Math.Round((double)tower.Motor.AimAngle * 1000.0, MidpointRounding.AwayFromZero));
                h = Mix(h, (ulong)tower.Ammo);
            }

            for (int i = 0; i < _world.Enemies.Count; i++)
            {
                EnemyRuntime enemy = _world.Enemies[i];
                h = Mix(h, (ulong)enemy.Id.Index);
                h = Mix(h, (ulong)Math.Round((double)enemy.Hp * 100.0, MidpointRounding.AwayFromZero));
                h = Mix(h, (ulong)Math.Round((double)enemy.Position.X * 100.0, MidpointRounding.AwayFromZero));
                h = Mix(h, (ulong)Math.Round((double)enemy.Position.Y * 100.0, MidpointRounding.AwayFromZero));
            }

            return h;
        }

        private void SyncCombatTargets()
        {
            _targetBuffer.Clear();
            for (int i = 0; i < _world.Enemies.Count; i++)
            {
                EnemyRuntime enemy = _world.Enemies[i];
                if (enemy.Alive) _targetBuffer.Add(enemy);
            }

            _combat.Targets.Clear();
            _combat.Targets.AddRange(_targetBuffer);
        }

        /// <summary>
        /// 出生线 = 固定塔最远射程 + 缓冲;城墙改版后额外保证至少一段固定行进距离,
        /// 避免敌人贴脸刷新导致塔的输出窗口过短。
        /// </summary>
        private float ResolveSpawnY()
        {
            float maxEdge = 0f;
            for (int i = 0; i < _world.Towers.Count; i++)
            {
                TowerAgent tower = _world.Towers[i];
                float edge = tower.Position.Y + tower.Stats.Range;
                if (edge > maxEdge) maxEdge = edge;
            }

            float y = maxEdge > 0f ? maxEdge + 4f : _map.Height - 2f;

            float minimum = _map.Height - 22f;
            if (y < minimum) y = minimum;
            if (y > _map.Height - 2f) y = _map.Height - 2f;
            return y;
        }

        private void CollectKills()
        {
            for (int i = _world.Enemies.Count - 1; i >= 0; i--)
            {
                EnemyRuntime enemy = _world.Enemies[i];
                if (enemy.Alive) continue;

                int reward = _economy.KillReward(enemy.Def.Bounty, 0f);
                _economy.AddGold(reward);
                Kills++;
                WaveKills++;
                WaveGold += reward;
                _world.Enemies.RemoveAt(i);
            }
        }

        private void TryBuild()
        {
            while (_builtCount < _buildOrder.Count && _builtCount < _map.Slots.Count)
            {
                TowerDef? def = _db.FindTower(_buildOrder[_builtCount]);
                if (def == null) break;
                if (!_economy.TrySpend(def.Price)) break;

                SlotDef slot = _map.Slots[_builtCount];
                TowerAgent tower = _world.AddTower(def, new Vec2(slot.X, slot.Y));
                _slotToTower[_builtCount] = tower.Id.Index;
                _builtCount++;
            }
        }

        /// <summary>
        /// 严格优先级:先把第一座机枪送到 Lv4(电磁反器材),再第二座机枪,再激光到 Lv4,最后才买 Lv5+。
        /// 高优先级节点买不起时选择攒钱,而不是降级消费。
        /// </summary>
        private void TryUpgrade()
        {
            int primaryMg = -1;
            for (int i = 0; i < _world.Towers.Count; i++)
            {
                if (_world.Towers[i].Def.Id == "machine_gun")
                {
                    primaryMg = _world.Towers[i].Id.Index;
                    break;
                }
            }

            for (int guard = 0; guard < 48; guard++)
            {
                TowerAgent? bestTower = null;
                string? bestNodeId = null;
                int bestKey = int.MaxValue;

                for (int i = 0; i < _world.Towers.Count; i++)
                {
                    TowerAgent tower = _world.Towers[i];
                    string[] path = tower.Def.Id == "machine_gun" ? MgUpgradePath : _laserPath;
                    if (tower.Upgrades.Count >= path.Length) continue;

                    int phase = UpgradePhase(tower, primaryMg);
                    int key = phase * 1000 + tower.Id.Index;
                    if (key < bestKey)
                    {
                        bestKey = key;
                        bestTower = tower;
                        bestNodeId = path[tower.Upgrades.Count];
                    }
                }

                if (bestTower == null || bestNodeId == null) return;

                UpgradeNodeDef? node = _db.FindUpgrade(bestNodeId);
                if (node == null) return;

                if (_economy.Gold < _upgrades.Cost(bestTower.Def, node)) return;

                if (!_upgrades.TryApply(bestTower, bestNodeId, out int cost)) return;
                OnUpgrade?.Invoke(bestTower.Id.Index, bestNodeId, cost);
            }
        }

        private static int UpgradePhase(TowerAgent tower, int primaryMg)
        {
            if (tower.Level >= 4) return 3;
            if (tower.Def.Id == "machine_gun") return tower.Id.Index == primaryMg ? 0 : 1;
            return 2;
        }

        private float HpScale(int wave)
        {
            float scale = 1f;
            float growth = 1f + _difficulty.HpGrowthPerWave;
            for (int i = 1; i < wave; i++) scale *= growth;
            return scale;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
            return hash;
        }

        // ---- 玩家接管接口(UI 调用;AutoPlay = false 时使用) --------------------

        public bool IsSlotOccupied(int slotIndex) => _slotToTower.ContainsKey(slotIndex);

        public int TowerIndexAtSlot(int slotIndex) =>
            _slotToTower.TryGetValue(slotIndex, out int index) ? index : -1;

        /// <summary>在指定槽位建造;金币不足 / 槽位占用 / id 非法时返回 false。</summary>
        public bool TryBuildTower(string towerId, int slotIndex)
        {
            if (Finished) return false;
            if (slotIndex < 0 || slotIndex >= _map.Slots.Count) return false;
            if (_slotToTower.ContainsKey(slotIndex)) return false;

            TowerDef? def = _db.FindTower(towerId);
            if (def == null) return false;
            if (!_economy.TrySpend(def.Price)) return false;

            SlotDef slot = _map.Slots[slotIndex];
            TowerAgent tower = _world.AddTower(def, new Vec2(slot.X, slot.Y));
            _slotToTower[slotIndex] = tower.Id.Index;
            return true;
        }

        public int UpgradeCostOf(int towerIndex, string nodeId)
        {
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower == null) return 0;
            UpgradeNodeDef? node = _db.FindUpgrade(nodeId);
            return node == null ? 0 : _upgrades.Cost(tower.Def, node);
        }

        public bool CanApplyUpgrade(int towerIndex, string nodeId)
        {
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower == null) return false;
            UpgradeNodeDef? node = _db.FindUpgrade(nodeId);
            return node != null && _upgrades.CanApply(tower, node);
        }

        /// <summary>应用升级;成功时通过 OnUpgrade 广播,便于表现层播特效 / 音效。</summary>
        public bool TryUpgradeTower(int towerIndex, string nodeId, out int cost)
        {
            cost = 0;
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower == null) return false;
            if (!_upgrades.TryApply(tower, nodeId, out cost)) return false;
            OnUpgrade?.Invoke(towerIndex, nodeId, cost);
            return true;
        }

        // ---- 波次控制 / 拆除 ---------------------------------------------------

        /// <summary>玩家点击"开始交战";仅在布防阶段有效。</summary>
        public bool StartNextWave() => _director.StartWaveNow();

        /// <summary>建造费用全额返还。</summary>
        public const float BuildRefundRate = 1.0f;

        /// <summary>升级费用返还 50%。</summary>
        public const float UpgradeRefundRate = 0.5f;

        /// <summary>某座塔的累计投入(建造价 + 已购升级)。</summary>
        public int TowerInvested(int towerIndex)
        {
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower == null) return 0;

            int total = tower.Def.Price;
            for (int i = 0; i < tower.Upgrades.Count; i++)
            {
                total += _upgrades.Cost(tower.Def, tower.Upgrades[i]);
            }
            return total;
        }

        /// <summary>某座塔已购升级的累计花费。</summary>
        public int TowerUpgradeSpent(int towerIndex)
        {
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower == null) return 0;

            int total = 0;
            for (int i = 0; i < tower.Upgrades.Count; i++)
            {
                total += _upgrades.Cost(tower.Def, tower.Upgrades[i]);
            }
            return total;
        }

        /// <summary>拆除返还 = 建造价 ×100% + 升级花费 ×50%(四舍五入)。</summary>
        public int SellRefund(int towerIndex)
        {
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower == null) return 0;

            float build = tower.Def.Price * BuildRefundRate;
            float upgrade = TowerUpgradeSpent(towerIndex) * UpgradeRefundRate;
            return (int)Math.Round(build + upgrade, MidpointRounding.AwayFromZero);
        }

        /// <summary>拆除防御塔并返还金币;槽位随即变为空位。</summary>
        public bool TrySellTower(int towerIndex, out int refund)
        {
            refund = 0;
            TowerAgent? tower = FindTowerById(towerIndex);
            if (tower == null || Finished) return false;

            refund = SellRefund(towerIndex);
            if (!_world.RemoveTower(tower)) return false;

            int slot = -1;
            foreach (KeyValuePair<int, int> pair in _slotToTower)
            {
                if (pair.Value == towerIndex) { slot = pair.Key; break; }
            }
            if (slot >= 0) _slotToTower.Remove(slot);

            _economy.AddGold(refund);
            return true;
        }
    }
}
