using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Combat;
using Game.Core;
using Game.Entity;
using Game.Model;

namespace SimHarness
{
    /// <summary>
    /// 单塔行为模拟:固定靶场,逐级应用升级,统计 12 秒内的总伤害与机制事件。
    /// 前 6 秒为集群靶(验证 AoE / 连锁 / 编队),后 6 秒只剩一只isolated重型靶
    /// (验证电容引爆 / 引雷针 / 单体穿甲)。目的不是平衡结论,而是确认行为与唯一机制真的在跑。
    /// </summary>
    internal static class TowerSim
    {
        private const float Dt = 1f / 30f;
        private const int Ticks = 360;
        private const int PhaseSwitchTick = 180;
        private const float DummyHp = 200000f;

        private static readonly Vec2 TowerPosition = new Vec2(40f, 40f);

        private static readonly Dictionary<string, string[]> Paths =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                {
                    "mortar", new[]
                    {
                        "mortar_lv2_heavy_charge", "mortar_lv3_arc_calibration", "mortar_lv4_fragmentation",
                        "mortar_lv5_quick_load", "mortar_lv6_adv_frag", "mortar_lv7_smart_implosion"
                    }
                },
                {
                    "rocket", new[]
                    {
                        "rocket_lv2_high_explosive", "rocket_lv3_ammo_expand", "rocket_lv4_missile_silo",
                        "rocket_lv5_fast_erect", "rocket_lv6_preset_sequence", "rocket_lv7_hydrogen"
                    }
                },
                {
                    "tesla", new[]
                    {
                        "tesla_lv2_high_voltage", "tesla_lv3_conductor", "tesla_lv4_ball_lightning",
                        "tesla_lv5_bipolar", "tesla_lv6_overload_core", "tesla_lv7_rolling_thunder"
                    }
                },
                {
                    "drone_hangar", new[]
                    {
                        "drone_lv2_expand", "drone_lv3_pipeline", "drone_lv4_mobile_factory",
                        "drone_lv5_hangar_expand", "drone_lv6_radar_up", "drone_lv7_battleship"
                    }
                }
            };

        private sealed class Metrics
        {
            public float Dealt;
            public int Impacts;
            public int ChainHits;
            public int Zones;
            public int Rods;
            public int MaxSquads;
        }

        public static int Run(ModelDatabase db, string towerId, ulong seed)
        {
            if (string.Equals(towerId, "all", StringComparison.OrdinalIgnoreCase))
            {
                int code = 0;
                foreach (string id in Paths.Keys)
                {
                    code = Math.Max(code, RunOne(db, id, seed));
                }
                return code;
            }

            return RunOne(db, towerId, seed);
        }

        private static int RunOne(ModelDatabase db, string towerId, ulong seed)
        {
            TowerDef? def = db.FindTower(towerId);
            if (def == null)
            {
                Console.Error.WriteLine("[towersim] 未找到防御塔:" + towerId);
                return 2;
            }

            if (!Paths.TryGetValue(towerId, out string[]? path))
            {
                Console.Error.WriteLine("[towersim] 未配置升级路线:" + towerId);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("===== " + towerId + " (" + def.Price + "g) =====");

            var totals = new Metrics();

            for (int level = 1; level <= 7; level++)
            {
                Metrics metrics = Simulate(db, def, path, level);
                totals.Dealt += metrics.Dealt;
                totals.Impacts += metrics.Impacts;
                totals.ChainHits += metrics.ChainHits;
                totals.Zones += metrics.Zones;
                totals.Rods += metrics.Rods;
                if (metrics.MaxSquads > totals.MaxSquads) totals.MaxSquads = metrics.MaxSquads;
            }

            Console.WriteLine(
                "  [total] dmg=" + totals.Dealt.ToString("F0", CultureInfo.InvariantCulture) +
                " impacts=" + totals.Impacts +
                " chainHits=" + totals.ChainHits +
                " zones=" + totals.Zones +
                " rods=" + totals.Rods);

            Console.WriteLine("  [events @ Lv7]");
            SimulateTrace(db, def, path, 7);

            return 0;
        }

        private static Metrics Simulate(ModelDatabase db, TowerDef def, string[] path, int level)
        {
            var world = new EntityWorld();
            var combat = new CombatWorld(db.Effects);
            TowerAgent tower = world.AddTower(def, TowerPosition);
            ApplyPath(db, tower, path, level);

            var enemies = new List<EnemyRuntime>();
            SpawnPhaseOne(world, db, enemies);

            combat.SquadConsumed = (towerIndex, drones) => tower.OnSquadConsumed(drones);

            var metrics = new Metrics();
            var hitBuffer = new List<EnemyRuntime>();
            var targetBuffer = new List<IAoETarget>();
            combat.Impacted = _ => metrics.Impacts++;

            for (int tick = 0; tick < Ticks; tick++)
            {
                if (tick == PhaseSwitchTick) SwitchToPhaseTwo(world, db, enemies);

                targetBuffer.Clear();
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].Alive) targetBuffer.Add(enemies[i]);
                }
                combat.Targets.Clear();
                combat.Targets.AddRange(targetBuffer);

