// 规则数据模型：规则类别枚举与规则对象（请求类型、通过/拦截所需属性），供值班判定引擎结构化裁决。
using System;
using System.Collections.Generic;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 规则类别（放行/请示/警戒三类）
    /// </summary>
    public enum RuleCategory
    {
        放行,    // 放行类规则
        请示,    // 请示上级类规则
        警戒     // 警戒状态类规则
    }

    /// <summary>
    /// 规则数据
    /// 数据来源：策划在 Excel（规则数据表）配置 → Python工具转JSON → GameDataManager 加载构建
    /// 判定语义（值班引擎）：
    ///   发出请求（可选）→ 访客反馈行为 → 计算可见属性 →
    ///   放行类：可见属性包含全部"通过所需属性" → 放行
    ///   警戒类：可见属性出现任一"拦截属性" → 警戒状态（不放行）
    /// </summary>
    public class Rule
    {
        public Rule(string id, string name, string description, RuleCategory category,
            int slotCost, string checkAttribute, string sideEffect,
            GuardRequestType requestType, List<VisitorAttribute> passAttributes, List<VisitorAttribute> failAttributes)
        {
            ID = id;
            Name = name;
            Description = description;
            Category = category;
            SlotCost = slotCost;
            CheckAttribute = checkAttribute;
            SideEffect = sideEffect;
            RequestType = requestType;
            PassAttributes = passAttributes ?? new List<VisitorAttribute>();
            FailAttributes = failAttributes ?? new List<VisitorAttribute>();
        }

        public string ID { get; }
        public string Name { get; }                    // 规则名，如"工牌核验"
        public string Description { get; }             // 规则描述
        public RuleCategory Category { get; }          // 类别：放行/请示/警戒
        public int SlotCost { get; }                   // 槽位费用（脑容量占用）
        public string CheckAttribute { get; }          // 检查属性描述，如"是否携带工牌"（玩家可见）
        public string SideEffect { get; }              // 副作用（规则费用），无则为"无"或空

        // ===== 结构化判定 =====
        public GuardRequestType RequestType { get; }              // 执行此规则时发出的请求
        public List<VisitorAttribute> PassAttributes { get; }     // 通过所需可见属性（放行类）
        public List<VisitorAttribute> FailAttributes { get; }     // 拦截属性（警戒类）

        public bool HasSideEffect => !string.IsNullOrEmpty(SideEffect) && SideEffect != "无";

        /// <summary>
        /// 解析规则类别字符串（JSON中为中文："放行"/"请示"/"警戒"），解析失败返回false
        /// </summary>
        public static bool TryParseCategory(string text, out RuleCategory category)
        {
            switch (text?.Trim())
            {
                case "放行": category = RuleCategory.放行; return true;
                case "请示": category = RuleCategory.请示; return true;
                case "警戒": category = RuleCategory.警戒; return true;
                default: category = RuleCategory.放行; return false;
            }
        }

        /// <summary>
        /// 解析请求类型字符串（"请出示工牌"/"请勿遮挡时间信息"/"请示上级"/"请求切换"/"询问姓名"/"询问曾用名"/"请说普通话"/"无"），未识别按"无"处理并告警
        /// </summary>
        public static GuardRequestType ParseRequestType(string text, string ruleId)
        {
            switch (text?.Trim())
            {
                case "请出示工牌": return GuardRequestType.请出示工牌;
                case "请勿遮挡时间信息": return GuardRequestType.请勿遮挡时间信息;
                case "请示上级": return GuardRequestType.请示上级;
                case "请求切换": return GuardRequestType.请求切换;
                case "询问姓名": return GuardRequestType.询问姓名;
                case "询问曾用名": return GuardRequestType.询问曾用名;
                case "请说普通话": return GuardRequestType.请说普通话;
                case "无":
                case "":
                case null: return GuardRequestType.无;
                default:
                    UnityEngine.Debug.LogWarning($"[Rule] 规则 {ruleId} 的请求类型 \"{text}\" 无法识别，已按\"无\"处理。合法值：请出示工牌/请勿遮挡时间信息/请示上级/请求切换/询问姓名/询问曾用名/请说普通话/无");
                    return GuardRequestType.无;
            }
        }

        public override string ToString()
        {
            return $"[Rule] ID={ID}, Name={Name}, Category={Category}, SlotCost={SlotCost}";
        }
    }
}
