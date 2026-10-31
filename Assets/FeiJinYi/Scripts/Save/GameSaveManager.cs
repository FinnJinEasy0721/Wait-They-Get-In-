// 玩家进度存档：只存当前值班天数，主菜单据此提供"继续游戏"
using UnityEngine;

public static class GameSaveManager
{
    // PlayerPrefs 键名
    private const string KeyDay = "FJY_Save_Day";

    // 是否存在存档
    public static bool HasSave
    {
        get { return PlayerPrefs.HasKey(KeyDay); }
    }

    // 存档的天数（无存档时为1）
    public static int SavedDay
    {
        get { return PlayerPrefs.GetInt(KeyDay, 1); }
    }

    // 保存当前所处天数
    public static void SaveDay(int day)
    {
        PlayerPrefs.SetInt(KeyDay, Mathf.Max(1, day));
        PlayerPrefs.Save();
    }

    // 清除存档（重新开始用）
    public static void ClearSave()
    {
        PlayerPrefs.DeleteKey(KeyDay);
        PlayerPrefs.Save();
    }
}
