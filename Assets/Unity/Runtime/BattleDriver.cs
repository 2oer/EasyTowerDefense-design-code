using System;
using Game.Battle;
using Game.Model;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 宿主入口:加载配置表 → 创建 BattleSimulation → 以固定 30 Hz 推进。
    /// 逻辑与渲染完全解耦:本类只负责时间步,不碰任何 GameObject。
    /// </summary>
    public sealed class BattleDriver : MonoBehaviour
    {
        [Header("对局配置")]
        [SerializeField] private string difficultyId = "normal";
        [SerializeField] private string mapId = "standard";
        [SerializeField] private int maxWaves = 30;
        [Tooltip("确定性种子(Unity 不支持序列化 ulong,这里用 int 传入)")]
        [SerializeField] private int seed = 12345;

        [Header("模式")]
        [Tooltip("true = AI 自动建造 / 升级(回归与演示);false = 由 UI 调用 TryBuildTower / TryUpgradeTower")]
        [SerializeField] private bool autoPlay = false;

        [Tooltip("true = 布防阶段等玩家点击「开始交战」;false = 准备 1.5 秒后自动开战")]
        [SerializeField] private bool manualWaveStart = true;

        [SerializeField] private bool autoStart = true;

        [Header("时间")]
        [SerializeField] private int speedMultiplier = 1;

        [Header("测试")]
        [Tooltip("初始金币覆盖;>0 时生效。默认 1000000 方便快速测试,正式发布请改回 0")]
        [SerializeField] private int startGoldOverride = 1000000;

        private float _accumulator;

        public ModelDatabase Database { get; private set; }
        public BattleSimulation Simulation { get; private set; }
        public bool Paused { get; set; }
        public int SpeedMultiplier
        {
            get { return speedMultiplier; }
            set { speedMultiplier = Mathf.Clamp(value, 1, 5); }
        }

        public event Action<BattleSimulation> BattleStarted;
        public event Action<BattleSimulation> BattleFinished;

        private void Awake()
        {
            // 配置表读取失败会直接抛错,便于在编辑器里第一时间发现。
            Database = DefsJsonLoader.LoadFromStreamingAssets();
        }

        private void Start()
        {
            if (autoStart) StartBattle();
        }

        public void StartBattle()
        {
            // autoPlay 必须在构造时就传入:构造函数会决定是否立刻执行 AI 建塔。
            Simulation = new BattleSimulation(Database, difficultyId, mapId, (ulong)seed,
                0f, "focus", autoPlay, manualWaveStart, startGoldOverride);
            Simulation.OnFinished += HandleFinished;
            Simulation.Begin(maxWaves);

            _accumulator = 0f;
            Paused = false;
            BattleStarted?.Invoke(Simulation);
        }

        public void Restart()
        {
            if (Simulation != null) Simulation.OnFinished -= HandleFinished;
            StartBattle();
        }

        private void HandleFinished()
        {
            BattleFinished?.Invoke(Simulation);
        }

        private void Update()
        {
            if (Simulation == null || Paused || Simulation.Finished) return;

            // 切出窗口再回来时 unscaledDeltaTime 会出现巨大尖峰;先夹住单帧步长,
            // 再把累积器限制在 4 步以内,避免"回到 Unity 后突然快进"。
            float frameDelta = Mathf.Min(Time.unscaledDeltaTime, 0.1f) * speedMultiplier;
            _accumulator = Mathf.Min(_accumulator + frameDelta, BattleSimulation.Dt * 4f);

            int guard = 0;
            while (_accumulator >= BattleSimulation.Dt && guard++ < 4)
            {
                Simulation.Step();
                _accumulator -= BattleSimulation.Dt;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) _accumulator = 0f;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) _accumulator = 0f;
        }

        /// <summary>玩家建造入口(UI 调用)。</summary>
        public bool TryBuild(string towerId, int slotIndex)
        {
            return Simulation != null && Simulation.TryBuildTower(towerId, slotIndex);
        }

        /// <summary>玩家升级入口(UI 调用)。</summary>
        public bool TryUpgrade(int towerIndex, string nodeId)
        {
            if (Simulation == null) return false;
            int cost;
            return Simulation.TryUpgradeTower(towerIndex, nodeId, out cost);
        }

        public bool AwaitingWaveStart =>
            Simulation != null && !Simulation.Finished && Simulation.AwaitingWaveStart;

        /// <summary>玩家点击「开始交战」。</summary>
        public bool StartWave()
        {
            return Simulation != null && Simulation.StartNextWave();
        }

        /// <summary>拆除防御塔,返回实际返还的金币。</summary>
        public bool TrySell(int towerIndex, out int refund)
        {
            refund = 0;
            return Simulation != null && Simulation.TrySellTower(towerIndex, out refund);
        }

        public int SellRefund(int towerIndex)
        {
            return Simulation != null ? Simulation.SellRefund(towerIndex) : 0;
        }

        public int TowerInvested(int towerIndex)
        {
            return Simulation != null ? Simulation.TowerInvested(towerIndex) : 0;
        }

        public int TowerUpgradeSpent(int towerIndex)
        {
            return Simulation != null ? Simulation.TowerUpgradeSpent(towerIndex) : 0;
        }
    }
}
