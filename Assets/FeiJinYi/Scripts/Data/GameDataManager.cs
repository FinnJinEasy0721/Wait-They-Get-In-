// ============================================================
// GameDataManager.cs
// 策划配置数据的运行时入口（单例）
//
// 数据流：Excel（Assets/GameData/Excel/）→ Python工具 → JSON
//        （Assets/GameData/Resources/GameData/）→ 本类Awake同步加载
//
// 使用方法：场景中放置挂载本组件的物体（或由 SelectionFlowController 自动创建），
//          Awake 中同步完成加载，其他系统在 Start 及之后直接访问数据。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 游戏数据管理器 - 单例模式
    /// 负责加载所有策划JSON配置，构建运行时对象（Book/Rule/Visitor），并提供访问入口
    /// </summary>
    public class GameDataManager : MonoBehaviour
    {
        public static GameDataManager Instance { get; private set; }

        // ===== 运行时数据（Awake加载后可用）=====

        /// <summary>所有规则（来自规则数据表）</summary>
        public List<Rule> Rules { get; private set; } = new List<Rule>();

        /// <summary>所有书籍（来自书籍数据表，已关联规则）</summary>
        public List<Book> Books { get; private set; } = new List<Book>();

        /// <summary>访客原始配置（来自访客数据表）</summary>
        public GameDataJSON.VisitorConfigJSON VisitorConfig { get; private set; }

        /// <summary>运行时访客对象（新列属性已合并进属性集）</summary>
        public List<Visitor> Visitors { get; private set; } = new List<Visitor>();

        /// <summary>访客行为库（访客ID → 行为列表）</summary>
        public Dictionary<string, List<VisitorBehaviour>> Behaviours { get; private set; }
            = new Dictionary<string, List<VisitorBehaviour>>();

        /// <summary>7天日程编排（来自7天日程编排表）</summary>
        public GameDataJSON.ScheduleConfigJSON ScheduleConfig { get; private set; }

        /// <summary>属性权重表（权重制判定核心，来自属性权重表.xlsx）</summary>
        public List<AttributeWeight> Weights { get; } = new List<AttributeWeight>();

        /// <summary>标签→权重配置索引</summary>
        public Dictionary<VisitorAttribute, AttributeWeight> WeightByTag { get; }
            = new Dictionary<VisitorAttribute, AttributeWeight>();

        /// <summary>分支事件表（分支系统：某天结算结果触发另一天的聊天/报纸/访客日程/值班播报变化）</summary>
        public List<BranchEvent> BranchEvents { get; private set; } = new List<BranchEvent>();

        /// <summary>已学标签集合（当前无任何授予来源，运行时恒为空；保留供判定器与编辑器面板接口兼容，识别能力只来自当日装配的规则书）</summary>
        public HashSet<VisitorAttribute> LearnedTags { get; } = new HashSet<VisitorAttribute>();

        // ===== 7天流程状态（跨场景持久；回到 MainMenu 时重置）=====

        /// <summary>当前值班天数（1~试用期天数）</summary>
        public int CurrentDay { get; private set; } = 1;

        /// <summary>按天结算历史（分支系统的条件判定数据源，结算时写入）</summary>
        public List<DayHistory> DayHistories { get; } = new List<DayHistory>();

        /// <summary>推进到下一天（日结算面板"下一天"按钮调用）</summary>
        public void AdvanceDay()
        {
            CurrentDay++;
            Debug.Log($"[GameDataManager] 推进到第 {CurrentDay} 天");
        }

        /// <summary>直接设置当前值班天数（存档续玩入口：继续游戏时从存档天数开始）</summary>
        public void SetCurrentDay(int day)
        {
            CurrentDay = Mathf.Max(1, day);
            Debug.Log($"[GameDataManager] 当前天数设置为第 {CurrentDay} 天（存档续玩）");
        }

        /// <summary>记录一天的结算历史（结算时调用；同一天重复记录以后者覆盖）</summary>
        public void RecordDayHistory(DayHistory history)
        {
            if (history == null) return;
            DayHistories.RemoveAll(h => h != null && h.Day == history.Day);
            DayHistories.Add(history);
        }

        /// <summary>计算第 day 天的生效内容：日程表为基底，叠加全部命中分支（聊天/报纸/访客序列/值班播报）</summary>
        public EffectiveDayPlan GetEffectiveDayPlan(int day)
        {
            return BranchResolver.Resolve(BranchEvents, DayHistories, day, GetDaySchedule(day));
        }

        /// <summary>预览某天的值班播报行（结算备注用：传入当日历史即可预演之后几天的播报，取每行首句）</summary>
        public List<string> PreviewFeedbackLines(int effectDay, DayHistory todayHistory)
        {
            var histories = DayHistories;
            if (todayHistory != null && !histories.Exists(h => h != null && h.Day == todayHistory.Day))
                histories = new List<DayHistory>(histories) { todayHistory };

            var plan = BranchResolver.Resolve(BranchEvents, histories, effectDay, null);
            var lines = new List<string>();
            foreach (var line in plan.FeedbackLines)
            {
                var sentence = BranchResolver.FirstSentence(line);
                if (!string.IsNullOrEmpty(sentence)) lines.Add(sentence);
            }
            return lines;
        }

        /// <summary>重置本轮值班状态（回到主菜单视为开始新一轮游戏）</summary>
        private void ResetRunState()
        {
            CurrentDay = 1;
            DayHistories.Clear();
            LearnedTags.Clear();
            Debug.Log("[GameDataManager] 回到主菜单，本轮值班状态已重置");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu")
                ResetRunState();
        }

        // 当前装配的规则Id集合（装配来源的识别判定用，值班开始时由 DutyFlowController 设置）
        private readonly HashSet<string> activeAssemblyRuleIds = new HashSet<string>();
        // 只告警一次的缺失权重标签
        private readonly HashSet<VisitorAttribute> warnedMissingWeights = new HashSet<VisitorAttribute>();

        public bool IsAllLoaded { get; private set; }

        /// <summary>所有配置加载完成事件（Awake末尾触发，早于任何Start）</summary>
        public event Action OnAllConfigsLoaded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            LoadAll();

            // 防止场景切换时物体被销毁导致数据丢失
            DontDestroyOnLoad(gameObject);

            // 回到主菜单时重置7天流程状态（视为开始新一轮游戏）
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>加载所有JSON配置并构建运行时对象</summary>
        private void LoadAll()
        {
            // 1. 加载并应用游戏常量（构建书籍时可能用到默认值兜底）
            GameConstants.ApplyFromJSON(GameDataJSON.LoadConstants());

            // 2. 规则
            var ruleConfig = GameDataJSON.LoadRules();
            Rules.Clear();
            if (ruleConfig.rules != null)
            {
                foreach (var entry in ruleConfig.rules)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                    Rules.Add(entry.ToRule());
                }
            }
            if (Rules.Count == 0)
                Debug.LogWarning("[GameDataManager] 规则数据为空，请检查规则数据表及JSON生成结果");

            // 3. 书籍（关联规则）
            Books = BuildBooks(GameDataJSON.LoadBooks());

            // 4. 访客（新列属性合并）+ 行为库 + 日程
            VisitorConfig = GameDataJSON.LoadVisitors();
            ScheduleConfig = GameDataJSON.LoadSchedule();
            BuildVisitorsAndBehaviours();

            // 5. 属性权重表（权重制判定）
            BuildWeights();

            // 6. 分支事件表（分支系统）
            BranchEvents = BuildBranches();

            IsAllLoaded = true;

            int scheduleCount = ScheduleConfig?.schedule?.Count ?? 0;
            int behaviourCount = 0;
            foreach (var kv in Behaviours) behaviourCount += kv.Value.Count;
            Debug.Log($"[GameDataManager] 所有配置数据加载完成：规则{Rules.Count}条，书籍{Books.Count}本，访客{Visitors.Count}个，行为{behaviourCount}条，日程{scheduleCount}天，权重{Weights.Count}条，分支{BranchEvents.Count}条");

            OnAllConfigsLoaded?.Invoke();
        }

        /// <summary>构建属性权重表索引（标签重复以后者为准并告警）</summary>
        private void BuildWeights()
        {
            Weights.Clear();
            WeightByTag.Clear();

            var weightConfig = GameDataJSON.LoadAttributeWeights();
            if (weightConfig.weights == null) return;

            foreach (var entry in weightConfig.weights)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Tag)) continue;
                var weight = entry.ToAttributeWeight();
                if (weight == null) continue;

                if (WeightByTag.ContainsKey(weight.Tag))
                {
                    Debug.LogWarning($"[GameDataManager] 属性权重表标签 \"{weight.Tag}\" 重复配置，后者覆盖前者");
                    Weights.RemoveAll(w => w.Tag == weight.Tag);
                }
                Weights.Add(weight);
                WeightByTag[weight.Tag] = weight;
            }

            if (Weights.Count == 0)
                Debug.LogWarning("[GameDataManager] 属性权重表为空，权重制判定将全部按0分处理，请检查属性权重表.xlsx及JSON生成结果");
        }

        /// <summary>构建分支事件列表（效果类型/判定值非法的行跳过并告警）</summary>
        private List<BranchEvent> BuildBranches()
        {
            var events = new List<BranchEvent>();
            var branchConfig = GameDataJSON.LoadBranches();
            if (branchConfig.branches != null)
            {
                foreach (var entry in branchConfig.branches)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.EventId)) continue;
                    var branchEvent = entry.ToBranchEvent();
                    if (branchEvent != null) events.Add(branchEvent);
                }
            }
            if (events.Count == 0)
                Debug.LogWarning("[GameDataManager] 分支事件表为空，各天将始终按日程表原样进行");
            return events;
        }

        // ===== 保安知识集合（权重制识别状态）=====

        /// <summary>设置当前装配的规则（值班开始时调用，装配来源的识别按此实时判定）</summary>
        public void SetActiveAssembly(IEnumerable<Rule> assembledRules)
        {
            activeAssemblyRuleIds.Clear();
            if (assembledRules == null) return;
            foreach (var rule in assembledRules)
            {
                if (rule != null) activeAssemblyRuleIds.Add(rule.ID);
            }
        }

        /// <summary>标签是否被保安识别（无权重行=天生认识按0分；装配来源命中=识别。能力唯一来源是当天装配的规则书）</summary>
        public bool IsRecognized(VisitorAttribute tag)
        {
            WeightByTag.TryGetValue(tag, out var weight);
            return WeightEvaluator.IsRecognized(weight, LearnedTags, activeAssemblyRuleIds);
        }

        /// <summary>当前装配规则Id快照（面板显示用）</summary>
        public IReadOnlyCollection<string> ActiveAssemblyRuleIds => activeAssemblyRuleIds;

        /// <summary>
        /// 计算可见标签的总分与计分明细（权重制判定入口；缺权重行的标签按0分并只告警一次）
        /// </summary>
        public List<TagScoreDetail> EvaluateScore(List<VisitorAttribute> visibleTags, out int totalScore)
        {
            foreach (var tag in visibleTags ?? new List<VisitorAttribute>())
            {
                if (!WeightByTag.ContainsKey(tag) && warnedMissingWeights.Add(tag))
                    Debug.LogWarning($"[GameDataManager] 标签 \"{tag}\" 未在属性权重表配置，按0分计（请补权重行）");
            }
            return WeightEvaluator.Evaluate(WeightByTag, visibleTags, LearnedTags, activeAssemblyRuleIds, out totalScore);
        }

        /// <summary>
        /// 构建运行时访客对象与行为库字典
        /// </summary>
        private void BuildVisitorsAndBehaviours()
        {
            Visitors.Clear();
            Behaviours.Clear();

            // 访客（含表情/工牌真假/证件时效新列合并）
            if (VisitorConfig?.visitors != null)
            {
                foreach (var entry in VisitorConfig.visitors)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                    Visitors.Add(entry.ToVisitor());
                }
            }

            // 行为库（按访客ID归组）
            var behaviourConfig = GameDataJSON.LoadBehaviours();
            if (behaviourConfig.behaviours != null)
            {
                foreach (var entry in behaviourConfig.behaviours)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Id) || string.IsNullOrEmpty(entry.VisitorId)) continue;

                    var behaviour = entry.ToBehaviour();
                    if (!Behaviours.TryGetValue(behaviour.VisitorID, out var list))
                    {
                        list = new List<VisitorBehaviour>();
                        Behaviours[behaviour.VisitorID] = list;
                    }
                    list.Add(behaviour);
                }
            }

            // 校验：日程引用的访客是否存在、有行为库的访客是否存在
            if (ScheduleConfig?.schedule != null)
            {
                foreach (var day in ScheduleConfig.schedule)
                {
                    if (day?.VisitorIds == null) continue;
                    foreach (var vid in day.VisitorIds)
                    {
                        if (GetVisitorById(vid) == null)
                            Debug.LogError($"[GameDataManager] {day.Day} 引用了不存在的访客ID：\"{vid}\"，请检查访客数据表与日程表");
                    }
                }
            }
        }

        /// <summary>
        /// 根据书籍JSON配置构建运行时Book对象：解析RuleIds关联规则、加载封面、刷新次数兜底
        /// </summary>
        private List<Book> BuildBooks(GameDataJSON.BookConfigJSON bookConfig)
        {
            var books = new List<Book>();
            if (bookConfig?.books == null) return books;

            foreach (var entry in bookConfig.books)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id))
                {
                    Debug.LogWarning("[GameDataManager] 跳过缺少Id的书籍配置项");
                    continue;
                }

                // 解析规则关联
                var rules = new List<Rule>();
                var missingIds = new List<string>();
                if (entry.RuleIds != null)
                {
                    foreach (var ruleId in entry.RuleIds)
                    {
                        Rule rule = GetRuleById(ruleId);
                        if (rule != null)
                            rules.Add(rule);
                        else
                            missingIds.Add(ruleId);
                    }
                }

                if (missingIds.Count > 0)
                    Debug.LogError($"[GameDataManager] 书籍 {entry.Id}({entry.DisplayName}) 引用了不存在的规则ID：{string.Join("、", missingIds)}，请检查规则数据表");

                if (rules.Count == 0)
                {
                    Debug.LogWarning($"[GameDataManager] 书籍 {entry.Id}({entry.DisplayName}) 没有可用规则，跳过该书籍");
                    continue;
                }

                // 刷新次数兜底
                int maxRefresh = entry.MaxRefreshCount > 0
                    ? entry.MaxRefreshCount
                    : GameConstants.DefaultMaxRefreshCount;

                // 封面（可选），从 Resources/GameData/BookCovers/ 加载
                Sprite cover = null;
                if (!string.IsNullOrEmpty(entry.CoverSpriteName))
                {
                    cover = Resources.Load<Sprite>($"{GameDataJSON.ResourceDir}BookCovers/{entry.CoverSpriteName}");
                    if (cover == null)
                        Debug.LogWarning($"[GameDataManager] 书籍 {entry.Id} 的封面 \"{entry.CoverSpriteName}\" 未找到（应位于 Resources/{GameDataJSON.ResourceDir}BookCovers/），将使用默认封面");
                }

                books.Add(new Book(entry.Id, entry.DisplayName, rules, maxRefresh, cover));
            }
            return books;
        }

        // ===== 数据访问接口 =====

        public Rule GetRuleById(string id)
        {
            return Rules.Find(r => r.ID == id);
        }

        public Book GetBookById(string id)
        {
            return Books.Find(b => b.ID == id);
        }

        public Visitor GetVisitorById(string id)
        {
            return Visitors.Find(v => v.ID == id);
        }

        /// <summary>获取指定访客的行为库（无配置返回空列表）</summary>
        public List<VisitorBehaviour> GetBehaviours(string visitorId)
        {
            if (Behaviours.TryGetValue(visitorId, out var list))
                return list;
            return new List<VisitorBehaviour>();
        }

        /// <summary>获取第dayNumber天的日程（无则返回null）</summary>
        public GameDataJSON.ScheduleDay GetDaySchedule(int dayNumber)
        {
            if (ScheduleConfig?.schedule == null) return null;
            string dayLabel = $"第{dayNumber}天";
            return ScheduleConfig.schedule.Find(s => s.Day == dayLabel);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
