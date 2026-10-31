// 值班日流程控制器：按节拍并行生成访客（每"访客生成间隔秒"一位、场上空了立即补位），保安一次处理队首一位、其余排队，检测前台拥挤，全部处理完后弹出日结算评分，并提供进入下一天（回ReadNews看资讯）的入口。
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 值班日流程控制器
    /// 编排：访客生成（开班第一位立即出场，之后每"访客生成间隔秒"固定出下一位，场上空了且还有人→立即补位；
    ///       多位访客可同时在场：队首站停留点被保安处理，其余在左侧排队等候，处理完下一位上前）→
    ///       保安逐条执行规则（GuardBrain，一次只处理队首一位，判定/结算口径不变）→
    ///       结果记录（值班记录，含"是否被惹生气过"）→ 拥挤检测（场上同时≥2位访客：记一行值班记录+当天历史）→
    ///       全部结束后显示日结算
    /// </summary>
    public class DutyFlowController : MonoBehaviour
    {
        [Header("场景引用")]
        [SerializeField] private RectTransform visitorLayer;      // 访客层（画布第一子物体，渲染在窗景背后）
        [SerializeField] private RectTransform stopPoint;         // 停留点（窗内，保安处理位）

        [Header("UI引用")]
        [SerializeField] private Text guardBubbleText;            // 保安气泡
        [SerializeField] private Text dutyLogText;                // 值班记录
        [SerializeField] private DutyLogScroll dutyLogScroll;     // 值班记录滚动组件（悬停滚轮翻看）
        [SerializeField] private GameObject settlementPanel;      // 日结算面板
        [SerializeField] private AudioClip settlementSfx;         // 结算面板弹出音效（一次性）
        [SerializeField] private SettlementPanelView settlementView; // 日结算面板视图（星级背景/星星飞入/放行情况动效）
        [SerializeField] private Text settlementTitleText;        // 日结算标题（第N天）
        [SerializeField] private Button nextDayButton;            // 结算面板"下一天"按钮
        [SerializeField] private Text nextDayLabel;               // "下一天"按钮文字（最后一天显示"结束试玩"）

        /// <summary>排队站位间距（画布单位）：队首站停留点，其余依次向左排开（访客宽432，保留15叠边防遮挡）</summary>
        private const float QueueSpacing = 417f;

        private GuardBrain brain;
        private VisitorSpawner spawner;
        private readonly List<Visitor> dayVisitors = new List<Visitor>();

        /// <summary>当前天数：由 GameDataManager 跨场景天数状态机驱动（1~试用期天数）</summary>
        private int dayNumber => GameDataManager.Instance != null ? GameDataManager.Instance.CurrentDay : 1;

        /// <summary>
        /// 保安判定引擎（懒构建：首位访客到达停留点时收集已装配规则，确保跨场景数据就绪）
        /// 同时把装配规则注册进 GameDataManager（装配来源的识别状态按当前装配实时判定）
        /// </summary>
        private GuardBrain Brain
        {
            get
            {
                if (brain == null)
                {
                    var assembledRules = new List<Rule>();
                    foreach (var book in GameDataManager.Instance.Books)
                    {
                        foreach (var rule in book.ConfirmedRules)
                            assembledRules.Add(rule);
                    }
                    GameDataManager.Instance.SetActiveAssembly(assembledRules);
                    brain = new GuardBrain(assembledRules, guardBubbleText, () => onField.Count);
                }
                return brain;
            }
        }

        // ===== 值班运行状态（并行模式）=====

        private readonly List<VisitorController> onField = new List<VisitorController>();   // 场上全部访客（走路/排队/处理中/离场中，销毁时移除）
        private readonly List<VisitorController> queue = new List<VisitorController>();     // 排队中（队首将站上停留点）
        private readonly List<VisitorController> leaving = new List<VisitorController>();  // 离场中
        private readonly Dictionary<VisitorController, Coroutine> activeMoves = new Dictionary<VisitorController, Coroutine>(); // 各访客的排队移动协程（可打断重排）
        private VisitorController processing;                                                // 正被保安处理的访客（一次仅一位）
        private float spawnTimer;
        private bool crowdActive;      // 当前是否处于拥挤（场上≥2人；人数降回<2复位，可再次触发）
        private bool wasCrowdedToday;  // 当天是否发生过拥挤（记入按天历史，分支条件用）

        // ===== 编辑器面板只读接口 =====

        /// <summary>当前天数（编辑器面板显示用）</summary>
        public int DayNumber => dayNumber;

        /// <summary>当前正在处理的访客（无则队首，编辑器面板显示用）</summary>
        public VisitorController CurrentFrontVisitor => processing != null ? processing : (queue.Count > 0 ? queue[0] : null);

        /// <summary>当前场上访客数（拥挤判定与"拥堵请示"规则演出用）</summary>
        public int OnFieldVisitorCount => onField.Count;

        /// <summary>当天是否发生过前台拥挤（编辑器面板显示用）</summary>
        public bool WasCrowdedToday => wasCrowdedToday;

        /// <summary>日结算面板是否已显示（值班结束标志）</summary>
        public bool IsSettlementShown => settlementPanel != null && settlementPanel.activeSelf;

        /// <summary>已处理访客的结果记录（编辑器面板显示用）</summary>
        public IReadOnlyList<VisitorResult> Results => results;

        // 结算数据
        public class VisitorResult
        {
            public Visitor Visitor;
            public DutyOutcome Outcome;
            public bool WasAngered;   // 处理过程中是否被惹生气过（锁存：即使最终被安抚放行也记）
        }
        private readonly List<VisitorResult> results = new List<VisitorResult>();
        private readonly List<string> logLines = new List<string>();

        private void Start()
        {
            if (GameDataManager.Instance == null)
            {
                new GameObject("GameDataManager").AddComponent<GameDataManager>();
            }

            if (settlementPanel != null) settlementPanel.SetActive(false);
            if (guardBubbleText != null) guardBubbleText.text = "";
            if (nextDayButton != null)
            {
                nextDayButton.onClick.RemoveAllListeners();
                nextDayButton.onClick.AddListener(GoToNextDay);
            }

            // 当天生效内容 = 日程表叠加命中分支（昨日播报行、访客序列含插入/移除；保安规则书在首次处理访客时懒构建，见 Brain 属性）
            var plan = GameDataManager.Instance.GetEffectiveDayPlan(dayNumber);
            foreach (var line in plan.FeedbackLines)
                AppendLog(line);
            foreach (var id in plan.VisitorIds)
            {
                var visitor = GameDataManager.Instance.GetVisitorById(id);
                if (visitor != null) dayVisitors.Add(visitor);
            }
            spawner = new VisitorSpawner(dayVisitors);

            AppendLog($"第{dayNumber}天 值班开始");
            StartCoroutine(DayRoutine());
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            logLines.Add(line);
            if (dutyLogText != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("【值班记录】");
                foreach (var l in logLines) sb.AppendLine(l);
                dutyLogText.text = sb.ToString();
            }
            if (dutyLogScroll != null) dutyLogScroll.Refresh();
        }

        // ===== 主流程 =====

        private IEnumerator DayRoutine()
        {
            bool spawnedAny = false;
            while (spawner.HasPending || queue.Count > 0 || processing != null || leaving.Count > 0)
            {
                // 1) 生成节拍：开班第一位立即出；之后每"访客生成间隔秒"出一位；场上空了且还有人→立即补位
                if (spawner.HasPending)
                {
                    if (!spawnedAny || onField.Count == 0 || spawnTimer >= GameConstants.VisitorSpawnInterval)
                    {
                        SpawnVisitor(spawner.SpawnNext());
                        spawnTimer = 0f;
                        spawnedAny = true;
                    }
                }
                spawnTimer += Time.deltaTime * GameConstants.DutySpeedMultiplier;

                // 2) 保安空闲且队首已站上停留点 → 开始处理（一次只处理一位）
                if (processing == null && queue.Count > 0 && queue[0].State == VisitorState.AtStop)
                {
                    processing = queue[0];
                    queue.RemoveAt(0);
                    StartCoroutine(ProcessCurrent());
                }

                // 3) 拥挤检测：场上人数从<2变≥2的上升沿记一行值班记录（再次发生各记一行）
                if (onField.Count >= 2 && !crowdActive)
                {
                    crowdActive = true;
                    wasCrowdedToday = true;
                    AppendLog("前台出现拥挤");
                }
                else if (onField.Count < 2)
                {
                    crowdActive = false;
                }

                yield return null;
            }

            ShowSettlement();
        }

        private void SpawnVisitor(Visitor data)
        {
            var visitor = VisitorController.Create(data, visitorLayer);
            var rt = visitor.Rect;
            Vector2 stopPos = GetAnchoredPos(stopPoint);
            // 从画面左侧窗外走来
            rt.anchoredPosition = new Vector2(stopPos.x - 1500f, stopPos.y);

            onField.Add(visitor);
            queue.Add(visitor);
            AppendLog($"{data.Name} 来到前台");
            RefreshQueueTargets();
        }

        /// <summary>重排队列站位：无人处理时队首站停留点、其余依次向左排开；有人处理时全体退后一位</summary>
        private void RefreshQueueTargets()
        {
            Vector2 stopPos = GetAnchoredPos(stopPoint);
            for (int i = 0; i < queue.Count; i++)
            {
                var v = queue[i];
                if (v == null) continue;
                int slot = i + (processing != null ? 1 : 0);
                Vector2 target = slot == 0 ? stopPos : new Vector2(stopPos.x - QueueSpacing * slot, stopPos.y);
                MoveToQueueTarget(v, target);
            }
        }

        private void MoveToQueueTarget(VisitorController v, Vector2 target)
        {
            StopMove(v);
            v.SetState(VisitorState.WalkingToStop);
            activeMoves[v] = StartCoroutine(WalkVisitor(v, target, () =>
            {
                if (v == null) return;
                activeMoves.Remove(v);
                // 到位回调：队首+保安空闲+就站在停留点 → 待处理，否则排队等待
                bool atStop = processing == null && queue.Count > 0 && queue[0] == v
                    && Vector2.Distance(v.Rect.anchoredPosition, GetAnchoredPos(stopPoint)) < 2f;
                v.SetState(atStop ? VisitorState.AtStop : VisitorState.WaitingInQueue);
            }));
        }

        private void StopMove(VisitorController v)
        {
            if (v != null && activeMoves.TryGetValue(v, out var co))
            {
                StopCoroutine(co);
                activeMoves.Remove(v);
            }
        }

        private IEnumerator WalkVisitor(VisitorController visitor, Vector2 target, System.Action onArrived)
        {
            yield return visitor.MoveTo(target);
            onArrived?.Invoke();
        }

        // ===== 保安处理队首访客 =====

        private IEnumerator ProcessCurrent()
        {
            var visitor = processing;
            yield return StartCoroutine(Brain.ProcessVisitor(visitor, outcome =>
            {
                results.Add(new VisitorResult { Visitor = visitor.Data, Outcome = outcome, WasAngered = visitor.IsAngry });
                AppendLog($"{visitor.Data.Name}：{OutcomeText(outcome)}");
                StartLeave(visitor, outcome);
            }));
            // 处理协程结束=队首空出，下一位上前（离场者不影响队列推进）
            processing = null;
            RefreshQueueTargets();
        }

        /// <summary>按结果让访客离场（放行→右侧进公司；逃跑/离开/拦截→往回走），离场不阻塞队列</summary>
        private void StartLeave(VisitorController visitor, DutyOutcome outcome)
        {
            leaving.Add(visitor);
            visitor.SetState(outcome == DutyOutcome.Fled ? VisitorState.Fleeing
                : outcome == DutyOutcome.LeftAngry ? VisitorState.Leaving
                : VisitorState.Released);
            StartCoroutine(LeaveField(visitor, outcome));
        }

        private IEnumerator LeaveField(VisitorController visitor, DutyOutcome outcome)
        {
            Vector2 stopPos = GetAnchoredPos(stopPoint);
            Vector2 exit;
            float speed = 0f;
            switch (outcome)
            {
                case DutyOutcome.Released:
                case DutyOutcome.ReleasedByConsult:
                    exit = new Vector2(stopPos.x + 1600f, stopPos.y);
                    break;
                case DutyOutcome.Fled:
                    exit = new Vector2(visitor.Rect.anchoredPosition.x - 1600f, stopPos.y);
                    speed = GameConstants.VisitorMoveSpeed * 1.6f; // 逃跑加速
                    break;
                default: // LeftAngry / BlockedAlert
                    exit = new Vector2(stopPos.x - 1600f, stopPos.y);
                    break;
            }

            StopMove(visitor);
            yield return visitor.MoveTo(exit, speed);
            visitor.ShowBubble("");
            onField.Remove(visitor);
            leaving.Remove(visitor);
            Destroy(visitor.gameObject);
        }

        private static Vector2 GetAnchoredPos(RectTransform marker)
        {
            return marker != null ? marker.anchoredPosition : Vector2.zero;
        }

        private static string OutcomeText(DutyOutcome outcome)
        {
            switch (outcome)
            {
                case DutyOutcome.Released: return "规则判定通过，正常放行";
                case DutyOutcome.Fled: return "被规则识破，转身逃跑（拦截成功）";
                case DutyOutcome.BlockedAlert: return "警戒拦截，未放行";
                case DutyOutcome.ReleasedByConsult: return "请示上级安抚后放行";
                case DutyOutcome.LeftAngry: return "愤怒离开（将收到投诉）";
                default: return outcome.ToString();
            }
        }

        // ===== 日结算 =====

        private void ShowSettlement()
        {
            int errorCount = 0;

            // 白色区访客放行情况行：按对错口径逐条生成（共享判定：结算行、按天历史、分支条件同口径）
            var lines = new List<SettlementPanelView.ResultLine>();
            var historyRecords = new List<DayHistoryRecord>();
            foreach (var r in results)
            {
                var verdict = DutyVerdictEvaluator.GetVerdict(r.Visitor, r.Outcome);
                bool correct = verdict == DutyVerdict.CorrectRelease || verdict == DutyVerdict.CorrectBlock;
                lines.Add(new SettlementPanelView.ResultLine
                {
                    Text = VerdictLineText(r.Visitor.Name, verdict),
                    Correct = correct
                });
                if (!correct) errorCount++;
                historyRecords.Add(new DayHistoryRecord(r.Visitor.ID, r.Visitor.Name, r.Outcome, verdict, r.WasAngered));
            }

            // 星级=计错扣星制：每个错误（错误放行/错误拦截，含逼走访客）扣1星，最低0星，全对3星
            int stars = Mathf.Max(0, 3 - errorCount);

            // 记录按天历史（分支系统的条件判定数据源；同一天重复结算以后者覆盖）
            var todayHistory = new DayHistory(dayNumber, stars, historyRecords, wasCrowdedToday);
            GameDataManager.Instance.RecordDayHistory(todayHistory);

            // 备注小字行=预演明天的值班播报（分支表"值班播报"命中事件的内容首句）；最后一天无明天则无备注
            var notes = dayNumber >= GameConstants.TrialDays
                ? new List<string>()
                : GameDataManager.Instance.PreviewFeedbackLines(dayNumber + 1, todayHistory);

            if (settlementTitleText != null)
                settlementTitleText.text = $"—— 第 {dayNumber} 天值班结算 ——";
            if (nextDayLabel != null)
                nextDayLabel.text = dayNumber >= GameConstants.TrialDays ? "结束试玩" : $"下一天（第 {dayNumber + 1} 天）";
            if (settlementPanel != null) settlementPanel.SetActive(true);
            // 弹出结算画面时播放一次音效
            if (settlementSfx != null) global::AudioManager.EnsureInstance().PlaySfx(settlementSfx);
            if (settlementView != null)
                settlementView.Show(stars, lines, notes);

            AppendLog($"第{dayNumber}天 值班结束");
            Debug.Log($"[DutyFlow] 第{dayNumber}天值班流程完成，共处理 {results.Count} 位访客，{stars}/3星，拥挤{(wasCrowdedToday ? "发生过" : "未发生")}");
        }

        /// <summary>结算行文字："xxx 正确放行/拦截" 或 "xxx 被你错误放行/拦截"</summary>
        private static string VerdictLineText(string name, DutyVerdict verdict)
        {
            switch (verdict)
            {
                case DutyVerdict.CorrectRelease: return $"{name} 正确放行";
                case DutyVerdict.WrongRelease: return $"{name} 被你错误放行";
                case DutyVerdict.CorrectBlock: return $"{name} 正确拦截";
                default: return $"{name} 被你错误拦截";
            }
        }

        /// <summary>
        /// 结算面板按钮：未满试用期→天数+1后回 ReadNews 看新一天资讯（再重新选书值班）；
        /// 试用期最后一天→回主菜单
        /// </summary>
        public void GoToNextDay()
        {
            if (GameDataManager.Instance.CurrentDay >= GameConstants.TrialDays)
            {
                Debug.Log($"[DutyFlow] 第{GameDataManager.Instance.CurrentDay}天试用期结束，返回主菜单");
                SceneFadeTransition.Switch("MainMenu");
                return;
            }

            GameDataManager.Instance.AdvanceDay();
            Debug.Log($"[DutyFlow] 进入第 {GameDataManager.Instance.CurrentDay} 天（先看资讯再重新选书）");
            SceneFadeTransition.Switch("ReadNews");
        }

        // ===== 编辑器工具入口（游戏流程面板"跳过到结算"按钮调用）=====

        /// <summary>
        /// 跳过当天剩余演出并立即结算：已处理访客结果保留；
        /// 场上访客（排队/处理中/离场中）与全部待生成的按判定模拟器同口径瞬间裁决补全，然后弹出日结算面板。
        /// 结算内容（星级/历史记录/值班播报预演/下一天按钮）与正常流程完全一致。
        /// </summary>
        public void SkipToSettlementImmediately()
        {
            if (IsSettlementShown) return;
            if (GameDataManager.Instance == null) return;

            // 停掉值班相关协程（日流程循环/规则处理/排队与离场移动），清运行状态
            StopAllCoroutines();
            activeMoves.Clear();

            // 确保装配已注册（懒构建与正常流程同口径，供模拟器的识别状态判定）
            _ = Brain;

            var assembledRules = new List<Rule>();
            foreach (var book in GameDataManager.Instance.Books)
                foreach (var rule in book.ConfirmedRules)
                    assembledRules.Add(rule);

            // 场上访客：已在结果里（离场中）只清理，未处理（排队/处理中）的瞬间裁决
            var onFieldCopy = new List<VisitorController>(onField);
            foreach (var v in onFieldCopy)
            {
                if (v == null) continue;
                bool alreadyRecorded = false;
                foreach (var r in results)
                {
                    if (r.Visitor == v.Data) { alreadyRecorded = true; break; }
                }
                if (!alreadyRecorded)
                    ResolveInstantly(v.Data, assembledRules);
                onField.Remove(v);
                Destroy(v.gameObject);
            }
            queue.Clear();
            leaving.Clear();
            processing = null;

            // 全部待生成访客瞬间裁决
            while (spawner != null && spawner.HasPending)
            {
                ResolveInstantly(spawner.SpawnNext(), assembledRules);
            }

            ShowSettlement();
        }

        /// <summary>按判定模拟器同口径瞬间裁决一位访客并记入结果（跳过演出用）</summary>
        private void ResolveInstantly(Visitor visitor, List<Rule> assembledRules)
        {
            var sim = DutyOutcomeSimulator.Simulate(
                visitor,
                GameDataManager.Instance.GetBehaviours(visitor.ID),
                assembledRules,
                GameDataManager.Instance.WeightByTag,
                GameDataManager.Instance.LearnedTags,
                new HashSet<string>(GameDataManager.Instance.ActiveAssemblyRuleIds));

            results.Add(new VisitorResult { Visitor = visitor, Outcome = sim.Outcome, WasAngered = sim.IsAngry });

            AppendLog($"{visitor.Name}：{OutcomeText(sim.Outcome)}");
            Debug.Log($"[DutyFlow][跳过结算] {visitor.Name} → {sim.Outcome}（总分{sim.TotalScore}）");
        }
    }
}
