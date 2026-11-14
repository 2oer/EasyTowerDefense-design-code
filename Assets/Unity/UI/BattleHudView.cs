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
        [SerializeField] private Text pauseLabel;
        [SerializeField] private Text speedLabel;

        private int _speedIndex;

        private static readonly int[] Speeds = { 1, 2, 3 };

        private void Start()
        {
            if (driver == null) driver = FindObjectOfType<BattleDriver>();

            if (pauseButton != null) pauseButton.onClick.AddListener(TogglePause);
            if (speedButton != null) speedButton.onClick.AddListener(CycleSpeed);

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
            if (goldText != null) goldText.text = sim.Gold.ToString();
            if (baseHpText != null) baseHpText.text = Mathf.CeilToInt(sim.BaseHp).ToString();
            if (waveText != null) waveText.text = "WAVE " + sim.Wave;

            if (baseHpFill != null)
                baseHpFill.fillAmount = sim.BaseMaxHp > 0f ? Mathf.Clamp01(sim.BaseHp / sim.BaseMaxHp) : 0f;

            if (statusText != null)
            {
                if (sim.BaseDestroyed) statusText.text = "基地已失守";
                else if (sim.Finished) statusText.text = "全部波次完成";
                else statusText.text = sim.InPrep ? "部署阶段" : "交战中";
            }
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
            if (pauseLabel != null) pauseLabel.text = driver != null && driver.Paused ? "继续" : "暂停";
            if (speedLabel != null) speedLabel.text = "速度 ×" + Speeds[_speedIndex];
        }
    }
}