                int zonesBefore = combat.Areas.Fields.Count;
                int rodsBefore = combat.Rods.Rods.Count;

                tower.Tick(Dt, enemies, hitBuffer, combat);
                combat.Tick(Dt);

                if (tower.Stats.Delivery == DeliveryKind.Chain)
                {
                    metrics.ChainHits += Math.Max(0, tower.LastHitCount - 1);
                }
                metrics.Zones += Math.Max(0, combat.Areas.Fields.Count - zonesBefore);
                metrics.Rods += Math.Max(0, combat.Rods.Rods.Count - rodsBefore);
                if (combat.Drones.Squads.Count > metrics.MaxSquads)
                    metrics.MaxSquads = combat.Drones.Squads.Count;

                for (int i = 0; i < enemies.Count; i++)
                {
                    EnemyRuntime enemy = enemies[i];
                    if (!enemy.Alive) continue;
                    enemy.TickBlock(Dt);
                    enemy.TickStatuses(Dt);
                }
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                metrics.Dealt += enemies[i].MaxHp - enemies[i].Hp;
            }

            TowerRuntimeStats stats = tower.Stats;
            Console.WriteLine(
                "  Lv" + level + " mech=" + (string.IsNullOrEmpty(stats.Mechanic) ? "-" : stats.Mechanic) +
                " dmg=" + metrics.Dealt.ToString("F0", CultureInfo.InvariantCulture) +
                " dps=" + (metrics.Dealt / (Ticks * Dt)).ToString("F1", CultureInfo.InvariantCulture) +
                " hits=" + metrics.Impacts +
                " chain=" + metrics.ChainHits +
                " zones=" + metrics.Zones +
                " rods=" + metrics.Rods +
                " squads=" + metrics.MaxSquads);

            return metrics;
        }

        /// <summary>最高等级下逐秒打印行为事件,便于肉眼确认机制确实触发。</summary>
        private static void SimulateTrace(ModelDatabase db, TowerDef def, string[] path, int level)
        {
            var world = new EntityWorld();
            var combat = new CombatWorld(db.Effects);
            TowerAgent tower = world.AddTower(def, TowerPosition);
            ApplyPath(db, tower, path, level);

            var enemies = new List<EnemyRuntime>();
            SpawnPhaseOne(world, db, enemies);
            combat.SquadConsumed = (towerIndex, drones) => tower.OnSquadConsumed(drones);

            int impactCount = 0;
            string lastMechanic = "-";
            combat.Impacted = projectile =>
            {
                impactCount++;
                lastMechanic = string.IsNullOrEmpty(projectile.Spec.Mechanic)
                    ? "direct"
                    : projectile.Spec.Mechanic;
            };

            var hitBuffer = new List<EnemyRuntime>();
            var targetBuffer = new List<IAoETarget>();
            float dealt = 0f;
            int lastSecond = -1;

            for (int tick = 0; tick < Ticks; tick++)
            {
                if (tick == PhaseSwitchTick) SwitchToPhaseTwo(world, db, enemies);

                targetBuffer.Clear();
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].Alive) targetBuffer.Add(enemies[i]);
                }
                combat.Targets.Clear();
                combat.Targets.AddRange(targetBuffer);

                tower.Tick(Dt, enemies, hitBuffer, combat);
                combat.Tick(Dt);

                for (int i = 0; i < enemies.Count; i++)
                {
                    EnemyRuntime enemy = enemies[i];
                    if (!enemy.Alive) continue;
                    enemy.TickBlock(Dt);
                    enemy.TickStatuses(Dt);
                }

                int second = (tick + 1) / 30;
                if (second == lastSecond) continue;
                lastSecond = second;

                float total = 0f;
                for (int i = 0; i < enemies.Count; i++)
                {
                    total += enemies[i].MaxHp - enemies[i].Hp;
                }
                // 单秒窗口内的伤害。阶段切换只瞬移靶子、不改变血量,因此不会污染统计。
                float delta = total - dealt;
                if (delta < 0f) delta = 0f;
                dealt = total;

                Console.WriteLine(
                    "    t=" + second.ToString("00", CultureInfo.InvariantCulture) + "s" +
                    " dmg/s=" + delta.ToString("F0", CultureInfo.InvariantCulture) +
                    " impacts=" + impactCount +
                    " last=" + lastMechanic +
                    " proj=" + combat.Projectiles.Count +
                    " zones=" + combat.Areas.Fields.Count +
                    " rods=" + combat.Rods.Rods.Count +
                    " squads=" + combat.Drones.Squads.Count);
            }
        }

        private static void ApplyPath(ModelDatabase db, TowerAgent tower, string[] path, int level)
        {
            for (int i = 0; i < level - 1 && i < path.Length; i++)
            {
                UpgradeNodeDef? node = db.FindUpgrade(path[i]);
                if (node != null) tower.ApplyUpgrade(node);
            }
        }

        private static void SpawnPhaseOne(EntityWorld world, ModelDatabase db, List<EnemyRuntime> enemies)
        {
            // 紧贴的四只:验证连锁 / 小范围 AoE。
            SpawnLine(world, db, enemies, "worker", new Vec2(50f, 40f), new Vec2(2f, 0f), 4);
            // 蜂窝状五只:验证爆炸覆盖。
            SpawnLine(world, db, enemies, "hunter", new Vec2(30f, 52f), new Vec2(1.5f, 0f), 5);
            // 中型:验证穿甲与单体。
            SpawnLine(world, db, enemies, "skirmisher", new Vec2(52f, 30f), new Vec2(3f, 0f), 2);
        }

        /// <summary>把集群靶移出战场,只留一只孤立重型靶,验证引爆类机制。</summary>
        private static void SwitchToPhaseTwo(
            EntityWorld world, ModelDatabase db, List<EnemyRuntime> enemies)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].Position = new Vec2(-500f - i, -500f);
            }
            SpawnOne(world, db, enemies, "skirmisher", new Vec2(55f, 55f));
        }

        private static void SpawnLine(
            EntityWorld world,
            ModelDatabase db,
            List<EnemyRuntime> enemies,
            string enemyId,
            Vec2 origin,
            Vec2 step,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                SpawnOne(world, db, enemies, enemyId, origin + step * i);
            }
        }

        private static void SpawnOne(
            EntityWorld world,
            ModelDatabase db,
            List<EnemyRuntime> enemies,
            string enemyId,
            Vec2 position)
        {
            EnemyDef? def = db.FindEnemy(enemyId);
            if (def == null) throw new InvalidOperationException("缺少敌人:" + enemyId);
            EnemyRuntime enemy = world.SpawnEnemy(def, position, DummyHp, db.Effects);
            enemies.Add(enemy);
        }
    }
}
