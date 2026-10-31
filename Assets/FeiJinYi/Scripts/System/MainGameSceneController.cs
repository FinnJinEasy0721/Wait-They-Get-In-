// 主场景占位控制器：显示当天日程标题与跨场景传递来的已装配规则清单，第二阶段值班执行在此基础上扩展。
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 主场景（值班阶段）占位控制器
    /// 显示保安室全屏背景 + 跨场景传递来的已装配规则书内容
    /// 第二阶段值班执行（访客判定、气泡、值班记录）将在此基础上扩展
    /// </summary>
    public class MainGameSceneController : MonoBehaviour
    {
        [Header("UI引用")]
        [SerializeField] private Text dayTitleText;         // 天数/日程标题
        [SerializeField] private Text assembledRulesText;  // 已装配规则列表

        private void Start()
        {
            // 数据管理器兜底（直接Play主场景时也能运行）
            if (GameDataManager.Instance == null)
            {
                new GameObject("GameDataManager").AddComponent<GameDataManager>();
            }

            int day = GameDataManager.Instance.CurrentDay; // 天数由 GameDataManager 7天流程状态机驱动
            UpdateDayTitle(day);
            UpdateAssembledRules();
        }

        /// <summary>按日程表更新天数标题</summary>
        private void UpdateDayTitle(int day)
        {
            if (dayTitleText == null) return;

            var schedule = GameDataManager.Instance.GetDaySchedule(day);
            dayTitleText.text = schedule != null
                ? $"第 {day} 天 · {schedule.CoreContent}"
                : $"第 {day} 天";
        }

        /// <summary>
        /// 展示选书场景传来的装配结果（Book对象由持久的GameDataManager跨场景持有）
        /// 版式：标题一行；每本书《书名》加粗居中一行，其后每条规则一行"【规则名】作用"
        /// </summary>
        private void UpdateAssembledRules()
        {
            int confirmedBooks = 0;
            int usedSlots = 0;
            var sb = new StringBuilder();
            sb.AppendLine("<b>今天是个好日子，我要当个好保安</b>");

            foreach (var book in GameDataManager.Instance.Books)
            {
                if (book.ConfirmedRules.Count == 0) continue;

                confirmedBooks++;
                sb.AppendLine($"<b>《{book.DisplayName}》</b>");
                foreach (var rule in book.ConfirmedRules)
                {
                    usedSlots += rule.SlotCost;
                    string effect = string.IsNullOrEmpty(rule.Description) ? rule.CheckAttribute : rule.Description;
                    sb.AppendLine($"【{rule.Name}】{effect}");
                }
            }

            sb.Append($"脑容量占用：{usedSlots}/{GameConstants.InitialBrainSlots}");

            if (assembledRulesText != null)
                assembledRulesText.text = sb.ToString();

            if (confirmedBooks == 0)
                Debug.LogWarning("[MainGame] 没有已装配的规则（若直接Play本场景属正常；正式流程请从ChooseBook场景进入）");
            else
                Debug.Log($"[MainGame] 主场景就绪：{confirmedBooks} 本书已装配，占用槽位 {usedSlots}/{GameConstants.InitialBrainSlots}");
        }
    }
}
