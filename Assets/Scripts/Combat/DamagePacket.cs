using Game.Core;
using Game.Model;

namespace Game.Combat
{
    /// <summary>伤害唯一入口。任何伤害都必须构造成本包再结算。</summary>
    public struct DamagePacket
    {
        public float Amount;
        public DamageType Type;
        public ArmorPierce Pierce;

        /// <summary>当前剩余留存率;非贯穿来源可填 0。</summary>
        public float Retention;

        /// <summary>阻滞力(0 = 无)。</summary>
        public float BlockForce;

        /// <summary>属性 DoT:属性衰减按一半计入。</summary>
        public bool IsDot;

        /// <summary>激光束标记:只有光束享受热熔易伤。</summary>
        public bool Beam;

        public EntityId Source;
    }
}
