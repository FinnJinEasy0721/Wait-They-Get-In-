// 书本场景组件：负责封面视觉与悬停/选中/确认三态表现，点击只上报，决策统一由 SelectionFlowController 管理。
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 员工手册游戏物体组件
    /// 交互：悬停、选中、未选三种状态
    /// 书本只负责视觉状态与点击上报，选中决策与面板开关统一由 SelectionFlowController 管理
    /// </summary>
    public class BookGameObject : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerClickHandler
    {
        [Header("视觉引用")]
        [SerializeField] private Image bookImage;             // 书本封面图片
        [SerializeField] private Outline bookOutline;         // 书本边缘线（选中时呼吸灯）
        [SerializeField] private GameObject highlightPanel;   // 高亮背景（底部图案背景）
        [SerializeField] private Text bookNameText;           // 书名文字（封面名称）
        [SerializeField] private Text bookStatusText;         // 装配状态文字

        private Book bookData;
        private SelectionFlowController controller;
        private bool isSelected;
        private bool isConfirmed;
        private Coroutine breathingCoroutine;
        private Color baseCoverColor;
        private Color baseNameColor;          // 书名原始颜色（取消装配时恢复）

        public Book BookData => bookData;
        public bool IsSelected => isSelected;
        public bool IsConfirmed => isConfirmed;

        /// <summary>初始化书本数据与视觉（生成后由 SelectionFlowController 调用）</summary>
        public void Initialize(Book data, SelectionFlowController owner)
        {
            bookData = data;
            controller = owner;

            if (bookNameText != null)
            {
                bookNameText.text = data.DisplayName;
                baseNameColor = bookNameText.color;
            }

            if (bookStatusText != null)
                bookStatusText.text = "未装配";

            if (bookImage != null)
            {
                if (data.CoverSprite != null)
                {
                    bookImage.sprite = data.CoverSprite;
                    // 有封面图时恢复原色显示，避免被封面底色染色
                    bookImage.color = Color.white;
                }
                baseCoverColor = bookImage.color;
            }

            // 封面图自带书名，配置了封面时隐藏叠加的书名文字避免重影
            if (bookNameText != null)
                bookNameText.gameObject.SetActive(data.CoverSprite == null);

            SetUnselectedState();
        }

        #region 交互实现

        /// <summary>
        /// 悬停：未选中时高亮书本/显示底部图案背景
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (highlightPanel != null)
                highlightPanel.SetActive(true);

            if (!isSelected)
                ApplyHighlight(GameConstants.HoverHighlightAmount);
        }

        /// <summary>移出书本：未选中时恢复普通外观</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (highlightPanel != null && !isSelected)
                highlightPanel.SetActive(false);

            if (!isSelected)
                ResetHighlight();
        }

        /// <summary>
        /// 点击选中；选中状态下再次点击=取消
        /// 书本不做状态决策，统一转发给流程控制器
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (controller != null)
                controller.OnBookClicked(this);
        }

        #endregion

        #region 状态管理（由流程控制器驱动）

        /// <summary>选中视觉：书本边缘线围绕+呼吸灯频闪</summary>
        public void SetSelectedVisual(bool selected)
        {
            if (isSelected == selected) return;

            isSelected = selected;

            if (isSelected)
            {
                if (bookOutline != null)
                    bookOutline.enabled = true;

                if (highlightPanel != null)
                    highlightPanel.SetActive(true);

                ApplyHighlight(GameConstants.SelectedHighlightAmount);

                // 修复点：呼吸灯必须通过 StartCoroutine 启动，否则永远不生效
                if (bookOutline != null && breathingCoroutine == null)
                    breathingCoroutine = StartCoroutine(BreathingRoutine());
            }
            else
            {
                StopBreathing();
                SetUnselectedState();
            }
        }

        /// <summary>
        /// 已装配视觉：书本显示装配完成的规则名
        /// </summary>
        public void SetConfirmedVisual(string ruleSummary)
        {
            isConfirmed = true;

            if (bookStatusText != null)
                bookStatusText.text = $"已装配：{ruleSummary}";

            if (bookNameText != null)
                bookNameText.color = new Color(0.55f, 0.95f, 0.6f);
        }

        /// <summary>
        /// 取消装配视觉：恢复"未装配"状态（状态文字与书名颜色复位）
        /// </summary>
        public void SetUnconfirmedVisual()
        {
            if (bookStatusText != null)
                bookStatusText.text = "未装配";

            if (bookNameText != null)
                bookNameText.color = baseNameColor;

            isConfirmed = false;
        }

        private void SetUnselectedState()
        {
            isSelected = false;
            StopBreathing();

            if (bookOutline != null)
                bookOutline.enabled = false;

            if (highlightPanel != null && !isConfirmed)
                highlightPanel.SetActive(false);

            ResetHighlight();
        }

        private void StopBreathing()
        {
            if (breathingCoroutine != null)
            {
                StopCoroutine(breathingCoroutine);
                breathingCoroutine = null;
            }
        }

        /// <summary>悬停/选中高亮：向白色插值，程度由常量配置（策划可调）</summary>
        private void ApplyHighlight(float amount)
        {
            if (bookImage != null)
                bookImage.color = Color.Lerp(baseCoverColor, Color.white, Mathf.Clamp01(amount));
        }

        private void ResetHighlight()
        {
            if (bookImage != null)
                bookImage.color = baseCoverColor;
        }

        /// <summary>
        /// 边缘线呼吸灯频闪：透明→亮黄往复，速度由常量配置
        /// </summary>
        private IEnumerator BreathingRoutine()
        {
            while (isSelected)
            {
                float speed = Mathf.Max(0.01f, GameConstants.BreathingEffectSpeed);

                // 亮起
                float t = 0f;
                while (t < 1f && isSelected)
                {
                    t += Time.deltaTime * speed;
                    if (bookOutline != null)
                        bookOutline.effectColor = new Color(1f, 1f, 0f, t * 0.8f);
                    yield return null;
                }

                // 熄灭
                t = 1f;
                while (t > 0f && isSelected)
                {
                    t -= Time.deltaTime * speed;
                    if (bookOutline != null)
                        bookOutline.effectColor = new Color(1f, 1f, 0f, t * 0.8f);
                    yield return null;
                }
            }
        }

        #endregion
    }
}
