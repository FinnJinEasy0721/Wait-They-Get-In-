using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 鼠标悬停时平滑放大，离开时恢复原状。挂在 Button 上即可。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/UI Hover Scale")]
public class UIHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("缩放设置")]
    [Tooltip("悬停时的缩放倍率，1.1 表示放大 10%")]
    [SerializeField] private float hoverScale = 1.1f;

    [Tooltip("过渡速度，越大越快，10~20 手感较好")]
    [SerializeField] private float lerpSpeed = 14f;

    [Tooltip("按钮不可交互（interactable = false）时不响应悬停")]
    [SerializeField] private bool ignoreWhenNotInteractable = true;

    private RectTransform _rect;
    private Button _button;
    private Vector3 _baseScale;     // 原始大小
    private Vector3 _targetScale;   // 目标大小
    private bool _hovering;
    private bool _inited;

    private void Awake() => Init();

    private void OnEnable()
    {
        Init();
        _hovering = false;
        if (_rect != null)
        {
            _rect.localScale = _baseScale;
            _targetScale = _baseScale;
        }
    }

    private void OnDisable()
    {
        // 防止按钮被隐藏/切换时停留在放大状态
        if (_rect != null) _rect.localScale = _baseScale;
    }

    private void Init()
    {
        if (_inited) return;
        _inited = true;

        _rect = transform as RectTransform;
        if (_rect == null)
        {
            Debug.LogError($"[UIHoverScale] {name} 不是 UI 对象（缺少 RectTransform），脚本已禁用。", this);
            enabled = false;
            return;
        }

        _button = GetComponent<Button>();
        _baseScale = _rect.localScale;
        _targetScale = _baseScale;
    }

    private void Update()
    {
        if (_rect == null) return;

        if ((_rect.localScale - _targetScale).sqrMagnitude > 1e-8f)
        {
            // 用 unscaledDeltaTime，暂停游戏（Time.timeScale = 0）时也能正常过渡
            float t = 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime);
            _rect.localScale = Vector3.Lerp(_rect.localScale, _targetScale, t);

            if ((_rect.localScale - _targetScale).sqrMagnitude < 1e-8f)
                _rect.localScale = _targetScale;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (ignoreWhenNotInteractable && _button != null && !_button.interactable) return;
        _hovering = true;
        UpdateTarget();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovering = false;
        UpdateTarget();
    }

    private void UpdateTarget()
    {
        _targetScale = _hovering ? _baseScale * hoverScale : _baseScale;
    }
}