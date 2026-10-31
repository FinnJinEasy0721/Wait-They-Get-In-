using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{
  public static SceneSwitcher instance { get; private set; }
    private bool isSwitching = false;
    private void Awake()
    {
        instance = this;
    }
 public void Switchto(string sceneName)
    {
        if (isSwitching)
            return;
        StartCoroutine(LoadSceneAsync(sceneName));
    }
    private IEnumerator LoadSceneAsync(string sceneName)
    {
        isSwitching = true;
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
          while(!op.isDone)
        {
            yield return null;
        }
        isSwitching = false;
    }

}
