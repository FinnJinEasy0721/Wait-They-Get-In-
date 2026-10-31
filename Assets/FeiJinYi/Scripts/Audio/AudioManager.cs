// 全局音频管理：常驻单例，背景音乐与音效两路音量通道，供后续音频资产接入
using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private AudioSource bgmSource;
    private AudioSource sfxSource;
    private Coroutine bgmFadeRoutine;   // 背景音乐渐入协程

    // 背景音乐音量（0~1），修改立即生效并保存
    public float BgmVolume
    {
        get { return GameSettings.BgmVolume; }
        set
        {
            GameSettings.BgmVolume = value;
            StopBgmFade();              // 手动改音量时取消正在进行的渐入
            if (bgmSource != null)
                bgmSource.volume = GameSettings.BgmVolume;
        }
    }

    // 音效音量（0~1），修改立即生效并保存
    public float SfxVolume
    {
        get { return GameSettings.SfxVolume; }
        set
        {
            GameSettings.SfxVolume = value;
            if (sfxSource != null)
                sfxSource.volume = GameSettings.SfxVolume;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = GameSettings.BgmVolume;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = GameSettings.SfxVolume;
    }

    // 确保单例存在（不存在则自动创建），供任意场景按需调用
    public static AudioManager EnsureInstance()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("AudioManager");
            go.AddComponent<AudioManager>();
        }
        return Instance;
    }

    // 播放背景音乐（循环），传入空则停止；fadeInSeconds>0 时从静音渐入到当前音量（首次进入场景的渐入感）
    public void PlayBgm(AudioClip clip, float fadeInSeconds = 0f)
    {
        if (clip == null)
        {
            StopBgm();
            return;
        }
        // 同一首已在播放则跳过，避免重复触发渐入
        if (bgmSource.clip == clip && bgmSource.isPlaying)
            return;

        StopBgmFade();
        bgmSource.clip = clip;
        if (fadeInSeconds <= 0f)
        {
            bgmSource.volume = GameSettings.BgmVolume;
            bgmSource.Play();
        }
        else
        {
            bgmSource.volume = 0f;
            bgmSource.Play();
            bgmFadeRoutine = StartCoroutine(FadeInBgmRoutine(fadeInSeconds));
        }
    }

    // 停止背景音乐
    public void StopBgm()
    {
        StopBgmFade();
        bgmSource.Stop();
        bgmSource.clip = null;
    }

    // 播放一条一次性音效
    public void PlaySfx(AudioClip clip)
    {
        if (clip != null)
            sfxSource.PlayOneShot(clip);
    }

    // 背景音乐渐入协程：音量 0 → 当前 BGM 音量，用 unscaledDeltaTime 保证暂停/低倍速时也能完成
    private IEnumerator FadeInBgmRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            bgmSource.volume = GameSettings.BgmVolume * Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        bgmSource.volume = GameSettings.BgmVolume;
        bgmFadeRoutine = null;
    }

    // 停止正在进行的背景音乐渐入
    private void StopBgmFade()
    {
        if (bgmFadeRoutine != null)
        {
            StopCoroutine(bgmFadeRoutine);
            bgmFadeRoutine = null;
        }
    }
}
