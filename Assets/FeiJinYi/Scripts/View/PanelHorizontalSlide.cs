// 面板水平滑动动效：面板根物体激活时窗口自动从屏幕左侧滑入到原位（遮罩同步淡入）；Close() 时窗口滑出到屏幕右侧、遮罩淡出，完成后失活根物体并回调（供继续/重开等后续动作）。
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 弹窗面板的水平滑动动效（挂面板根物体，如 SavePromptPanel / SettingsPanel）：
    /// 进场——根物体 SetActive(true) 后 OnEnable 自动把窗口（panel 子物体）摆到屏幕左侧外，
    /// 滑回原位（easeOutCubic），全屏遮罩（dimBackground）同步淡入到原透明度；
    /// 关闭——Close() 把窗口滑出到屏幕右侧外（easeInCubic）、遮罩淡出，完成后失活根物体并触发回调。
    /// 动画用未缩放时间（unscaledDeltaTime），暂停（timeScale=0）下也能正常播放。
    /// </summary>
    public class PanelHorizontalSlide : MonoBehaviour
    {
        [Tooltip("滑动的窗口物体（面板根下的 Panel 子物体）")]
        [SerializeField] private RectTransform panel;
        [Tooltip("全屏遮罩（面板根下的 DimBackground 子物体，可为空=只滑窗不淡遮罩）")]
        [SerializeField] private Image dimBackground;
        [Tooltip("滑入时长（秒）")]
        [SerializeField] private float openDuration = 0.32f;
        [Tooltip("滑出时长（秒）")]
        [SerializeField] private float closeDuration = 0.26f;
        [Tooltip("滑出屏幕外的额外余量（画布单位）")]
        [SerializeField] private float offscreenMargin = 80f;

        private Vector2 panelOrigin;
        private float dimTargetAlpha = 0.6f;
        private Coroutine running;

        private void Awake()
        {
            if (panel != null) panelOrigin = panel.anchoredPosition;
            if (dimBackground != null) dimTargetAlpha = dimBackground.color.a;
        }

        private void OnEnable()
        {
            if (panel == null) return;
            StopRunning();
            // 同帧先摆到屏幕左侧待命（避免居中闪现一帧），再滑入
            float offX = OffscreenX(-1f);
            panel.anchoredPosition = new Vector2(offX, panelOrigin.y);
            if (dimBackground != null) SetDimAlpha(0f);
            running = StartCoroutine(SlideRoutine(offX, panelOrigin.x, openDuration, dimTargetAlpha, true, null));
        }

        /// <summary>播关闭动效：窗口滑出到屏幕右侧+遮罩淡出，完成后失活根物体并回调（返回/继续/重开等关闭路径统一入口）</summary>
        public void Close(System.Action onClosed = null)
        {
            if (!gameObject.activeSelf || panel == null)
            {
                gameObject.SetActive(false);
                onClosed?.Invoke();
                return;
            }
            StopRunning();
            running = StartCoroutine(SlideRoutine(panel.anchoredPosition.x, OffscreenX(1f), closeDuration, 0f, false, () =>
            {
                gameObject.SetActive(false);
                panel.anchoredPosition = panelOrigin;   // 复位，供下次滑入
                if (dimBackground != null) SetDimAlpha(dimTargetAlpha);
                onClosed?.Invoke();
            }));
        }

        /// <summary>窗口中心需要移出屏幕外多远（画布单位，带方向）</summary>
        private float OffscreenX(float dir)
        {
            var parent = transform.parent as RectTransform;
            float parentW = parent != null ? parent.rect.width : 1920f;
            return dir * (parentW * 0.5f + panel.rect.width * 0.5f + offscreenMargin);
        }

        private void SetDimAlpha(float a)
        {
            if (dimBackground == null) return;
            var c = dimBackground.color;
            c.a = a;
            dimBackground.color = c;
        }

        private void StopRunning()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }
        }

        private IEnumerator SlideRoutine(float fromX, float toX, float dur, float dimTo, bool easeOut, System.Action onDone)
        {
            float fromDim = dimBackground != null ? dimBackground.color.a : 0f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / dur)
            {
                float e = easeOut ? 1f - Mathf.Pow(1f - t, 3f) : t * t * t; // 进easeOutCubic / 出easeInCubic
                panel.anchoredPosition = new Vector2(Mathf.LerpUnclamped(fromX, toX, e), panelOrigin.y);
                if (dimBackground != null) SetDimAlpha(Mathf.LerpUnclamped(fromDim, dimTo, e));
                yield return null;
            }
            panel.anchoredPosition = new Vector2(toX, panelOrigin.y);
            if (dimBackground != null) SetDimAlpha(dimTo);
            running = null;
            onDone?.Invoke();
        }
    }
}
