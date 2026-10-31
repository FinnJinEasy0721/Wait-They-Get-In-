// 指针音效组件：挂到按钮或任意可点击 UI（书本、卡片等）上，悬停/点击/移开（未点击）时播放对应音效；音效剪辑在预制体中配置，留空则不播
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 指针音效：响应指针进入（悬停）、点击、移开三种交互播放对应音效。
/// 不要求物体带 Button，书本/卡片这类自行处理点击的 UI 也可以挂；指针事件沿父链冒泡，挂在根节点即可覆盖其子图形。
/// 移开音效只在"悬停后未点击就移开"时播放；点击后移开（如切场景/按钮失活）不再补播。
/// 音效走 AudioManager.PlaySfx，自动受设置面板的"音效音量"控制。
/// </summary>
public class ButtonSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IPointerExitHandler
{
    [Header("悬停 / 点击 / 移开音效（留空不播）")]
    [SerializeField] private AudioClip hoverClip;
    [SerializeField] private AudioClip clickClip;
    [SerializeField] private AudioClip exitClip;

    private bool clickedThisHover;   // 本次悬停期间是否已点击（点击后的移开不再补播移开音效）

    public AudioClip HoverClip { get { return hoverClip; } set { hoverClip = value; } }
    public AudioClip ClickClip { get { return clickClip; } set { clickClip = value; } }
    public AudioClip ExitClip { get { return exitClip; } set { exitClip = value; } }

    public void OnPointerEnter(PointerEventData eventData)
    {
        clickedThisHover = false;
        if (hoverClip != null) AudioManager.EnsureInstance().PlaySfx(hoverClip);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        clickedThisHover = true;
        if (clickClip != null) AudioManager.EnsureInstance().PlaySfx(clickClip);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 点击后（按钮失活/切场景）触发的 Exit 不播移开音效
        if (clickedThisHover) { clickedThisHover = false; return; }
        if (exitClip != null) AudioManager.EnsureInstance().PlaySfx(exitClip);
    }
}
