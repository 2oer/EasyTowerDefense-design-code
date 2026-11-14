using System;
using System.Collections.Generic;
using Game.Core;
using Game.Model;

namespace Game.Battle
{
    public struct SpawnRequest
    {
        public EnemyDef Def;
        public Vec2 Position;

        public SpawnRequest(EnemyDef def, Vec2 position)
        {
            Def = def;
            Position = position;
        }
    }

    /// <summary>
    /// 波次导演:按文档公式生成本波名册,分 4 档释放,清场后进入下一波准备期。
    /// 只依赖 Model + Core,不接触实体。
    /// </summary>
    public sealed class WaveDirector
    {
        private enum Phase
        {
            Prep,
            Spawning,
            Clearing,
            Done
        }

        public const float PrepSeconds = 1.5f;

        private readonly WaveRules _rules;
        private readonly DifficultyDef _difficulty;
        private readonly MapDef _map;
        private readonly Rng _rng;
        private readonly List<EnemyDef> _smallPool;
        private readonly List<EnemyDef> _mediumPool;
        private readonly List<EnemyDef> _largePool;

        private readonly List<EnemyDef> _roster = new List<EnemyDef>();
        private readonly List<float> _weights = new List<float>();

        private Phase _phase = Phase.Done;
        private int _wave;
        private int _maxWaves;
        private float _prepTimer;
        private float _elapsed;
        private float _spawnTimer;
        private float _tierInterval;
        private int _cursor;
        private int _tier;
        private int _tierEnd;
        private int _tierQuota;
        private int _alive;
        private float _spawnY;
        private int _completedWave;
        private bool _waveStartedFlag;
        private bool _waveCompletedFlag;
        private bool _awaitingStart;
        private int _spawnSerial;

        public List<SpawnRequest> PendingSpawns { get; } = new List<SpawnRequest>();

        public int Wave => _wave;
        public bool AllWavesDone => _phase == Phase.Done;
        public bool InPrep => _phase == Phase.Prep;

        /// <summary>true = 布防阶段一直等待,直到显式调用 StartWaveNow();false = 1.5 s 后自动开战。</summary>
        public bool ManualStart { get; set; }

        /// <summary>是否正停在"等待玩家开战"的布防阶段。</summary>
        public bool AwaitingStart => _phase == Phase.Prep && _awaitingStart;

        public WaveDirector(ModelDatabase db, DifficultyDef difficulty, MapDef map, Rng rng)
        {
            _rules = db.Waves;
            _difficulty = difficulty;
            _map = map;
            _rng = rng;
            _smallPool = db.EnemiesOfSize(EnemySize.Small);
            _mediumPool = db.EnemiesOfSize(EnemySize.Medium);
            _largePool = db.EnemiesOfSize(EnemySize.Large);
        }

        public void Begin(int maxWaves)
        {
            _maxWaves = maxWaves;
            _wave = 1;
            _phase = Phase.Prep;
            if (ManualStart)
            {
                _awaitingStart = true;
                _prepTimer = 0f;
            }
            else
            {
                _prepTimer = PrepSeconds;
            }
        }

        /// <summary>玩家点击"开始交战"。仅在布防阶段且等待开战时有效。</summary>
        public bool StartWaveNow()
        {
            if (_phase != Phase.Prep || !_awaitingStart) return false;
            _awaitingStart = false;
            StartWave();
            return true;
        }

        public void Tick(float dt, int aliveCount, float spawnY)
        {
            _alive = aliveCount;
            _spawnY = spawnY;
            switch (_phase)
            {
                case Phase.Prep:
                    if (_awaitingStart) break;
                    _prepTimer -= dt;
                    if (_prepTimer <= 0f) StartWave();
                    break;
                case Phase.Spawning:
                    TickSpawning(dt);
                    break;
                case Phase.Clearing:
                    if (aliveCount == 0) CompleteWave();
                    break;
            }
        }

        public bool ConsumeWaveStarted(out int wave)
        {
            wave = _wave;
            if (!_waveStartedFlag) return false;
            _waveStartedFlag = false;
            return true;
        }

        public bool ConsumeWaveCompleted(out int wave)
        {
            wave = _completedWave;
            if (!_waveCompletedFlag) return false;
            _waveCompletedFlag = false;
            return true;
        }

        public bool IsLargeWave(int wave) => wave > 0 && wave % 5 == 0;

        private void StartWave()
        {
            BuildRoster();
            _cursor = 0;
            _tier = 0;
            _spawnSerial = 0;
            _tierQuota = Math.Max(1, (_roster.Count + _rules.Tiers - 1) / _rules.Tiers);
            _tierEnd = Math.Min(_roster.Count, _tierQuota);
            _elapsed = 0f;
            _spawnTimer = 0f;
            _tierInterval = WaveDuration(_wave) / _rules.Tiers;
            _phase = _roster.Count == 0 ? Phase.Clearing : Phase.Spawning;
            _waveStartedFlag = true;
        }

