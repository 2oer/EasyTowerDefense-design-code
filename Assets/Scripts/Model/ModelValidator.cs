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
            }

            if (db.FindDifficulty("normal") == null)
                throw new ModelValidationException("缺少 normal 难度");

            for (int i = 0; i < db.Maps.Count; i++)
            {
                MapDef m = db.Maps[i];
                if (m.Width <= 0f || m.Height <= 0f) throw new ModelValidationException(m.Id + ": 地图尺寸非法");
                if (m.BaseMaxHp <= 0f) throw new ModelValidationException(m.Id + ": baseMaxHp 必须 > 0");
                if (m.Slots.Count > 4) throw new ModelValidationException(m.Id + ": 防御塔空位不得超过 4 个");
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
            if (w.MediumGrowth <= 1f || w.LargeGrowth <= 1f)
                throw new ModelValidationException("中 / 大型成长倍率必须 > 1");
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
