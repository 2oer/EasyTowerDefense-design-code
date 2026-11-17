namespace Game.Model
{
    /// <summary>
    /// 防御塔运行时属性:基础面板叠加已选升级后的结果。
    /// 塔与塔之间互不共享,升级或干扰变化时整份重建。
    /// </summary>
    public sealed class TowerRuntimeStats
    {
        public float Damage;
        public float FireRate;
        public float Range;
        public float TurnSpeed;
        public float BlockForce;
        public float Retention;
        public int Ammo;
        public float ReloadTime;
        public float BeamWidth;
        public float BulletRadius;
        public float ExplosionRadius;
        public int BeamCount = 1;
        public bool Continuous;

        /// <summary>持续照射总时长(秒);仅 Continuous = true 时有效。</summary>
        public float ContinuousDuration;

        /// <summary>持续照射的伤害结算间隔(秒)。</summary>
        public float ContinuousInterval;

        /// <summary>单次结算的伤害倍率(基础 0.75)。</summary>
        public float ContinuousDamageScale = 1f;

        /// <summary>一次照射结束后的重装间隔(秒)。</summary>
        public float ContinuousRest;
        public ArmorPierce Pierce;
        public TowerStrategy Strategy;
        public DeliveryKind Delivery;
        public DamageType DamageType;

        /// <summary>唯一升级机制标签;空 = 无特殊机制。</summary>
        public string Mechanic = string.Empty;

        public float MinRange;
        public float ProjectileSpeed;
        public bool HomingProjectile;
        public int Salvo = 1;
        public bool NoTurn;
        public int ChainCount;
        public float ChainRange;
        public float ChainFalloff;
        public int SubCount;
        public float SubDamage = 1f;
        public float SubRadius;
        public float SpreadRadius;
        public float FirstStageDamage = 1f;
        public float SecondStageDamage = 1f;
        public float SecondStageRadius = 1f;
        public float ZoneScale = 1f;
        public int ChargeStacks;
        public float AuraRadius;
        public float AuraInterval;
        public float ProjectileLifetime;

        public int DroneSquad;
        public int DroneCapacity;
        public float DroneReload;
        public float DroneDeployInterval;
        public float DroneAmmo;
        public float DroneFireRate;
        public float DroneDamage;
        public float DroneRange;
        public float DroneSpeed;
        public float DroneSelfDestructRadius;
        public float DroneSelfDestructDamage;
        public float RadarRange;

        public static TowerRuntimeStats FromDef(TowerDef def)
        {
            return new TowerRuntimeStats
            {
                Damage = def.Damage,
                FireRate = def.FireRate,
                Range = def.Range,
                TurnSpeed = def.TurnSpeed,
                BlockForce = def.BlockForce,
                Retention = def.Retention,
                Ammo = def.Ammo,
                ReloadTime = def.ReloadTime,
                BeamWidth = def.BeamWidth,
                BulletRadius = def.BulletRadius,
                ExplosionRadius = def.ExplosionRadius,
                BeamCount = 1,
                Continuous = false,
                Pierce = def.Pierce,
                Strategy = def.Strategy,
                Delivery = def.Delivery,
                DamageType = def.DamageType,
                Mechanic = string.Empty,
                MinRange = def.MinRange,
                ProjectileSpeed = def.ProjectileSpeed,
                HomingProjectile = def.HomingProjectile,
                Salvo = def.Salvo <= 0 ? 1 : def.Salvo,
                NoTurn = def.NoTurn,
                ChainCount = def.ChainCount,
                ChainRange = def.ChainRange,
                ChainFalloff = def.ChainFalloff,
                SubCount = def.SubCount,
                SubDamage = def.SubDamage <= 0f ? 1f : def.SubDamage,
                SubRadius = def.SubRadius,
                SpreadRadius = def.SpreadRadius,
                FirstStageDamage = def.FirstStageDamage <= 0f ? 1f : def.FirstStageDamage,
                SecondStageDamage = def.SecondStageDamage <= 0f ? 1f : def.SecondStageDamage,
                SecondStageRadius = def.SecondStageRadius <= 0f ? 1f : def.SecondStageRadius,
                ZoneScale = def.ZoneScale <= 0f ? 1f : def.ZoneScale,
                ChargeStacks = def.ChargeStacks,
                AuraRadius = def.AuraRadius,
                AuraInterval = def.AuraInterval,
                ProjectileLifetime = def.ProjectileLifetime,
                DroneSquad = def.DroneSquad,
                DroneCapacity = def.DroneCapacity,
                DroneReload = def.DroneReload,
                DroneDeployInterval = def.DroneDeployInterval,
                DroneAmmo = def.DroneAmmo,
                DroneFireRate = def.DroneFireRate,
                DroneDamage = def.DroneDamage,
                DroneRange = def.DroneRange,
                DroneSpeed = def.DroneSpeed,
                DroneSelfDestructRadius = def.DroneSelfDestructRadius,
                DroneSelfDestructDamage = def.DroneSelfDestructDamage,
                RadarRange = def.RadarRange
            };
        }
    }
}
