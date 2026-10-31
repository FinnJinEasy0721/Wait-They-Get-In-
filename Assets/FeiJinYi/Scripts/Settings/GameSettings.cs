// 玩家设置中心：音量/分辨率/屏幕模式的读取、保存与应用，无存档时用默认值
using System.Collections.Generic;
using UnityEngine;

public static class GameSettings
{
    // PlayerPrefs 键名（FJY 前缀避免与其他系统冲突）
    private const string KeyBgmVolume = "FJY_Settings_BgmVolume";
    private const string KeySfxVolume = "FJY_Settings_SfxVolume";
    private const string KeyResWidth = "FJY_Settings_ResWidth";
    private const string KeyResHeight = "FJY_Settings_ResHeight";
    private const string KeyScreenMode = "FJY_Settings_ScreenMode";

    // 默认值：背景音乐/音效全音量、2560x1440 全屏
    private const float DefaultVolume = 1f;
    private const int DefaultResWidth = 2560;
    private const int DefaultResHeight = 1440;
    private const FullScreenMode DefaultScreenMode = FullScreenMode.ExclusiveFullScreen;

    public static readonly Vector2Int DefaultResolution = new Vector2Int(DefaultResWidth, DefaultResHeight);

    // 背景音乐音量（0~1），写入即保存
    public static float BgmVolume
    {
        get { return PlayerPrefs.GetFloat(KeyBgmVolume, DefaultVolume); }
        set { PlayerPrefs.SetFloat(KeyBgmVolume, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    // 音效音量（0~1），写入即保存
    public static float SfxVolume
    {
        get { return PlayerPrefs.GetFloat(KeySfxVolume, DefaultVolume); }
        set { PlayerPrefs.SetFloat(KeySfxVolume, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    // 已保存的分辨率，无存档时返回默认值
    public static Vector2Int SavedResolution
    {
        get
        {
            if (!HasScreenSave)
                return DefaultResolution;
            return new Vector2Int(PlayerPrefs.GetInt(KeyResWidth, DefaultResWidth),
                PlayerPrefs.GetInt(KeyResHeight, DefaultResHeight));
        }
    }

    // 已保存的屏幕模式，无存档时返回默认全屏
    public static FullScreenMode SavedScreenMode
    {
        get { return (FullScreenMode)PlayerPrefs.GetInt(KeyScreenMode, (int)DefaultScreenMode); }
    }

    public static bool HasScreenSave
    {
        get { return PlayerPrefs.HasKey(KeyResWidth) && PlayerPrefs.HasKey(KeyResHeight); }
    }

    // 保存分辨率选择
    public static void SaveResolution(int width, int height)
    {
        PlayerPrefs.SetInt(KeyResWidth, width);
        PlayerPrefs.SetInt(KeyResHeight, height);
        PlayerPrefs.Save();
    }

    // 保存屏幕模式选择
    public static void SaveScreenMode(FullScreenMode mode)
    {
        PlayerPrefs.SetInt(KeyScreenMode, (int)mode);
        PlayerPrefs.Save();
    }

    // 游戏启动/回到主菜单时调用：按存档（无存档则按默认值）应用屏幕设置
    public static void ApplySavedOrDefault()
    {
        Vector2Int res = SavedResolution;
        ApplyResolution(res.x, res.y, SavedScreenMode);
    }

    // 应用分辨率与屏幕模式；目标分辨率本机不支持时自动落到最接近的支持档位
    public static void ApplyResolution(int width, int height, FullScreenMode mode)
    {
        Vector2Int closest = ClosestSupported(width, height);
        Screen.SetResolution(closest.x, closest.y, mode);
    }

    // 获取本机支持的分辨率列表：按宽高去重（保留最高刷新率），按面积从大到小排序
    public static List<Vector2Int> GetSupportedResolutions()
    {
        var unique = new Dictionary<Vector2Int, float>();
        foreach (Resolution r in Screen.resolutions)
        {
            Vector2Int size = new Vector2Int(r.width, r.height);
            if (!unique.ContainsKey(size) || (float)r.refreshRateRatio.value > unique[size])
                unique[size] = (float)r.refreshRateRatio.value;
        }
        var list = new List<Vector2Int>(unique.Keys);
        list.Sort((a, b) => { int area = b.x * b.y - a.x * a.y; return area != 0 ? area : b.x - a.x; });
        return list;
    }

    // 在支持列表中找最接近的分辨率；列表为空（编辑器等场景）时原样返回
    public static Vector2Int ClosestSupported(int width, int height)
    {
        List<Vector2Int> list = GetSupportedResolutions();
        if (list.Count == 0)
            return new Vector2Int(width, height);
        Vector2Int best = list[0];
        long bestDist = long.MaxValue;
        foreach (Vector2Int r in list)
        {
            long dw = r.x - width;
            long dh = r.y - height;
            long dist = dw * dw + dh * dh;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = r;
            }
        }
        return best;
    }

    // 清除全部玩家设置（恢复出厂状态）
    public static void ClearAll()
    {
        PlayerPrefs.DeleteKey(KeyBgmVolume);
        PlayerPrefs.DeleteKey(KeySfxVolume);
        PlayerPrefs.DeleteKey(KeyResWidth);
        PlayerPrefs.DeleteKey(KeyResHeight);
        PlayerPrefs.DeleteKey(KeyScreenMode);
        PlayerPrefs.Save();
    }
}
