using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Battle;
using Game.Combat;
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
                " tiers=" + db.Waves.Tiers);

            if (options.SelfTest)
            {
                return RunSelfTest(db, options) ? 0 : 3;
            }

            ulong firstHash;
            int firstWave;
            RunOnce(db, options, 1, true, out firstHash, out firstWave);

            ulong secondHash;
            int secondWave;
            RunOnce(db, options, 2, false, out secondHash, out secondWave);

            bool deterministic = firstHash == secondHash && firstWave == secondWave;
            Console.WriteLine(
                "[hash] run1=0x" + firstHash.ToString("X16") +
                " run2=0x" + secondHash.ToString("X16") +
                " determinism=" + (deterministic ? "OK" : "FAIL"));

            return deterministic ? 0 : 4;
        }

        private static void RunOnce(
            ModelDatabase db,
            CliOptions options,
            int runIndex,
            bool verbose,
            out ulong hash,
            out int waveReached)
        {
            var sim = new BattleSimulation(db, options.Difficulty, options.MapId, options.Seed, options.BaseHpOverride);

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

            waveReached = sim.Wave;
            hash = sim.ComputeStateHash();

            if (verbose)
            {
                Console.WriteLine(
                    "[result] run=" + runIndex +
                    " wave=" + waveReached + "/" + options.Waves +
                    " base=" + sim.BaseHp.ToString("0.0", CultureInfo.InvariantCulture) + "/" + sim.BaseMaxHp.ToString("0", CultureInfo.InvariantCulture) +
                    " gold=" + sim.Gold +
                    " meta=" + sim.Meta +
                    " kills=" + sim.Kills +
                    " leaks=" + sim.Leaks +
                    " spawned=" + sim.Spawned +
                    " ticks=" + sim.Tick +
                    " simTime=" + sim.Time.ToString("0.0", CultureInfo.InvariantCulture) + "s" +
                    " state=" + stopReason);
            }
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

            Console.WriteLine("[selftest] " + (ok ? "PASS" : "FAIL") + " seed=" + options.Seed);
            return ok;
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

        private static bool Check(string name, float actual, float expected)
        {
            bool ok = Math.Abs(actual - expected) < 1e-4f;
            Console.WriteLine("[check] " + (ok ? "OK  " : "FAIL") + " " + name +
                              " (actual=" + actual.ToString("0.###", CultureInfo.InvariantCulture) + ")");
            return ok;
        }
    }
}
