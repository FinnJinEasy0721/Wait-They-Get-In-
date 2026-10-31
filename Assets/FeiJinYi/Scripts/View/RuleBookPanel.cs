// 规则书面板：展示候选规则、提供单条定向刷新与确认装配，校验脑容量槽位并向流程控制器广播确认事件。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 规则书面板
    /// 功能：展示候选规则（首次随机提供）、单条定向刷新（换出不同规则，次数-1）、
    ///       确认提交选中规则、刷新次数显示
    /// 面板由 SelectionFlowController 持有单实例并控制开关，本类只负责内容与交互
    /// </summary>
    public class RuleBookPanel : MonoBehaviour
    {
        [Header("UI引用")]
        [SerializeField] private Text panelTitleText;         // 面板标题
        [SerializeField] private RectTransform contentArea;   // 内容区域（规则条目父节点）
        [SerializeField] private Button confirmButton;        // 确认按钮
        [SerializeField] private Text refreshCountText;        // 刷新次数显示
        [SerializeField] private Text statusText;              // 状态/错误提示文本
        [SerializeField] private RuleDisplayItem ruleItemPrefab; // 规则条目Prefab
        [SerializeField] private Button closeButton;            // 关闭按钮（合上书本）

        private Book currentBook;
        private readonly List<RuleDisplayItem> displayItems = new List<RuleDisplayItem>();
        private int usedSlotsElsewhere;      // 其他书已占用槽位（容量校验用）
        private int totalSlots;              // 总脑容量槽位
        private bool isOpen;

        public Book BookData => currentBook;
        public bool IsOpen => isOpen;

        /// <summary>当前展示的规则条目（只读，供流程控制器/测试遍历）</summary>
        public IReadOnlyList<RuleDisplayItem> DisplayItems => displayItems;

        /// <summary>确认装配事件：参数为 本书 + 选中的规则列表（由流程控制器订阅）</summary>
        public event System.Action<Book, List<Rule>> OnConfirmed;

        /// <summary>请求关闭面板事件（由流程控制器订阅：取消书本选中并合上面板）</summary>
        public event System.Action OnCloseRequested;

        private void Awake()
        {
            // 绑定确认按钮（面板由控制器Instantiate生成，Awake在实例化时执行一次）
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveAllListeners();
                confirmButton.onClick.AddListener(Confirm);
            }

            // 绑定关闭按钮（不提交选中规则，仅上报请求，由流程控制器决策）
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(RequestClose);
            }
        }

        #region 开关与初始化

        /// <summary>
        /// 打开面板展示指定书籍的候选规则
        /// 候选规则由 Book.GetShownCandidates 随机抽取并在书本上保留（重开面板不重掷）
        /// </summary>
        public void Open(Book book, int usedSlotsElsewhere, int totalSlots)
        {
            currentBook = book;
            this.usedSlotsElsewhere = usedSlotsElsewhere;
            this.totalSlots = totalSlots;
            isOpen = true;

            if (panelTitleText != null)
                panelTitleText.text = $"{book.DisplayName} · 规则书";

            gameObject.SetActive(true);
            SetStatus(book.IsConfirmed ? "该书已装配过规则，可重新调整后再次确认" : "");
            RebuildDisplay();
            UpdateRefreshCountUI();
        }

        /// <summary>关闭面板（由流程控制器调用）</summary>
        public void Close()
        {
            isOpen = false;
            ClearDisplayItems();
            currentBook = null;
            gameObject.SetActive(false);
        }

        /// <summary>重建规则条目列表（每次打开时基于书本保留的候选规则）</summary>
        private void RebuildDisplay()
        {
            ClearDisplayItems();

            if (currentBook == null) return;

            List<Rule> candidates = currentBook.GetShownCandidates(GameConstants.RulesShownPerBookFirstTime);

            foreach (Rule rule in candidates)
            {
                RuleDisplayItem item = Instantiate(ruleItemPrefab, contentArea);
                item.Initialize(rule, this);
                // 调优重开：恢复该书之前已确认的规则选中状态
                if (currentBook.IsRuleConfirmed(rule))
                    item.SetSelected(true);;
                displayItems.Add(item);
            }

            if (displayItems.Count < GameConstants.RulesShownPerBookFirstTime)
                SetStatus($"该书规则池只有 {displayItems.Count} 条规则可展示");
        }

        #endregion

        #region 刷新（单条定向，次数-1）

        /// <summary>
        /// 刷新单条规则（由规则条目的刷新按钮调用）
        /// 新规则从该书规则池随机抽取，且不同于当前展示的所有候选规则
        /// </summary>
        public bool TryRefreshItem(RuleDisplayItem item)
        {
            if (currentBook == null || !isOpen) return false;

            int index = displayItems.IndexOf(item);
            if (index < 0) return false;

            if (!currentBook.HasRefreshCount)
            {
                SetStatus("刷新次数已用完");
                Debug.LogWarning("[RuleBookPanel] 刷新次数已用完");
                return false;
            }

            // 排除当前展示的全部候选规则，保证换出的规则"与其不一样"且不与其他条目重复
            var displayed = new List<Rule>();
            foreach (var di in displayItems)
                if (di.RuleData != null) displayed.Add(di.RuleData);

            Rule newRule = currentBook.RandomProvideRule(exclude: displayed);
            if (newRule == null)
            {
                SetStatus("这本书的规则池已没有其他规则可换");
                Debug.LogWarning("[RuleBookPanel] 规则池无其他规则可换，刷新取消");
                return false;
            }

            Rule oldRule = item.RuleData;
            currentBook.ReplaceShownCandidate(index, newRule);
            item.UpdateRule(newRule);   // 刷新后的规则需要玩家重新确认选择
            currentBook.DecrementRefreshCount();
            UpdateRefreshCountUI();
            SetStatus("");

            Debug.Log($"[RuleBookPanel] 刷新规则: {oldRule?.Name} -> {newRule.Name}（剩余刷新次数 {currentBook.RemainingRefreshCount}）");
            return true;
        }

        #endregion

        #region 规则选中（带数量限制与脑容量校验）

        /// <summary>
        /// 切换某条规则的选中状态（由规则条目的选择按钮调用）
        /// 本书内已选满 常量"每本书需选定规则数" 条时，勾选新规则自动替换已选条目（无需手动取消）；
        /// 容量校验：替换后 其他书占用 + 保留的已选费用 + 新规则费用 不得超过总槽位，不足则保持原选定
        /// </summary>
        public bool ToggleItemSelection(RuleDisplayItem item)
        {
            if (currentBook == null || !isOpen || item == null) return false;

            // 取消选中始终允许
            if (item.IsSelected)
            {
                item.SetSelected(false);
                SetStatus("");
                return true;
            }

            // 已选满时计算要被替换掉的条目（每本书1条时即当前已选的那条）
            int selectedCount = 0;
            foreach (var di in displayItems)
                if (di.IsSelected) selectedCount++;

            int slotsToFree = selectedCount + 1 - GameConstants.RulesToSelectPerBook;
            var toRemove = new List<RuleDisplayItem>();
            if (slotsToFree > 0)
            {
                foreach (var di in displayItems)
                {
                    if (di.IsSelected)
                    {
                        toRemove.Add(di);
                        if (toRemove.Count == slotsToFree) break;
                    }
                }
            }

            // 脑容量校验（被替换条目的费用已扣除）：其他书已用 + 保留的已选费用 + 本条规则费用 ≤ 总槽位
            var removed = new HashSet<RuleDisplayItem>(toRemove);
            int selectedCost = 0;
            foreach (var di in displayItems)
                if (di.IsSelected && !removed.Contains(di)) selectedCost += di.RuleData.SlotCost;

            int projected = usedSlotsElsewhere + selectedCost + item.RuleData.SlotCost;
            if (projected > totalSlots)
            {
                SetStatus($"脑容量不足：其他书已占用{usedSlotsElsewhere}槽位，本条规则还需{item.RuleData.SlotCost}槽位（总容量{totalSlots}）");
                Debug.LogWarning($"[RuleBookPanel] 脑容量不足，拦截选择：{item.RuleData.Name}（需{item.RuleData.SlotCost}槽位）");
                return false;   // 容量不足时不做任何替换，保持原选定
            }

            // 提交替换：先取消旧条目，再选中新条目
            foreach (var old in toRemove)
                old.SetSelected(false);

            item.SetSelected(true);

            if (toRemove.Count == 1)
                SetStatus($"已用「{item.RuleData.Name}」替换「{toRemove[0].RuleData.Name}」");
            else if (toRemove.Count > 1)
                SetStatus($"已选定「{item.RuleData.Name}」（替换了{toRemove.Count}条规则）");
            else
                SetStatus("");
            return true;
        }

        #endregion

        #region 确认（提交选中规则）

        /// <summary>
        /// 确认按钮：提交选中的规则（数量与容量双重校验）
        /// 一条都不选时确认 = 取消本书之前的装配（该书恢复未装配，槽位释放）
        /// </summary>
        public void Confirm()
        {
            if (currentBook == null || !isOpen) return;

            var selected = new List<Rule>();
            foreach (var item in displayItems)
            {
                if (item.IsSelected)
                    selected.Add(item.RuleData);
            }

            if (selected.Count > GameConstants.RulesToSelectPerBook)
            {
                SetStatus($"最多只能选定 {GameConstants.RulesToSelectPerBook} 条规则（当前已选 {selected.Count} 条）");
                Debug.LogWarning($"[RuleBookPanel] 确认被拦截：最多{GameConstants.RulesToSelectPerBook}条，实际{selected.Count}条");
                return;
            }

            // 未选任何规则：清空本书装配，重开面板不再显示"已选"
            if (selected.Count == 0)
            {
                currentBook.ConfirmRules(selected);
                Debug.Log($"[RuleBookPanel] 书籍 {currentBook.DisplayName} 未选定规则，取消装配");

                OnConfirmed?.Invoke(currentBook, selected);
                return;
            }

            int totalCost = usedSlotsElsewhere;
            foreach (var rule in selected) totalCost += rule.SlotCost;

            if (totalCost > totalSlots)
            {
                SetStatus($"脑容量不足：共需{totalCost}槽位，超出总容量{totalSlots}");
                return;
            }

            currentBook.ConfirmRules(selected);

            var names = new List<string>();
            foreach (var rule in selected) names.Add(rule.Name);
            Debug.Log($"[RuleBookPanel] 书籍 {currentBook.DisplayName} 确认装配: {string.Join("、", names)}");

            OnConfirmed?.Invoke(currentBook, selected);
            // 面板关闭由流程控制器在 OnConfirmed 处理中统一执行
        }

        /// <summary>关闭按钮：不提交选中规则，仅上报关闭请求（由流程控制器取消选中并合上面板）</summary>
        private void RequestClose()
        {
            OnCloseRequested?.Invoke();
        }

        #endregion

        #region 刷新次数显示

        /// <summary>更新刷新次数文本，并同步各条目刷新按钮可用状态</summary>
        private void UpdateRefreshCountUI()
        {
            if (refreshCountText != null && currentBook != null)
            {
                refreshCountText.text = $"刷新次数：{currentBook.RemainingRefreshCount}";
            }

            foreach (var item in displayItems)
            {
                if (item != null)
                    item.SetRefreshInteractable(currentBook != null && currentBook.HasRefreshCount);
            }
        }

        #endregion

        #region 辅助

        /// <summary>设置面板内提示文本（空串为清空）</summary>
        public void SetStatus(string text)
        {
            if (statusText != null)
                statusText.text = text;
        }

        private void ClearDisplayItems()
        {
            foreach (var item in displayItems)
            {
                if (item != null)
                    Destroy(item.gameObject);
            }
            displayItems.Clear();
        }

        private void OnDestroy()
        {
            ClearDisplayItems();
        }

        #endregion
    }
}
