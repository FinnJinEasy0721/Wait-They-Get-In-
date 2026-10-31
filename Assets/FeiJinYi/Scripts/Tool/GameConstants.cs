// 游戏常量配置：代码内写死默认值兜底，运行时由游戏常量表JSON覆盖，供各系统统一读取调参。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 游戏常量配置
    /// 默认值写死在代码中作为兜底；运行时由 GameDataManager 从 游戏常量.xlsx → GameConstants.json 加载并覆盖
    /// 策划只需编辑 Excel 中"常量名/值"两列，键名必须与 ApplyFromJSON 中的映射完全一致
    /// </summary>
    public static class GameConstants
    {
        // === 选书阶段 ===

        /// <summary>初始显示书籍数量（键：初始书本数量）</summary>
        public static int InitialBookCount = 3;

        /// <summary>首次每本书随机展示的候选规则数（键：首次每本书展示候选规则数）</summary>
        public static int RulesShownPerBookFirstTime = 2;

        /// <summary>每本书需要选定的规则数（键：每本书需选定规则数）</summary>
        public static int RulesToSelectPerBook = 1;

        /// <summary>保安初始脑容量槽位数（键：初始脑容量槽位）</summary>
        public static int InitialBrainSlots = 3;

        /// <summary>书本水平排列间距（键：书本排列间距）</summary>
        public static float BookSpawnSpacing = 120f;

        /// <summary>书籍默认刷新次数（书籍表未配置时的兜底值，键：默认刷新次数）</summary>
        public static int DefaultMaxRefreshCount = 3;

        // === 值班阶段（第二阶段预留）===

        /// <summary>每天最小访客数（键：每天最小访客数）</summary>
        public static int MinVisitorsPerDay = 1;

        /// <summary>每天最大访客数（键：每天最大访客数）</summary>
        public static int MaxVisitorsPerDay = 3;

        /// <summary>试用期天数（键：试用期天数）</summary>
        public static int TrialDays = 7;

        // === 值班阶段 ===

        /// <summary>访客生成间隔秒：开班第一位立即出场，之后每隔该秒数出下一位；场上空了且还有人→立即补位（键：访客生成间隔秒）</summary>
        public static float VisitorSpawnInterval = 10f;

        /// <summary>值班判定节奏秒：保安每条规则执行/请求/反馈的间隔（键：值班判定节奏秒）</summary>
        public static float DutyStepInterval = 1.2f;

        /// <summary>愤怒触发的懵逼累计次数（键：愤怒触发懵逼次数）</summary>
        public static int DazeCountForAnger = 3;

        /// <summary>访客移动速度：屏幕坐标单位/秒（键：访客移动速度）</summary>
        public static float VisitorMoveSpeed = 300f;

        /// <summary>值班速度倍率：>1加快值班流程（键：值班速度倍率）</summary>
        public static float DutySpeedMultiplier = 1f;

        // === 权重制判定（解法二：属性权重表）===

        /// <summary>放行阈值：访客总分≥此值放行（键：放行阈值）</summary>
        public static int ReleaseThreshold = 5;

        /// <summary>警戒阈值：访客总分≤此值警戒拦截（键：警戒阈值）</summary>
        public static int AlertThreshold = -5;

        /// <summary>未识别默认权重：标签未识别且权重表该格留空时的兜底分（键：未识别默认权重）</summary>
        public static int UnrecognizedDefaultWeight = -2;

        // === UI表现 ===

        /// <summary>呼吸灯频闪速度（键：呼吸灯速度）</summary>
        public static float BreathingEffectSpeed = 2f;

        /// <summary>悬停高亮程度0~1（键：悬停高亮系数）</summary>
        public static float HoverHighlightAmount = 0.25f;

        /// <summary>选中高亮程度0~1（键：选中高亮系数）</summary>
        public static float SelectedHighlightAmount = 0.45f;

        // ===== 从JSON应用（由 GameDataManager 调用）=====

        // 策划自定义的额外键值对（GetInt/GetFloat 读取）
        private static readonly Dictionary<string, string> extraConstants = new Dictionary<string, string>();

        /// <summary>
        /// 将常量JSON应用到静态字段。识别的键见各字段注释；未识别的键存入附加表供 GetInt/GetFloat 读取
        /// </summary>
        public static void ApplyFromJSON(GameDataJSON.GameConstantsJSON config)
        {
            extraConstants.Clear();
            if (config?.constants == null) return;

            int applied = 0;
            foreach (var entry in config.constants)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                string key = entry.key.Trim();
                string value = entry.value?.Trim() ?? "";

                switch (key)
                {
                    case "初始书本数量":
                        if (TryParseInt(key, value, out int bookCount)) { InitialBookCount = bookCount; applied++; }
                        break;
                    case "首次每本书展示候选规则数":
                        if (TryParseInt(key, value, out int shown)) { RulesShownPerBookFirstTime = shown; applied++; }
                        break;
                    case "每本书需选定规则数":
                        if (TryParseInt(key, value, out int toSelect)) { RulesToSelectPerBook = toSelect; applied++; }
                        break;
                    case "初始脑容量槽位":
                        if (TryParseInt(key, value, out int slots)) { InitialBrainSlots = slots; applied++; }
                        break;
                    case "默认刷新次数":
                        if (TryParseInt(key, value, out int refresh)) { DefaultMaxRefreshCount = refresh; applied++; }
                        break;
                    case "每天最小访客数":
                        if (TryParseInt(key, value, out int minV)) { MinVisitorsPerDay = minV; applied++; }
                        break;
                    case "每天最大访客数":
                        if (TryParseInt(key, value, out int maxV)) { MaxVisitorsPerDay = maxV; applied++; }
                        break;
                    case "试用期天数":
                        if (TryParseInt(key, value, out int trial)) { TrialDays = trial; applied++; }
                        break;
                    case "愤怒触发懵逼次数":
                        if (TryParseInt(key, value, out int daze)) { DazeCountForAnger = daze; applied++; }
                        break;
                    case "放行阈值":
                        if (TryParseInt(key, value, out int release)) { ReleaseThreshold = release; applied++; }
                        break;
                    case "警戒阈值":
                        if (TryParseInt(key, value, out int alert)) { AlertThreshold = alert; applied++; }
                        break;
                    case "未识别默认权重":
                        if (TryParseInt(key, value, out int unrecWeight)) { UnrecognizedDefaultWeight = unrecWeight; applied++; }
                        break;
                    default:
                        if (key == "书本排列间距")
                        {
                            if (TryParseFloat(key, value, out float spacing)) { BookSpawnSpacing = spacing; applied++; }
                        }
                        else if (key == "访客生成间隔秒")
                        {
                            if (TryParseFloat(key, value, out float interval)) { VisitorSpawnInterval = interval; applied++; }
                        }
                        else if (key == "值班判定节奏秒")
                        {
                            if (TryParseFloat(key, value, out float duty)) { DutyStepInterval = duty; applied++; }
                        }
                        else if (key == "访客移动速度")
                        {
                            if (TryParseFloat(key, value, out float speed)) { VisitorMoveSpeed = speed; applied++; }
                        }
                        else if (key == "值班速度倍率")
                        {
                            if (TryParseFloat(key, value, out float mult))
                            {
                                DutySpeedMultiplier = mult > 0.01f ? mult : 1f;
                                applied++;
                            }
                        }
                        else if (key == "呼吸灯速度")
                        {
                            if (TryParseFloat(key, value, out float speed)) { BreathingEffectSpeed = speed; applied++; }
                        }
                        else if (key == "悬停高亮系数")
                        {
                            if (TryParseFloat(key, value, out float hover)) { HoverHighlightAmount = hover; applied++; }
                        }
                        else if (key == "选中高亮系数")
                        {
                            if (TryParseFloat(key, value, out float selected)) { SelectedHighlightAmount = selected; applied++; }
                        }
                        else
                        {
                            // 未识别的键保留在附加表，供系统扩展使用
                            extraConstants[key] = value;
                            Debug.LogWarning($"[GameConstants] 未识别的常量键 \"{key}\"，已存入附加表（可用 GameConstants.GetString/GetInt 读取）");
                        }
                        break;
                }
            }
            Debug.Log($"[GameConstants] 常量应用完成：{applied} 个已知键，{extraConstants.Count} 个附加键");
        }

        /// <summary>读取附加常量（策划自定义键）</summary>
        public static bool TryGetString(string key, out string value)
        {
            return extraConstants.TryGetValue(key, out value);
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            return TryGetString(key, out var v) && int.TryParse(v, out int result) ? result : defaultValue;
        }

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            return TryGetString(key, out var v) && float.TryParse(v, out float result) ? result : defaultValue;
        }

        private static bool TryParseInt(string key, string value, out int result)
        {
            if (int.TryParse(value, out result)) return true;
            Debug.LogWarning($"[GameConstants] 常量 \"{key}\" 的值 \"{value}\" 不是整数，保留默认值");
            return false;
        }

        private static bool TryParseFloat(string key, string value, out float result)
        {
            if (float.TryParse(value, out result)) return true;
            Debug.LogWarning($"[GameConstants] 常量 \"{key}\" 的值 \"{value}\" 不是数字，保留默认值");
            return false;
        }
    }
}
