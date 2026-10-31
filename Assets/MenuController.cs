// 主菜单控制：开始游戏（经SceneSwitcher切场景）、打开设置面板、退出游戏、存档续玩弹窗
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MenuController : MonoBehaviour
{
    // 开始游戏要切换到的场景名
    [SerializeField] private string startSceneName = "ReadNews";

    // 设置面板（主菜单内弹出，默认隐藏）
    [SerializeField] private GameObject settingsPanel;

    // 存档续玩弹窗（有存档时点开始游戏弹出，默认隐藏）
    [SerializeField] private GameObject savePromptPanel;

    // 继续游戏按钮下方的动态副标题（显示"从第N天继续"）
    [SerializeField] private Text continueSubText;

    private void Start()
    {
        // 进入主菜单时应用已保存的音量与屏幕设置（无存档则用默认值）
        GameSettings.ApplySavedOrDefault();
    }

    // 开始游戏——无存档直接进入ReadNews；有存档先弹"继续/重新开始"选择框（激活后自动播左滑入动效）
    public void StartGame()
    {
        if (GameSaveManager.HasSave && savePromptPanel != null)
        {
            if (continueSubText != null)
                continueSubText.text = $"从第 {GameSaveManager.SavedDay} 天继续";
            savePromptPanel.SetActive(true);
            return;
        }
        SwitchToStartScene();
    }

    // 继续游戏——从存档的天数继续（当天的ReadNews开头）；存档框滑出后再切场景
    public void ContinueGame()
    {
        EnsureGameData();
        SecurityGuardSimulator.GameDataManager.Instance.SetCurrentDay(GameSaveManager.SavedDay);
        CloseSavePromptThen(SwitchToStartScene);
    }

    // 重新开始——清除存档，从第1天开始；存档框滑出后再切场景
    public void RestartGame()
    {
        GameSaveManager.ClearSave();
        CloseSavePromptThen(SwitchToStartScene);
    }

    // 关闭存档选择框（返回主菜单；播右滑出后失活）
    public void CloseSavePrompt()
    {
        if (savePromptPanel != null)
        {
            var slide = savePromptPanel.GetComponent<SecurityGuardSimulator.PanelHorizontalSlide>();
            if (slide != null) slide.Close();
            else savePromptPanel.SetActive(false);
        }
    }

    // 关闭存档选择框（带滑出动效），滑出完成后执行后续动作（继续/重开按钮用）；面板未显示则立即执行
    private void CloseSavePromptThen(System.Action after)
    {
        if (savePromptPanel != null && savePromptPanel.activeSelf)
        {
            var slide = savePromptPanel.GetComponent<SecurityGuardSimulator.PanelHorizontalSlide>();
            if (slide != null)
            {
                slide.Close(after);
                return;
            }
        }
        after?.Invoke();
    }

    // 主菜单场景里没有GameDataManager，续玩需要先创建并把天数写进去
    private void EnsureGameData()
    {
        if (SecurityGuardSimulator.GameDataManager.Instance == null)
        {
            GameObject go = new GameObject("GameDataManager");
            go.AddComponent<SecurityGuardSimulator.GameDataManager>();
        }
    }

    // 带淡入淡出黑幕切换到开始场景（无SceneSwitcher时由过渡层直接加载兜底）
    private void SwitchToStartScene()
    {
        SecurityGuardSimulator.SceneFadeTransition.Switch(startSceneName);
    }

    // 退出游戏——关闭应用程序
    public void QuitGame()
    {
        Debug.Log("游戏已退出");
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // 设置按钮——打开设置面板
    public void OpenSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(true);
        else
            Debug.LogError("MenuController 未指定设置面板（settingsPanel 字段）");
    }
}