        private void TickSpawning(float dt)
        {
            _elapsed += dt;

            if (_cursor >= _roster.Count)
            {
                _phase = Phase.Clearing;
                return;
            }

            if (_cursor >= _tierEnd && _tier < _rules.Tiers - 1)
            {
                float gate = (_tier + 1) * _tierInterval;
                bool unlockedByTime = _elapsed >= gate;
                bool unlockedByClear = _alive <= _rules.TierEarlyUnlockRatio * Math.Max(1, _tierQuota);
                if (!unlockedByTime && !unlockedByClear) return;

                _tier++;
                _tierEnd = Math.Min(_roster.Count, _cursor + _tierQuota);
            }

            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;

            EnemyDef def = _roster[_cursor];
            PendingSpawns.Add(new SpawnRequest(def, NextSpawnPosition(def)));
            _cursor++;
            _spawnTimer = _rng.Range(_rules.SpawnIntervalMin, _rules.SpawnIntervalMax);
        }

        private void CompleteWave()
        {
            _completedWave = _wave;
            _waveCompletedFlag = true;
            if (_wave >= _maxWaves)
            {
                _phase = Phase.Done;
                return;
            }
            _wave++;
            _phase = Phase.Prep;
            if (ManualStart)
            {
                _awaitingStart = true;
            }
            else
            {
                _prepTimer = PrepSeconds;
            }
        }

        private float WaveDuration(int wave)
        {
            float duration = _rules.DurationBase + _rules.DurationPerWave * wave;
            if (duration < _rules.DurationMin) duration = _rules.DurationMin;
            if (duration > _rules.DurationMax) duration = _rules.DurationMax;
            return duration;
        }

        private void BuildRoster()
        {
            _roster.Clear();

            int earlyWaves = Math.Max(1, _rules.EarlyGrowthWaves);
            int earlySteps = Math.Min(_wave, earlyWaves) - 1;
            int lateSteps = Math.Max(0, _wave - earlyWaves);
            int earlyStep = _difficulty.SmallStepEarly > 0 ? _difficulty.SmallStepEarly : _difficulty.SmallStep;

            int small = _difficulty.SmallBase
                      + earlyStep * earlySteps
                      + _difficulty.SmallStep * lateSteps;
            if (IsLargeWave(_wave))
                small = MathUtil.RoundToInt(small * _rules.LargeWaveMultiplier);

            int medium = 0;
            if (_wave >= _rules.MediumStartWave)
            {
                int step = (_wave - _rules.MediumStartWave) / Math.Max(1, _rules.MediumGrowthWaves);
                medium = MathUtil.RoundToInt(
                    _rules.MediumBase * Pow(_rules.MediumGrowth, step) * _difficulty.MediumShare);
            }

            int large = 0;
            if (_wave >= _rules.LargeStartWave)
            {
                int step = (_wave - _rules.LargeStartWave) / Math.Max(1, _rules.LargeGrowthWaves);
                large = MathUtil.RoundToInt(
                    _rules.LargeBase * Pow(_rules.LargeGrowth, step) * _difficulty.LargeShare);
            }

            int total = small + medium + large;
            if (total > _rules.HardCap) small = Math.Max(0, small - (total - _rules.HardCap));

            AddRandom(_smallPool, small);
            AddRandom(_mediumPool, medium);
            AddRandom(_largePool, large);

            Shuffle(_roster);
        }

        private void AddRandom(List<EnemyDef> pool, int count)
        {
            if (count <= 0 || pool.Count == 0) return;
            _weights.Clear();
            for (int i = 0; i < pool.Count; i++) _weights.Add(pool[i].Weight);
            for (int i = 0; i < count; i++)
            {
                int pick = _rng.PickWeighted(_weights);
                _roster.Add(pool[pick]);
            }
        }

        private void Shuffle(List<EnemyDef> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.RangeInt(0, i + 1);
                EnemyDef tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>
        /// 出生点从中轴开始向两侧铺开,到边界后回绕到中轴继续。
        /// 目的只是避免同一位置挤太多敌人,而不是把后续生成永久推到两侧。
        /// 大型敌人的横向偏移压缩到 30%,尽量从中轴附近压上来,便于玩家集火。
        /// </summary>
        private Vec2 NextSpawnPosition(EnemyDef def)
        {
            float center = _map.Width * 0.5f;

            // 早期只在中线附近生成,随波次逐步扩大可用宽度。
            float waveSpan = 6f + _wave * 2.8f;
            float maxOffset = Math.Min(center - 2f, waveSpan);

            const float bandStep = 2.2f;
            int bandCount = Math.Max(1, (int)(maxOffset / bandStep) + 1);

            int serial = _spawnSerial++;
            float side = (serial % 2 == 0) ? 1f : -1f;
            int band = (serial / 2) % bandCount;
            float offset = band * bandStep + _rng.Range(0f, 1.2f);
            if (offset > maxOffset) offset = maxOffset;

            // 大型敌人尽量靠近地图中轴:偏移压到 30%,避免在边路被单塔逐个消耗。
            if (def.Size == EnemySize.Large) offset *= 0.3f;

            float x = center + side * offset;
            if (x < 2f) x = 2f;
            if (x > _map.Width - 2f) x = _map.Width - 2f;

            float y = _spawnY + _rng.Range(-1.5f, 1.5f);
            if (y < 0f) y = 0f;
            if (y > _map.Height) y = _map.Height;
            return new Vec2(x, y);
        }

        private static float Pow(float value, int exponent)
        {
            float result = 1f;
            for (int i = 0; i < exponent; i++) result *= value;
            return result;
        }
    }
}
