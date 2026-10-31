using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TimeController : MonoBehaviour
{
    public static TimeController intance;
    public bool isPaused { get; private set; }
    public event System.Action GamePaused;
    public event System.Action GameResumed;
    public event System.Action GameSpeedChanged;
    private void Awake()
    {
        if(intance==null)
        {
            intance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    public void Pause()
    {
        Time.timeScale = 0f;
        isPaused = true;
        GamePaused?.Invoke();
    }
    public void Resume()
    {
        Time.timeScale = 1f;
        isPaused = false;
        GameResumed?.Invoke();
    }
    public void SetSpeed(float speed)
    {
        Time.timeScale = speed;
        isPaused = false;
        GameSpeedChanged?.Invoke();
    }
}

