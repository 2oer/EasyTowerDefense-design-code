namespace Game.Model
{
    /// <summary>唯一升级机制标签。JSON 里写字符串,代码只比较常量。</summary>
    public static class TowerMechanics
    {
        public const string None = "";

        // 迫击炮
        public const string Cluster = "cluster";
        public const string Napalm = "napalm";
        public const string Cryo = "cryo";
        public const string Fragmentation = "fragmentation";
        public const string ArrayBattery = "array_battery";
        public const string SmartImplosion = "smart_implosion";

        // 火箭塔
        public const string Swarm = "swarm";
        public const string PlasmaMissile = "plasma_missile";
        public const string SplitWarhead = "split_warhead";
        public const string Thermobaric = "thermobaric";
        public const string Hydrogen = "hydrogen";
        public const string MissileSilo = "missile_silo";

        // 电弧塔
        public const string Capacitor = "capacitor";
        public const string Fission = "fission";
        public const string Rod = "lightning_rod";
        public const string BallLightning = "ball_lightning";
        public const string LeakCurrent = "leak_current";
        public const string RollingThunder = "rolling_thunder";

        // 无人机库
        public const string Hurricane = "hurricane";
        public const string Aurora = "aurora";
        public const string Thunderstorm = "thunderstorm";
        public const string MobileFactory = "mobile_factory";
        public const string Battleship = "battleship";
        public const string RefinedHangar = "refined_hangar";
    }
}
