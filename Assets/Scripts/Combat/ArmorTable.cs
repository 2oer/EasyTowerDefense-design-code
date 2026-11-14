using Game.Model;

namespace Game.Combat
{
    /// <summary>
    /// 护甲系数:同级 100%;高一级 50%;高两级 0%;低一级或两级 120%。
    /// 碎甲易伤由第二个重载给出,把 50/0 提升到 70/20。
    /// </summary>
    public static class ArmorTable
    {
        public static float Coefficient(ArmorPierce pierce, ArmorClass armor)
        {
            return Coefficient(pierce, armor, false);
        }

        public static float Coefficient(ArmorPierce pierce, ArmorClass armor, bool shattered)
        {
            int diff = (int)armor - (int)pierce;
            if (diff <= 0) return 1.20f;
            if (diff == 1) return shattered ? 0.70f : 0.50f;
            return shattered ? 0.20f : 0.00f;
        }
    }
}
