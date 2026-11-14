using System;

namespace Game.Core
{
    /// <summary>槽位 + 代际的实体标识,防止复用槽位后误引用旧实体。</summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Index;
        public readonly int Generation;

        public EntityId(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public static readonly EntityId None = new EntityId(-1, 0);

        public bool IsValid => Index >= 0;

        public bool Equals(EntityId other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

        public override int GetHashCode() => (Index * 397) ^ Generation;

        public override string ToString() => Index + ":" + Generation;
    }
}
