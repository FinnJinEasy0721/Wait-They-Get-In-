// 悬停抖动组件：鼠标悬停进入时让物体短促地左右抖动一下（幅度随时间衰减），结束后回到原位。
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 悬停抖动
    /// IPointerEnter 触发一次短促的位置抖动（水平方向、幅度衰减）；
    /// 抖动期间再次进入会重新触发；失能时立即恢复原位
    /// </summary>
    public class HoverShake : MonoBehaviour, IPointerEnterHandler
    {
        [Tooltip("抖动持续时间（秒）")]
        [SerializeField] private float duration = 0.45f;
        [Tooltip("初始抖动幅度（画布单位）")]
        [SerializeField] private float amplitude = 10f;
        [Tooltip("抖动频率（弧度/秒，越大抖得越密）")]
        [SerializeField] private float frequency = 26f;

        private RectTransform rect;
        private Vector2 basePos;
        private Coroutine shaking;

        private void Awake()
        {
            rect = transform as RectTransform;
            basePos = rect != null ? rect.anchoredPosition : Vector2.zero;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (rect == null || !isActiveAndEnabled) return;
            if (shaking != null) StopCoroutine(shaking);
            shaking = StartCoroutine(ShakeRoutine());
        }

        private IEnumerator ShakeRoutine()
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float decay = 1f - Mathf.Clamp01(t / duration);
                float offset = Mathf.Sin(t * frequency) * amplitude * decay;
                rect.anchoredPosition = basePos + new Vector2(offset, 0f);
                yield return null;
            }
            rect.anchoredPosition = basePos;
            shaking = null;
        }

        private void OnDisable()
        {
            if (shaking != null)
            {
                StopCoroutine(shaking);
                shaking = null;
            }
            if (rect != null)
                rect.anchoredPosition = basePos;
        }
    }
}
