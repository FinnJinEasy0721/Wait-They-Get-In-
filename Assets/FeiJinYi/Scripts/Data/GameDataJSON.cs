// ============================================================
// GameDataJSON.cs
// 策划数据管线的数据模型 + 加载器（全项目唯一JSON模型定义）
//
// 数据流：策划编辑 Excel（Assets/GameData/Excel/*.xlsx）
//        → 运行 Excel转Unity数据工具.py 生成 JSON
//        → JSON 放置在 Assets/GameData/Resources/GameData/ 目录
//        → 运行时通过 Resources.Load 加载（本文件）
//
// 注意：
//   1. 各 Entry 类的字段名必须与 Excel 表头（第3行）完全一致，
//      否则 JsonUtility 反序列化不到数据 —— 修改表头需同步修改这里。
//   2. 游戏常量表使用键值对列表而非字典，因为 JsonUtility 不支持 Dictionary。
//   3. 分支事件表例外：Condition 列由导表工具解析为 Conditions 列表、Content 按效果类型
//      预解析为 ChatMessages/NewsTitle/NewsContent/VisitorIds 字段（Excel 列名与 JSON 字段不一一对应）。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 策划JSON配置加载器
    /// 通过 Resources.Load<TextAsset> 加载 "GameData/" 目录下的JSON并反序列化
    /// </summary>
    public static class GameDataJSON
    {
        /// <summary>JSON在Resources中的目录（对应 Assets/GameData/Resources/GameData/）</summary>
        public const string ResourceDir = "GameData/";

        // ===== 规则配置 =====

        [Serializable]
        public class RuleConfigJSON
        {
            public List<RuleEntry> rules;
        }

        [Serializable]
        public class RuleEntry
        {
            public string Id;
            public string Name;
            public string Description;
            public string Category;       // "放行"/"请示"/"警戒"
            public int SlotCost;
            public string CheckAttribute;
            public string SideEffect;
            public string RequestType;                 // 发出请求："请出示工牌"/"请勿遮挡时间信息"/"请示上级"/"无"
            public List<string> PassAttributes;       // 通过所需可见属性
            public List<string> FailAttributes;       // 拦截属性（警戒类）

            /// <summary>转换为运行时Rule对象，类别无法识别时按"放行"处理并告警</summary>
            public Rule ToRule()
            {
                if (!Rule.TryParseCategory(Category, out var category))
                {
                    Debug.LogWarning($"[GameDataJSON] 规则 {Id} 的类别 \"{Category}\" 无法识别，已按\"放行\"处理。合法值：放行/请示/警戒");
                }
                GuardRequestType requestType = Rule.ParseRequestType(RequestType, Id);
                var passAttrs = Visitor.ParseAttributes(PassAttributes, $"规则{Id}");
                var failAttrs = Visitor.ParseAttributes(FailAttributes, $"规则{Id}");
                return new Rule(Id, Name, Description, category, SlotCost, CheckAttribute, SideEffect,
                    requestType, passAttrs, failAttrs);
            }
        }

        /// <summary>加载规则配置</summary>
        public static RuleConfigJSON LoadRules()
        {
            return Load<RuleConfigJSON>("RuleConfig");
        }

        // ===== 书籍配置 =====

        [Serializable]
        public class BookConfigJSON
        {
            public List<BookEntry> books;
        }

        [Serializable]
        public class BookEntry
        {
            public string Id;
            public string DisplayName;
            public List<string> RuleIds;      // 该书包含的规则ID（逗号分隔→列表）
            public int MaxRefreshCount;
            public string CoverSpriteName;    // 封面图名（可选），从 Resources/GameData/BookCovers/ 加载
        }

        /// <summary>加载书籍配置</summary>
        public static BookConfigJSON LoadBooks()
        {
            return Load<BookConfigJSON>("BookConfig");
        }

        // ===== 访客配置 =====

        [Serializable]
        public class VisitorConfigJSON
        {
            public List<VisitorEntry> visitors;
        }

        [Serializable]
        public class VisitorEntry
        {
            public string Id;
            public string Name;
            public List<string> Attributes;   // 属性标签（逗号分隔→列表），取值见 VisitorAttribute 枚举
            public int DayUnlock;
            public int Difficulty;
            public string Expression;            // 表情：笑容/沮丧/心虚/无
            public string BadgeAuthenticity;     // 工牌真假：真/假/无
            public string BadgeExpiry;            // 证件时效：有效/过期/无
            public string CorrectOutcome;         // 标准处置：放行/拦截（留空=按小偷特征推导）

            /// <summary>转换为运行时Visitor对象（新列属性合并进属性标签集合）</summary>
            public Visitor ToVisitor()
            {
                var allAttrs = new List<string>(Attributes ?? new List<string>());
                if (!string.IsNullOrEmpty(Expression) && Expression != "无") allAttrs.Add(Expression);
                if (!string.IsNullOrEmpty(BadgeAuthenticity) && BadgeAuthenticity != "无")
                    allAttrs.Add(BadgeAuthenticity == "真" ? "工牌为真" : "工牌为假");
                if (!string.IsNullOrEmpty(BadgeExpiry) && BadgeExpiry != "无")
                    allAttrs.Add(BadgeExpiry == "过期" ? "证件过期" : "证件有效");

                var parsed = Visitor.ParseAttributes(allAttrs, Id);
                return new Visitor(Id, Name, parsed, DayUnlock, Difficulty, CorrectOutcome);
            }
        }

        /// <summary>加载访客配置</summary>
        public static VisitorConfigJSON LoadVisitors()
        {
            return Load<VisitorConfigJSON>("VisitorConfig");
        }

        // ===== 访客行为库配置 =====

        [Serializable]
        public class BehaviourConfigJSON
        {
            public List<BehaviourEntry> behaviours;
        }

        [Serializable]
        public class BehaviourEntry
        {
            public string Id;
            public string VisitorId;
            public string ActionName;          // 行为名（程序效果）
            public string TriggerRequest;      // 触发请求："请出示工牌"/"请勿遮挡时间信息"/"放行"/"请示上级"/"请求切换"/"询问姓名"/"询问曾用名"/"请说普通话"/"无"
            public string TriggerCondition;    // 触发条件：空/需属性:标签/计数:懵逼>=N/状态:愤怒
            public string ResponseText;        // 回应文本（访客气泡）
            public List<string> HideAttributes; // 行为发生时对保安屏蔽的可见属性

            /// <summary>转换为运行时行为对象</summary>
            public VisitorBehaviour ToBehaviour()
            {
                GuardRequestType request = ParseTriggerRequest(TriggerRequest, Id);
                var hide = Visitor.ParseAttributes(HideAttributes, $"行为{Id}");
                return new VisitorBehaviour(Id, VisitorId, ActionName ?? "", request,
                    TriggerCondition ?? "", ResponseText ?? "", hide);
            }

            private static GuardRequestType ParseTriggerRequest(string text, string behaviourId)
            {
                switch (text?.Trim())
                {
                    case "请出示工牌": return GuardRequestType.请出示工牌;
                    case "请勿遮挡时间信息": return GuardRequestType.请勿遮挡时间信息;
                    case "请示上级": return GuardRequestType.请示上级;
                    case "放行": return GuardRequestType.放行;
                    case "请求切换": return GuardRequestType.请求切换;
                    case "询问姓名": return GuardRequestType.询问姓名;
                    case "询问曾用名": return GuardRequestType.询问曾用名;
                    case "请说普通话": return GuardRequestType.请说普通话;
                    case "无":
                    case "":
                    case null: return GuardRequestType.无;
                    default:
                        Debug.LogWarning($"[GameDataJSON] 行为 {behaviourId} 的触发请求 \"{text}\" 无法识别，已按\"无\"处理");
                        return GuardRequestType.无;
                }
            }
        }

        /// <summary>加载访客行为库</summary>
        public static BehaviourConfigJSON LoadBehaviours()
        {
            return Load<BehaviourConfigJSON>("BehaviourConfig");
        }

        // ===== 属性权重配置（权重制判定核心表）=====

        [Serializable]
        public class AttributeWeightConfigJSON
        {
            public List<AttributeWeightEntry> weights;
        }

        [Serializable]
        public class AttributeWeightEntry
        {
            public string Tag;                   // 标签（VisitorAttribute 枚举中文名）
            public int RecognizedWeight;         // 识别时权重
            public string UnrecognizedWeight;     // 未识别时权重（空=用常量"未识别默认权重"）
            public string RecognizeSources;       // 识别来源：装配:规则Id / 新闻:第N天 / 投诉:标签，分号分隔

            /// <summary>转换为运行时权重对象：标签无法识别返回null并告警</summary>
            public AttributeWeight ToAttributeWeight()
            {
                if (!Enum.TryParse((Tag ?? "").Trim(), out VisitorAttribute tag))
                {
                    Debug.LogWarning($"[GameDataJSON] 属性权重表的标签 \"{Tag}\" 无法识别（需先扩充 VisitorAttribute 枚举再填表）");
                    return null;
                }
                int? unrec = null;
                var unrecText = (UnrecognizedWeight ?? "").Trim();
                if (!string.IsNullOrEmpty(unrecText))
                {
                    if (int.TryParse(unrecText, out int unrecValue))
                        unrec = unrecValue;
                    else
                        Debug.LogWarning($"[GameDataJSON] 标签 {Tag} 的未识别时权重 \"{unrecText}\" 不是整数，按留空处理（用常量兜底）");
                }
                return new AttributeWeight(tag, RecognizedWeight, unrec, RecognizeSources);
            }
        }

        /// <summary>加载属性权重表</summary>
        public static AttributeWeightConfigJSON LoadAttributeWeights()
        {
            return Load<AttributeWeightConfigJSON>("AttributeWeightConfig");
        }

        // ===== 分支事件配置（分支系统：某天结算结果触发另一天剧情变化；宽表：一行=一个完整分支）=====

        [Serializable]
        public class BranchConfigJSON
        {
            public List<BranchEntry> branches;
        }

        [Serializable]
        public class BranchEntry
        {
            public string EventId;                       // 事件Id（branch_001）
            public string ConditionDay;                  // 条件日"第N天"
            public string Visitor1;                      // 条件访客1（空=无此条件）
            public string Verdict1;                      // 条件1结果（正确放行/错误放行/正确拦截/错误拦截/逼走/生气）
            public string Visitor2;                      // 条件访客2（空=无）
            public string Verdict2;                      // 条件2结果（空=无）
            public string DayState;                      // 当天状态条件（3值，空=不要求）
            public string EffectDay;                     // 生效日"第N天"
            public List<string> AddVisitors;             // 新增访客（排当天最前）
            public List<string> RemoveVisitors;          // 删除访客（从原日程剔除）
            public List<ChatMessage> ChatMessages;       // 新聊天（整块替换）
            public string NewsTitle;                      // 新报纸标题
            public string NewsContent;                    // 新报纸正文
            public string Feedback;                       // 值班记录提示

            /// <summary>转换为运行时分支事件：结果/状态值无法识别或没有任何条件时返回null并告警</summary>
            public BranchEvent ToBranchEvent()
            {
                BranchVerdict? v1 = ParseVerdict(Verdict1, EventId, "结果1");
                BranchVerdict? v2 = ParseVerdict(Verdict2, EventId, "结果2");

                BranchDayState? state = null;
                var stateText = (DayState ?? "").Trim();
                if (stateText.Length > 0)
                {
                    if (!Enum.TryParse(stateText, out BranchDayState parsed))
                    {
                        Debug.LogWarning($"[GameDataJSON] 分支事件 {EventId} 的当天状态 \"{DayState}\" 无法识别，该行已跳过。合法值：当天有访客被逼走/当天有访客被惹生气/当天拥挤");
                        return null;
                    }
                    state = parsed;
                }

                // 至少要有一个条件（Python已校验，运行时兜底）
                if (v1 == null && v2 == null && state == null)
                {
                    Debug.LogWarning($"[GameDataJSON] 分支事件 {EventId} 没有任何触发条件，该行已跳过");
                    return null;
                }

                int condDay = BranchResolver.ParseDayNumber(ConditionDay);
                int effectDay = BranchResolver.ParseDayNumber(EffectDay);
                if (condDay < 1 || effectDay < 1)
                    Debug.LogWarning($"[GameDataJSON] 分支事件 {EventId} 的条件日/生效日无法解析（应为\"第N天\"）：\"{ConditionDay}\" → \"{EffectDay}\"，该事件永不命中");

                return new BranchEvent(this, condDay, effectDay, v1, v2, state);
            }

            /// <summary>解析结果值：空=无此条件(null)；无法识别=告警并返回null（调用处结合原文判空跳过）</summary>
            private static BranchVerdict? ParseVerdict(string text, string eventId, string field)
            {
                var t = (text ?? "").Trim();
                if (string.IsNullOrEmpty(t)) return null;
                if (Enum.TryParse(t, out BranchVerdict verdict)) return verdict;
                Debug.LogWarning($"[GameDataJSON] 分支事件 {eventId} 的{field} \"{text}\" 无法识别，该行已跳过。合法值：正确放行/错误放行/正确拦截/错误拦截/逼走/生气");
                return null;
            }
        }

        /// <summary>加载分支事件表</summary>
        public static BranchConfigJSON LoadBranches()
        {
            return Load<BranchConfigJSON>("BranchConfig");
        }

        // ===== 游戏常量 =====

        [Serializable]
        public class GameConstantsJSON
        {
            public List<ConstantEntry> constants;
        }

        [Serializable]
        public class ConstantEntry
        {
            public string key;     // 常量名（中文，与 GameConstants.ApplyFromJSON 中的映射一致）
            public string value;   // 常量值（统一以字符串存储，读取时按需转int/float）
        }

        /// <summary>加载游戏常量</summary>
        public static GameConstantsJSON LoadConstants()
        {
            return Load<GameConstantsJSON>("GameConstants");
        }

        // ===== 7天日程编排 =====

        [Serializable]
        public class ScheduleConfigJSON
        {
            public List<ScheduleDay> schedule;
        }

        [Serializable]
        public class ScheduleDay
        {
            public string Day;               // "第1天" ~ "第7天"
            public string CoreContent;       // 核心内容
            public List<string> VisitorIds;  // 当天访客ID（逗号分隔→列表）
            public string EventDescription;  // 事件描述
            public string PlayerTask;        // 玩家任务
            public string SpecialRule;       // 特殊规则
            public List<ChatMessage> ChatMessages;  // 手机聊天记录（ReadNews场景显示）
            public string ChatGroupName;                    // 手机群聊名称（ReadNews聊天面板顶部显示）
            public string NewsTitle;                  // 报纸标题（ReadNews场景显示，空=当天无报纸）
            public string NewsContent;                // 报纸正文

            /// <summary>该天是否有任何可看的资讯（聊天或报纸）</summary>
            public bool HasNewsOrChat =>
                (ChatMessages != null && ChatMessages.Count > 0) || !string.IsNullOrEmpty(NewsTitle);
        }

        /// <summary>单条聊天消息（日程表 ChatMessages 列："发送者:内容"）</summary>
        [Serializable]
        public class ChatMessage
        {
            public string Sender;  // 发送者
            public string Text;    // 消息内容
        }

        /// <summary>加载日程编排</summary>
        public static ScheduleConfigJSON LoadSchedule()
        {
            return Load<ScheduleConfigJSON>("ScheduleConfig");
        }

        // ===== 通用加载 =====

        /// <summary>加载并反序列化指定配置，文件缺失或解析失败时返回空配置并告警</summary>
        private static T Load<T>(string fileName) where T : class, new()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceDir + fileName);
            if (asset == null)
            {
                Debug.LogWarning($"[GameDataJSON] 配置文件缺失：Resources/{ResourceDir}{fileName}.json（策划请先运行 Excel转Unity数据工具.py 生成）");
                return new T();
            }

            try
            {
                T data = JsonUtility.FromJson<T>(asset.text);
                if (data == null)
                {
                    Debug.LogError($"[GameDataJSON] {fileName}.json 解析结果为空，请检查文件内容是否合法");
                    return new T();
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameDataJSON] {fileName}.json 解析异常：{e.Message}");
                return new T();
            }
        }
    }
}
