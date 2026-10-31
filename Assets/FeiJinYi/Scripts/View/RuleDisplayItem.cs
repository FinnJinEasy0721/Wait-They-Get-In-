// 规则条目组件：规则书面板内单条规则的名称/类别/属性展示与选择、单条刷新交互。
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 规则条目显示组件（规则书面板内的单条规则）
    /// 展示：规则名称、类别、槽位费用、检查属性、副作用
    /// 交互：选择/取消选中、单条定向刷新
    /// 所有校验（数量限制、脑容量）由 RuleBookPanel 统一执行
    /// </summary>
    public class RuleDisplayItem : MonoBehaviour
    {
        [Header("UI引用")]
        [SerializeField] private Text ruleNameText;           // 规则名称
        [SerializeField] private Text ruleCategoryText;       // 规则类别+槽位费用
        [SerializeField] private Text ruleAttributeText;       // 检查属性
        [SerializeField] private Text ruleSideEffectText;      // 副作用
        [SerializeField] private Button selectButton;          // 选择按钮
        [SerializeField] private Text selectButtonText;        // 选择按钮文字
        [SerializeField] private Button refreshButton;         // 刷新按钮
        [SerializeField] private Image backgroundImage;        // 条目背景（选中时变色）

        private Rule currentRule;
        private RuleBookPanel parentPanel;
        private bool isSelected;

        private static readonly Color SelectedColor = new Color(0.62f, 0.85f, 0.64f, 0.95f);

        public Rule RuleData => currentRule;
        public bool IsSelected => isSelected;

        /// <summary>初始化规则条目（面板重建列表时调用）</summary>
        public void Initialize(Rule rule, RuleBookPanel panel)
        {
            currentRule = rule;
            parentPanel = panel;
            isSelected = false;

            ApplyRuleToUI();
            BindEvents();
            ApplySelectionVisual();
        }

        /// <summary>刷新规则后更新显示（新规则默认未选中，需玩家重新选择）</summary>
        public void UpdateRule(Rule newRule)
        {
            currentRule = newRule;
            isSelected = false;

            ApplyRuleToUI();
            ApplySelectionVisual();
        }

        /// <summary>设置选中状态（仅负责视觉，校验逻辑在 RuleBookPanel）</summary>
        public void SetSelected(bool selected)
        {
            isSelected = selected;
            ApplySelectionVisual();
        }

        /// <summary>设置刷新按钮可用性（刷新次数用完时由面板统一关闭）</summary>
        public void SetRefreshInteractable(bool interactable)
        {
            if (refreshButton != null)
                refreshButton.interactable = interactable;
        }

        #region 内部实现

        private void ApplyRuleToUI()
        {
            if (currentRule == null) return;

            if (ruleNameText != null)
                ruleNameText.text = currentRule.Name;

            if (ruleCategoryText != null)
                ruleCategoryText.text = $"类别：{currentRule.Category} ｜ 槽位：{currentRule.SlotCost}";

            if (ruleAttributeText != null)
                ruleAttributeText.text = $"检查：{currentRule.CheckAttribute}";

            if (ruleSideEffectText != null)
                ruleSideEffectText.text = currentRule.HasSideEffect
                    ? $"副作用：{currentRule.SideEffect}"
                    : "副作用：无";
        }

        private void ApplySelectionVisual()
        {
            if (backgroundImage != null)
                backgroundImage.color = isSelected ? SelectedColor : Color.white;

            if (selectButtonText != null)
                selectButtonText.text = isSelected ? "已选" : "选择";
        }

        private void BindEvents()
        {
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(OnSelectClicked);
            }

            if (refreshButton != null)
            {
                refreshButton.onClick.RemoveAllListeners();
                refreshButton.onClick.AddListener(OnRefreshClicked);
            }
        }

        /// <summary>选择按钮：转发给面板做数量/容量校验</summary>
        private void OnSelectClicked()
        {
            if (parentPanel != null)
            {
                parentPanel.ToggleItemSelection(this);
            }
        }

        /// <summary>刷新按钮：转发给面板执行单条定向刷新</summary>
        private void OnRefreshClicked()
        {
            if (parentPanel != null)
            {
                parentPanel.TryRefreshItem(this);
            }
        }

        #endregion
    }
}
