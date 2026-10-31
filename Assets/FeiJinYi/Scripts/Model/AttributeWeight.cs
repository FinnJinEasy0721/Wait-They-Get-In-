// 属性权重数据模型：标签判定权重与识别来源解析，配套静态计分求值器，供权重制判定引擎与面板模拟器共用。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 属性权重（权重制判定核心数据）
    /// 判定语义：访客的每个可见标签按"保安是否认识该标签"取识别时/未识别时权重，求和后对照放行/警戒阈值。
    /// 识别来源（RecognizeSources）分号分隔，留空=天生认识（无需学习）：
    ///   装配:规则Id —— 当前装配着该规则时认识（能力唯一来源，每天重选）
    ///   新闻:第N天 / 投诉:标签 —— 仅解析保留数据兼容，运行时不再授予识别（能力不从新闻/差评获得）
    /// </summary>
    public class AttributeWeight
    {
        public AttributeWeight(VisitorAttribute tag, int recognizedWeight, int? unrecognizedWeight, string recognizeSources)
        {
            Tag = tag;
            RecognizedWeight = recognizedWeight;
            UnrecognizedWeight = unrecognizedWeight;
            RawSources = recognizeSources ?? "";
            AssemblySourceRuleIds = new List<string>();
            NewsSourceDays = new List<int>();
            ComplaintTriggerTags = new List<VisitorAttribute>();
            ParseSources(RawSources);
        }

        public VisitorAttribute Tag { get; }
        public int RecognizedWeight { get; }
        public int? UnrecognizedWeight { get; }          // null=未单独配置，用常量"未识别默认权重"
        public string RawSources { get; }

        public List<string> AssemblySourceRuleIds { get; }          // 装配来源：规则Id
        public List<int> NewsSourceDays { get; }                    // 新闻来源：生效天数
        public List<VisitorAttribute> ComplaintTriggerTags { get; } // 投诉来源：触发标签

        /// <summary>是否需要学习：配置了任何识别来源才算"需要学习"，否则天生认识</summary>
        public bool RequiresLearning =>
            AssemblySourceRuleIds.Count > 0 || NewsSourceDays.Count > 0 || ComplaintTriggerTags.Count > 0;

        private void ParseSources(string sources)
        {
            if (string.IsNullOrEmpty(sources)) return;
            foreach (var rawPart in sources.Replace("；", ";").Split(';'))
            {
                var part = rawPart.Trim();
                if (string.IsNullOrEmpty(part)) continue;

                if (part.StartsWith("装配:"))
                {
                    var ruleId = part.Substring("装配:".Length).Trim();
                    if (!string.IsNullOrEmpty(ruleId) && !AssemblySourceRuleIds.Contains(ruleId))
                        AssemblySourceRuleIds.Add(ruleId);
                    else if (string.IsNullOrEmpty(ruleId))
                        Debug.LogWarning($"[AttributeWeight] 标签 {Tag} 的装配来源 \"{part}\" 缺少规则Id");
                }
                else if (part.StartsWith("新闻:"))
                {
                    var dayText = part.Substring("新闻:".Length).Trim().Replace("第", "").Replace("天", "");
                    if (int.TryParse(dayText, out int day) && !NewsSourceDays.Contains(day))
                        NewsSourceDays.Add(day);
                    else
                        Debug.LogWarning($"[AttributeWeight] 标签 {Tag} 的新闻来源 \"{part}\" 无法解析天数");
                }
                else if (part.StartsWith("投诉:"))
                {
                    var tagText = part.Substring("投诉:".Length).Trim();
                    if (Enum.TryParse(tagText, out VisitorAttribute trigger) && !ComplaintTriggerTags.Contains(trigger))
                        ComplaintTriggerTags.Add(trigger);
                    else
                        Debug.LogWarning($"[AttributeWeight] 标签 {Tag} 的投诉触发标签 \"{tagText}\" 无法识别（需先扩充 VisitorAttribute 枚举）");
                }
                else
                {
                    Debug.LogWarning($"[AttributeWeight] 标签 {Tag} 的识别来源 \"{part}\" 语法不合法（应为 装配:规则Id / 新闻:第N天 / 投诉:标签）");
                }
            }
        }
    }

    /// <summary>单个可见标签的计分明细（判定引擎与面板模拟器共用）</summary>
    public class TagScoreDetail
    {
        public VisitorAttribute Tag;
        public bool Recognized;   // 保安是否识别该标签
        public int Weight;        // 实际取用的权重
    }

    /// <summary>
    /// 权重计分求值器（纯静态无状态）：输入权重配置+保安知识状态+可见标签，输出计分明细与总分。
    /// 运行时由 GameDataManager 用自身状态调用；编辑器面板模拟器用模拟状态调用，两处共用同一套计分逻辑。
    /// </summary>
    public static class WeightEvaluator
    {
        /// <summary>标签是否被识别：无权重行或无需学习=天生认识；已学（新闻/投诉）或装配来源命中=认识</summary>
        public static bool IsRecognized(AttributeWeight weight, HashSet<VisitorAttribute> learnedTags, HashSet<string> assemblyRuleIds)
        {
            if (weight == null) return true;                       // 无配置行：按0分天生认识
            if (!weight.RequiresLearning) return true;             // 来源留空：天生认识
            if (learnedTags != null && learnedTags.Contains(weight.Tag)) return true;
            if (weight.AssemblySourceRuleIds.Count > 0 && assemblyRuleIds != null)
            {
                foreach (var ruleId in weight.AssemblySourceRuleIds)
                {
                    if (assemblyRuleIds.Contains(ruleId)) return true;
                }
            }
            return false;
        }

        /// <summary>取标签实际权重：识别取识别权重；未识别取单独配置，未配置用常量兜底</summary>
        public static int GetWeight(AttributeWeight weight, bool recognized)
        {
            if (weight == null) return 0;
            if (recognized) return weight.RecognizedWeight;
            return weight.UnrecognizedWeight ?? GameConstants.UnrecognizedDefaultWeight;
        }

        /// <summary>
        /// 计算一组可见标签的计分明细与总分。
        /// weights：标签→权重配置；visibleTags：访客当前可见标签；learnedTags：已学会集合；assemblyRuleIds：当前装配规则Id
        /// </summary>
        public static List<TagScoreDetail> Evaluate(Dictionary<VisitorAttribute, AttributeWeight> weights,
            List<VisitorAttribute> visibleTags, HashSet<VisitorAttribute> learnedTags, HashSet<string> assemblyRuleIds,
            out int totalScore)
        {
            var details = new List<TagScoreDetail>();
            totalScore = 0;
            if (visibleTags == null) return details;

            foreach (var tag in visibleTags)
            {
                weights.TryGetValue(tag, out var weight);
                bool recognized = IsRecognized(weight, learnedTags, assemblyRuleIds);
                int w = GetWeight(weight, recognized);
                details.Add(new TagScoreDetail { Tag = tag, Recognized = recognized, Weight = w });
                totalScore += w;
            }
            return details;
        }

        /// <summary>按总分得到结局带：≥放行阈值→放行；≤警戒阈值→警戒拦截；中间→请示（磨人收尾）</summary>
        public static DutyScoreBand GetBand(int totalScore)
        {
            if (totalScore >= GameConstants.ReleaseThreshold) return DutyScoreBand.放行;
            if (totalScore <= GameConstants.AlertThreshold) return DutyScoreBand.警戒拦截;
            return DutyScoreBand.请示;
        }
    }

    /// <summary>总分结局带（权重制三带）</summary>
    public enum DutyScoreBand
    {
        放行,
        请示,
        警戒拦截
    }
}
