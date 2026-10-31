// 值班场景右上角：倍速切换按钮与游戏内设置面板（继续游戏/当天重开/存档返回主菜单），ESC 可开关该面板
using UnityEngine;
using UnityEngine.UI;

public class InGameSettingsController : MonoBehaviour
{
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button speedButton;
    [SerializeField] private Image speedIcon;
    [SerializeField] private Sprite speedNormalSprite;
    [SerializeField] private Sprite speedFastSprite;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button saveExitButton;

    private bool isFast = false;
    private SecurityGuardSimulator.DutyFlowController dutyFlow;

    private void Awake()
    {
        settingsButton.onClick.AddListener(OpenSettings);
        speedButton.onClick.AddListener(ToggleSpeed);
        continueButton.onClick.AddListener(ContinueGame);
        restartButton.onClick.AddListener(RestartToday);
        saveExitButton.onClick.AddListener(SaveAndReturnMenu);
        isFast = false;
        if (speedIcon != null && speedNormalSprite != null)
            speedIcon.sprite = speedNormalSprite;

        dutyFlow = FindObjectOfType<SecurityGuardSimulator.DutyFlowController>();
    }

    // ESC：面板未开则弹出（暂停值班），已开则等同"继续游戏"关闭并恢复原倍速
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePausePanel();
    }

    /// <summary>开关值班设置面板（ESC 与右上角按钮共用入口）</summary>
    public void TogglePausePanel()
    {
        if (settingsPanel != null && settingsPanel.activeSelf)
        {
            ContinueGame();
            return;
        }

        // 结算面板显示时不弹设置面板，避免与结算交互叠加
        if (dutyFlow == null)
            dutyFlow = FindObjectOfType<SecurityGuardSimulator.DutyFlowController>();
        if (dutyFlow != null && dutyFlow.IsSettlementShown) return;

        OpenSettings();
    }

    // 切换1倍/2倍速（TimeController驱动timeScale，值班演出全链路走缩放时间）
    private void ToggleSpeed()
    {
        isFast = !isFast;
        EnsureTimeController().SetSpeed(isFast ? 2f : 1f);
        if (speedIcon != null && speedFastSprite != null && speedNormalSprite != null)
            speedIcon.sprite = isFast ? speedFastSprite : speedNormalSprite;
    }

    // 打开设置面板并暂停值班
    private void OpenSettings()
    {
        EnsureTimeController().Pause();
        settingsPanel.SetActive(true);
    }

    // 继续游戏——关闭面板并按当前倍速恢复
    private void ContinueGame()
    {
        settingsPanel.SetActive(false);
        TimeController tc = TimeController.intance;
        if (tc != null)
        {
            if (isFast)
                tc.SetSpeed(2f);
            else
                tc.Resume();
        }
    }

    // 重新开始——当天从ReadNews开头重来（天数与前几天的历史保持不变）
    private void RestartToday()
    {
        SwitchScene("ReadNews");
    }

    // 保存并返回主菜单——记录当前天数，之后从主菜单"继续游戏"回到当天的ReadNews
    private void SaveAndReturnMenu()
    {
        GameSaveManager.SaveDay(SecurityGuardSimulator.GameDataManager.Instance.CurrentDay);
        SwitchScene("MainMenu");
    }

    // 带淡入淡出黑幕切换场景（当天重开/保存返回主菜单共用；无SceneSwitcher时由过渡层直接加载兜底）
    private void SwitchScene(string sceneName)
    {
        SecurityGuardSimulator.SceneFadeTransition.Switch(sceneName);
    }

    // 离开值班场景时恢复1倍速并解除暂停（倍速与暂停都不跨场景保留）
    private void OnDestroy()
    {
        if (TimeController.intance != null)
            TimeController.intance.Resume();
    }

    // 值班场景直接启动Play时场景里可能没有TimeController（正常流程由主菜单常驻），缺失则补建
    private TimeController EnsureTimeController()
    {
        if (TimeController.intance == null)
        {
            GameObject go = new GameObject("TimeController");
            go.AddComponent<TimeController>();
        }
        return TimeController.intance;
    }
}
