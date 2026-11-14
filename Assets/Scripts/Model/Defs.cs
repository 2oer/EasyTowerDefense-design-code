namespace Game.Model
{
    /// <summary>防御塔基础定义。升级数值只在升级库,不落在这里。</summary>
    public sealed class TowerDef
    {
        public string Id { get; set; } = string.Empty;
        public int Price { get; set; }
        public float Damage { get; set; }
        public float FireRate { get; set; }
        public float Range { get; set; }
        public float TurnSpeed { get; set; }
        public float BlockForce { get; set; }
        public ArmorPierce Pierce { get; set; }
        public float Retention { get; set; }
        public int Ammo { get; set; }
        public float ReloadTime { get; set; }
        public float ExplosionRadius { get; set; }
        public float BeamWidth { get; set; }
        public TowerStrategy Strategy { get; set; }
        public DeliveryKind Delivery { get; set; }
        public float BulletRadius { get; set; } = 0.25f;

        // ---- 通用战斗扩展 --------------------------------------------------------
        public DamageType DamageType { get; set; } = DamageType.Ballistic;

        /// <summary>最小射程;0 表示无限制。</summary>
        public float MinRange { get; set; }

        /// <summary>弹丸飞行速度;0 表示即时命中。</summary>
        public float ProjectileSpeed { get; set; }

        /// <summary>弹丸是否追踪目标(火箭);false = 抛射到预判落点(迫击炮)。</summary>
        public bool HomingProjectile { get; set; }

        /// <summary>每轮齐射发数(阵列炮 / 火箭齐射)。</summary>
        public int Salvo { get; set; } = 1;

        /// <summary>无需转向(迫击炮 / 无人机库)。</summary>
        public bool NoTurn { get; set; }

        // ---- 电弧 -----------------------------------------------------------------
        public int ChainCount { get; set; }
        public float ChainRange { get; set; }
        public float ChainFalloff { get; set; }

        // ---- 空爆子母弹 -----------------------------------------------------------
        public int SubCount { get; set; }
        public float SubDamage { get; set; } = 1f;
        public float SubRadius { get; set; }
        public float SpreadRadius { get; set; }

        // ---- 高爆破片弹 -----------------------------------------------------------
        public float FirstStageDamage { get; set; } = 1f;
        public float SecondStageDamage { get; set; } = 1f;
        public float SecondStageRadius { get; set; } = 1f;

        // ---- 属性区域(凝固汽油弹 / 液氮弹 / 热压弹) ---------------------------
        public float ZoneScale { get; set; } = 1f;

        /// <summary>命中施加的充电层数(电浆导弹 / 球形闪电 / 漏电)。</summary>
        public int ChargeStacks { get; set; }

        /// <summary>飞行途中周期放电半径 / 间隔(漏电)。</summary>
        public float AuraRadius { get; set; }
        public float AuraInterval { get; set; }

        /// <summary>投掷物最长寿命;0 = 不限(漏电)。</summary>
        public float ProjectileLifetime { get; set; }

        // ---- 无人机库 -------------------------------------------------------------
        public int DroneSquad { get; set; }
        public int DroneCapacity { get; set; }
        public float DroneReload { get; set; }
        public float DroneDeployInterval { get; set; }
        public float DroneAmmo { get; set; }
        public float DroneFireRate { get; set; }
        public float DroneDamage { get; set; }
        public float DroneRange { get; set; }
        public float DroneSpeed { get; set; }
        public float DroneSelfDestructRadius { get; set; }
        public float DroneSelfDestructDamage { get; set; }
        public float RadarRange { get; set; }
    }

    /// <summary>敌人定义。数量与波次节奏不在这里。</summary>
    public sealed class EnemyDef
    {
        public string Id { get; set; } = string.Empty;
        public EnemySize Size { get; set; }
        public ArmorClass Armor { get; set; }
        public float Hp { get; set; }
        public float Speed { get; set; }
        public float BlockThreshold { get; set; }
        public float DecayBallistic { get; set; }
        public float DecayEnergy { get; set; }
        public float DecayExplosive { get; set; }
        public float DecayElemental { get; set; }

        /// <summary>易伤轴:该类伤害 +50%。null 表示无易伤。</summary>
        public DamageType? Vulnerability { get; set; }

        public int Bounty { get; set; }

        /// <summary>同体型内的随机生成权重。</summary>
        public float Weight { get; set; } = 1f;

        /// <summary>实弹 / 能量衰减按点数扣减留存率;其余类型不阻碍贯穿。</summary>
        public float Decay(DamageType type)
        {
            switch (type)
            {
                case DamageType.Ballistic: return DecayBallistic;
                case DamageType.Energy: return DecayEnergy;
                default: return 0f;
            }
        }

        /// <summary>近战 / 远程的体型系数。</summary>
        public float BodyCoef
        {
            get
            {
                switch (Size)
                {
                    case EnemySize.Small: return 0.30f;
                    case EnemySize.Medium: return 0.70f;
                    default: return 1.50f;
                }
            }
        }

        public float MeleeInterval
        {
            get
            {
                switch (Size)
                {
                    case EnemySize.Small: return 1.0f;
                    case EnemySize.Medium: return 1.5f;
                    default: return 2.5f;
                }
            }
        }

        /// <summary>碰撞半径(体积上限的一半)。</summary>
        public float Radius
        {
            get
            {
                switch (Size)
                {
                    case EnemySize.Small: return 0.5f;
                    case EnemySize.Medium: return 1.0f;
                    default: return 7.5f;
                }
            }
        }
    }

    public sealed class DifficultyDef
    {
        public string Id { get; set; } = string.Empty;
        public float HpGrowthPerWave { get; set; }
        public float GoldRate { get; set; }
        public float MediumShare { get; set; } = 1f;
        public float LargeShare { get; set; } = 1f;
        public int StartGold { get; set; }
        public int SmallBase { get; set; }

        /// <summary>第 11 波起的小型每波增量。</summary>
        public int SmallStep { get; set; }

        /// <summary>前 10 波的小型每波增量;0 表示沿用 SmallStep。</summary>
        public int SmallStepEarly { get; set; }

        public float MetaRate { get; set; } = 1f;
    }

    /// <summary>波次节奏规则(公式参数,不是逐波数据)。</summary>
    public sealed class WaveRules
    {
        public int MediumStartWave { get; set; } = 5;
        public int MediumBase { get; set; } = 2;
        public int MediumGrowthWaves { get; set; } = 5;
        public float MediumGrowth { get; set; } = 1.5f;
        public int LargeStartWave { get; set; } = 10;
        public int LargeBase { get; set; } = 1;
        public int LargeGrowthWaves { get; set; } = 10;
        public float LargeGrowth { get; set; } = 1.5f;
        public float LargeWaveMultiplier { get; set; } = 1.25f;
        public float DurationBase { get; set; } = 18f;
        public float DurationPerWave { get; set; } = 0.6f;
        public float DurationMin { get; set; } = 20f;
        public float DurationMax { get; set; } = 45f;
        public int Tiers { get; set; } = 4;
        public float SpawnIntervalMin { get; set; } = 0.25f;
        public float SpawnIntervalMax { get; set; } = 0.6f;
        public float TierEarlyUnlockRatio { get; set; } = 0.30f;
        public int HardCap { get; set; } = 500;

        /// <summary>小型成长提速的波数上限(含),此后回落到 SmallStep。</summary>
        public int EarlyGrowthWaves { get; set; } = 10;

        /// <summary>每波清场固定金币(再乘难度金币率)。</summary>
        public int WaveClearGold { get; set; } = 60;
    }

    public sealed class SlotDef
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    public sealed class MapDef
    {
        public string Id { get; set; } = string.Empty;
        public float Width { get; set; }
        public float Height { get; set; }
        public float BaseX { get; set; }
        public float BaseY { get; set; }
        public float BaseMaxHp { get; set; } = 15000f;
        public System.Collections.Generic.List<SlotDef> Slots { get; set; } =
            new System.Collections.Generic.List<SlotDef>();
    }
}
