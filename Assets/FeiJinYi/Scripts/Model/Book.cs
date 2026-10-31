// 书籍数据模型：规则池与候选规则随机提供、单条定向刷新、装配确认与槽位占用统计。
using System.Collections.Generic;
using UnityEngine;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 书籍/语料数据
    /// 功能：存储规则、被查阅、随机提供规则
    /// 数据来源：策划在 Excel（书籍数据表）配置 RuleIds → GameDataManager 解析关联规则后构建
    /// </summary>
    public class Book
    {
        public Book(string id, string displayName, List<Rule> availableRules,
            int maxRefreshCount, Sprite coverSprite = null)
        {
            ID = id;
            DisplayName = displayName;
            AvailableRules = availableRules ?? new List<Rule>();
            MaxRefreshCount = maxRefreshCount;
            CoverSprite = coverSprite;
            remainingRefreshCount = maxRefreshCount;
        }

        public string ID { get; }
        public string DisplayName { get; }             // 封面显示名称
        public Sprite CoverSprite { get; }             // 封面图标（可选，未配置时为null）
        public List<Rule> AvailableRules { get; }      // 该书可提供的规则池
        public int MaxRefreshCount { get; }            // 最大刷新次数

        // ===== 运行时状态（每次开局由 Initialize 重置）=====

        private readonly List<Rule> shownCandidates = new List<Rule>(); // 当前面板展示的候选规则（保留以便重开面板时不变）
        private List<Rule> confirmedRules = new List<Rule>();          // 已确认装配进保安规则书的规则
        private int remainingRefreshCount;

        public int RemainingRefreshCount => remainingRefreshCount;
        public bool HasRefreshCount => remainingRefreshCount > 0;
        public IReadOnlyList<Rule> ShownCandidates => shownCandidates;
        public IReadOnlyList<Rule> ConfirmedRules => confirmedRules;
        public bool IsConfirmed => confirmedRules.Count > 0;

        /// <summary>重置运行时状态（重新开局选书时调用）</summary>
        public void Initialize()
        {
            remainingRefreshCount = MaxRefreshCount;
            shownCandidates.Clear();
            confirmedRules = new List<Rule>();
        }

        /// <summary>
        /// 随机提供规则
        /// 可排除指定规则集合，用于"生成与其不一样的新规则"的刷新需求
        /// </summary>
        public Rule RandomProvideRule(ICollection<Rule> exclude = null)
        {
            if (AvailableRules == null || AvailableRules.Count == 0)
                return null;

            List<Rule> pool = new List<Rule>(AvailableRules);
            if (exclude != null)
                pool.RemoveAll(r => exclude.Contains(r));

            if (pool.Count == 0)
                return null;

            return pool[Random.Range(0, pool.Count)];
        }

        /// <summary>
        /// 获取面板展示的候选规则；首次调用时随机抽取 count 条互不重复的规则并保留
        /// 规则池不足 count 条时返回全部规则池
        /// </summary>
        public List<Rule> GetShownCandidates(int count)
        {
            if (shownCandidates.Count == 0)
            {
                List<Rule> pool = new List<Rule>(AvailableRules);
                for (int i = 0; i < count && pool.Count > 0; i++)
                {
                    int index = Random.Range(0, pool.Count);
                    shownCandidates.Add(pool[index]);
                    pool.RemoveAt(index);
                }
            }
            return new List<Rule>(shownCandidates);
        }

        /// <summary>替换第 index 条候选规则（刷新时使用）</summary>
        public void ReplaceShownCandidate(int index, Rule newRule)
        {
            if (index < 0 || index >= shownCandidates.Count) return;
            shownCandidates[index] = newRule;
        }

        /// <summary>减少刷新次数</summary>
        public void DecrementRefreshCount()
        {
            if (remainingRefreshCount > 0)
                remainingRefreshCount--;
        }

        /// <summary>确认装配规则（覆盖该书之前的装配结果，支持随时调优装卸）</summary>
        public void ConfirmRules(List<Rule> rules)
        {
            confirmedRules = new List<Rule>(rules);
        }

        /// <summary>该规则是否已被本书装配确认（调优重开面板时恢复选中状态用）</summary>
        public bool IsRuleConfirmed(Rule rule)
        {
            return confirmedRules.Contains(rule);
        }

        /// <summary>该书已装配规则占用的槽位数</summary>
        public int UsedSlots
        {
            get
            {
                int total = 0;
                foreach (var rule in confirmedRules)
                    total += rule.SlotCost;
                return total;
            }
        }

        public override string ToString()
        {
            return $"[Book] ID={ID}, DisplayName={DisplayName}, Rules={AvailableRules?.Count ?? 0}";
        }
    }
}
