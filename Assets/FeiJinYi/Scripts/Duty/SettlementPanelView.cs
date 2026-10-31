// 结算面板视图：按星级换背景与评级文案，黄色星星飞入槽位、放行情况勾叉缓动出现的日结算演出。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 日结算面板视图（挂在 SettlementPanel 根物体，由 DutyFlowController 驱动）
    /// 背景图按星级切换（0-3星对应不同表情的警察形象）：
    ///   黄色区=评级文字（不合格/合格/良好/优秀）、粉色区=三颗星星（黄色飞入指定槽位/灰色常驻）、
    ///   白色区=逐条访客放行情况（正确勾/错误叉缓动出现）与备注小字行
    /// </summary>
    public class SettlementPanelView : MonoBehaviour
    {
        [Header("面板引用")]
        [SerializeField] private Image backgroundImage;      // 面板背景（运行时按星级换 Sprite）
        [SerializeField] private Text ratingText;            // 黄色区评级文字
        [SerializeField] private RectTransform linesRoot;    // 白色区记录行容器
        [Tooltip("白色区单条记录模板（文字+图标，默认隐藏，克隆用）")]
        [SerializeField] private GameObject lineTemplate;    // 记录行模板

        [Header("星星槽（左中右三颗）")]
        [SerializeField] private Image[] starSlots = new Image[3];

        [Header("素材")]
        [Tooltip("0~3星结算背景，顺序对应星级")]
        [SerializeField] private Sprite[] backgroundByStars = new Sprite[4];
        [SerializeField] private Sprite yellowStarSprite;
        [SerializeField] private Sprite greyStarSprite;
        [SerializeField] private Sprite yesIconSprite;
        [SerializeField] private Sprite noIconSprite;

        /// <summary>白色区一行访客处理记录</summary>
        public struct ResultLine
        {
            public string Text;    // 记录文字（如"小张 正确放行"）
            public bool Correct;   // true=勾图标，false=叉图标
        }

        private static readonly string[] RatingWords = { "不合格", "合格", "良好", "优秀" };
        private const float LineRowHeight = 34f;   // 访客行高
        private const float NoteRowHeight = 26f;    // 备注行高
        private const float IconSize = 28f;          // 勾/叉图标边长
        private const float IconGap = 10f;          // 文字与图标间距
        private static readonly Color LineColor = new Color(0.13f, 0.14f, 0.2f);
        private static readonly Color NoteColor = new Color(0.42f, 0.45f, 0.52f);

        private class RowInfo
        {
            public Text Text;
            public Image Icon;          // 备注行为 null
            public Vector2 TextPos;     // 文字最终位置（入场从左侧滑入）
            public Color BaseColor;     // 文字基色（动效中调 Alpha）
            public float Height;        // 行实际高度（含换行撑高）
        }
        private readonly List<RowInfo> rows = new List<RowInfo>();

        /// <summary>展示结算面板：切换背景与评级文字，星星飞入、记录行逐条缓动出现（物体需已激活）</summary>
        public void Show(int stars, List<ResultLine> lines, List<string> notes)
        {
            stars = Mathf.Clamp(stars, 0, 3);
            if (backgroundImage != null && backgroundByStars[stars] != null)
                backgroundImage.sprite = backgroundByStars[stars];
            if (ratingText != null)
                ratingText.text = RatingWords[stars];

            StopAllCoroutines();
            BuildRows(lines, notes);

            // 星星：灰色常驻直接显示；黄色（获得的星）从面板下方逐颗飞入槽位
            for (int i = 0; i < starSlots.Length; i++)
            {
                var slot = starSlots[i];
                if (slot == null) continue;
                bool earned = i < stars;
                slot.sprite = earned ? yellowStarSprite : greyStarSprite;
                slot.color = Color.white;
                slot.transform.localScale = Vector3.one;
                slot.transform.localRotation = Quaternion.identity;
                slot.gameObject.SetActive(!earned);
                if (earned)
                    StartCoroutine(FlyStarToSlot(slot, i * 0.22f));
            }

            StartCoroutine(RevealRows());
        }

        /// <summary>克隆模板构建白色区全部行，初始隐藏等待动效。
        /// 两列流式排布：先填左半列，放不下再换右半列（最多两列，超出部分仍继续向下溢出）。</summary>
        private void BuildRows(List<ResultLine> lines, List<string> notes)
        {
            rows.Clear();
            for (int i = linesRoot.childCount - 1; i >= 0; i--)
            {
                var child = linesRoot.GetChild(i);
                if (lineTemplate == null || child != lineTemplate.transform)
                    Destroy(child.gameObject);
            }

            float totalWidth = Mathf.Max(1f, linesRoot.rect.width);
            float totalHeight = Mathf.Max(1f, linesRoot.rect.height);
            const float columnGap = 10f;        // 左右两列间隙
            float columnWidth = (totalWidth - columnGap) * 0.5f;
            const float topPadding = 4f;

            float cursorY = topPadding;
            int column = 0;                      // 0=左列，1=右列

            void Emit(string content, Color color, int fontSize, Sprite iconSprite, float baseRowHeight)
            {
                // 左列放不下下一行时换右列（换列后仍放不下则继续向下溢出）
                if (column == 0 && cursorY + baseRowHeight > totalHeight)
                {
                    column = 1;
                    cursorY = topPadding;
                }
                float columnX = column == 0 ? 0f : columnWidth + columnGap;
                var row = CreateRow(cursorY, baseRowHeight, content, color, fontSize, iconSprite, columnX, columnWidth);
                if (row != null)
                {
                    rows.Add(row);
                    cursorY += row.Height;
                }
            }

            if (lines != null)
            {
                foreach (var line in lines)
                    Emit(line.Text, LineColor, 24, line.Correct ? yesIconSprite : noIconSprite, LineRowHeight);
            }
            if (notes != null)
            {
                foreach (var note in notes)
                    Emit(note, NoteColor, 19, null, NoteRowHeight);
            }
        }

        /// <summary>创建一行：文字（按列宽约束，超出自动换行）+ 可选勾/叉图标，初始为隐藏状态。返回行的实际高度（含换行撑高）。</summary>
        private RowInfo CreateRow(float cursor, float rowHeight, string content, Color color, int fontSize, Sprite iconSprite, float columnX, float columnWidth)
        {
            if (lineTemplate == null) return null;
            var item = Instantiate(lineTemplate, linesRoot);
            item.transform.localPosition = Vector3.zero;
            item.transform.localScale = Vector3.one;

            var rt = item.transform as RectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(columnX, -cursor);
            rt.sizeDelta = new Vector2(columnWidth, rowHeight);
            item.SetActive(true);

            var info = new RowInfo();
            var text = item.GetComponentInChildren<Text>(true);
            var icon = item.GetComponentInChildren<Image>(true);

            if (text != null)
            {
                text.fontSize = fontSize;
                text.color = color;
                text.text = content;

                bool hasIcon = iconSprite != null;
                float iconSpace = hasIcon ? IconSize + IconGap : 0f;
                float maxTextWidth = Mathf.Max(20f, columnWidth - iconSpace - 6f);

                // 文字约束到列宽，长备注在列内换行（勾/叉仅出现在访客行，均为单行短文本）
                var textRt = text.transform as RectTransform;
                textRt.anchorMin = new Vector2(0f, 0f);
                textRt.anchorMax = new Vector2(0f, 1f);
                textRt.pivot = new Vector2(0f, 0.5f);
                textRt.anchoredPosition = new Vector2(0f, 0f);
                textRt.sizeDelta = new Vector2(maxTextWidth, -8f);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;

                // 实测内容宽度（单行）用于把图标排在文字后面
                float textWidth = MeasureTextWidth(text, content);

                info.Text = text;
                info.TextPos = textRt.anchoredPosition;
                info.BaseColor = color;
                // 入场初始：透明 + 左移待滑入
                text.color = new Color(color.r, color.g, color.b, 0f);
                textRt.anchoredPosition = info.TextPos - new Vector2(14f, 0f);

                // 按列宽测量换行后实际高度，动态撑高该行（备注长文本可能 2 行）
                float wrappedHeight = MeasureWrappedHeight(text, content, maxTextWidth);
                rowHeight = Mathf.Max(rowHeight, wrappedHeight + 8f);

                if (icon != null)
                {
                    bool hasIconSprite = iconSprite != null;
                    icon.gameObject.SetActive(hasIconSprite);
                    if (hasIconSprite)
                    {
                        icon.sprite = iconSprite;
                        var irt = icon.transform as RectTransform;
                        irt.sizeDelta = new Vector2(IconSize, IconSize);
                        irt.anchoredPosition = new Vector2(textWidth + IconGap, 0f);
                        icon.transform.localScale = Vector3.zero;   // 待回弹出现
                        info.Icon = icon;
                    }
                }
            }

            rt.sizeDelta = new Vector2(columnWidth, rowHeight);
            info.Height = rowHeight;
            return info;
        }

        /// <summary>黄色星星从面板下方飞入槽位：位移缓出、回弹缩放、旋转归零</summary>
        private IEnumerator FlyStarToSlot(Image star, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);

            var rect = star.transform as RectTransform;
            var parentRect = rect.parent as RectTransform;
            Vector2 target = rect.anchoredPosition;
            Vector2 launch = new Vector2(0f, -parentRect.rect.height * 0.62f);

            rect.anchoredPosition = launch;
            rect.localScale = Vector3.one * 0.2f;
            rect.localRotation = Quaternion.Euler(0f, 0f, -35f);
            star.gameObject.SetActive(true);

            const float duration = 0.5f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                float e = EaseOutCubic(t);
                rect.anchoredPosition = Vector2.LerpUnclamped(launch, target, e);
                rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1f, EaseOutBack(t));
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(-35f, 0f, e));
                yield return null;
            }
            rect.anchoredPosition = target;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        /// <summary>记录行逐条出现：文字淡入滑入，随后勾/叉图标回弹缩放出现</summary>
        private IEnumerator RevealRows()
        {
            yield return new WaitForSeconds(0.35f);   // 留给星星先起飞

            foreach (var row in rows)
            {
                if (row.Text != null)
                {
                    var tr = row.Text.transform as RectTransform;
                    Vector2 start = row.TextPos - new Vector2(14f, 0f);
                    const float textDur = 0.22f;
                    for (float t = 0f; t < 1f; t += Time.deltaTime / textDur)
                    {
                        float e = EaseOutCubic(t);
                        tr.anchoredPosition = Vector2.LerpUnclamped(start, row.TextPos, e);
                        row.Text.color = new Color(row.BaseColor.r, row.BaseColor.g, row.BaseColor.b, e);
                        yield return null;
                    }
                    tr.anchoredPosition = row.TextPos;
                    row.Text.color = row.BaseColor;
                }

                if (row.Icon != null)
                {
                    const float iconDur = 0.3f;
                    for (float t = 0f; t < 1f; t += Time.deltaTime / iconDur)
                    {
                        row.Icon.transform.localScale = Vector3.one * EaseOutBack(t);
                        yield return null;
                    }
                    row.Icon.transform.localScale = Vector3.one;
                }
                yield return new WaitForSeconds(0.06f);
            }
        }

        /// <summary>实测单行文字的渲染宽度（用于把图标排在文字后面）</summary>
        private static float MeasureTextWidth(Text text, string content)
        {
            var settings = text.GetGenerationSettings(new Vector2(2000f, 100f));
            return text.cachedTextGenerator.GetPreferredWidth(content, settings) / text.pixelsPerUnit;
        }

        /// <summary>实测文字在指定列宽内换行后的渲染高度（用于动态撑高备注行）</summary>
        private static float MeasureWrappedHeight(Text text, string content, float maxWidth)
        {
            var settings = text.GetGenerationSettings(new Vector2(maxWidth, 3000f));
            return text.cachedTextGenerator.GetPreferredHeight(content, settings) / text.pixelsPerUnit;
        }

        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
