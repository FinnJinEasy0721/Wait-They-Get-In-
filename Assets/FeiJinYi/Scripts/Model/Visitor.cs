// 访客数据模型：定义访客属性、保安请求类型枚举、访客行为类与访客对象（属性集与属性自测）。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 访客属性标签（离散可核实）
    /// 新增属性时需同步扩充此枚举，Excel中按中文名配置
    /// </summary>
    public enum VisitorAttribute
    {
        有工牌,
        无工牌,
        有制服,
        无制服,
        在名单内,
        在名单外,
        口音正常,
        口音异常,
        行为正常,
        行为异常,
        持证,
        证件过期,
        声称认识老板,
        // ===== 表情/工牌真假/证件时效 =====
        笑容,
        沮丧,
        心虚,
        工牌为真,
        工牌为假,
        证件有效,
        // ===== 需要保安学习的标签（配属性权重表的识别来源）=====
        新样式工牌,
        // ===== 登记类标签 =====
        完成访客登记,
        未完成登记,
        完成登记
    }

    /// <summary>
    /// 保安向访客发出的请求类型
    /// 规则表 RequestType 列配置；行为库 TriggerRequest 列触发
    /// </summary>
    public enum GuardRequestType
    {
        无,                    // 不发请求（直接观察判定）
        请出示工牌,
        请勿遮挡时间信息,     // 针对小偷的"遮挡时间信息"行为
        请示上级,             // 请示类规则触发
        放行,                  // 放行通知（触发访客"说谢谢"等行为）
        请求切换,             // 工牌类请求（原型请求细分；具体回应由行为库配置）
        询问姓名,             // 姓名类请求
        询问曾用名,           // 姓名类请求
        请说普通话            // 口音类请求
    }

    /// <summary>
    /// 访客行为
    /// 数据来源：访客行为库.xlsx → BehaviourConfig.json
    /// ActionName 决定程序效果；触发条件由请求和属性驱动
    /// </summary>
    [Serializable]
    public class VisitorBehaviour
    {
        public VisitorBehaviour(string id, string visitorId, string actionName,
            GuardRequestType triggerRequest, string triggerCondition, string responseText,
            List<VisitorAttribute> hideAttributes)
        {
            ID = id;
            VisitorID = visitorId;
            ActionName = actionName;
            TriggerRequest = triggerRequest;
            TriggerCondition = triggerCondition;
            ResponseText = responseText;
            HideAttributes = hideAttributes;
        }

        public string ID { get; }
        public string VisitorID { get; }
        public string ActionName { get; }             // 行为名（程序效果）：出示工牌/遮挡时间信息/逃跑/说谢谢/懵逼/愤怒/离开/解释工牌忘带
        public GuardRequestType TriggerRequest { get; } // 触发请求（无=仅按条件触发）
        public string TriggerCondition { get; }        // 触发条件原文（空=请求即触发）
        public string ResponseText { get; }             // 访客气泡回应文本
        public List<VisitorAttribute> HideAttributes { get; } // 行为发生时对保安屏蔽的可见属性

        /// <summary>解析触发条件里的属性自测（"需属性:标签1,标签2"形式），非该形式返回空列表</summary>
        public List<VisitorAttribute> RequiredAttributes()
        {
            var result = new List<VisitorAttribute>();
            if (string.IsNullOrEmpty(TriggerCondition)) return result;
            if (!TriggerCondition.StartsWith("需属性:")) return result;
            return Visitor.ParseAttributes(TriggerCondition.Substring("需属性:".Length).Split(','), VisitorID);
        }

        /// <summary>是否为懵逼计数条件（"计数:懵逼>=N"）</summary>
        public bool TryGetDazeCountCondition(out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(TriggerCondition) || !TriggerCondition.StartsWith("计数:懵逼>=")) return false;
            string numPart = TriggerCondition.Substring("计数:懵逼>=".Length);
            return int.TryParse(numPart, out count);
        }

        /// <summary>是否为请求计数条件（"计数:请求>=N"）</summary>
        public bool TryGetRequestCountCondition(out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(TriggerCondition) || !TriggerCondition.StartsWith("计数:请求>=")) return false;
            string numPart = TriggerCondition.Substring("计数:请求>=".Length);
            return int.TryParse(numPart, out count);
        }

        /// <summary>是否为状态条件（"状态:愤怒"）</summary>
        public bool IsAngryStateCondition()
        {
            return TriggerCondition == "状态:愤怒";
        }
    }

    /// <summary>
    /// 访客数据
    /// 数据来源：策划在 Excel（访客数据表）配置 → Python工具转JSON → GameDataManager 加载
    /// </summary>
    [Serializable]
    public class Visitor
    {
        public Visitor(string id, string name, List<VisitorAttribute> attributes,
            int dayUnlock = 1, int difficulty = 1, string correctOutcome = "")
        {
            ID = id;
            Name = name;
            Attributes = attributes ?? new List<VisitorAttribute>();
            DayUnlock = dayUnlock;
            Difficulty = difficulty;
            CorrectOutcome = correctOutcome ?? "";
        }

        public string ID { get; }
        public string Name { get; }
        public List<VisitorAttribute> Attributes { get; }    // 属性标签集合（含新列合并的表情/真假/时效）
        public int DayUnlock { get; }                         // 解锁天数（第几天开始可能出现）
        public int Difficulty { get; }                       // 难度评级
        public string CorrectOutcome { get; }                // 标准处置：放行/拦截（策划配置；空=按小偷特征推导）

        /// <summary>该访客的标准处置是否为拦截：null=策划未配置（回退小偷特征推导）</summary>
        public bool? ExpectedBlock
        {
            get
            {
                var outcome = CorrectOutcome?.Trim();
                if (outcome == "拦截") return true;
                if (outcome == "放行") return false;
                return null;
            }
        }

        public bool HasAttribute(VisitorAttribute attr) => Attributes.Contains(attr);

        public bool HasAllAttributes(params VisitorAttribute[] attrs)
        {
            foreach (var attr in attrs)
            {
                if (!Attributes.Contains(attr))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 解析属性字符串列表（JSON中为中文枚举名，如"有工牌"），无法识别的属性会被记录到日志并跳过
        /// </summary>
        public static List<VisitorAttribute> ParseAttributes(IEnumerable<string> attributeStrings, string visitorId)
        {
            var result = new List<VisitorAttribute>();
            foreach (var text in attributeStrings)
            {
                if (Enum.TryParse(text?.Trim(), out VisitorAttribute attr))
                {
                    if (!result.Contains(attr))
                        result.Add(attr);
                }
                else
                {
                    Debug.LogWarning($"[Visitor] 访客 {visitorId} 存在无法识别的属性 \"{text}\"，请检查访客数据表或扩充 VisitorAttribute 枚举");
                }
            }
            return result;
        }

        public override string ToString()
        {
            return $"[Visitor] ID={ID}, Name={Name}, Attributes={string.Join(",", Attributes)}";
        }
    }
}
