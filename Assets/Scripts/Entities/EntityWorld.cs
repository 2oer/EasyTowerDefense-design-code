using System.Collections.Generic;
using Game.Core;
using Game.Model;

namespace Game.Entity
{
    /// <summary>实体容器。Battle 只通过它增删单位,保证 id 分配与遍历顺序确定。</summary>
    public sealed class EntityWorld
    {
        private int _nextEnemyIndex;
        private int _nextTowerIndex;

        public List<EnemyRuntime> Enemies { get; } = new List<EnemyRuntime>();
        public List<TowerAgent> Towers { get; } = new List<TowerAgent>();

        public EnemyRuntime SpawnEnemy(EnemyDef def, Vec2 position, float hp, StatusRules statusRules)
        {
            var enemy = new EnemyRuntime();
            enemy.Initialize(new EntityId(_nextEnemyIndex++, 0), def, position, hp, statusRules);
            Enemies.Add(enemy);
            return enemy;
        }

        public TowerAgent AddTower(TowerDef def, Vec2 position)
        {
            var tower = new TowerAgent();
            tower.Initialize(new EntityId(_nextTowerIndex++, 0), def, position);
            Towers.Add(tower);
            return tower;
        }

        /// <summary>移除防御塔(拆除)。槽位映射由 BattleSimulation 负责同步。</summary>
        public bool RemoveTower(TowerAgent tower)
        {
            if (tower == null) return false;
            return Towers.Remove(tower);
        }

        /// <summary>移除所有已死亡敌人,返回被移除的数量。</summary>
        public int PruneDeadEnemies()
        {
            int removed = 0;
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                if (Enemies[i].Alive) continue;
                Enemies.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public int AliveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Enemies.Count; i++)
                {
                    if (Enemies[i].Alive) count++;
                }
                return count;
            }
        }
    }
}
