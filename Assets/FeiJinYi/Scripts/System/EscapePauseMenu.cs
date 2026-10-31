// ESC 暂停菜单：非值班场景按 ESC 打开/关闭暂停界面，打开时冻结游戏时间，提供继续游戏与返回主菜单
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// ESC 暂停菜单：按 ESC 弹出暂停界面，暂停界面中再按 ESC 关闭；也可点"继续游戏"关闭。
    /// 打开时冻结游戏时间（优先走常驻的 TimeController，缺失时直接置 timeScale=0），关闭后恢复。
    /// "返回主菜单"会记录当前天数再经淡入淡出过渡切回 MainMenu，之后可从主菜单继续当天。
    /// </summary>
    public class EscapePauseMenu : MonoBehaviour
    {
        [Header("界面引用")]
        [SerializeField] private GameObject panelRoot;      // 暂停界面根（含半透明遮罩），默认关闭
        [SerializeField] private Button continueButton;     // 继续游戏
        [SerializeField] private Button returnMenuButton;   // 返回主菜单

        [Header("暂停行为")]
        [SerializeField] private bool pauseGameTime = true; // 打开时冻结游戏时间

        private bool isOpen;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);

            if (continueButton != null)
            {
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(ResumeGame);
            }

            if (returnMenuButton != null)
            {
                returnMenuButton.onClick.RemoveAllListeners();
                returnMenuButton.onClick.AddListener(ReturnToMenu);
            }
        }

        private void Update()
        {
            // 暂停时 timeScale=0，但 Input 仍能响应 ESC
            if (Input.GetKeyDown(KeyCode.Escape))
                Toggle();
        }

        /// <summary>开关暂停界面（ESC 与界面按钮共用入口）</summary>
        public void Toggle()
        {
            if (isOpen)
                ResumeGame();
            else
                OpenPause();
        }

        /// <summary>打开暂停界面并冻结时间</summary>
        public void OpenPause()
        {
            if (isOpen) return;

            isOpen = true;
            if (panelRoot != null)
                panelRoot.SetActive(true);

            if (pauseGameTime)
                SetPaused(true);
        }

        /// <summary>关闭暂停界面并恢复时间（继续游戏按钮）</summary>
        public void ResumeGame()
        {
            if (!isOpen) return;

            isOpen = false;
            if (panelRoot != null)
                panelRoot.SetActive(false);

            if (pauseGameTime)
                SetPaused(false);
        }

        /// <summary>返回主菜单：记录当前天数后带过渡切换场景（之后可从主菜单继续当天）</summary>
        private void ReturnToMenu()
        {
            isOpen = false;
            if (panelRoot != null)
                panelRoot.SetActive(false);
            SetPaused(false);

            GameDataManager data = GameDataManager.Instance;
            if (data != null)
                GameSaveManager.SaveDay(data.CurrentDay);

            SceneFadeTransition.Switch("MainMenu");
        }

        /// <summary>冻结/恢复游戏时间（与值班场景一致：优先用常驻 TimeController）</summary>
        private void SetPaused(bool paused)
        {
            if (TimeController.intance != null)
            {
                if (paused)
                    TimeController.intance.Pause();
                else
                    TimeController.intance.Resume();
            }
            else
            {
                Time.timeScale = paused ? 0f : 1f;
            }
        }

        // 离开场景时确保时间恢复，暂停状态不跨场景保留
        private void OnDestroy()
        {
            if (TimeController.intance != null)
                TimeController.intance.Resume();
            else
                Time.timeScale = 1f;
        }
    }
}
