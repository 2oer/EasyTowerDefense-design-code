using System;
using System.Collections.Generic;

namespace Game.Model
{
    public sealed class ModelValidationException : Exception
    {
        public ModelValidationException(string message) : base(message)
        {
        }
    }

    /// <summary>启动期硬校验:不通过直接抛错,不带病运行。</summary>
    public static class ModelValidator
    {
        public static void Validate(ModelDatabase db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (db.Towers.Count == 0) throw new ModelValidationException("towers 为空");
            if (db.Enemies.Count == 0) throw new ModelValidationException("enemies 为空");
            if (db.Difficulties.Count == 0) throw new ModelValidationException("difficulties 为空");
            if (db.Maps.Count == 0) throw new ModelValidationException("maps 为空");

            ValidateWaves(db.Waves);

            for (int i = 0; i < db.Towers.Count; i++)
            {
                TowerDef t = db.Towers[i];
                if (string.IsNullOrWhiteSpace(t.Id)) throw new ModelValidationException("防御塔 id 为空");
                if (t.Price <= 0) throw new ModelValidationException(t.Id + ": price 必须 > 0");
                if (t.Damage <= 0f) throw new ModelValidationException(t.Id + ": damage 必须 > 0");
                if (t.FireRate <= 0f) throw new ModelValidationException(t.Id + ": fireRate 必须 > 0");
                if (t.Range <= 0f) throw new ModelValidationException(t.Id + ": range 必须 > 0");
                if (t.TurnSpeed < 0f) throw new ModelValidationException(t.Id + ": turnSpeed 不得为负");
                if (t.Retention < 0f) throw new ModelValidationException(t.Id + ": retention 不得为负");
                if (t.Ammo < 0) throw new ModelValidationException(t.Id + ": ammo 不得为负");
                if (t.Ammo > 0 && t.ReloadTime < 0f) throw new ModelValidationException(t.Id + ": reloadTime 不得为负");
                if (t.Delivery == DeliveryKind.HitscanRect && t.BeamWidth <= 0f)
                    throw new ModelValidationException(t.Id + ": HitscanRect 必须给出 beamWidth");
                if (t.Delivery == DeliveryKind.HitscanLine && t.BulletRadius <= 0f)
                    throw new ModelValidationException(t.Id + ": HitscanLine 必须给出 bulletRadius");
            }

            for (int i = 0; i < db.Enemies.Count; i++)
            {
                EnemyDef e = db.Enemies[i];
                if (string.IsNullOrWhiteSpace(e.Id)) throw new ModelValidationException("敌人 id 为空");
                if (e.Hp <= 0f) throw new ModelValidationException(e.Id + ": hp 必须 > 0");
                if (e.Speed <= 0f) throw new ModelValidationException(e.Id + ": speed 必须 > 0");
                if (e.BlockThreshold <= 0f) throw new ModelValidationException(e.Id + ": blockThreshold 必须 > 0");
                if (e.Bounty < 0) throw new ModelValidationException(e.Id + ": bounty 不得为负");
                if (e.Weight <= 0f) throw new ModelValidationException(e.Id + ": weight 必须 > 0");
                CheckDecay(e.Id, "decayBallistic", e.DecayBallistic);
                CheckDecay(e.Id, "decayEnergy", e.DecayEnergy);
                CheckPercent(e.Id, "decayExplosive", e.DecayExplosive);
                CheckPercent(e.Id, "decayElemental", e.DecayElemental);
            }

            for (int i = 0; i < db.Difficulties.Count; i++)
            {
                DifficultyDef d = db.Difficulties[i];
                if (d.HpGrowthPerWave <= 0f) throw new ModelValidationException(d.Id + ": hpGrowthPerWave 必须 > 0");
                if (d.GoldRate <= 0f) throw new ModelValidationException(d.Id + ": goldRate 必须 > 0");
                if (d.StartGold < 0) throw new ModelValidationException(d.Id + ": startGold 不得为负");
                if (d.SmallBase <= 0) throw new ModelValidationException(d.Id + ": smallBase 必须 > 0");
                if (d.SmallStep < 0) throw new ModelValidationException(d.Id + ": smallStep 不得为负");
                if (d.SmallStepEarly < 0) throw new ModelValidationException(d.Id + ": smallStepEarly 不得为负");
            }

            ValidateUpgrades(db);
            ValidateEffects(db.Effects);

            if (db.FindDifficulty("normal") == null)
                throw new ModelValidationException("缺少 normal 难度");

            for (int i = 0; i < db.Maps.Count; i++)
            {
                MapDef m = db.Maps[i];
                if (m.Width <= 0f || m.Height <= 0f) throw new ModelValidationException(m.Id + ": 地图尺寸非法");
                if (m.BaseMaxHp <= 0f) throw new ModelValidationException(m.Id + ": baseMaxHp 必须 > 0");
                if (m.Slots.Count > 8) throw new ModelValidationException(m.Id + ": 防御塔空位不得超过 8 个");
                for (int s = 0; s < m.Slots.Count; s++)
                {
                    SlotDef slot = m.Slots[s];
                    if (slot.X < 0f || slot.X > m.Width || slot.Y < 0f || slot.Y > m.Height)
                        throw new ModelValidationException(m.Id + ": 空位坐标越界");
                }
            }
        }

