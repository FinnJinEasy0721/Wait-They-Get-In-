// 悬停换图组件：鼠标悬停时把所在 Image 换成悬停图，移出或失能时恢复原图。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 悬停换图
    /// 挂在带 Image 的物体上：IPointerEnter 切换为 hoverSprite，IPointerExit / 物体失能时恢复 normalSprite
    /// </summary>
    public class HoverSpriteSwapper : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("常态显示的图（留空则自动取 Image 当前 sprite）")]
        [SerializeField] private Sprite normalSprite;
        [Tooltip("悬停时显示的图")]
        [SerializeField] private Sprite hoverSprite;

        private Image image;
        private bool swapped;

        private void Awake()
        {
            image = GetComponent<Image>();
            if (normalSprite == null && image != null)
                normalSprite = image.sprite;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (image == null || hoverSprite == null) return;
            image.sprite = hoverSprite;
            swapped = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Restore();
        }

        /// <summary>恢复常态图（幂等，仅在已切换时生效）</summary>
        private void Restore()
        {
            if (image == null || !swapped) return;
            if (normalSprite != null)
                image.sprite = normalSprite;
            swapped = false;
        }

        private void OnDisable()
        {
            Restore();
        }
    }
}
