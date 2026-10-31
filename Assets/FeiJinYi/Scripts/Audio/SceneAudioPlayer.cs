// 场景音频播放器：挂在场景的空物体上，负责本场景背景音乐（循环+渐入）与自动给场景内所有按钮接上悬停/点击/移开音效
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 场景音频播放器：每个场景的空物体挂一个，按场景配置各自的背景音乐。
/// 进入场景时背景音乐循环播放并渐入（首次进入有渐入感），音量受设置面板"背景音乐"滑条控制。
/// 同时扫描本场景所有按钮，为缺少 ButtonSound 的按钮补一个（用下方默认音效）；
/// 若某按钮预制体已挂 ButtonSound 并配置了专属音效，则以按钮上的为准。
/// </summary>
[DisallowMultipleComponent]
public class SceneAudioPlayer : MonoBehaviour
{
    [Header("背景音乐")]
    [Tooltip("本场景循环播放的背景音乐，留空则不播放")]
    [SerializeField] private AudioClip backgroundMusic;
    [Tooltip("进入场景时的渐入时长（秒），0 为立即播放")]
    [SerializeField] private float bgmFadeInSeconds = 1.5f;

    [Header("按钮默认音效（按钮预制体未单独配置时使用）")]
    [SerializeField] private AudioClip defaultHoverClip;
    [SerializeField] private AudioClip defaultClickClip;
    [SerializeField] private AudioClip defaultExitClip;

    private void Start()
    {
        // 背景音乐：循环 + 渐入（AudioManager 常驻，音量由设置面板控制）
        if (backgroundMusic != null)
            AudioManager.EnsureInstance().PlayBgm(backgroundMusic, bgmFadeInSeconds);

        WireAllButtons();
    }

    /// <summary>扫描本场景所有按钮（含失活的），为缺少 ButtonSound 的按钮补一个并使用默认音效</summary>
    public void WireAllButtons()
    {
        foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button == null) continue;
            // 只处理本场景的按钮（跳过预制体资源、DontDestroyOnLoad 等其他场景对象）
            if (button.gameObject.scene != gameObject.scene) continue;

            var sound = button.GetComponent<ButtonSound>();
            if (sound == null)
            {
                sound = button.gameObject.AddComponent<ButtonSound>();
                sound.HoverClip = defaultHoverClip;
                sound.ClickClip = defaultClickClip;
                sound.ExitClip = defaultExitClip;
            }
        }
    }
}
