// 分支系统模型：结算对错判定（全局唯一口径）、按天结算历史（含生气/拥挤事实）、分支事件与生效内容解析器（纯函数，无运行状态）。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecurityGuardSimulator
{
    /// <summary>结算对错判定结果（放行/拦截 × 正确/错误）</summary>
    public enum DutyVerdict
    {
        CorrectRelease,   // 正确放行
        WrongRelease,     // 错误放行
        CorrectBlock,     // 正确拦截
        WrongBlock        // 错误拦截
    }

    /// <summary>
    /// 结算对错判定（全局唯一口径：结算面板行、按天历史、分支触发条件共用）
    /// 标准处置以访客表 CorrectOutcome 列为准（留空回退小偷特征推导）；被逼愤怒离开一律视为错误拦截
    /// </summary>
    public static class DutyVerdictEvaluator
    {
        /// <summary>判定一位访客的当日处理对错</summary>
        public static DutyVerdict GetVerdict(Visitor visitor, DutyOutcome outcome)
        {
            bool shouldBlock = visitor.ExpectedBlock ?? IsThief(visitor);
            switch (outcome)
            {
                case DutyOutcome.Released:
                case DutyOutcome.ReleasedByConsult:
                    return shouldBlock ? DutyVerdict.WrongRelease : DutyVerdict.CorrectRelease;
                case DutyOutcome.Fled:
                case DutyOutcome.BlockedAlert:
                    return shouldBlock ? DutyVerdict.CorrectBlock : DutyVerdict.WrongBlock;
                default: // LeftAngry：磨人磨到访客愤怒离开
                    return DutyVerdict.WrongBlock;
            }
        }

        /// <summary>以行为库特征判断小偷（心虚+证件过期且带工牌）</summary>
        public static bool IsThief(Visitor visitor)
        {
            return visitor.HasAttribute(VisitorAttribute.心虚)
                && visitor.HasAttribute(VisitorAttribute.证件过期)
                && visitor.HasAttribute(VisitorAttribute.有工牌);
        }
    }

    /// <summary>单访客的当日处理记录（分支条件判定的最小数据）</summary>
    [Serializable]
    public class DayHistoryRecord
    {
        public DayHistoryRecord(string visitorId, string visitorName, DutyOutcome outcome, DutyVerdict verdict, bool wasAngered)
        {
            VisitorId = visitorId;
            VisitorName = visitorName;
            Outcome = outcome;
            Verdict = verdict;
            WasAngered = wasAngered;
        }

        public string VisitorId { get; }
        public string VisitorName { get; }
        public DutyOutcome Outcome { get; }
        public DutyVerdict Verdict { get; }

        /// <summary>处理过程中是否被惹生气过（锁存：即使最终被安抚放行也记，与"逼走"区分）</summary>
        public bool WasAngered { get; }
    }

    /// <summary>一天的结算历史（星级 + 每位访客的处理对错 + 当天拥挤事实，结算时写入 GameDataManager）</summary>
    [Serializable]
    public class DayHistory
    {
        public DayHistory(int day, int stars, List<DayHistoryRecord> records, bool wasCrowded)
        {
            Day = day;
            Stars = stars;
            Records = records ?? new List<DayHistoryRecord>();
            WasCrowded = wasCrowded;
        }

        public int Day { get; }
        public int Stars { get; }
        public List<DayHistoryRecord> Records { get; }

        /// <summary>当天是否发生过前台拥挤（场上同时≥2位访客）</summary>
        public bool WasCrowded { get; }
    }

    /// <summary>分支访客级条件的结果值（逼走=被磨到愤怒离开；生气=被惹生气过、与最终结局无关）</summary>
    public enum BranchVerdict
    {
        正确放行,
        错误放行,
        正确拦截,
        错误拦截,
        逼走,
        生气
    }

    /// <summary>分支的当天状态条件</summary>
    public enum BranchDayState
    {
        当天有访客被逼走,
        当天有访客被惹生气,
        当天拥挤
    }

    /// <summary>分支事件（Excel 宽表一行：条件日满足条件 → 生效日改写全部非空效果列）</summary>
    [Serializable]
    public class BranchEvent
    {
        public BranchEvent(GameDataJSON.BranchEntry entry, int conditionDayNumber, int effectDayNumber,
            BranchVerdict? verdict1, BranchVerdict? verdict2, BranchDayState? dayState)
        {
            EventId = entry.EventId;
            ConditionDay = entry.ConditionDay;
            ConditionDayNumber = conditionDayNumber;
            Visitor1 = (entry.Visitor1 ?? "").Trim();
            Verdict1 = verdict1;
            Visitor2 = (entry.Visitor2 ?? "").Trim();
            Verdict2 = verdict2;
            DayState = dayState;
            EffectDay = entry.EffectDay;
            EffectDayNumber = effectDayNumber;
            AddVisitors = entry.AddVisitors != null ? new List<string>(entry.AddVisitors) : new List<string>();
            RemoveVisitors = entry.RemoveVisitors != null ? new List<string>(entry.RemoveVisitors) : new List<string>();
            ChatMessages = entry.ChatMessages != null
                ? new List<GameDataJSON.ChatMessage>(entry.ChatMessages)
                : new List<GameDataJSON.ChatMessage>();
            NewsTitle = (entry.NewsTitle ?? "").Trim();
            NewsContent = (entry.NewsContent ?? "").Trim();
            Feedback = (entry.Feedback ?? "").Trim();
        }

        public string EventId { get; }
        public string ConditionDay { get; }                  // 原文"第N天"（面板显示用）
        public int ConditionDayNumber { get; }               // 解析失败=-1（永不命中）
        public string Visitor1 { get; }                      // 条件访客1（空=无此条件）
        public BranchVerdict? Verdict1 { get; }
        public string Visitor2 { get; }                      // 条件访客2（空=无）
        public BranchVerdict? Verdict2 { get; }
        public BranchDayState? DayState { get; }             // 当天状态条件（null=不要求）
        public string EffectDay { get; }
        public int EffectDayNumber { get; }
        public List<string> AddVisitors { get; }             // 新增访客（排当天最前）
        public List<string> RemoveVisitors { get; }          // 删除访客（从原日程剔除）
        public List<GameDataJSON.ChatMessage> ChatMessages { get; }  // 新聊天（整块替换）
        public string NewsTitle { get; }                      // 新报纸标题（空=不改）
        public string NewsContent { get; }                    // 新报纸正文
        public string Feedback { get; }                       // 值班记录提示（空=无）

        public bool HasChat => ChatMessages != null && ChatMessages.Count > 0;
        public bool HasNews => !string.IsNullOrEmpty(NewsTitle);
        public bool HasVisitorChanges => (AddVisitors != null && AddVisitors.Count > 0) || (RemoveVisitors != null && RemoveVisitors.Count > 0);
        public bool HasFeedback => !string.IsNullOrEmpty(Feedback);

        /// <summary>条件摘要（面板显示用）</summary>
        public string DescribeConditions()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Visitor1) && Verdict1.HasValue) parts.Add($"{Visitor1}:{Verdict1.Value}");
            if (!string.IsNullOrEmpty(Visitor2) && Verdict2.HasValue) parts.Add($"{Visitor2}:{Verdict2.Value}");
            if (DayState.HasValue) parts.Add(DayState.Value.ToString());
            return parts.Count > 0 ? string.Join("；", parts) : "（无条件）";
        }

        /// <summary>效果摘要（面板显示用）</summary>
        public string DescribeEffect()
        {
            var parts = new List<string>();
            if (AddVisitors != null && AddVisitors.Count > 0) parts.Add($"新增 {string.Join("、", AddVisitors)}");
            if (RemoveVisitors != null && RemoveVisitors.Count > 0) parts.Add($"删除 {string.Join("、", RemoveVisitors)}");
            if (HasChat) parts.Add($"新聊天{ChatMessages.Count}条");
            if (HasNews) parts.Add($"新报纸「{NewsTitle}」");
            if (HasFeedback) parts.Add($"提示：{Feedback}");
            return string.Join("；", parts);
        }
    }

    /// <summary>生效日内容（分支改写后的当天实际数据；场景统一从这里读，不直接读日程表）</summary>
    [Serializable]
    public class EffectiveDayPlan
    {
        public List<string> VisitorIds = new List<string>();          // 当天访客出场序列（含新增/删除）
        public List<GameDataJSON.ChatMessage> ChatMessages = new List<GameDataJSON.ChatMessage>(); // 当天聊天（含替换）
        public string NewsTitle = "";                                 // 当天报纸标题（含替换）
        public string NewsContent = "";                               // 当天报纸正文（含替换）
        public List<string> FeedbackLines = new List<string>();       // 当天值班开始时的提示行
        public List<BranchEvent> ActiveEvents = new List<BranchEvent>(); // 当天命中的分支事件（应用顺序=表格行序）
    }

    /// <summary>分支解析器：输入分支表+按天历史+天数，输出该天生效内容（纯函数，场景与编辑器面板共用同一口径）</summary>
    public static class BranchResolver
    {
        /// <summary>解析"第N天"格式的天数，失败返回-1</summary>
        public static int ParseDayNumber(string dayLabel)
        {
            var text = dayLabel ?? "";
            int start = text.IndexOf('第');
            int end = text.IndexOf('天');
            if (start < 0 || end <= start + 1) return -1;
            var digits = text.Substring(start + 1, end - start - 1);
            return int.TryParse(digits, out int n) ? n : -1;
        }

        /// <summary>
        /// 计算第 day 天的生效内容：以日程表为基础，按表格行序应用全部命中分支（一行=一个完整分支）：
        /// 新增访客统一排在当天最前、删除访客从原日程剔除、新聊天/新报纸整块替换（后行覆盖前行）、值班提示按行序追加
        /// </summary>
        public static EffectiveDayPlan Resolve(List<BranchEvent> events, List<DayHistory> histories,
            int day, GameDataJSON.ScheduleDay baseDay)
        {
            var plan = new EffectiveDayPlan();
            if (baseDay != null)
            {
                if (baseDay.VisitorIds != null) plan.VisitorIds.AddRange(baseDay.VisitorIds);
                if (baseDay.ChatMessages != null) plan.ChatMessages.AddRange(baseDay.ChatMessages);
                plan.NewsTitle = baseDay.NewsTitle ?? "";
                plan.NewsContent = baseDay.NewsContent ?? "";
            }

            // 命中筛选：生效日=当天 且 条件日历史满足全部条件（访客结果对+当天状态，全为AND）
            var active = new List<BranchEvent>();
            if (events != null)
            {
                foreach (var e in events)
                {
                    if (e == null || e.EffectDayNumber != day) continue;
                    var condHistory = FindHistory(histories, e.ConditionDayNumber);
                    if (!ConditionsMatch(e, condHistory)) continue;
                    active.Add(e);
                }
            }
            plan.ActiveEvents.AddRange(active);

            // 按行序生效（新增访客先收集，最后统一排到最前，避免被删除类效果误删）
            var inserted = new List<string>();
            foreach (var e in active)
            {
                if (e.RemoveVisitors != null)
                {
                    foreach (var vid in e.RemoveVisitors)
                        plan.VisitorIds.RemoveAll(v => v == vid);
                }
                if (e.HasChat)
                    plan.ChatMessages = new List<GameDataJSON.ChatMessage>(e.ChatMessages);
                if (e.HasNews)
                {
                    plan.NewsTitle = e.NewsTitle;
                    plan.NewsContent = e.NewsContent;
                }
                if (e.HasFeedback)
                    plan.FeedbackLines.Add(e.Feedback);
                if (e.AddVisitors != null)
                    inserted.AddRange(e.AddVisitors);
            }
            if (inserted.Count > 0)
                plan.VisitorIds.InsertRange(0, inserted);

            return plan;
        }

        /// <summary>取文本首个完整句子（到首个句末标点、含标点；无标点返回原文），结算备注预演提示用</summary>
        public static string FirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int idx = text.IndexOfAny(new[] { '。', '！', '？', '!', '?' });
            return idx >= 0 ? text.Substring(0, idx + 1) : text;
        }

        private static DayHistory FindHistory(List<DayHistory> histories, int day)
        {
            if (histories == null) return null;
            return histories.Find(h => h != null && h.Day == day);
        }

        /// <summary>事件全部条件须在条件日历史上同时满足（访客结果对+当天状态，全为AND）</summary>
        private static bool ConditionsMatch(BranchEvent e, DayHistory condHistory)
        {
            bool hasVisitorCond1 = !string.IsNullOrEmpty(e.Visitor1) && e.Verdict1.HasValue;
            bool hasVisitorCond2 = !string.IsNullOrEmpty(e.Visitor2) && e.Verdict2.HasValue;
            if (!hasVisitorCond1 && !hasVisitorCond2 && !e.DayState.HasValue) return false;
            // 条件日没打过（无结算历史）->状态与结果都无从判定，不命中
            if (condHistory == null || condHistory.Records == null || condHistory.Records.Count == 0) return false;

            if (hasVisitorCond1 && !VisitorMatches(condHistory, e.Visitor1, e.Verdict1.Value)) return false;
            if (hasVisitorCond2 && !VisitorMatches(condHistory, e.Visitor2, e.Verdict2.Value)) return false;
            if (e.DayState.HasValue && !StateMatches(condHistory, e.DayState.Value)) return false;
            return true;
        }

        private static bool VisitorMatches(DayHistory history, string visitorId, BranchVerdict verdict)
        {
            foreach (var r in history.Records)
            {
                if (r != null && r.VisitorId == visitorId)
                    return VerdictMatches(r, verdict);
            }
            return false;
        }

        private static bool StateMatches(DayHistory history, BranchDayState state)
        {
            switch (state)
            {
                case BranchDayState.当天有访客被逼走:
                    foreach (var r in history.Records)
                        if (r != null && r.Outcome == DutyOutcome.LeftAngry) return true;
                    return false;
                case BranchDayState.当天有访客被惹生气:
                    foreach (var r in history.Records)
                        if (r != null && r.WasAngered) return true;
                    return false;
                case BranchDayState.当天拥挤:
                    return history.WasCrowded;
                default:
                    return false;
            }
        }

        private static bool VerdictMatches(DayHistoryRecord record, BranchVerdict verdict)
        {
            switch (verdict)
            {
                case BranchVerdict.正确放行: return record.Verdict == DutyVerdict.CorrectRelease;
                case BranchVerdict.错误放行: return record.Verdict == DutyVerdict.WrongRelease;
                case BranchVerdict.正确拦截: return record.Verdict == DutyVerdict.CorrectBlock;
                case BranchVerdict.错误拦截: return record.Verdict == DutyVerdict.WrongBlock;
                case BranchVerdict.逼走: return record.Outcome == DutyOutcome.LeftAngry;
                case BranchVerdict.生气: return record.WasAngered;
                default: return false;
            }
        }
    }
}
