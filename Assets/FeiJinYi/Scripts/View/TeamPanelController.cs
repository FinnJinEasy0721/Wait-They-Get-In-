// 开发团队面板控制器：主菜单"制作人员"按钮弹出全屏团队名单（TeamPannel），进场=面板淡入+成员文字按从上到下顺序逐个缩放弹入（返回按钮最后弹出），返回=整体快速收起（淡出+轻微缩小）后失活。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 开发团队面板（TeamPannel）显示/隐藏与动效：
    /// 进场——面板整体淡入并轻微放大到位，随后全部子物体（12条成员文字+返回按钮，按层级顺序）
    /// 逐个从零缩放弹入（easeOutBack）并自上方40单位滑落归位，间隔0.06秒；
    /// 返回——整体快速收起（0.25秒淡出+缩到96%）后失活并复位。
    /// 主菜单"制作人员"按钮（TeamButton）持久绑定 Show()；返回按钮经序列化字段在 Awake 运行时接线 Hide()。
    /// </summary>
    public class TeamPanelController : MonoBehaviour
    {
        [Tooltip("右下角\"返回\"按钮（面板子物体）")]
        [SerializeField] private Button returnButton;

        private CanvasGroup group;
        private Coroutine running;
        private readonly List<RectTransform> staggerItems = new List<RectTransform>();

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();

            // 逐个入场的子物体按层级顺序收集（=视觉上从上到下，返回按钮在最后）
            staggerItems.Clear();
            foreach (Transform c in transform)
                staggerItems.Add((RectTransform)c);

            if (returnButton != null)
            {
                returnButton.onClick.RemoveAllListeners();
                returnButton.onClick.AddListener(Hide);
            }
            ResetStates();
        }

        /// <summary>显示面板并播放入场动效（"制作人员"按钮入口；面板未激活时才响应）</summary>
        public void Show()
        {
            if (gameObject.activeSelf) return;
            gameObject.SetActive(true); // 首次激活会先跑 Awake 完成初始化
            if (running != null) { StopCoroutine(running); }
            ResetStates();
            running = StartCoroutine(EntranceRoutine());
        }

        /// <summary>收起面板（"返回"按钮入口）：整体快速收起</summary>
        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            if (running != null) { StopCoroutine(running); running = null; }
            running = StartCoroutine(ExitRoutine());
        }

        /// <summary>复位所有动效状态（面板满透明度、全体原始大小位置）</summary>
        private void ResetStates()
        {
            if (group != null) group.alpha = 1f;
            transform.localScale = Vector3.one;
            if (staggerItems != null)
                foreach (var item in staggerItems)
                    if (item != null) item.localScale = Vector3.one;
        }

        private IEnumerator EntranceRoutine()
        {
            // 0) 先把成员藏起来（缩0+上移40），后面逐个放出来
            int n = staggerItems.Count;
            var origins = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                origins[i] = staggerItems[i].anchoredPosition;
                staggerItems[i].localScale = Vector3.zero;
                staggerItems[i].anchoredPosition = origins[i] + new Vector2(0f, 40f);
            }

            // 1) 面板整体淡入（0.3秒，easeOut）+从97%放大到位
            const float fadeIn = 0.3f;
            var startScale = new Vector3(0.97f, 0.97f, 1f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / fadeIn)
            {
                float e = 1f - (1f - t) * (1f - t); // easeOutQuad
                group.alpha = e;
                transform.localScale = Vector3.Lerp(startScale, Vector3.one, e);
                yield return null;
            }
            group.alpha = 1f;
            transform.localScale = Vector3.one;
            yield return new WaitForSeconds(0.08f);

            // 2) 成员逐个弹入：间隔0.06秒、单个0.28秒 easeOutBack + 自上方滑落
            const float interval = 0.06f;
            const float popDur = 0.28f;
            float total = (n - 1) * interval + popDur;
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                for (int i = 0; i < n; i++)
                {
                    float lt = Mathf.Clamp01((t - i * interval) / popDur);
                    staggerItems[i].localScale = Vector3.one * EaseOutBack(lt);
                    staggerItems[i].anchoredPosition = origins[i] + new Vector2(0f, 40f * (1f - EaseOutCubic(lt)));
                }
                yield return null;
            }
            for (int i = 0; i < n; i++)
            {
                staggerItems[i].localScale = Vector3.one;
                staggerItems[i].anchoredPosition = origins[i];
            }
            running = null;
        }

        private IEnumerator ExitRoutine()
        {
            const float dur = 0.25f;
            var shrink = new Vector3(0.96f, 0.96f, 1f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
            {
                float e = t * t; // easeInQuad：先慢后快收走
                group.alpha = 1f - e;
                transform.localScale = Vector3.Lerp(Vector3.one, shrink, e);
                yield return null;
            }
            gameObject.SetActive(false);
            ResetStates();
            running = null;
        }

        private static float EaseOutBack(float t)
        {
            const float c = 1.70158f;
            return 1f + (c + 1f) * Mathf.Pow(t - 1f, 3f) + c * Mathf.Pow(t - 1f, 2f);
        }

        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }
    }
}
