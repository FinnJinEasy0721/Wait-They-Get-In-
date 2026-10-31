// 调试跳转驱动器（编辑器工具，静态状态机）：从ReadNews/ChooseBook自动推进到主场景并瞬间弹出结算，由游戏流程面板Update驱动Tick。
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SecurityGuardSimulator.EditorTools
{
    /// <summary>
    /// 调试跳转驱动器（仅编辑器Play模式使用，静态状态机，由游戏流程面板的 Update 每帧驱动 Tick）
    /// 流程：ReadNews点"开始选书"（若在ReadNews）→ ChooseBook自动装配全部书籍（最小费用优先，容量不足用刷新换候选）
    ///       → MainGameScene就绪 → 调用 DutyFlowController.SkipToSettlementImmediately 瞬间结算
    /// 面板窗口关闭会中断推进（Tick无人驱动）；任一步超时或失败自动结束并在控制台报错
    /// </summary>
    public static class DebugSkipDriver
    {
        private const int StageIdle = -1;
        private const int StageReadNews = 0;      // 等待并点击ReadNews的"开始选书"
        private const int StageWaitChooseBook = 1; // 等选书流程就绪
        private const int StageAssemble = 2;       // 自动装配全部书籍
        private const int StageWaitDuty = 3;       // 等主场景值班控制器→瞬间结算

        private static int stage = StageIdle;
        private static float stageDeadline;

        public static bool IsRunning => stage != StageIdle;

        /// <summary>启动推进（已在推进中则忽略）</summary>
        public static void Launch()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[SkipDriver] 推进已在进行中，忽略重复启动");
                return;
            }
            stage = StageReadNews;
            stageDeadline = Time.realtimeSinceStartup + 20f;
            Debug.Log("[SkipDriver] 开始自动推进到当天结算（关闭游戏流程面板会中断推进）");
        }

        /// <summary>由游戏流程面板 Update 驱动；未启动时空转</summary>
        public static void Tick()
        {
            if (stage == StageIdle) return;
            if (Time.realtimeSinceStartup > stageDeadline)
            {
                Fail($"阶段{stage}超时");
                return;
            }

            switch (stage)
            {
                case StageReadNews:
                    TickReadNews();
                    break;
                case StageWaitChooseBook:
                    TickWaitChooseBook();
                    break;
                case StageAssemble:
                    TickAssemble();
                    break;
                case StageWaitDuty:
                    TickWaitDuty();
                    break;
            }
        }

        private static void EnterStage(int next, float timeoutSeconds)
        {
            stage = next;
            stageDeadline = Time.realtimeSinceStartup + timeoutSeconds;
        }

        // 阶段0：ReadNews点"开始选书"（已在选书阶段则直接跳装配）
        private static void TickReadNews()
        {
            var flow = Object.FindObjectOfType<SelectionFlowController>();
            if (flow != null)
            {
                EnterStage(StageAssemble, 20f);
                return;
            }

            var readNews = Object.FindObjectOfType<ReadNewsSceneController>();
            if (readNews != null)
            {
                readNews.StartChoosingBooks();
                Debug.Log("[SkipDriver] 已在ReadNews点\"开始选书\"");
                EnterStage(StageWaitChooseBook, 20f);
            }
        }

        // 阶段1：等选书流程控制器与书籍生成
        private static void TickWaitChooseBook()
        {
            var flow = Object.FindObjectOfType<SelectionFlowController>();
            if (flow == null) return;
            if (flow.SpawnedBooks.Count == 0) return;   // Start尚未生成书籍，再等
            EnterStage(StageAssemble, 20f);
        }

        // 阶段2：自动装配全部书籍（同步完成，容量不足用刷新换候选）
        private static void TickAssemble()
        {
            var flow = Object.FindObjectOfType<SelectionFlowController>();
            if (flow == null) { Fail("装配阶段选书流程消失（场景切换？）"); return; }

            if (flow.CurrentState == SelectionFlowController.FlowState.Completed)
            {
                EnterStage(StageWaitDuty, 20f);
                return;
            }

            int bookIndex = 0;
            while (flow.CurrentState != SelectionFlowController.FlowState.Completed && bookIndex < flow.SpawnedBooks.Count)
            {
                var bookGO = flow.SpawnedBooks[bookIndex];
                var panel = flow.Panel;
                if (!(panel != null && panel.IsOpen && panel.BookData == bookGO.BookData))
                {
                    flow.OnBookClicked(bookGO);
                    panel = flow.Panel;
                }
                if (panel == null || !panel.IsOpen || panel.DisplayItems.Count == 0)
                {
                    Fail($"书《{bookGO.BookData.DisplayName}》面板未能打开");
                    return;
                }

                // 选规则：优先1费候选；预算=总槽位-已占用，需为后续每本未装配书本预留1槽，
                // 找不到合适候选→刷新换候选再试（防2费占满槽位把后面的书卡死）
                int totalSlots = GameConstants.InitialBrainSlots;
                int booksLeft = flow.SpawnedBooks.Count - bookIndex;
                bool selected = false;
                int attempts = 0;
                while (!selected && attempts < 10)
                {
                    var candidates = panel.DisplayItems.OrderBy(i => i.RuleData.SlotCost).ToList();
                    int budget = totalSlots - flow.GetUsedSlotsTotal();
                    int maxCost = budget - (booksLeft - 1);   // 预留后本条可用费用上限

                    var pick = candidates.FirstOrDefault(i => i.RuleData.SlotCost == 1)
                               ?? candidates.FirstOrDefault(i => i.RuleData.SlotCost <= maxCost);
                    if (pick != null && panel.ToggleItemSelection(pick)) { selected = true; break; }

                    if (!panel.TryRefreshItem(panel.DisplayItems[0])) break;
                    attempts++;
                }
                if (!selected)
                {
                    Fail($"书《{bookGO.BookData.DisplayName}》无法在预留槽位内选定规则");
                    return;
                }
                panel.Confirm();
                bookIndex++;
            }

            if (flow.CurrentState != SelectionFlowController.FlowState.Completed)
            {
                Fail("装配未全部完成");
                return;
            }
            EnterStage(StageWaitDuty, 20f);
        }

        // 阶段3：等主场景值班控制器就绪 → 瞬间结算
        private static void TickWaitDuty()
        {
            var duty = Object.FindObjectOfType<DutyFlowController>();
            if (duty == null) return;   // 场景仍在切换
            duty.SkipToSettlementImmediately();
            Debug.Log($"[SkipDriver] 已跳过到第 {duty.DayNumber} 天结算面板");
            stage = StageIdle;
        }

        private static void Fail(string reason)
        {
            Debug.LogError("[SkipDriver] 推进失败：" + reason);
            stage = StageIdle;
        }
    }
}
