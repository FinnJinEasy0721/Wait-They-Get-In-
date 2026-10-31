//标题图片模拟悬停和点击效果组件
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
[RequireComponent(typeof(CanvasGroup))]
public class TitleSignEffect : MonoBehaviour,
    IPointerEnterHandler, IPointerClickHandler
{
    [Header("悬停晃动")]
    [Tooltip("左右晃动的最大角度")]
    public float hoverAngle = 9f;

    [Tooltip("每次晃动的时间")]
    public float hoverHalfDuration = 0.12f;

    [Tooltip("晃动幅度衰减")]
    [Range(0.1f, 1f)]
    public float hoverDamping = 0.65f;

    [Header("点击坠落")]
    [Tooltip("点击后的震动角度")]
    public float hitAngle = 5f;

    [Tooltip("点击震动持续时间")]
    public float hitDuration = 0.12f;

    [Tooltip("坠落持续时间")]
    public float fallDuration = 0.8f;

    [Tooltip("坠落的额外水平位移")]
    public float fallSideways = 100f;

    [Tooltip("坠落旋转角度")]
    public float fallAngle = 65f;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;

    private Vector2 originalPosition;
    private Quaternion originalRotation;

    private Coroutine currentAnimation;
    private bool isFalling;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();

        originalPosition = rectTransform.anchoredPosition;
        originalRotation = rectTransform.localRotation;
    }

    // 鼠标进入标题区域
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isFalling)
            return;

        if (currentAnimation != null)
            StopCoroutine(currentAnimation);

        currentAnimation = StartCoroutine(HoverShake());
    }

    // 鼠标点击标题
    public void OnPointerClick(PointerEventData eventData)
    {
        if (isFalling)
            return;

        isFalling = true;

        if (currentAnimation != null)
            StopCoroutine(currentAnimation);

        // 防止标题消失后继续接收点击
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        currentAnimation = StartCoroutine(FallDown());
    }

    // 左右晃动两下，逐渐恢复
    private IEnumerator HoverShake()
    {
        rectTransform.anchoredPosition = originalPosition;
        rectTransform.localRotation = originalRotation;

        float angle = hoverAngle;

        for (int i = 0; i < 4; i++)
        {
            float targetAngle = (i % 2 == 0)
                ? angle
                : -angle;

            yield return RotateTo(
                originalRotation * Quaternion.Euler(
                    0f, 0f, targetAngle),
                hoverHalfDuration
            );

            angle *= hoverDamping;
        }

        yield return RotateTo(originalRotation, 0.1f);

        rectTransform.localRotation = originalRotation;
        rectTransform.anchoredPosition = originalPosition;

        currentAnimation = null;
    }

    // 点击后先震动，再坠落并淡出
    private IEnumerator FallDown()
    {
        // 短促的受击反馈
        yield return RotateTo(
            originalRotation * Quaternion.Euler(
                0f, 0f, hitAngle),
            hitDuration * 0.5f
        );

        yield return RotateTo(
            originalRotation * Quaternion.Euler(
                0f, 0f, -hitAngle),
            hitDuration * 0.5f
        );

        float elapsed = 0f;
        Vector2 startPosition = rectTransform.anchoredPosition;
        Quaternion startRotation = rectTransform.localRotation;

        while (elapsed < fallDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / fallDuration);

            // 前段加速，后段继续下坠
            float fallT = t * t;

            rectTransform.anchoredPosition =
                startPosition +
                new Vector2(
                    fallSideways * fallT,
                    -900f * fallT
                );

            rectTransform.localRotation =
                startRotation *
                Quaternion.Euler(
                    0f, 0f, -fallAngle * fallT);

            canvasGroup.alpha = 1f - t;

            yield return null;
        }

        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    // 平滑旋转
    private IEnumerator RotateTo(
        Quaternion targetRotation,
        float duration)
    {
        Quaternion startRotation = rectTransform.localRotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);

            // SmoothStep 缓动
            t = t * t * (3f - 2f * t);

            rectTransform.localRotation =
                Quaternion.Slerp(startRotation, targetRotation, t);

            yield return null;
        }

        rectTransform.localRotation = targetRotation;
    }
}
