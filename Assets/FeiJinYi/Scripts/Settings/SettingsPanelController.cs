// 设置面板：背景音乐/音效音量滑条、分辨率与屏幕模式下拉的界面交互与保存
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Text bgmValueText;
    [SerializeField] private Text sfxValueText;
    [SerializeField] private Dropdown resolutionDropdown;
    [SerializeField] private Dropdown screenModeDropdown;
    [SerializeField] private Button closeButton;

    // 屏幕显示模式选项，与 GameSettings 保存的 FullScreenMode 一一对应
    private static readonly string[] ScreenModeNames = { "全屏", "无边框窗口", "窗口" };
    private static readonly FullScreenMode[] ScreenModes =
    {
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.FullScreenWindow,
        FullScreenMode.Windowed
    };

    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    private void OnEnable()
    {
        // 面板每次打开都重挂事件、重新同步当前设置，避免重复监听
        bgmSlider.onValueChanged.RemoveAllListeners();
        bgmSlider.onValueChanged.AddListener(OnBgmChanged);
        sfxSlider.onValueChanged.RemoveAllListeners();
        sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        resolutionDropdown.onValueChanged.RemoveAllListeners();
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        screenModeDropdown.onValueChanged.RemoveAllListeners();
        screenModeDropdown.onValueChanged.AddListener(OnScreenModeChanged);
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(ClosePanel);

        RefreshUi();
    }

    // 把当前已保存的设置同步到界面控件
    private void RefreshUi()
    {
        AudioManager audio = AudioManager.EnsureInstance();

        bgmSlider.SetValueWithoutNotify(audio.BgmVolume);
        sfxSlider.SetValueWithoutNotify(audio.SfxVolume);
        bgmValueText.text = ToPercent(audio.BgmVolume);
        sfxValueText.text = ToPercent(audio.SfxVolume);

        resolutions.Clear();
        resolutions.AddRange(GameSettings.GetSupportedResolutions());
        var options = new List<Dropdown.OptionData>();
        foreach (Vector2Int r in resolutions)
            options.Add(new Dropdown.OptionData(r.x + " × " + r.y));
        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(options);
        Vector2Int saved = GameSettings.SavedResolution;
        int resIndex = resolutions.FindIndex(r => r.x == saved.x && r.y == saved.y);
        if (resIndex < 0)
        {
            Vector2Int closest = GameSettings.ClosestSupported(saved.x, saved.y);
            resIndex = resolutions.FindIndex(r => r.x == closest.x && r.y == closest.y);
        }
        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, resIndex));
        resolutionDropdown.RefreshShownValue();

        var modeOptions = new List<Dropdown.OptionData>();
        foreach (string name in ScreenModeNames)
            modeOptions.Add(new Dropdown.OptionData(name));
        screenModeDropdown.ClearOptions();
        screenModeDropdown.AddOptions(modeOptions);
        int modeIndex = System.Array.IndexOf(ScreenModes, GameSettings.SavedScreenMode);
        screenModeDropdown.SetValueWithoutNotify(Mathf.Max(0, modeIndex));
        screenModeDropdown.RefreshShownValue();
    }

    private void OnBgmChanged(float value)
    {
        AudioManager.EnsureInstance().BgmVolume = value;
        bgmValueText.text = ToPercent(value);
    }

    private void OnSfxChanged(float value)
    {
        AudioManager.EnsureInstance().SfxVolume = value;
        sfxValueText.text = ToPercent(value);
    }

    private void OnResolutionChanged(int index)
    {
        if (index < 0 || index >= resolutions.Count)
            return;
        GameSettings.SaveResolution(resolutions[index].x, resolutions[index].y);
        ApplyScreen();
    }

    private void OnScreenModeChanged(int index)
    {
        if (index < 0 || index >= ScreenModes.Length)
            return;
        GameSettings.SaveScreenMode(ScreenModes[index]);
        ApplyScreen();
    }

    // 按当前已保存的分辨率+屏幕模式应用到屏幕（编辑器内由 Game View 接管，打包后真实生效）
    private void ApplyScreen()
    {
        Vector2Int res = GameSettings.SavedResolution;
        GameSettings.ApplyResolution(res.x, res.y, GameSettings.SavedScreenMode);
    }

    private void ClosePanel()
    {
        // 播右滑出后失活（无动效组件时直接失活兜底）
        var slide = GetComponent<SecurityGuardSimulator.PanelHorizontalSlide>();
        if (slide != null) slide.Close();
        else gameObject.SetActive(false);
    }

    private static string ToPercent(float value)
    {
        return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
    }
}
