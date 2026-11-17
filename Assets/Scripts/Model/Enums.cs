namespace Game.Model
{
    public enum EnemySize
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    /// <summary>护甲等级与体型一一绑定。</summary>
    public enum ArmorClass
    {
        Low = 0,
        Mid = 1,
        Heavy = 2
    }

    public enum ArmorPierce
    {
        Low = 0,
        Mid = 1,
        High = 2
    }

    public enum DamageType
    {
        Ballistic = 0,
        Energy = 1,
        Explosive = 2,
        Elemental = 3,
        Radiation = 4
    }

    public enum TowerStrategy
    {
        Focus = 0,
        Spray = 1,

        /// <summary>战略:自动锁定当前生命值最高的目标(斩首),用于发射井 / 大舰巨炮。</summary>
        Strategic = 2
    }

    public enum DeliveryKind
    {
        HitscanLine = 0,
        HitscanRect = 1,
        HitscanNearFuse = 2,
        Chain = 3,
        Projectile = 4
    }
}