        private static void ValidateWaves(WaveRules w)
        {
            if (w == null) throw new ModelValidationException("waves 规则缺失");
            if (w.Tiers <= 0) throw new ModelValidationException("tiers 必须 > 0");
            if (w.SpawnIntervalMin <= 0f) throw new ModelValidationException("spawnIntervalMin 必须 > 0");
            if (w.SpawnIntervalMax < w.SpawnIntervalMin)
                throw new ModelValidationException("spawnIntervalMax 不得小于 spawnIntervalMin");
            if (w.DurationMin <= 0f || w.DurationMax < w.DurationMin)
                throw new ModelValidationException("波次时长区间非法");
            if (w.HardCap <= 0) throw new ModelValidationException("hardCap 必须 > 0");
            if (w.EarlyGrowthWaves < 1) throw new ModelValidationException("earlyGrowthWaves 必须 ≥ 1");
            if (w.WaveClearGold < 0) throw new ModelValidationException("waveClearGold 不得为负");
            if (w.MediumGrowth <= 1f || w.LargeGrowth <= 1f)
                throw new ModelValidationException("中 / 大型成长倍率必须 > 1");
        }

        private static void ValidateUpgrades(ModelDatabase db)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < db.Upgrades.Count; i++)
            {
                UpgradeNodeDef u = db.Upgrades[i];
                if (string.IsNullOrWhiteSpace(u.Id)) throw new ModelValidationException("升级节点 id 为空");
                if (!seen.Add(u.Id)) throw new ModelValidationException(u.Id + ": 升级节点 id 重复");
                if (db.FindTower(u.TowerId) == null)
                    throw new ModelValidationException(u.Id + ": towerId 不存在:" + u.TowerId);
                if (u.Level < 2 || u.Level > 7)
                    throw new ModelValidationException(u.Id + ": level 必须在 2–7");
                if (u.CostFactor <= 0f)
                    throw new ModelValidationException(u.Id + ": costFactor 必须 > 0");
                if (u.Unique && u.Level != 4 && u.Level != 7)
                    throw new ModelValidationException(u.Id + ": unique 只能出现在 Lv4 / Lv7");

                for (int r = 0; r < u.Requires.Count; r++)
                {
                    string reqId = u.Requires[r];
                    UpgradeNodeDef? req = db.FindUpgrade(reqId);
                    if (req == null)
                        throw new ModelValidationException(u.Id + ": 前置节点不存在:" + reqId);
                    if (req.Level != u.Level - 1)
                        throw new ModelValidationException(u.Id + ": 前置节点等级必须恰好低一级:" + reqId);
                }

                for (int m = 0; m < u.Modifiers.Count; m++)
                {
                    StatModifier mod = u.Modifiers[m];
                    if (float.IsNaN(mod.Value) || float.IsInfinity(mod.Value))
                        throw new ModelValidationException(u.Id + ": 修饰值非法");
                    if (mod.Op == ModifierOp.Mul && mod.Value <= 0f)
                        throw new ModelValidationException(u.Id + ": Mul 修饰值必须 > 0");
                }
            }
        }

        private static void ValidateEffects(StatusRules effects)
        {
            if (effects == null) throw new ModelValidationException("effects 规则缺失");

            BurnRule burn = effects.Burn;
            if (burn == null) throw new ModelValidationException("effects.burn 缺失");
            if (burn.MaxStacks <= 0) throw new ModelValidationException("burn.maxStacks 必须 > 0");
            if (burn.Duration <= 0f) throw new ModelValidationException("burn.duration 必须 > 0");
            if (burn.TickInterval <= 0f) throw new ModelValidationException("burn.tickInterval 必须 > 0");
            if (burn.BaseDamagePerSecond <= 0f)
                throw new ModelValidationException("burn.baseDamagePerSecond 必须 > 0");
            if (burn.StackMultipliers.Count != burn.MaxStacks)
                throw new ModelValidationException("burn.stackMultipliers 长度必须等于 maxStacks");
            for (int i = 0; i < burn.StackMultipliers.Count; i++)
            {
                if (burn.StackMultipliers[i] <= 0f)
                    throw new ModelValidationException("burn.stackMultipliers 必须 > 0");
            }

            ChillRule chill = effects.Chill;
            if (chill == null) throw new ModelValidationException("effects.chill 缺失");
            if (chill.MaxStacks <= 0) throw new ModelValidationException("chill.maxStacks 必须 > 0");
            if (chill.Duration <= 0f) throw new ModelValidationException("chill.duration 必须 > 0");
            if (chill.ThresholdReductions.Count != chill.MaxStacks)
                throw new ModelValidationException("chill.thresholdReductions 长度必须等于 maxStacks");
            float reductionSum = 0f;
            for (int i = 0; i < chill.ThresholdReductions.Count; i++)
            {
                float r = chill.ThresholdReductions[i];
                if (r < 0f || r >= 1f)
                    throw new ModelValidationException("chill.thresholdReductions 必须落在 [0,1)");
                reductionSum += r;
            }
            if (reductionSum >= 1f)
                throw new ModelValidationException("chill 满层阈值降幅必须 < 100%");
            if (chill.MaxStackElementalDecayScale < 0f || chill.MaxStackElementalDecayScale > 1f)
                throw new ModelValidationException("chill.maxStackElementalDecayScale 必须落在 [0,1]");

            HeatMeltRule heat = effects.HeatMelt;
            if (heat == null) throw new ModelValidationException("effects.heatMelt 缺失");
            if (heat.MaxMultiplier < 1f) throw new ModelValidationException("heatMelt.maxMultiplier 必须 ≥ 1");
            if (heat.RampSeconds <= 0f) throw new ModelValidationException("heatMelt.rampSeconds 必须 > 0");

            if (effects.ArmorBreak == null) throw new ModelValidationException("effects.armorBreak 缺失");
            if (effects.ArmorBreak.Duration <= 0f)
                throw new ModelValidationException("armorBreak.duration 必须 > 0");

            ChargeRule charge = effects.Charge;
            if (charge == null) throw new ModelValidationException("effects.charge 缺失");
            if (charge.MaxStacks <= 0) throw new ModelValidationException("charge.maxStacks 必须 > 0");
            if (charge.BaseDamage <= 0f) throw new ModelValidationException("charge.baseDamage 必须 > 0");
            if (charge.StackDamageBonus < 0f)
                throw new ModelValidationException("charge.stackDamageBonus 不得为负");
            if (charge.RadiusByStacks.Count != charge.MaxStacks)
                throw new ModelValidationException("charge.radiusByStacks 长度必须等于 maxStacks");
            for (int i = 0; i < charge.RadiusByStacks.Count; i++)
            {
                if (charge.RadiusByStacks[i] <= 0f)
                    throw new ModelValidationException("charge.radiusByStacks 必须 > 0");
            }

            DecayRule decay = effects.Decay;
            if (decay == null) throw new ModelValidationException("effects.decay 缺失");
            if (decay.MaxStacks <= 0) throw new ModelValidationException("decay.maxStacks 必须 > 0");
            if (decay.TickInterval <= 0f) throw new ModelValidationException("decay.tickInterval 必须 > 0");
            if (decay.ExponentBase <= 1f) throw new ModelValidationException("decay.exponentBase 必须 > 1");

            var areaIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < effects.Areas.Count; i++)
            {
                AreaEffectRule area = effects.Areas[i];
                if (string.IsNullOrWhiteSpace(area.Id))
                    throw new ModelValidationException("区域效果 id 为空");
                if (!areaIds.Add(area.Id))
                    throw new ModelValidationException(area.Id + ": 区域效果 id 重复");
                if (area.Kind != StatusKind.Burn && area.Kind != StatusKind.Chill)
                    throw new ModelValidationException(area.Id + ": 区域只支持 Burn / Chill");
                if (area.Duration <= 0f) throw new ModelValidationException(area.Id + ": duration 必须 > 0");
                if (area.ApplyInterval <= 0f)
                    throw new ModelValidationException(area.Id + ": applyInterval 必须 > 0");
                if (area.StacksPerApply <= 0)
                    throw new ModelValidationException(area.Id + ": stacksPerApply 必须 > 0");
                if (area.BlockForce < 0f)
                    throw new ModelValidationException(area.Id + ": blockForce 不得为负");
            }
        }

        private static void CheckDecay(string id, string field, float value)
        {
            if (value < 0f || value > 6f)
                throw new ModelValidationException(id + ": " + field + " 超出 [0,6] 点数区间");
        }

        private static void CheckPercent(string id, string field, float value)
        {
            if (value < 0f || value > 1f)
                throw new ModelValidationException(id + ": " + field + " 必须是 [0,1] 的百分比");
        }
    }
}
