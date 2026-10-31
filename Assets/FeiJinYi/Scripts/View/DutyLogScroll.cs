// 值班记录面板悬停滚动：鼠标移上时用滚轮上下翻看记录，新记录追加后自动滚到最新（底部）。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 值班记录面板滚动组件（挂在 DutyLogPanel 上）
    /// 记录文字超出纸张区时，指针悬停滚动滚轮即可上下翻看；
    /// DutyFlowController 每追加一条记录后调用 Refresh 重算内容高度并滚到最新。
    /// 依赖：DutyLogText 使用顶部锚定 + VerticalOverflow=Overflow，父节点 DutyLogViewport 挂 RectMask2D 负责裁剪。
    /// </summary>
    public class DutyLogScroll : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Text contentText;       // 记录文字（DutyLogText）
        [SerializeField] private RectTransform viewport; // 裁剪视口（DutyLogViewport，挂 RectMask2D）

        private bool hovering;        // 指针是否悬停在面板上
        private float scrollOffset;   // 当前滚动偏移（0=最顶部，maxScroll=最底部）
        private float maxScroll;      // 最大可滚动量 = 内容高 - 视口高

        private const float WheelStep = 26f; // 每一格滚轮滚动的画布单位（约 1.5 行）

        public void OnPointerEnter(PointerEventData eventData) => hovering = true;

        public void OnPointerExit(PointerEventData eventData) => hovering = false;

        private void Update()
        {
            if (!hovering) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f)) return;
            // 向上滚（wheel>0）看更早记录，向下滚看更新记录
            scrollOffset = Mathf.Clamp(scrollOffset - wheel * WheelStep, 0f, maxScroll);
            ApplyOffset();
        }

        /// <summary>内容变更后重算文字高度与滚动范围，并滚到最新（底部）</summary>
        public void Refresh()
        {
            if (contentText == null) return;

            float width = contentText.rectTransform.rect.width;
            float contentHeight = MeasureHeight(contentText, contentText.text, width);
            // 撑开文字高度，让超出视口的部分交给 RectMask2D 裁剪（高度至少铺满视口）
            float viewportHeight = viewport != null ? viewport.rect.height : contentHeight;
            contentText.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, Mathf.Max(contentHeight, viewportHeight));

            maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
            ScrollToBottom();
        }

        /// <summary>滚到最新（底部）</summary>
        public void ScrollToBottom()
        {
            scrollOffset = maxScroll;
            ApplyOffset();
        }

        private void ApplyOffset()
        {
            if (contentText == null) return;
            var pos = contentText.rectTransform.anchoredPosition;
            contentText.rectTransform.anchoredPosition = new Vector2(pos.x, scrollOffset);
        }

        /// <summary>实测文字在指定宽度内换行后的渲染高度（与 SettlementPanelView.MeasureWrappedHeight 同口径）</summary>
        private static float MeasureHeight(Text text, string content, float maxWidth)
        {
            if (string.IsNullOrEmpty(content)) return 0f;
            var settings = text.GetGenerationSettings(new Vector2(maxWidth, 3000f));
            return text.cachedTextGenerator.GetPreferredHeight(content, settings) / text.pixelsPerUnit;
        }
    }
}
