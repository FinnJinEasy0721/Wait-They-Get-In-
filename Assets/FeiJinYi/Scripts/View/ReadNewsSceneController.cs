// ReadNews场景控制器：按当天生效内容（日程表叠加命中分支）渲染手机聊天记录与报纸，看完跳转选书场景。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// ReadNews 场景控制器（选书前看资讯，预知当天会发生什么）
    /// 内容读当天生效数据（日程表为基底，叠加命中的分支事件：聊天追加/替换、报纸替换）：
    ///   ChatMessages = 手机聊天记录（多条"发送者:内容"）
    ///   NewsTitle / NewsContent = 报纸标题与正文
    /// 交互：点手机图标弹出聊天面板，点报纸展开报纸面板；面板打开时点击屏幕任意位置收起；点"开始选书"切换场景
    /// </summary>
    public class ReadNewsSceneController : MonoBehaviour
    {
        [Header("UI引用")]
        [SerializeField] private Text dayTitleText;               // 天数标题
        [SerializeField] private GameObject phonePanel;           // 手机聊天面板
        [SerializeField] private RectTransform chatContentArea;   // 聊天消息容器
        [Tooltip("单条聊天消息模板（带背景Image的根物体，默认隐藏，克隆用）")]
        [SerializeField] private GameObject chatMessageTemplate;  // 单条聊天消息模板
        [SerializeField] private GameObject newsPanel;             // 报纸面板
        [SerializeField] private Text newsTitleText;               // 报纸标题
        [SerializeField] private Text newsContentText;             // 报纸正文
        [SerializeField] private Text taskHintText;                // 玩家任务提示（可选）
        [SerializeField] private GameObject phoneDimMask;           // 聊天面板打开时的全屏遮罩（背景虚化+拦截误点）
        [SerializeField] private Button closePhoneButton;          // "关闭手机"按钮
        [Tooltip("手机/报纸面板打开时激活的全屏透明层：点击屏幕任意位置收起当前面板")]
        [SerializeField] private Button clickToCloseMask;           // 点击任意位置关闭层（渲染在最顶层）
        [SerializeField] private Text phoneTitleText;               // 手机群聊名称（日程表 ChatGroupName 配置）

        [Header("配置")]
        [Tooltip("看完资讯后切换的目标场景名（需已加入Build Settings）")]
        [SerializeField] private string nextSceneName = "ChooseBook";

        /// <summary>当前天数：由 GameDataManager 跨场景天数状态机驱动（每日重新选书）</summary>
        private int dayNumber => GameDataManager.Instance != null ? GameDataManager.Instance.CurrentDay : 1;
        private GameDataJSON.ScheduleDay schedule;

        /// <summary>当天生效内容（日程表叠加命中分支：聊天追加/替换、报纸替换）</summary>
        private EffectiveDayPlan dayPlan;

        private void Start()
        {
            if (GameDataManager.Instance == null)
            {
                new GameObject("GameDataManager").AddComponent<GameDataManager>();
            }

            // "关闭手机"按钮接线（运行时绑定）
            if (closePhoneButton != null)
            {
                closePhoneButton.onClick.RemoveAllListeners();
                closePhoneButton.onClick.AddListener(ClosePhonePanel);
            }
            // "点击任意位置关闭"透明层接线（与手机/报纸面板同开同关）
            if (clickToCloseMask != null)
            {
                clickToCloseMask.onClick.RemoveAllListeners();
                clickToCloseMask.onClick.AddListener(CloseAllPanels);
                clickToCloseMask.gameObject.SetActive(false);
            }
            if (phoneDimMask != null) phoneDimMask.SetActive(false);

            schedule = GameDataManager.Instance.GetDaySchedule(dayNumber);
            dayPlan = GameDataManager.Instance.GetEffectiveDayPlan(dayNumber);
            if (schedule == null)
            {
                Debug.LogError($"[ReadNews] 日程表中找不到 第{dayNumber}天 的配置，请检查 7天日程编排.xlsx");
                if (dayTitleText != null)
                    dayTitleText.text = $"第 {dayNumber} 天（日程数据缺失）";
                return;
            }

            if (dayTitleText != null)
                dayTitleText.text = $"第 {dayNumber} 天 · 上班前（{schedule.CoreContent}）";

            // 群聊名称（策划在 7天日程编排.xlsx J列 ChatGroupName 配置，空则兜底）
            if (phoneTitleText != null)
                phoneTitleText.text = string.IsNullOrEmpty(schedule.ChatGroupName) ? "工作群" : schedule.ChatGroupName;

            BuildChatPanel();
            BuildNewsPanel();

            if (taskHintText != null)
                taskHintText.text = string.IsNullOrEmpty(schedule.PlayerTask)
                    ? "今天的任务：见机行事"
                    : $"今天的任务：{schedule.PlayerTask}";

            Debug.Log($"[ReadNews] 第{dayNumber}天资讯就绪：聊天{CountChat()}条，报纸{(string.IsNullOrEmpty(schedule.NewsTitle) ? "无" : "有")}");
        }

        private int CountChat()
        {
            return dayPlan?.ChatMessages?.Count ?? 0;
        }

        /// <summary>构建手机聊天面板（克隆消息模板逐条显示"发送者：内容"，带气泡背景）</summary>
        private void BuildChatPanel()
        {
            if (phonePanel != null) phonePanel.SetActive(false);
            if (phoneDimMask != null) phoneDimMask.SetActive(false);
            if (chatContentArea == null || chatMessageTemplate == null) return;

            // 清空旧消息（保留模板）
            for (int i = chatContentArea.childCount - 1; i >= 0; i--)
            {
                var child = chatContentArea.GetChild(i);
                if (child != chatMessageTemplate.transform)
                    Destroy(child.gameObject);
            }

            var messages = dayPlan?.ChatMessages;
            if (messages == null || messages.Count == 0) return;

            // 气泡最大宽度：屏幕白区宽 - 左右余量（ContentArea 已锚定白区内缩）
            float maxBubbleWidth = chatContentArea.rect.width - 16f;

            foreach (var msg in messages)
            {
                // 克隆模板根物体（含气泡背景Image与子Text），保持局部位置归零
                var item = Instantiate(chatMessageTemplate, chatContentArea);
                item.transform.localPosition = Vector3.zero;
                item.transform.localScale = Vector3.one;
                item.SetActive(true);

                var text = item.GetComponentInChildren<Text>();
                if (text != null)
                {
                    string content = $"{msg.Sender}：{msg.Text}";
                    text.text = content;
                    LayoutChatBubble(item, text, content, maxBubbleWidth);
                }
            }
        }

        /// <summary>
        /// 按手机聊天样式排版单条气泡：
        /// 宽度=文本首选宽（超长自动换行、上限 maxBubbleWidth），高度随行数增长，
        /// 并把 LayoutElement.preferredHeight 同步给 VerticalLayoutGroup 控高
        /// </summary>
        public static void LayoutChatBubble(GameObject bubble, Text text, string content, float maxBubbleWidth, int padding = 12)
        {
            var bubbleRect = bubble.transform as RectTransform;
            if (text == null || bubbleRect == null) return;

            var settings = text.GetGenerationSettings(new Vector2(maxBubbleWidth - padding * 2, float.MaxValue));
            float preferredWidth = Mathf.Min(text.cachedTextGenerator.GetPreferredWidth(content, settings) / text.pixelsPerUnit, maxBubbleWidth - padding * 2);
            float preferredHeight = text.cachedTextGenerator.GetPreferredHeight(content, settings) / text.pixelsPerUnit;

            float bubbleW = preferredWidth + padding * 2;
            float bubbleH = preferredHeight + padding * 2;

            bubbleRect.sizeDelta = new Vector2(bubbleW, bubbleH);
            var le = bubble.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = bubbleH;
        }

        /// <summary>构建报纸面板（标题+正文；当天生效内容无报纸则隐藏入口，分支"报纸"效果可替换原报纸）</summary>
        private void BuildNewsPanel()
        {
            if (newsPanel != null) newsPanel.SetActive(false);

            bool hasNews = dayPlan != null && !string.IsNullOrEmpty(dayPlan.NewsTitle);
            if (newsTitleText != null)
                newsTitleText.text = hasNews ? dayPlan.NewsTitle : "";
            if (newsContentText != null)
                newsContentText.text = hasNews ? (dayPlan.NewsContent ?? "") : "";
        }

        /// <summary>手机图标点击：开关聊天面板（打开时全屏遮罩虚化背景+点击任意位置关闭层）</summary>
        public void TogglePhonePanel()
        {
            if (phonePanel == null) return;
            bool show = !phonePanel.activeSelf;
            phonePanel.SetActive(show);
            if (phoneDimMask != null) phoneDimMask.SetActive(show);
            SetClickToCloseMask(show);
            if (show && newsPanel != null) newsPanel.SetActive(false);
            Debug.Log("[ReadNews] 聊天面板=" + show);
        }

        /// <summary>"关闭手机"按钮：收起聊天面板与背景遮罩</summary>
        public void ClosePhonePanel()
        {
            if (phonePanel != null) phonePanel.SetActive(false);
            if (phoneDimMask != null) phoneDimMask.SetActive(false);
            SetClickToCloseMask(false);
            Debug.Log("[ReadNews] 手机已收起");
        }

        /// <summary>点击屏幕任意位置收起当前打开的手机/报纸面板（透明点击层的入口）</summary>
        public void CloseAllPanels()
        {
            if (phonePanel != null) phonePanel.SetActive(false);
            if (phoneDimMask != null) phoneDimMask.SetActive(false);
            if (newsPanel != null) newsPanel.SetActive(false);
            SetClickToCloseMask(false);
            Debug.Log("[ReadNews] 面板已收起（点击任意位置）");
        }

        /// <summary>开关"点击任意位置关闭"透明层（与手机/报纸面板同开同关）</summary>
        private void SetClickToCloseMask(bool on)
        {
            if (clickToCloseMask != null) clickToCloseMask.gameObject.SetActive(on);
        }

        /// <summary>报纸点击：开关报纸面板（互斥收起聊天面板与遮罩，打开时启用点击任意位置关闭层）</summary>
        public void ToggleNewsPanel()
        {
            if (newsPanel == null) return;
            if (string.IsNullOrEmpty(dayPlan?.NewsTitle))
            {
                Debug.LogWarning("[ReadNews] 今天没有报纸（当天生效内容的 NewsTitle 为空）");
                return;
            }
            bool show = !newsPanel.activeSelf;
            newsPanel.SetActive(show);
            if (show)
            {
                if (phonePanel != null) phonePanel.SetActive(false);
                if (phoneDimMask != null) phoneDimMask.SetActive(false);
            }
            SetClickToCloseMask(show);
            Debug.Log("[ReadNews] 报纸面板=" + show);
        }

        /// <summary>"开始选书"按钮：带淡入淡出切换到选书场景</summary>
        public void StartChoosingBooks()
        {
            Debug.Log($"[ReadNews] 切换场景: {nextSceneName}");
            SceneFadeTransition.Switch(nextSceneName);
        }
    }
}
