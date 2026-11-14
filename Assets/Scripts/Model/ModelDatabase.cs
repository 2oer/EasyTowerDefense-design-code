using System;
using System.Collections.Generic;

namespace Game.Model
{
    /// <summary>五个库的运行时载体。JSON 为权威源,本类只负责持有与查找。</summary>
    public sealed class ModelDatabase
    {
        public List<TowerDef> Towers { get; } = new List<TowerDef>();
        public List<EnemyDef> Enemies { get; } = new List<EnemyDef>();
        public List<DifficultyDef> Difficulties { get; } = new List<DifficultyDef>();
        public List<MapDef> Maps { get; } = new List<MapDef>();
        public WaveRules Waves { get; set; } = new WaveRules();

        public TowerDef? FindTower(string id)
        {
            for (int i = 0; i < Towers.Count; i++)
            {
                if (string.Equals(Towers[i].Id, id, StringComparison.Ordinal)) return Towers[i];
            }
            return null;
        }

        public DifficultyDef? FindDifficulty(string id)
        {
            for (int i = 0; i < Difficulties.Count; i++)
            {
                if (string.Equals(Difficulties[i].Id, id, StringComparison.Ordinal)) return Difficulties[i];
            }
            return null;
        }

        public MapDef? FindMap(string id)
        {
            for (int i = 0; i < Maps.Count; i++)
            {
                if (string.Equals(Maps[i].Id, id, StringComparison.Ordinal)) return Maps[i];
            }
            return null;
        }

        /// <summary>按体型筛出敌人定义,保持 JSON 顺序以保证确定性。</summary>
        public List<EnemyDef> EnemiesOfSize(EnemySize size)
        {
            var result = new List<EnemyDef>();
            for (int i = 0; i < Enemies.Count; i++)
            {
                if (Enemies[i].Size == size) result.Add(Enemies[i]);
            }
            return result;
        }
    }
}
