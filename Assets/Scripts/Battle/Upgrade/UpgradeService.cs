using System;
using Game.Entity;
using Game.Model;

namespace Game.Battle
{
    /// <summary>
    /// 升级结算:费用 = 基础价 × costFactor × (目标等级 − 1)²,唯一升级再 ×1.25。
    /// 只允许逐级升级,且前置节点中至少有一个已被选中。
    /// </summary>
    public sealed class UpgradeService
    {
        private readonly ModelDatabase _db;
        private readonly EconomySystem _economy;

        public UpgradeService(ModelDatabase db, EconomySystem economy)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        public int Cost(TowerDef def, UpgradeNodeDef node)
        {
            double steps = node.Level - 1;
            double cost = def.Price * (double)node.CostFactor * steps * steps;
            if (node.Unique) cost *= 1.25;
            return (int)Math.Round(cost, MidpointRounding.AwayFromZero);
        }

        public bool CanApply(TowerAgent tower, UpgradeNodeDef node)
        {
            if (!string.Equals(node.TowerId, tower.Def.Id, StringComparison.Ordinal)) return false;
            if (tower.HasUpgrade(node.Id)) return false;
            if (tower.Level + 1 != node.Level) return false;

            if (node.Requires.Count == 0) return true;
            for (int i = 0; i < node.Requires.Count; i++)
            {
                if (tower.HasUpgrade(node.Requires[i])) return true;
            }
            return false;
        }

        public bool TryApply(TowerAgent tower, string nodeId, out int cost)
        {
            cost = 0;
            UpgradeNodeDef? node = _db.FindUpgrade(nodeId);
            if (node == null || !CanApply(tower, node)) return false;

            int price = Cost(tower.Def, node);
            if (!_economy.TrySpend(price)) return false;

            tower.ApplyUpgrade(node);
            cost = price;
            return true;
        }
    }
}
