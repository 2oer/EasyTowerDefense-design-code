using Game.Core;
using Game.Model;

namespace Game.Targeting
{
    /// <summary>
    /// 索敌只依赖这份只读视图,Entity 层负责实现,避免 Targeting 反向依赖 Entity。
    /// </summary>
    public interface ITargetCandidate
    {
        EntityId Id { get; }
        Vec2 Position { get; }
        float Radius { get; }
        float Hp { get; }
        float MaxHp { get; }
        float DamageSent { get; }
        EnemyDef Def { get; }
    }
}
