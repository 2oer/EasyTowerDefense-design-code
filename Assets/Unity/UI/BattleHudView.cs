using UnityEngine;
using UnityEngine.UI;

namespace Game.UnityBridge.UI
{
    /// <summary>顶部 HUD:金币 / 基地耐久 / 波次 / 暂停与倍速。</summary>
    public sealed class BattleHudView : MonoBehaviour
    {
        [SerializeField] private BattleDriver driver;
        [SerializeField] private Text goldText;
        [SerializeField] private Text baseHpText;
        [SerializeField] private Text waveText;
        [SerializeField] private Text statusText;
        [SerializeField] private Image baseHpFill;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button speedButton;
        [SerializeField] private Button startWaveButton;
        [SerializeField] private Text pauseLabel;
        [SerializeField] private Text speedLabel;
        [SerializeField] private Text startWaveLabel;

        private int _speedIndex;

        private static readonly int[] Speeds = { 1, 2, 5 };

        private static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        private static readonly Color Muted = new Color(0.38f, 0.42f, 0.47f);
        private static readonly Color Warn = new Color(0.78f, 0.1f, 0.12f);

        private void Start()
        {
            if (driver == null) driver = FindObjectOfType<BattleDriver>();

            if (pauseButton != null) pauseButton.onClick.AddListener(TogglePause);
            if (speedButton != null) speedButton.onClick.AddListener(CycleSpeed);
            if (startWaveButton != null) startWaveButton.onClick.AddListener(StartWave);

            if (driver != null) driver.BattleStarted += HandleBattleStarted;
            RefreshLabels();
        }

        private void OnDestroy()
        {
            if (driver != null) driver.BattleStarted -= HandleBattleStarted;
        }

        private void HandleBattleStarted(Game.Battle.BattleSimulation simulation)
        {
            _speedIndex = 0;
            if (driver != null) driver.SpeedMultiplier = Speeds[_speedIndex];
            RefreshLabels();
        }

        private void Update()
        {
            if (driver == null || driver.Simulation == null) return;

            Game.Battle.BattleSimulation sim = driver.Simulation;
            bool preparing = !sim.Finished && (driver.AwaitingWaveStart || sim.InPrep);
            bool largeNext = preparing && sim.IsLargeWave(sim.Wave);

            if (goldText != null) goldText.text = sim.Gold.ToString();
            if (baseHpText != null) baseHpText.text = Mathf.CeilToInt(sim.BaseHp).ToString();
            if (waveText != null)
            {
                // 布防阶段若下一波为大型波,把波数标红作为提前预告。
                waveText.text = largeNext ? "WAVE " + sim.Wave + " · 大型" : "WAVE " + sim.Wave;
                waveText.color = largeNext ? Warn : Ink;
            }

            if (baseHpFill != null)
                baseHpFill.fillAmount = sim.BaseMaxHp > 0f ? Mathf.Clamp01(sim.BaseHp / sim.BaseMaxHp) : 0f;

            if (statusText != null)
            {
                if (sim.BaseDestroyed) statusText.text = "基地已失守";
                else if (sim.Finished) statusText.text = "全部波次完成";
                else if (preparing) statusText.text = largeNext ? "大型波来袭" : "布防阶段";
                else statusText.text = "交战中";
                statusText.color = largeNext ? Warn : Muted;
            }

            if (startWaveButton != null)
            {
                bool canStart = driver.AwaitingWaveStart;
                startWaveButton.interactable = canStart;
                if (startWaveLabel != null)
                    startWaveLabel.text = canStart ? "开始交战" : "交战中";
            }
        }

        private void StartWave()
        {
            if (driver != null) driver.StartWave();
        }

        private void TogglePause()
        {
            if (driver == null) return;
            driver.Paused = !driver.Paused;
            RefreshLabels();
        }

        private void CycleSpeed()
        {
            if (driver == null) return;
            _speedIndex = (_speedIndex + 1) % Speeds.Length;
            driver.SpeedMultiplier = Speeds[_speedIndex];
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (pauseLabel != null) pauseLabel.text = driver != null && driver.Paused ? "▶" : "II";
            if (speedLabel != null) speedLabel.text = "速度 ×" + Speeds[_speedIndex];
        }
    }
}
