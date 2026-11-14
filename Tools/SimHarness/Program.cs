using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Battle;
using Game.Combat;
using Game.Core;
using Game.Entity;
using Game.Model;

namespace SimHarness
{
    internal sealed class CliOptions
    {
        public int Waves = 30;
        public ulong Seed = 12345UL;
        public string Difficulty = "normal";
        public string MapId = "standard";
        public string DefsPath = Path.Combine(AppContext.BaseDirectory, "Defs");
        public float BaseHpOverride;
        public float WaveTimeout = 900f;
        public bool SelfTest;
        public string LaserBranch = "focus";
        public bool CompareDifficulties;
        public string TowerSim = string.Empty;

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--waves":
                        options.Waves = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--seed":
                        options.Seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--difficulty":
                        options.Difficulty = args[++i];
                        break;
                    case "--map":
                        options.MapId = args[++i];
                        break;
                    case "--defs":
                        options.DefsPath = args[++i];
                        break;
                    case "--base-hp":
                        options.BaseHpOverride = float.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--wave-timeout":
                        options.WaveTimeout = float.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--selftest":
                        options.SelfTest = true;
                        break;
                    case "--laser-branch":
                        options.LaserBranch = args[++i];
                        break;
                    case "--compare-difficulties":
                        options.CompareDifficulties = true;
                        break;
                    case "--towersim":
                        options.TowerSim = args[++i];
                        break;
                    default:
                        throw new ArgumentException("未知参数:" + args[i]);
                }
            }
            return options;
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            CliOptions options;
            ModelDatabase db;
            try
            {
                options = CliOptions.Parse(args);
                db = JsonModelLoader.Load(options.DefsPath);
                ModelValidator.Validate(db);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[load] FAILED: " + ex.Message);
                return 2;
            }

            Console.WriteLine(
                "[defs] towers=" + db.Towers.Count +
                " enemies=" + db.Enemies.Count +
                " difficulties=" + db.Difficulties.Count +
                " maps=" + db.Maps.Count +
                " upgrades=" + db.Upgrades.Count +
                " areas=" + db.Effects.Areas.Count +
                " tiers=" + db.Waves.Tiers);

            if (options.SelfTest)
            {
                return RunSelfTest(db, options) ? 0 : 3;
            }

            if (!string.IsNullOrEmpty(options.TowerSim))
            {
                return TowerSim.Run(db, options.TowerSim, options.Seed);
            }

            if (options.CompareDifficulties)
            {
                return RunCompareDifficulties(db, options);
            }

            RunResult first = RunOnce(db, options, 1, true);
            RunResult second = RunOnce(db, options, 2, false);

            bool deterministic = first.Hash == second.Hash && first.Wave == second.Wave;
            Console.WriteLine(
                "[hash] run1=0x" + first.Hash.ToString("X16") +
                " run2=0x" + second.Hash.ToString("X16") +
                " determinism=" + (deterministic ? "OK" : "FAIL"));

            return deterministic ? 0 : 4;
        }

        private sealed class RunResult
        {
            public ulong Hash;
            public int Wave;
            public string StopReason = "running";
            public long Kills;
            public long Leaks;
            public long Spawned;
            public int Gold;
            public int Meta;
            public float BaseHp;
            public float BaseMaxHp;
            public float SimTime;
            public int Tick;
        }

        private static int RunCompareDifficulties(ModelDatabase db, CliOptions options)
        {
            Console.WriteLine("[compare] waves=" + options.Waves + " seed=" + options.Seed +
                              " laser=" + options.LaserBranch);
            Console.WriteLine("difficulty | wave | kills | leaks | base% | gold | meta | state");
            string[] order = { "easy", "normal", "hard" };
            string original = options.Difficulty;
            for (int i = 0; i < order.Length; i++)
            {
                options.Difficulty = order[i];
                RunResult r = RunOnce(db, options, 1, false);
                float basePct = r.BaseMaxHp > 0f ? r.BaseHp / r.BaseMaxHp * 100f : 0f;
                Console.WriteLine(
                    order[i] + " | " + r.Wave + "/" + options.Waves +
                    " | " + r.Kills +
                    " | " + r.Leaks +
                    " | " + basePct.ToString("0.0", CultureInfo.InvariantCulture) + "%" +
                    " | " + r.Gold +
                    " | " + r.Meta +
                    " | " + r.StopReason);
            }
            options.Difficulty = original;
            return 0;
        }

        private static RunResult RunOnce(
            ModelDatabase db,
            CliOptions options,
            int runIndex,
            bool verbose)
        {
            var sim = new BattleSimulation(db, options.Difficulty, options.MapId, options.Seed,
                options.BaseHpOverride, options.LaserBranch);

            float waveStartedAt = 0f;
            sim.OnWaveStart = wave =>
            {
                waveStartedAt = sim.Time;
                if (verbose) Console.WriteLine("[wave " + wave.ToString("00") + "] start" + (wave % 5 == 0 ? " (大型波)" : string.Empty));
            };

            sim.OnUpgrade = (towerIndex, nodeId, cost) =>
            {
                if (!verbose) return;
                Console.WriteLine("[upgrade] wave " + sim.Wave.ToString("00") +
                                  " tower#" + towerIndex + " " + nodeId +
                                  " -" + cost + "g (left=" + sim.Gold + ")");
            };

            sim.OnWaveEnd = (wave, kills, leaks, gold) =>
            {
                if (!verbose) return;
                Console.WriteLine(
                    "[wave " + wave.ToString("00") + "] end" +
                    " spawned=" + sim.WaveSpawned +
                    " kill=" + kills +
                    " leak=" + leaks +
                    " gold=+" + gold +
                    " base=" + sim.BaseHp.ToString("0.0", CultureInfo.InvariantCulture) +
                    " t=" + sim.Time.ToString("0.0", CultureInfo.InvariantCulture) + "s");
            };

            sim.Begin(options.Waves);

            int guard = options.Waves * 30000;
            int ticks = 0;
            string stopReason = "running";
            while (!sim.Finished && ticks < guard)
            {
                sim.Step();
                ticks++;

                if (!sim.Finished && sim.Time - waveStartedAt > options.WaveTimeout)
                {
                    stopReason = "wave_timeout";
                    break;
                }
            }

            if (sim.BaseDestroyed) stopReason = "base_destroyed";
            else if (sim.Finished) stopReason = "cleared";
            else if (ticks >= guard) stopReason = "tick_guard";

            var result = new RunResult
            {
                Wave = sim.Wave,
                Hash = sim.ComputeStateHash(),
                StopReason = stopReason,
                Kills = sim.Kills,
                Leaks = sim.Leaks,
                Spawned = sim.Spawned,
                Gold = sim.Gold,
                Meta = sim.Meta,
                BaseHp = sim.BaseHp,
                BaseMaxHp = sim.BaseMaxHp,
                SimTime = sim.Time,
                Tick = sim.Tick
            };

            if (verbose)
            {
                Console.WriteLine(
                    "[result] run=" + runIndex +
                    " wave=" + result.Wave + "/" + options.Waves +
                    " base=" + result.BaseHp.ToString("0.0", CultureInfo.InvariantCulture) + "/" + result.BaseMaxHp.ToString("0", CultureInfo.InvariantCulture) +
                    " gold=" + result.Gold +
                    " meta=" + result.Meta +
                    " kills=" + result.Kills +
                    " leaks=" + result.Leaks +
                    " spawned=" + result.Spawned +
                    " ticks=" + result.Tick +
                    " simTime=" + result.SimTime.ToString("0.0", CultureInfo.InvariantCulture) + "s" +
                    " state=" + result.StopReason);
            }

            return result;
        }

        private static bool RunSelfTest(ModelDatabase db, CliOptions options)
        {
            bool ok = true;

            ok &= Check("装甲 低级穿甲→低级护甲 = 1.20",
                ArmorTable.Coefficient(ArmorPierce.Low, ArmorClass.Low), 1.20f);
            ok &= Check("装甲 低级穿甲→中级护甲 = 0.50",
                ArmorTable.Coefficient(ArmorPierce.Low, ArmorClass.Mid), 0.50f);
            ok &= Check("装甲 低级穿甲→重型护甲 = 0.00",
                ArmorTable.Coefficient(ArmorPierce.Low, ArmorClass.Heavy), 0.00f);
            ok &= Check("装甲 高级穿甲→重型护甲 = 1.20",
                ArmorTable.Coefficient(ArmorPierce.High, ArmorClass.Heavy), 1.20f);
            ok &= Check("装甲 碎甲 低级→重型 = 0.20",
                ArmorTable.Coefficient(ArmorPierce.Low, ArmorClass.Heavy, true), 0.20f);

            EnemyDef? worker = db.Enemies.Find(e => e.Id == "worker");
            if (worker == null)
            {
                Console.WriteLine("[selftest] FAIL: 缺少 worker");
                return false;
            }

            var laserProbe = new DamagePacket { Type = DamageType.Energy, Pierce = ArmorPierce.Low, Retention = 5f };
            ok &= Check("激光留存 5 对能量衰减 1 的贯穿数 = 5", CountHits(laserProbe, worker), 5f);

            var fiveRetentionProbe = new DamagePacket { Type = DamageType.Ballistic, Pierce = ArmorPierce.Low, Retention = 5f };
            ok &= Check("留存 5 对实弹衰减 2 的贯穿数 = 3", CountHits(fiveRetentionProbe, worker), 3f);

            var mgProbe = new DamagePacket { Type = DamageType.Ballistic, Pierce = ArmorPierce.Low, Retention = 2f };
            ok &= Check("机枪留存 2 对实弹衰减 2 的贯穿数 = 1", CountHits(mgProbe, worker), 1f);

            EnemyDef? furnace = db.Enemies.Find(e => e.Id == "furnace");
            if (furnace != null)
            {
                var ballistic = new DamagePacket { Amount = 100f, Type = DamageType.Ballistic, Pierce = ArmorPierce.Low };
                ok &= Check("熔炉(重型护甲)吃低级穿甲 = 0", DamageCalculator.Amount(ballistic, furnace), 0f);

                var highBallistic = new DamagePacket { Amount = 100f, Type = DamageType.Ballistic, Pierce = ArmorPierce.High };
                ok &= Check("熔炉实弹易伤 1.20 × 1.50 = 1.80", DamageCalculator.Amount(highBallistic, furnace), 180f);
            }

            TowerDef? mg = db.FindTower("machine_gun");
            TowerDef? laser = db.FindTower("laser");
            ok &= Check("机枪塔存在且射速 3", mg != null && Math.Abs(mg.FireRate - 3f) < 1e-4f ? 1f : 0f, 1f);
            ok &= Check("激光塔存在且束宽 0.5", laser != null && Math.Abs(laser.BeamWidth - 0.5f) < 1e-4f ? 1f : 0f, 1f);

            UpgradeNodeDef? lv2 = db.FindUpgrade("mg_lv2_quick_feed");
            UpgradeNodeDef? lv3 = db.FindUpgrade("mg_lv3_high_speed_bolt");
            UpgradeNodeDef? lv4 = db.FindUpgrade("mg_lv4_antimateriel");
            bool chainOk = mg != null && lv2 != null && lv3 != null && lv4 != null;
            ok &= Check("电磁反器材升级链存在(Lv2/Lv3/Lv4)", chainOk ? 1f : 0f, 1f);

            if (chainOk)
            {
                var nodes = new List<UpgradeNodeDef> { lv2!, lv3!, lv4! };
                TowerRuntimeStats stats = TowerStatsBuilder.Build(mg!, nodes);
                ok &= Check("电磁反器材 伤害 57.6", stats.Damage, 57.6f);
                ok &= Check("电磁反器材 射速 1.8", stats.FireRate, 1.8f);
                ok &= Check("电磁反器材 弹匣 10", stats.Ammo, 10f);
                ok &= Check("电磁反器材 换弹 0.54", stats.ReloadTime, 0.54f);
                ok &= Check("电磁反器材 射程 33.75", stats.Range, 33.75f);
                ok &= Check("电磁反器材 留存 10", stats.Retention, 10f);
                ok &= Check("电磁反器材 高级穿甲", stats.Pierce == ArmorPierce.High ? 1f : 0f, 1f);
                ok &= Check("电磁反器材 集中策略", stats.Strategy == TowerStrategy.Focus ? 1f : 0f, 1f);
                ok &= Check("升级费用 Lv2+Lv3+Lv4 = 1625",
                    CostOf(mg!, lv2!) + CostOf(mg!, lv3!) + CostOf(mg!, lv4!), 1625f);
            }

            Console.WriteLine("[selftest] 升级节点总数 = " + db.Upgrades.Count);
            ok &= Check("升级节点共 72 个(机枪/激光/迫击炮/火箭/电弧/无人机)", db.Upgrades.Count, 72f);

            UpgradeNodeDef? laser2 = db.FindUpgrade("laser_lv2_efficient_cool");
            UpgradeNodeDef? laser3 = db.FindUpgrade("laser_lv3_stable_circuit");
            UpgradeNodeDef? laser4Focus = db.FindUpgrade("laser_lv4_focus_lens");
            UpgradeNodeDef? laser4Sweep = db.FindUpgrade("laser_lv4_sweep_lens");

            if (laser != null && laser2 != null && laser3 != null && laser4Focus != null && laser4Sweep != null)
            {
                TowerRuntimeStats lv3Stats = TowerStatsBuilder.Build(laser,
                    new List<UpgradeNodeDef> { laser2, laser3 });
                ok &= Check("激光 Lv3 单体 DPS ≈ 205.3", lv3Stats.Damage * lv3Stats.FireRate, 205.3f, 0.2f);

                TowerRuntimeStats focus = TowerStatsBuilder.Build(laser,
                    new List<UpgradeNodeDef> { laser2, laser3, laser4Focus });
                ok &= Check("聚焦透镜 单体 DPS ≈ 291", focus.Damage * focus.FireRate, 291f, 0.5f);
                ok &= Check("聚焦透镜 束宽 2.0", focus.BeamWidth, 2f);
                ok &= Check("聚焦透镜 留存 9", focus.Retention, 9f);
                ok &= Check("聚焦透镜 中级穿甲", focus.Pierce == ArmorPierce.Mid ? 1f : 0f, 1f);

                TowerRuntimeStats sweep = TowerStatsBuilder.Build(laser,
                    new List<UpgradeNodeDef> { laser2, laser3, laser4Sweep });
                ok &= Check("扫荡透镜 3 束", sweep.BeamCount, 3f);
                ok &= Check("扫荡透镜 单束伤害 = Lv3 × 0.5", sweep.Damage, lv3Stats.Damage * 0.5f, 1e-3f);
            }

            // ---- 四塔数据与整树构建 ---------------------------------------------
            TowerDef? mortar = db.FindTower("mortar");
            TowerDef? rocket = db.FindTower("rocket");
            TowerDef? tesla = db.FindTower("tesla");
            TowerDef? drone = db.FindTower("drone_hangar");
            ok &= Check("迫击炮 最小射程 10", mortar?.MinRange ?? -1f, 10f);
            ok &= Check("迫击炮 无转向", mortar != null && mortar.NoTurn ? 1f : 0f, 1f);
            ok &= Check("火箭 追踪弹", rocket != null && rocket.HomingProjectile ? 1f : 0f, 1f);
            ok &= Check("电弧 连锁 3", tesla?.ChainCount ?? -1f, 3f);
            ok &= Check("无人机 编队 6", drone?.DroneSquad ?? -1f, 6f);
            ok &= Check("无人机 库存 48", drone?.DroneCapacity ?? -1f, 48f);

            var paths = new Dictionary<string, string[]>
            {
                {
                    "mortar", new[]
                    {
                        "mortar_lv2_heavy_charge", "mortar_lv3_arc_calibration", "mortar_lv4_cluster",
                        "mortar_lv5_fragmentation", "mortar_lv6_napalm", "mortar_lv7_smart_implosion"
                    }
                },
                {
                    "rocket", new[]
                    {
                        "rocket_lv2_quick_reload", "rocket_lv3_swarm", "rocket_lv4_split_warhead",
                        "rocket_lv5_plasma_missile", "rocket_lv6_thermobaric", "rocket_lv7_hydrogen"
                    }
                },
                {
                    "tesla", new[]
                    {
                        "tesla_lv2_high_voltage", "tesla_lv3_capacitor", "tesla_lv4_ball_lightning",
                        "tesla_lv5_lightning_rod", "tesla_lv6_rolling_thunder", "tesla_lv7_leak_current"
                    }
                },
                {
                    "drone_hangar", new[]
                    {
                        "drone_lv2_expand", "drone_lv3_refined_ammo", "drone_lv4_mobile_factory",
                        "drone_lv5_hurricane", "drone_lv6_thunderstorm", "drone_lv7_refined_hangar"
                    }
                }
            };

            var expectedMechanic = new Dictionary<string, string>
            {
                { "mortar", "smart_implosion" },
                { "rocket", "hydrogen" },
                { "tesla", "leak_current" },
                { "drone_hangar", "refined_hangar" }
            };

            foreach (KeyValuePair<string, string[]> entry in paths)
            {
                TowerDef? towerDef = db.FindTower(entry.Key);
                if (towerDef == null)
                {
                    ok &= Check("塔定义存在 " + entry.Key, 0f, 1f);
                    continue;
                }

                var nodes = new List<UpgradeNodeDef>();
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    UpgradeNodeDef? node = db.FindUpgrade(entry.Value[i]);
                    if (node == null) continue;
                    nodes.Add(node);
                }

                ok &= Check(entry.Key + " 升级链完整 (6)", nodes.Count, 6f);
                TowerRuntimeStats full = TowerStatsBuilder.Build(towerDef, nodes);
                string expected = expectedMechanic[entry.Key];
                ok &= Check(entry.Key + " Lv7 机制 = " + expected,
                    string.Equals(full.Mechanic, expected, StringComparison.Ordinal) ? 1f : 0f, 1f);
            }

            // ---- 特殊效果:状态容器 -------------------------------------------------
            StatusRules effects = db.Effects;
            ok &= Check("区域效果 燃烧 / 低温 共 2 个", effects.Areas.Count, 2f);

            var status = new StatusSet();
            status.Reset(effects);

            status.ApplyBurn(1, ArmorPierce.Low);
            ok &= Check("燃烧 1 层每跳 20", status.Tick(1f).BurnDamage, 20f);
            status.Clear();
            status.ApplyBurn(5, ArmorPierce.Low);
            ok &= Check("燃烧 5 层每跳 100", status.Tick(1f).BurnDamage, 100f);
            status.ApplyBurn(5, ArmorPierce.Low);
            ok &= Check("燃烧层数上限 5", status.BurnStacks, 5f);
            float burnTotal = 0f;
            for (int i = 0; i < 3; i++) burnTotal += status.Tick(1f).BurnDamage;
            ok &= Check("燃烧 3 秒共 3 跳 = 300", burnTotal, 300f, 1e-3f);
            ok &= Check("燃烧 3 秒后清空", status.BurnStacks, 0f);

            status.Clear();
            status.ApplyChill(3);
            ok &= Check("低温满层阻滞阈值 ×0.75", status.BlockThresholdMultiplier, 0.75f);
            ok &= Check("低温满层属性衰减 ×0.5", status.ElementalDecayScale, 0.5f);

            status.Clear();
            status.AddHeatMelt(1.5f);
            ok &= Check("热熔 1.5 秒 = ×1.5", status.HeatMeltMultiplier, 1.5f);
            status.AddHeatMelt(5f);
            ok &= Check("热熔上限 ×2.0", status.HeatMeltMultiplier, 2f);
            status.ResetHeatMelt();
            ok &= Check("热熔切换目标清零", status.HeatMeltMultiplier, 1f);

            status.Clear();
            status.ApplyArmorBreak();
            DamageContext broken = status.BuildDamageContext();
            ok &= Check("碎甲 低级穿甲→中级护甲 = 0.70",
                ArmorTable.Coefficient(ArmorPierce.Low, ArmorClass.Mid, broken.ArmorBroken), 0.70f);
            ok &= Check("碎甲 低级穿甲→重型护甲 = 0.20",
                ArmorTable.Coefficient(ArmorPierce.Low, ArmorClass.Heavy, broken.ArmorBroken), 0.20f);

            status.Clear();
            status.TryApplyCharge(1, ArmorPierce.Low, out _);
            status.TryApplyCharge(1, ArmorPierce.Low, out _);
            status.TryApplyCharge(1, ArmorPierce.Low, out _);
            bool charged = status.TryApplyCharge(1, ArmorPierce.Low, out ChargePayload chargeFull);
            ok &= Check("充电满 3 层再施加立即引爆", charged ? 1f : 0f, 1f);
            ok &= Check("充电 3 层引爆伤害 128", chargeFull.Damage, 128f);
            ok &= Check("充电 3 层引爆范围 4", chargeFull.Radius, 4f);
            ok &= Check("充电引爆后清空层数", status.ChargeStacks, 0f);

            status.Clear();
            status.TryApplyCharge(2, ArmorPierce.Low, out _);
            bool deathCharged = status.TryDetonateOnDeath(out ChargePayload chargeDeath);
            ok &= Check("充电 2 层死亡触发引爆", deathCharged ? 1f : 0f, 1f);
            ok &= Check("充电 2 层死亡引爆伤害 104", chargeDeath.Damage, 104f);
            ok &= Check("充电 2 层死亡引爆范围 2", chargeDeath.Radius, 2f);

            status.Clear();
            status.ApplyDecay(8);
            ok &= Check("衰变 8 层首跳 256", status.Tick(2f).DecayDamage, 256f);
            ok &= Check("衰变结算后层数 4", status.DecayStacks, 4f);
            ok &= Check("衰变 4 层跳 16", status.Tick(2f).DecayDamage, 16f);
            ok &= Check("衰变 2 层跳 4", status.Tick(2f).DecayDamage, 4f);
            ok &= Check("衰变 1 层跳 2", status.Tick(2f).DecayDamage, 2f);
            ok &= Check("衰变层数归零", status.DecayStacks, 0f);

            // ---- 特殊效果:经护甲 / 衰减后的实际伤害 -------------------------------
            var plainDef = new EnemyDef
            {
                Id = "test_plain", Size = EnemySize.Small, Armor = ArmorClass.Low,
                Hp = 1000f, Speed = 1f, BlockThreshold = 3f
            };
            EnemyRuntime burnEnemy = MakeEnemy(effects, plainDef, Vec2.Zero, 1000f);
            burnEnemy.Status.ApplyBurn(1, ArmorPierce.Low);
            burnEnemy.TickStatuses(1f);
            ok &= Check("燃烧 1 层经护甲后 24", 1000f - burnEnemy.Hp, 24f, 1e-3f);

            EnemyRuntime beamEnemy = MakeEnemy(effects, plainDef, Vec2.Zero, 1000f);
            beamEnemy.Status.AddHeatMelt(3f);
            var beamPacket = new DamagePacket
            {
                Amount = 100f, Type = DamageType.Energy, Pierce = ArmorPierce.Low, Beam = true
            };
            ok &= Check("热熔满层光束 100×1.2×2 = 240",
                DamageCalculator.Amount(beamPacket, plainDef, beamEnemy.BuildDamageContext()), 240f, 1e-3f);
            beamPacket.Beam = false;
            ok &= Check("非光束不吃热熔 = 120",
                DamageCalculator.Amount(beamPacket, plainDef, beamEnemy.BuildDamageContext()), 120f, 1e-3f);

            var elementalDef = new EnemyDef
            {
                Id = "test_elemental", Size = EnemySize.Medium, Armor = ArmorClass.Low,
                Hp = 1000f, Speed = 1f, BlockThreshold = 3f, DecayElemental = 0.4f
            };
            EnemyRuntime chillEnemy = MakeEnemy(effects, elementalDef, Vec2.Zero, 1000f);
            var elementalPacket = new DamagePacket
            {
                Amount = 100f, Type = DamageType.Elemental, Pierce = ArmorPierce.Low
            };
            ok &= Check("属性伤害 100×1.2×0.6 = 72",
                DamageCalculator.Amount(elementalPacket, elementalDef, DamageContext.Neutral), 72f, 1e-3f);
            chillEnemy.Status.ApplyChill(3);
            ok &= Check("低温满层属性衰减减半后 = 96",
                DamageCalculator.Amount(elementalPacket, elementalDef, chillEnemy.BuildDamageContext()), 96f, 1e-3f);

            var heavyDef = new EnemyDef
            {
                Id = "test_heavy", Size = EnemySize.Large, Armor = ArmorClass.Heavy,
                Hp = 7000f, Speed = 1f, BlockThreshold = 20f
            };
            EnemyRuntime heavyEnemy = MakeEnemy(effects, heavyDef, Vec2.Zero, 7000f);
            heavyEnemy.Status.ApplyArmorBreak();
            var heavyPacket = new DamagePacket
            {
                Amount = 100f, Type = DamageType.Ballistic, Pierce = ArmorPierce.Low
            };
            ok &= Check("碎甲后低级穿甲打重型 = 20",
                DamageCalculator.Amount(heavyPacket, heavyDef, heavyEnemy.BuildDamageContext()), 20f, 1e-3f);

            // ---- 范围伤害 -----------------------------------------------------------
            EnemyRuntime blastCenter = MakeEnemy(effects, plainDef, Vec2.Zero, 1000f);
            EnemyRuntime blastNear = MakeEnemy(effects, plainDef, new Vec2(1f, 0f), 1000f);
            EnemyRuntime blastFar = MakeEnemy(effects, plainDef, new Vec2(5f, 0f), 1000f);
            var explosionTargets = new List<IAoETarget> { blastCenter, blastNear, blastFar };
            var aoeBuffer = new List<IAoETarget>();
            var explosionPacket = new DamagePacket
            {
                Amount = 100f, Type = DamageType.Explosive, Pierce = ArmorPierce.Low
            };
            int explosionHits = ExplosionResolver.Apply(
                explosionPacket, Vec2.Zero, 2f, explosionTargets, aoeBuffer);
            ok &= Check("爆炸半径 2 命中 2 个目标", explosionHits, 2f);
            ok &= Check("爆心目标吃满 120", 1000f - blastCenter.Hp, 120f, 1e-3f);
            ok &= Check("半径外目标不掉血", 1000f - blastFar.Hp, 0f);

            EnemyRuntime chargeCenter = MakeEnemy(effects, plainDef, Vec2.Zero, 1000f);
            EnemyRuntime chargeNear = MakeEnemy(effects, plainDef, new Vec2(1f, 0f), 1000f);
            EnemyRuntime chargeFar = MakeEnemy(effects, plainDef, new Vec2(9f, 0f), 1000f);
            var chargeTargets = new List<IAoETarget> { chargeCenter, chargeNear, chargeFar };
            int chargeHits = ChargeResolver.Detonate(
                new ChargePayload(3, 128f, 4f, ArmorPierce.Low), Vec2.Zero, chargeTargets, aoeBuffer);
            ok &= Check("充电 3 层引爆命中 2 个目标", chargeHits, 2f);
            ok &= Check("充电引爆单体 128×1.2 = 153.6", 1000f - chargeCenter.Hp, 153.6f, 1e-2f);

            // ---- 区域效果 -----------------------------------------------------------
            EnemyRuntime zoneInside = MakeEnemy(effects, plainDef, Vec2.Zero, 100000f);
            EnemyRuntime zoneOutside = MakeEnemy(effects, plainDef, new Vec2(20f, 0f), 100000f);
            var zoneTargets = new List<IAoETarget> { zoneInside, zoneOutside };
            var zones = new AreaEffectSystem();
            zones.Add(effects.FindArea("burn_zone")!, Vec2.Zero, 5f);
            for (int i = 0; i < 120; i++) zones.Tick(1f / 30f, zoneTargets);
            ok &= Check("燃烧区域 3 秒叠加 3 层", zoneInside.Status.BurnStacks, 3f);
            ok &= Check("区域外目标不叠层", zoneOutside.Status.BurnStacks, 0f);

            zones.Clear();
            zones.Add(effects.FindArea("chill_zone")!, Vec2.Zero, 5f);
            for (int i = 0; i < 120; i++) zones.Tick(1f / 30f, zoneTargets);
            ok &= Check("低温区域叠加 3 层", zoneInside.Status.ChillStacks, 3f);
            ok &= Check("低温区域附带阻滞力", zoneInside.BlockForce > 0f ? 1f : 0f, 1f);

            // ---- 避雷针 -------------------------------------------------------------
            var rods = new LightningRodSystem();
            LightningRod rod = rods.Add(new Vec2(5f, 0f), 12f, 60f, 3);
            ok &= Check("避雷针可被连锁选中", rods.FindForChain(Vec2.Zero, 20f) != null ? 1f : 0f, 1f);
            LightningRodSystem.Consume(rod);
            ok &= Check("避雷针次数 3→2", rod.Charges, 2f);
            rods.Tick(61f);
            ok &= Check("避雷针超时移除", rods.Rods.Count, 0f);

            Console.WriteLine("[selftest] " + (ok ? "PASS" : "FAIL") + " seed=" + options.Seed);
            return ok;
        }

        private static EnemyRuntime MakeEnemy(
            StatusRules rules, EnemyDef def, Vec2 position, float hp)
        {
            var enemy = new EnemyRuntime();
            enemy.Initialize(new EntityId(0, 0), def, position, hp, rules);
            return enemy;
        }

        private static int CostOf(TowerDef def, UpgradeNodeDef node)
        {
            double steps = node.Level - 1;
            double cost = def.Price * (double)node.CostFactor * steps * steps;
            if (node.Unique) cost *= 1.25;
            return (int)Math.Round(cost, MidpointRounding.AwayFromZero);
        }

        private static float CountHits(in DamagePacket probe, EnemyDef def)
        {
            float retention = probe.Retention;
            int hits = 0;
            while (hits < 32)
            {
                if (!PenetrationResolver.CanContinue(retention)) break;
                hits++;
                retention = PenetrationResolver.Consume(retention, probe, def);
            }
            return hits;
        }

        private static bool Check(string name, float actual, float expected, float tolerance = 1e-4f)
        {
            bool ok = Math.Abs(actual - expected) < tolerance;
            Console.WriteLine("[check] " + (ok ? "OK  " : "FAIL") + " " + name +
                              " (actual=" + actual.ToString("0.###", CultureInfo.InvariantCulture) + ")");
            return ok;
        }
    }
}
