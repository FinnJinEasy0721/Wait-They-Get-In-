// 访客运行时控制器：驱动访客移动与状态切换，按行为库响应保安请求并在头顶白色气泡中显示说话内容；愤怒等情绪在气泡右侧弹出共用表情图（约定 Resources/Expressions/Expression_情绪名，缺图自动隐藏）；有立绘素材的访客加载 VisitorPrefabs 预制体，无素材用白色方块兜底。
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>访客运行时状态</summary>
    public enum VisitorState
    {
        WalkingToStop,   // 移动中（前往停留点/队列点）
        WaitingInQueue,  // 队列等待
        AtStop,          // 停留点等待保安处理（过程入口）
        Processing,      // 保安正在执行规则
        Released,        // 已放行（走向出口）
        Fleeing,         // 逃跑（小偷）
        Leaving,         // 愤怒离开
        Gone             // 已离场
    }

    /// <summary>
    /// 访客运行时控制器：移动、停留、响应保安请求
    /// 视觉两种形态：Resources/VisitorPrefabs/Visitor_{ID} 预制体存在时用立绘，否则白色方块兜底；
    /// 头顶白色气泡显示说话内容（访客胸前不再显示名字）
    /// 行为响应按行为库触发（请求+属性自测+计数），无匹配时默认"懵逼"
    /// </summary>
    public class VisitorController : MonoBehaviour
    {
        // 立绘访客显示尺寸（半身像3:4比例；高度=窗洞高的3/4≈576，按新背景GameBG右窗洞实测767.7）
        private const float PortraitWidth = 432f;
        private const float PortraitHeight = 576f;
        public Visitor Data { get; private set; }
        public VisitorState State { get; private set; } = VisitorState.WalkingToStop;
        public bool IsAngry { get; private set; }
        public int DazeCount { get; private set; }          // 懵逼累计次数
        public int RequestCount { get; private set; }        // 收到保安请求累计次数

        /// <summary>访客气泡上的"逃跑"行为是否已触发（保安判定引擎据此中止流程）</summary>
        public bool HasFled { get; private set; }

        private readonly HashSet<VisitorAttribute> hiddenAttributes = new HashSet<VisitorAttribute>();
        private readonly List<VisitorBehaviour> behaviours = new List<VisitorBehaviour>();

        private Image blockImage;
        private Text bubbleText;
        private RectTransform bubbleRoot;
        private Image expressionPopup;        // 头顶表情图（愤怒等情绪触发弹出）
        private Coroutine expressionRoutine;   // 表情弹出缩放动画协程

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>
        /// 创建访客运行时物体：优先加载 Resources/VisitorPrefabs/Visitor_{数字ID} 立绘预制体
        /// （访客ID形如 visitor_001，取其数字部分匹配预制体名），
        /// 不存在（素材未做）时用白色方块兜底；说话气泡由代码构建（不再显示胸前名字）
        /// </summary>
        public static VisitorController Create(Visitor data, Transform parent)
        {
            GameObject go;
            Image img;

            string numericId = data.ID != null && data.ID.StartsWith("visitor_")
                ? data.ID.Substring("visitor_".Length)
                : data.ID;
            var prefab = Resources.Load<GameObject>("VisitorPrefabs/Visitor_" + numericId);
            if (prefab != null)
            {
                go = Instantiate(prefab, parent, false);
                go.name = "Visitor_" + data.Name;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(PortraitWidth, PortraitHeight);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f); // 底部pivot：站位点=脚底位置，贴住窗台不悬浮
                img = go.GetComponent<Image>();
                if (img == null) img = go.AddComponent<Image>();
                img.preserveAspect = true;
            }
            else
            {
                go = new GameObject("Visitor_" + data.Name);
                go.transform.SetParent(parent, false);
                var rt = go.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(206, 261); // 与立绘放大幅度一致的兜底白块
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                img = go.AddComponent<Image>();
                img.color = Color.white;
            }

            var visitor = go.AddComponent<VisitorController>();
            visitor.Initialize(data, img);
            return visitor;
        }

        private void Initialize(Visitor data, Image block)
        {
            Data = data;
            blockImage = block;
            behaviours.AddRange(GameDataManager.Instance.GetBehaviours(data.ID));

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rootRect = (RectTransform)transform;

            // 行为气泡（头顶上方，白底黑字；高度按访客实际高度计算）
            var bubbleGo = new GameObject("Bubble");
            bubbleGo.transform.SetParent(transform, false);
            bubbleRoot = bubbleGo.AddComponent<RectTransform>();
            bubbleRoot.anchorMin = bubbleRoot.anchorMax = bubbleRoot.pivot = new Vector2(0.5f, 0f);
            bubbleRoot.anchoredPosition = new Vector2(0, rootRect.rect.height + 25f);
            bubbleRoot.sizeDelta = new Vector2(430, 90);
            var bubbleBg = bubbleGo.AddComponent<Image>();
            bubbleBg.color = Color.white;
            bubbleBg.raycastTarget = false;

            var bubbleTextGo = new GameObject("BubbleText");
            bubbleTextGo.transform.SetParent(bubbleGo.transform, false);
            bubbleText = bubbleTextGo.AddComponent<Text>();
            bubbleText.font = font;
            bubbleText.fontSize = 17;
            bubbleText.color = Color.black;
            bubbleText.alignment = TextAnchor.UpperCenter;
            bubbleText.raycastTarget = false;
            SetStretch(bubbleText.rectTransform, 10, 6);

            // 初始隐藏气泡（无文本时不显示底框，避免"悬空白条"）
            ShowBubble("");

            // 情绪表情图（气泡右侧同高，愤怒等情绪触发弹出；共用图约定 Resources/Expressions/Expression_情绪名）
            var exprGo = new GameObject("ExpressionPopup");
            exprGo.transform.SetParent(transform, false);
            var exprRt = exprGo.AddComponent<RectTransform>();
            exprRt.anchorMin = exprRt.anchorMax = exprRt.pivot = new Vector2(0.5f, 0f);
            exprRt.anchoredPosition = new Vector2(270f, rootRect.rect.height + 25f); // 气泡右侧、与气泡同高（访客增高后头顶已近窗顶，再往上会出窗）
            exprRt.sizeDelta = new Vector2(90f, 90f);
            expressionPopup = exprGo.AddComponent<Image>();
            expressionPopup.raycastTarget = false;
            expressionPopup.preserveAspect = true;
            expressionPopup.enabled = false;
        }

        private static void SetStretch(RectTransform rt, int padding = 0, int vPadding = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, vPadding);
            rt.offsetMax = new Vector2(-padding, -vPadding);
        }

        // ===== 状态切换 =====

        public void SetState(VisitorState state)
        {
            State = state;
        }

        public void SetFleeing()
        {
            HasFled = true;
            State = VisitorState.Fleeing;
            ShowBubble("（转身逃跑！）");
        }

        public void MarkReleased()
        {
            State = VisitorState.Released;
        }

        // ===== 行为响应 =====

        /// <summary>
        /// 响应保安请求：按行为库匹配触发（同一请求可触发多条=组合行为，如"出示工牌并遮挡"）；
        /// 无匹配行为时默认"懵逼"并累计计数；每次请求后检查愤怒触发条件
        /// </summary>
        public void RespondToRequest(GuardRequestType request)
        {
            RequestCount++;

            var fired = new List<VisitorBehaviour>();
            var texts = new List<string>();

            foreach (var b in behaviours)
            {
                if (b.TriggerRequest != request) continue;
                // 属性自测条件（需属性:标签1,标签2）
                if (!CheckAttributeCondition(b)) continue;
                FireBehaviour(b, texts);
                fired.Add(b);
            }

            if (fired.Count == 0)
            {
                // 默认行为：懵逼
                DazeCount++;
                ShowBubble("（一脸懵逼）？？？");
            }
            else
            {
                ShowBubble(string.Join("\n", texts));
                // 动作效果：逃跑 → 进入逃跑状态（保安判定引擎据此中止并记录拦截成功）
                foreach (var b in fired)
                {
                    if (b.ActionName == "逃跑")
                    {
                        SetFleeing();
                        break;
                    }
                }
            }

            // 检查愤怒触发（计数条件行为，TriggerRequest=无）
            CheckAngryBehaviours();
        }

        private bool CheckAttributeCondition(VisitorBehaviour b)
        {
            var required = b.RequiredAttributes();
            if (required.Count == 0) return true;
            foreach (var attr in required)
            {
                if (!Data.HasAttribute(attr)) return false;
            }
            return true;
        }

        private void FireBehaviour(VisitorBehaviour b, List<string> texts)
        {
            texts.Add(b.ResponseText);
            // 遮挡效果：对保安屏蔽指定可见属性
            foreach (var attr in b.HideAttributes)
            {
                hiddenAttributes.Add(attr);
            }
        }

        /// <summary>检查愤怒类行为（懵逼计数/请求计数达到阈值），触发则进入愤怒状态</summary>
        private void CheckAngryBehaviours()
        {
            if (IsAngry) return;

            foreach (var b in behaviours)
            {
                if (b.TriggerRequest != GuardRequestType.无) continue;

                bool hit = false;
                if (b.TryGetDazeCountCondition(out int dazeNeed) && DazeCount >= dazeNeed) hit = true;
                if (b.TryGetRequestCountCondition(out int reqNeed) && RequestCount >= reqNeed) hit = true;

                if (hit)
                {
                    IsAngry = true;
                    ShowBubble(b.ResponseText);
                    if (blockImage != null)
                        blockImage.color = new Color(1f, 0.55f, 0.45f); // 愤怒变色提示
                    ShowHeadExpression("愤怒"); // 气泡右侧弹出愤怒表情图（图片未配置时自动隐藏）
                    break;
                }
            }
        }

        /// <summary>触发"状态:愤怒"类行为（离开），无配置返回null</summary>
        public string FireAngryLeaveBehaviour()
        {
            foreach (var b in behaviours)
            {
                if (b.TriggerRequest == GuardRequestType.无 && b.IsAngryStateCondition())
                {
                    ShowBubble(b.ResponseText);
                    State = VisitorState.Leaving;
                    return b.ResponseText;
                }
            }
            return null;
        }

        /// <summary>访客可见属性（真实属性 - 行为屏蔽的属性，如"遮挡时间信息"屏蔽证件过期）</summary>
        public List<VisitorAttribute> GetVisibleAttributes()
        {
            var visible = new List<VisitorAttribute>();
            foreach (var attr in Data.Attributes)
            {
                if (!hiddenAttributes.Contains(attr))
                    visible.Add(attr);
            }
            return visible;
        }

        /// <summary>
        /// 在气泡右侧弹出情绪表情图：共用图约定路径 Resources/Expressions/Expression_情绪名（如 Expression_愤怒），
        /// 图片未配置时静默不显示；有图则以缩放弹出动效显示，持续到访客离场
        /// </summary>
        public void ShowHeadExpression(string emotionKey)
        {
            if (expressionPopup == null) return;

            var sprite = Resources.Load<Sprite>("Expressions/Expression_" + emotionKey);
            if (sprite == null)
            {
                // 图片导入类型不是 Sprite 时按 Texture2D 兜底生成（丢图即生效，无需改导入设置）
                var texture = Resources.Load<Texture2D>("Expressions/Expression_" + emotionKey);
                if (texture != null)
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }
            if (sprite == null) return; // 图片未添加，保持隐藏

            expressionPopup.sprite = sprite;
            expressionPopup.enabled = true;
            if (expressionRoutine != null) StopCoroutine(expressionRoutine);
            expressionRoutine = StartCoroutine(PopInExpressionRoutine());
        }

        /// <summary>表情图弹出缩放动画（0→1，easeOutBack 轻微回弹）</summary>
        private System.Collections.IEnumerator PopInExpressionRoutine()
        {
            const float duration = 0.3f;
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / duration);
                const float overshoot = 1.70158f;
                float e = 1f + (overshoot + 1f) * Mathf.Pow(t - 1f, 3f) + overshoot * Mathf.Pow(t - 1f, 2f);
                expressionPopup.rectTransform.localScale = Vector3.one * Mathf.Max(0f, e);
                yield return null;
            }
            expressionPopup.rectTransform.localScale = Vector3.one;
            expressionRoutine = null;
        }

        /// <summary>显示气泡文本（空串隐藏）</summary>
        public void ShowBubble(string text)
        {
            if (bubbleRoot != null)
                bubbleRoot.gameObject.SetActive(!string.IsNullOrEmpty(text));
            if (bubbleText != null)
                bubbleText.text = text;
        }

        /// <summary>移动到指定锚点位置（协程，速度由常量配置，受值班速度倍率影响）</summary>
        public System.Collections.IEnumerator MoveTo(Vector2 targetAnchoredPos, float speedOverride = 0f)
        {
            float speed = speedOverride > 0f ? speedOverride : GameConstants.VisitorMoveSpeed;
            speed *= Mathf.Max(0.01f, GameConstants.DutySpeedMultiplier);
            Vector2 start = Rect.anchoredPosition;
            float distance = Vector2.Distance(start, targetAnchoredPos);
            if (distance < 1f) yield break;

            float duration = distance / Mathf.Max(1f, speed);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                Rect.anchoredPosition = Vector2.Lerp(start, targetAnchoredPos, Mathf.Clamp01(t));
                yield return null;
            }
            Rect.anchoredPosition = targetAnchoredPos;
        }
    }
}
