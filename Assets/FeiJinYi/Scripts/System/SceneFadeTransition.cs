// 场景切换淡入淡出过渡：Switch(sceneName) 代替直接调 SceneSwitcher.Switchto——全屏黑幕先淡出至黑、切场景、新场景就绪后黑幕再淡入消失；黑幕常驻跨场景（DontDestroyOnLoad）置顶渲染，过渡期间拦截点击防误触。
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 场景切换的淡入淡出过渡入口（静态类，游戏各处切场景统一走 Switch）：
    /// 全屏黑幕 0.35 秒淡出 → SceneSwitcher 异步切场景（场景中无 SceneSwitcher 时直接 LoadScene 兜底）→
    /// 新场景加载完成后黑幕 0.35 秒淡入消失。过渡期间黑幕拦截鼠标点击；重复切换请求被忽略。
    /// 黑幕画布常驻（DontDestroyOnLoad）、sortingOrder=32000 置顶；动画用未缩放时间（值班暂停时也能切换）。
    /// </summary>
    public static class SceneFadeTransition
    {
        /// <summary>是否正在过渡中（过渡期间忽略新的切换请求）</summary>
        public static bool IsTransitioning { get; private set; }

        /// <summary>带淡入淡出切换到目标场景（各控制器切场景的统一入口）</summary>
        public static void Switch(string sceneName)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning($"[SceneFade] 过渡进行中，忽略重复切换请求: {sceneName}");
                return;
            }
            var overlay = EnsureOverlay();
            if (overlay == null)
            {
                Debug.LogError("[SceneFade] 黑幕创建失败，直接加载场景: " + sceneName);
                SceneManager.LoadScene(sceneName);
                return;
            }
            IsTransitioning = true;
            overlay.Begin(sceneName, () => IsTransitioning = false);
        }

        /// <summary>获取常驻黑幕（不存在则创建：DontDestroyOnLoad 置顶画布+全屏黑图）</summary>
        private static SceneFadeOverlay EnsureOverlay()
        {
            var existing = Object.FindObjectOfType<SceneFadeOverlay>(true);
            if (existing != null) return existing;

            var go = new GameObject("SceneFadeOverlay");
            Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            // 必须挂 GraphicRaycaster，否则黑幕的 Image(raycastTarget=true) 不会真正拦截射线，黑屏阶段点击会穿透到下层场景
            go.AddComponent<GraphicRaycaster>();

            var imgGo = new GameObject("BlackImage");
            imgGo.transform.SetParent(go.transform, false);
            var irt = imgGo.AddComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var img = imgGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            return go.AddComponent<SceneFadeOverlay>();
        }
    }

    /// <summary>黑幕驱动（由 SceneFadeTransition 创建并调用，不挂到场景物体上）</summary>
    public class SceneFadeOverlay : MonoBehaviour
    {
        private const float FadeDuration = 0.35f;  // 单段淡入/淡出时长
        private const float LoadTimeout = 5f;     // 场景加载超时（防坏场景名导致永久黑屏）

        private Image image;

        /// <summary>开始一次完整过渡：淡出→切场景→淡入；完成后回调（解除过渡锁）</summary>
        public void Begin(string sceneName, System.Action onDone)
        {
            StopAllCoroutines();
            StartCoroutine(Transition(sceneName, onDone));
        }

        private IEnumerator Transition(string sceneName, System.Action onDone)
        {
            if (image == null) image = GetComponentInChildren<Image>(true);
            image.raycastTarget = true; // 黑屏阶段拦截点击

            // 1) 淡出至黑（从当前透明度开始，避免打断上一次未完成的淡入时产生透明闪烁）
            float startAlpha = image.color.a;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / FadeDuration)
            {
                SetAlpha(Mathf.Lerp(startAlpha, 1f, t));
                yield return null;
            }
            SetAlpha(1f);

            // 2) 切场景（优先 SceneSwitcher 异步加载；无实例时直接加载兜底）
            bool loaded = false;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) => loaded = true;
            SceneManager.sceneLoaded += onLoaded;
            if (SceneSwitcher.instance != null)
            {
                SceneSwitcher.instance.Switchto(sceneName);
            }
            else
            {
                Debug.LogWarning("[SceneFade] 场景中无 SceneSwitcher，直接加载: " + sceneName);
                SceneManager.LoadScene(sceneName);
            }

            float waited = 0f;
            while (!loaded && waited < LoadTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            SceneManager.sceneLoaded -= onLoaded;
            if (!loaded)
                Debug.LogError($"[SceneFade] 场景加载超时（{LoadTimeout}s）: {sceneName}，恢复画面");
            else
                yield return null; // 等新场景完成一帧初始化渲染再淡入

            // 3) 场景已就绪：立刻解除过渡锁与点击拦截，让新场景在淡入期间即可交互，
            //    否则玩家在淡入期间"迅速点击开始上班"时，切换请求会被 IsTransitioning 吞掉而卡死
            image.raycastTarget = false;
            onDone?.Invoke();

            // 4) 从黑淡入（此阶段新场景已可交互；若玩家立刻点击会触发新一次 Switch，由 Begin 的 StopAllCoroutines 接管）
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / FadeDuration)
            {
                SetAlpha(1f - t);
                yield return null;
            }
            SetAlpha(0f);
        }

        private void SetAlpha(float a)
        {
            if (image == null) return;
            var c = image.color;
            c.a = a;
            image.color = c;
        }
    }
}
