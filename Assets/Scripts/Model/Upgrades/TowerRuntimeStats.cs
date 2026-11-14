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
        public ArmorPierce Pierce;
        public TowerStrategy Strategy;
        public DeliveryKind Delivery;
        public DamageType DamageType;

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
                DamageType = def.Delivery == DeliveryKind.HitscanRect ? DamageType.Energy : DamageType.Ballistic
            };
        }
    }
}
