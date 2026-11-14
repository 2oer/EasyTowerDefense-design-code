using System;
using Game.Model;

namespace Game.Battle
{
    /// <summary>金币与局外货币。击杀赏金只在击杀时结算,不设清场奖励。</summary>
    public sealed class EconomySystem
    {
        private readonly DifficultyDef _difficulty;

        public EconomySystem(DifficultyDef difficulty)
        {
            _difficulty = difficulty;
            Gold = difficulty.StartGold;
        }

        public int Gold { get; private set; }
        public int Meta { get; private set; }

        public int KillReward(int bounty, float goldBonus)
        {
            float value = bounty * _difficulty.GoldRate * (1f + goldBonus);
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        public void AddGold(int amount)
        {
            if (amount > 0) Gold += amount;
        }

        public bool TrySpend(int amount)
        {
            if (amount <= 0 || Gold < amount) return false;
            Gold -= amount;
            return true;
        }

        /// <summary>普通波 3×波数,大型波 20×波数,即时入账。</summary>
        public void AwardWaveMeta(int wave, bool isLargeWave)
        {
            int baseAmount = (isLargeWave ? 20 : 3) * wave;
            int amount = (int)Math.Round(baseAmount * _difficulty.MetaRate, MidpointRounding.AwayFromZero);
            if (amount > 0) Meta += amount;
        }
    }
}
