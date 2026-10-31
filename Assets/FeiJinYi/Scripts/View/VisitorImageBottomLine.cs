// 访客立绘图像底边线（编辑器对位工具）：挂在访客预制体上，自动解码立绘PNG并在"画面内容的最低一排像素"处生成一条红色横线（宽度=内容横向范围），便于肉眼对齐窗台等场景参照物。仅编辑器内显示，打包自动隐藏。
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 在访客立绘的图像内容底边处显示一条红色横线（编辑器专用调试工具）。
    /// 线的位置=立绘PNG中最低不透明像素行映射到RectTransform后的高度，
    /// 与立绘矩形底边（矩形框底）不同——立绘PNG自带透明边，内容底边才是肉眼看到的"图片最下面"。
    /// 用途：拖动/调整访客位置时对齐窗台等参照物。运行打包版不显示。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class VisitorImageBottomLine : MonoBehaviour
    {
        [Tooltip("线粗细（画布单位）")]
        [SerializeField] private float lineThickness = 4f;
        [Tooltip("线颜色")]
        [SerializeField] private Color lineColor = new Color(1f, 0.15f, 0.15f, 0.95f);

        private const string LineChildName = "ContentBottomLine";
        private Image cachedPortrait;
        private Sprite cachedSprite;
        private Vector2 cachedSize;
        private Sprite lastComputedSprite;

        private void OnEnable()
        {
            cachedPortrait = GetComponent<Image>();
#if UNITY_EDITOR
            EnsureLine();
            Recompute();
#else
            // 打包版：隐藏调试线
            var line = transform.Find(LineChildName);
            if (line != null) line.gameObject.SetActive(false);
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (isActiveAndEnabled) Recompute();
        }

        private void Update()
        {
            // 编辑模式下拖动不需要重算（线是子物体跟随移动）；仅当立绘/尺寸变化时重算
            if (Application.isPlaying) return;
            var img = GetComponent<Image>();
            var rt = (RectTransform)transform;
            var sp = img != null ? img.sprite : null;
            if ((img != cachedPortrait) || (sp != cachedSprite) || (rt.sizeDelta != cachedSize))
            {
                cachedPortrait = img;
                cachedSprite = sp;
                cachedSize = rt.sizeDelta;
                Recompute();
            }
        }

        /// <summary>确保线子物体存在（不存在则创建）</summary>
        public void EnsureLine()
        {
            var lineT = transform.Find(LineChildName);
            if (lineT == null)
            {
                var lineGo = new GameObject(LineChildName);
                lineGo.transform.SetParent(transform, false);
                var lrt = lineGo.AddComponent<RectTransform>();
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f); // 底边中心锚点
                lrt.pivot = new Vector2(0.5f, 0.5f);
                var img = lineGo.AddComponent<Image>();
                img.raycastTarget = false;
                lineT = lineGo.transform;
            }
            var line = lineT.GetComponent<Image>();
            line.color = lineColor;
        }

        /// <summary>解码立绘PNG，把线放到图像内容底边处（宽度=内容横向范围）</summary>
        public void Recompute()
        {
            EnsureLine();
            var lineRt = transform.Find(LineChildName) as RectTransform;
            if (lineRt == null) return;

            var img = GetComponent<Image>();
            var sprite = img != null ? img.sprite : null;
            lastComputedSprite = sprite;
            var rt = (RectTransform)transform;

            ContentBounds? bounds = sprite != null ? ReadSpriteContentBounds(sprite) : (ContentBounds?)null;
            if (bounds == null)
            {
                // 解码失败兜底：横跨整个矩形、贴矩形底边
                lineRt.anchoredPosition = new Vector2(0f, lineThickness * 0.5f);
                lineRt.sizeDelta = new Vector2(rt.rect.width, lineThickness);
                return;
            }

            var b = bounds.Value;
            // 立绘绘制区（preserveAspect 下等比缩放居中）
            float ppu = sprite.pixelsPerUnit <= 0f ? 100f : sprite.pixelsPerUnit;
            float sw = sprite.rect.width / ppu, sh = sprite.rect.height / ppu;
            float rectW = rt.rect.width, rectH = rt.rect.height;
            float scale = img.preserveAspect ? Mathf.Min(rectW / sw, rectH / sh) : 0f;
            if (!img.preserveAspect) scale = Mathf.Min(rectW / sw, rectH / sh); // 非等比模式下按高度方向推（立绘均为3:4满铺，两者一致）
            float drawnW = sw * scale, drawnH = sh * scale;
            float offX = (rectW - drawnW) * 0.5f, offY = (rectH - drawnH) * 0.5f;

            // 内容底边/横向范围 → 矩形局部坐标（原点=pivot；线锚点在矩形底边中心，取相对量）
            float contentBottomY = offY + b.bottom * drawnH;
            float contentW = (b.right - b.left) * drawnW;
            float contentCenterX = offX + (b.left + b.right) * 0.5f * drawnW;

            lineRt.anchoredPosition = new Vector2(contentCenterX, contentBottomY);
            lineRt.sizeDelta = new Vector2(Mathf.Max(20f, contentW), lineThickness);
        }

        /// <summary>解码立绘PNG字节，返回内容范围（0~1归一化，bottom=距矩形底边的比例）</summary>
        private static ContentBounds? ReadSpriteContentBounds(Sprite sprite)
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(sprite.texture);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2);
            try
            {
                if (!tex.LoadImage(bytes)) return null;
                var cols = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                var rect = sprite.rect; // 纹理坐标（y自下而上）
                int x0 = Mathf.Clamp((int)rect.x, 0, w - 1);
                int x1 = Mathf.Clamp((int)(rect.x + rect.width) - 1, 0, w - 1);
                int y0 = Mathf.Clamp((int)rect.y, 0, h - 1);
                int y1 = Mathf.Clamp((int)(rect.y + rect.height) - 1, 0, h - 1);

                int lowest = -1, left = int.MaxValue, right = -1;
                for (int y = y0; y <= y1 && lowest < 0; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (cols[y * w + x].a > 16)
                        {
                            lowest = y;
                            break;
                        }
                    }
                }
                if (lowest < 0) return null;
                // 内容横向范围取整体bbox（在sprite区域内扫一遍）
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (cols[y * w + x].a > 16) { if (x < left) left = x; if (x > right) right = x; }

                return new ContentBounds
                {
                    bottom = (lowest - rect.y) / rect.height,
                    left = (left - rect.x) / rect.width,
                    right = (right - rect.x) / rect.width,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private struct ContentBounds
        {
            public float bottom; // 0=矩形底边, 1=顶边（内容最低像素行位置）
            public float left;
            public float right;
        }
#endif
    }
}
