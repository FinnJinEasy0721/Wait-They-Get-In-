// 游戏流程面板（编辑器窗口）：编辑模式展示策划JSON配置全貌与跨表校验（含属性权重表/分支事件表与判定模拟器），运行模式实时查看装配进度/值班状态与今日命中分支，并提供跳过到结算的快速调试入口。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SecurityGuardSimulator.EditorTools
{
    /// <summary>
    /// 游戏流程面板（编辑器工具）
    /// 展示整个游戏的游玩流程逻辑与运行参数：
    ///   ① 游玩流程总览（场景链，运行时高亮当前所处场景）
    ///   ② 策划配置加载信息（各表条数、缺失文件、跨表引用校验问题）
    ///   ③ 书籍与装配详情（每本书规则池、运行时玩家装配结果）
    ///   ④ 日程与访客出场顺序（每天核心内容、出场顺序、访客特征与行为库、聊天/报纸摘要）
    ///   ⑤ 属性权重表全貌（标签权重、识别来源解析）
    ///   ⑥ 分支事件表全貌（条件→效果、跨表校验）
    ///   ⑦ 权重判定模拟器（dry-run：勾选装配+选天数 → 逐访客计分明细与结局预测）
    ///   ⑧ 值班实时状态（当前访客、已处理结果、装配规则、已学标签、今日命中分支、结算历史）
    ///   ⑨ 快速调试：跳过当天直接结算（任意阶段一键推进到结算面板，测试7天循环用）
    /// 编辑模式：直接读 Resources 下的JSON展示配置全貌（不用进Play）
    /// 运行模式：读取运行时单例实时状态，随游戏自动刷新
    /// 打开方式：菜单 Window > 保安模拟器 > 游戏流程面板
    /// </summary>
    public class GameFlowInspectorWindow : EditorWindow
    {
        [MenuItem("Window/保安模拟器/游戏流程面板")]
        public static void Open()
        {
            var window = GetWindow<GameFlowInspectorWindow>();
            window.titleContent = new GUIContent("游戏流程面板");
            window.minSize = new Vector2(560, 480);
        }

        // 编辑模式缓存的数据（避免每帧重读JSON）
        private bool editDataLoaded;
        private GameDataJSON.RuleConfigJSON ruleConfig;
        private GameDataJSON.BookConfigJSON bookConfig;
        private GameDataJSON.VisitorConfigJSON visitorConfig;
        private GameDataJSON.BehaviourConfigJSON behaviourConfig;
        private GameDataJSON.ScheduleConfigJSON scheduleConfig;
        private GameDataJSON.GameConstantsJSON constantsConfig;
        private GameDataJSON.AttributeWeightConfigJSON weightConfig;
        private GameDataJSON.BranchConfigJSON branchConfig;

        // 权重制模拟器状态（编辑模式，跨重绘保留）
        private readonly HashSet<string> simAssemblyRuleIds = new HashSet<string>();   // 模拟装配的规则Id
        private readonly HashSet<VisitorAttribute> simLearnedTags = new HashSet<VisitorAttribute>(); // 模拟已学标签（投诉学会等）
        private int simDayNumber = 1;
        private Dictionary<VisitorAttribute, AttributeWeight> editWeights;
        private Dictionary<string, Rule> editRulesById;
        private Dictionary<string, List<VisitorBehaviour>> editBehavioursById;

        private Vector2 scroll;
        private float lastRepaintTime;

        private void OnEnable()
        {
            editDataLoaded = false;
            lastRepaintTime = 0f;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            // 进出Play时缓存失效，重新加载
            editDataLoaded = false;
            Repaint();
        }

        private void Update()
        {
            // 驱动"一键推进到结算"调试状态机（未启动时空转；窗口关闭会中断推进）
            DebugSkipDriver.Tick();

            // 运行中每0.5秒自动刷新一次面板
            if (EditorApplication.isPlaying && Time.realtimeSinceStartup - lastRepaintTime > 0.5f)
            {
                lastRepaintTime = Time.realtimeSinceStartup;
                Repaint();
            }
        }

        private void OnGUI()
        {
            // 运行中优先显示运行时数据；否则显示编辑模式的静态配置
            if (EditorApplication.isPlaying)
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawRuntimeSections();
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!editDataLoaded)
            {
                LoadEditData();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新配置数据", GUILayout.Width(110)))
            {
                editDataLoaded = false;
            }
            GUILayout.Label("（编辑模式：直接读取 Resources/GameData 下的JSON，无需进Play）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawFlowOverview();
            DrawConfigSummary();
            DrawBooksSection();
            DrawScheduleSection();
            DrawWeightsSection();
            DrawBranchesSection();
            DrawSimulatorSection();
            EditorGUILayout.EndScrollView();
        }

        // ===== 数据加载 =====

        private void LoadEditData()
        {
            ruleConfig = GameDataJSON.LoadRules();
            bookConfig = GameDataJSON.LoadBooks();
            visitorConfig = GameDataJSON.LoadVisitors();
            behaviourConfig = GameDataJSON.LoadBehaviours();
            scheduleConfig = GameDataJSON.LoadSchedule();
            constantsConfig = GameDataJSON.LoadConstants();
            weightConfig = GameDataJSON.LoadAttributeWeights();
            branchConfig = GameDataJSON.LoadBranches();
            editBooks = null; // 刷新时重建书籍缓存
            editWeights = null;
            editRulesById = null;
            editBehavioursById = null;
            editDataLoaded = true;
        }

        // ===== 分区1：游玩流程总览 =====

        private static readonly string[] FlowSteps =
        {
            "MainMenu（主菜单，队友场景）",
            "ReadNews（上班前：点手机看群聊天、点报纸看新闻，预知当天访客与事件）",
            "ChooseBook（选书装配：3本书各随机展示2条候选、每本选1条、脑容量3槽、可单条刷新换不同规则）",
            "MainGameScene（值班：访客严格串行生成 → 保安按书本顺序逐条执行规则演出 → 权重总分三带裁决 → 结算面板：星级/通报批评/投诉）",
        };

        private void DrawFlowOverview()
        {
            SectionHeader("① 游玩流程总览");
            string activeScene = EditorApplication.isPlaying
                ? SceneManager.GetActiveScene().name
                : null;

            for (int i = 0; i < FlowSteps.Length; i++)
            {
                string step = FlowSteps[i];
                string sceneName = ExtractSceneName(step);
                bool isCurrent = activeScene != null && sceneName == activeScene;
                var style = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    wordWrap = true,
                };
                string text = isCurrent
                    ? $"<b><color=#2EA3FF>▶ {step}</color></b>（当前场景）"
                    : $"{i + 1}. {step}";
                GUILayout.Label(text, style);
            }
        }

        private static string ExtractSceneName(string step)
        {
            int start = step.IndexOf('（');
            return start > 0 ? step.Substring(0, start) : step;
        }

        // ===== 分区2：策划配置加载信息 =====

        private void DrawConfigSummary(bool runtime = false)
        {
            SectionHeader(runtime ? "② 运行时配置加载信息" : "② 策划配置加载信息（8张表）");

            int ruleCount, bookCount, visitorCount, behaviourCount, dayCount, weightCount, branchCount;
            List<string> issues = new List<string>();

            if (runtime)
            {
                var dm = GameDataManager.Instance;
                ruleCount = dm != null ? dm.Rules.Count : 0;
                bookCount = dm != null ? dm.Books.Count : 0;
                visitorCount = dm != null ? dm.Visitors.Count : 0;
                behaviourCount = 0;
                if (dm != null) foreach (var kv in dm.Behaviours) behaviourCount += kv.Value.Count;
                dayCount = dm != null && dm.ScheduleConfig != null && dm.ScheduleConfig.schedule != null ? dm.ScheduleConfig.schedule.Count : 0;
                weightCount = dm != null ? dm.Weights.Count : 0;
                branchCount = dm != null ? dm.BranchEvents.Count : 0;

                if (dm == null)
                    issues.Add("场景中没有 GameDataManager（选书/主场景会自动创建；直接Play其他场景则无数据）");
                else if (!dm.IsAllLoaded)
                    issues.Add("配置尚未加载完成");
            }
            else
            {
                ruleCount = ruleConfig?.rules?.Count ?? 0;
                bookCount = bookConfig?.books?.Count ?? 0;
                visitorCount = visitorConfig?.visitors?.Count ?? 0;
                behaviourCount = behaviourConfig?.behaviours?.Count ?? 0;
                dayCount = scheduleConfig?.schedule?.Count ?? 0;
                weightCount = weightConfig?.weights?.Count ?? 0;
                branchCount = branchConfig?.branches?.Count ?? 0;

                if (ruleCount == 0) issues.Add("RuleConfig.json 缺失或为空——请运行 Excel转Unity数据工具.py");
                if (bookCount == 0) issues.Add("BookConfig.json 缺失或为空");
                if (visitorCount == 0) issues.Add("VisitorConfig.json 缺失或为空");
                if (behaviourCount == 0) issues.Add("BehaviourConfig.json 缺失或为空（访客将全部只出默认懵逼反应）");
                if (dayCount == 0) issues.Add("ScheduleConfig.json 缺失或为空");
                if (weightCount == 0) issues.Add("AttributeWeightConfig.json 缺失或为空（权重制判定将全部按0分）");
                if (branchCount == 0) issues.Add("BranchConfig.json 缺失或为空（分支系统不生效，各天按日程表原样进行）");
                ValidateEditData(issues);
            }

            EditorGUILayout.HelpBox(
                $"规则：{ruleCount} 条　｜　书籍：{bookCount} 本　｜　访客：{visitorCount} 个　｜　行为：{behaviourCount} 条　｜　日程：{dayCount} 天　｜　权重：{weightCount} 条　｜　分支：{branchCount} 条",
                MessageType.None);

            if (issues.Count > 0)
            {
                EditorGUILayout.HelpBox($"发现 {issues.Count} 处校验问题：", MessageType.Warning);
                foreach (var issue in issues)
                {
                    var style = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
                    GUILayout.Label($"<color=#B03A2E>✗ {issue}</color>", style);
                }
            }
            else
            {
                GUILayout.Label("✓ 跨表引用校验通过（书籍规则ID/日程访客ID/行为库访客ID均存在）", EditorStyles.miniLabel);
            }
        }

        /// <summary>编辑模式下做跨表引用校验（与Python工具、GameDataManager运行时校验对齐）</summary>
        private void ValidateEditData(List<string> issues)
        {
            var ruleIds = new HashSet<string>((ruleConfig?.rules ?? new List<GameDataJSON.RuleEntry>())
                .Where(r => r != null && !string.IsNullOrEmpty(r.Id)).Select(r => r.Id));
            var visitorIds = new HashSet<string>((visitorConfig?.visitors ?? new List<GameDataJSON.VisitorEntry>())
                .Where(v => v != null && !string.IsNullOrEmpty(v.Id)).Select(v => v.Id));

            foreach (var b in bookConfig?.books ?? new List<GameDataJSON.BookEntry>())
            {
                if (b == null) continue;
                foreach (var rid in b.RuleIds ?? new List<string>())
                {
                    if (!ruleIds.Contains(rid))
                        issues.Add($"书籍表 {b.Id}：引用了不存在的规则ID \"{rid}\"");
                }
                if ((b.RuleIds == null || b.RuleIds.Count == 0))
                    issues.Add($"书籍表 {b.Id}：规则池为空");
            }

            foreach (var day in scheduleConfig?.schedule ?? new List<GameDataJSON.ScheduleDay>())
            {
                if (day == null) continue;
                foreach (var vid in day.VisitorIds ?? new List<string>())
                {
                    if (!visitorIds.Contains(vid))
                        issues.Add($"日程表 {day.Day}：引用了不存在的访客ID \"{vid}\"");
                }
                if (day.ChatMessages == null || day.ChatMessages.Count == 0 && string.IsNullOrEmpty(day.NewsTitle))
                    issues.Add($"日程表 {day.Day}：聊天与报纸均未配置（ReadNews场景无内容可看）");
            }

            foreach (var bhv in behaviourConfig?.behaviours ?? new List<GameDataJSON.BehaviourEntry>())
            {
                if (bhv == null) continue;
                if (!visitorIds.Contains(bhv.VisitorId))
                    issues.Add($"行为库 {bhv.Id}：所属访客 \"{bhv.VisitorId}\" 不在访客数据表中");
            }

            // 权重表：标签合法/重复、来源引用完整性（规则Id/日程天数/触发标签）
            var scheduleDays = new HashSet<string>((scheduleConfig?.schedule ?? new List<GameDataJSON.ScheduleDay>())
                .Where(d => d != null && !string.IsNullOrEmpty(d.Day)).Select(d => d.Day));
            var seenTags = new HashSet<VisitorAttribute>();
            foreach (var w in weightConfig?.weights ?? new List<GameDataJSON.AttributeWeightEntry>())
            {
                if (w == null || string.IsNullOrEmpty(w.Tag)) { issues.Add("属性权重表：存在Tag为空的行"); continue; }
                if (!Enum.TryParse(w.Tag.Trim(), out VisitorAttribute tag))
                {
                    issues.Add($"属性权重表：标签 \"{w.Tag}\" 不在代码白名单（需先扩充 VisitorAttribute 枚举）");
                    continue;
                }
                if (!seenTags.Add(tag))
                    issues.Add($"属性权重表：标签 \"{w.Tag}\" 重复配置");
                var unrecText = (w.UnrecognizedWeight ?? "").Trim();
                if (!string.IsNullOrEmpty(unrecText) && !int.TryParse(unrecText, out _))
                    issues.Add($"属性权重表 {w.Tag}：未识别时权重 \"{unrecText}\" 不是整数");

                foreach (var rawPart in (w.RecognizeSources ?? "").Replace("；", ";").Split(';'))
                {
                    var part = rawPart.Trim();
                    if (string.IsNullOrEmpty(part)) continue;
                    if (part.StartsWith("装配:"))
                    {
                        var rid = part.Substring("装配:".Length).Trim();
                        if (string.IsNullOrEmpty(rid)) issues.Add($"属性权重表 {w.Tag}：来源 \"{part}\" 缺少规则ID");
                        else if (!ruleIds.Contains(rid)) issues.Add($"属性权重表 {w.Tag}：来源引用了不存在的规则ID \"{rid}\"");
                    }
                    else if (part.StartsWith("新闻:"))
                    {
                        var dayLabel = part.Substring("新闻:".Length).Trim();
                        var normalized = dayLabel.StartsWith("第") ? dayLabel : $"第{dayLabel}天";
                        if (!scheduleDays.Contains(normalized))
                            issues.Add($"属性权重表 {w.Tag}：来源引用的日程 \"{dayLabel}\" 不在7天日程表中");
                    }
                    else if (part.StartsWith("投诉:"))
                    {
                        var trig = part.Substring("投诉:".Length).Trim();
                        if (string.IsNullOrEmpty(trig)) issues.Add($"属性权重表 {w.Tag}：来源 \"{part}\" 缺少触发标签");
                        else if (!Enum.TryParse(trig, out VisitorAttribute _))
                            issues.Add($"属性权重表 {w.Tag}：投诉触发标签 \"{trig}\" 不在代码白名单");
                    }
                    else
                    {
                        issues.Add($"属性权重表 {w.Tag}：来源 \"{part}\" 语法不合法（应为 装配:规则Id / 新闻:第N天 / 投诉:标签）");
                    }
                }
            }

            // 权重覆盖完整性：访客用到的标签必须有权重行
            var weightTags = new HashSet<VisitorAttribute>();
            foreach (var w in weightConfig?.weights ?? new List<GameDataJSON.AttributeWeightEntry>())
            {
                if (w != null && Enum.TryParse((w.Tag ?? "").Trim(), out VisitorAttribute t)) weightTags.Add(t);
            }
            var usedTags = new HashSet<string>();
            foreach (var v in visitorConfig?.visitors ?? new List<GameDataJSON.VisitorEntry>())
            {
                if (v == null) continue;
                foreach (var a in v.Attributes ?? new List<string>()) usedTags.Add(a);
                if (!string.IsNullOrEmpty(v.Expression) && v.Expression != "无") usedTags.Add(v.Expression);
                if (!string.IsNullOrEmpty(v.BadgeAuthenticity) && v.BadgeAuthenticity != "无") usedTags.Add(v.BadgeAuthenticity == "真" ? "工牌为真" : "工牌为假");
                if (!string.IsNullOrEmpty(v.BadgeExpiry) && v.BadgeExpiry != "无") usedTags.Add(v.BadgeExpiry == "过期" ? "证件过期" : "证件有效");
            }
            foreach (var used in usedTags)
            {
                if (Enum.TryParse(used, out VisitorAttribute usedTag) && !weightTags.Contains(usedTag))
                    issues.Add($"属性权重表：标签 \"{used}\" 被访客使用但未配置权重行（运行时按0分计）");
            }

            // 分支事件表（宽表）：条件配对完整性/判定值/天数先后/效果非空与跨表引用（与Python工具校验对齐）
            var validVerdicts = new HashSet<string> { "正确放行", "错误放行", "正确拦截", "错误拦截", "逼走", "生气" };
            var validStates = new HashSet<string> { "当天有访客被逼走", "当天有访客被惹生气", "当天拥挤" };
            var seenEventIds = new HashSet<string>();
            var baseDayVisitors = new Dictionary<string, HashSet<string>>();
            foreach (var day in scheduleConfig?.schedule ?? new List<GameDataJSON.ScheduleDay>())
            {
                if (day == null || string.IsNullOrEmpty(day.Day)) continue;
                baseDayVisitors[day.Day] = new HashSet<string>(day.VisitorIds ?? new List<string>());
            }

            foreach (var b in branchConfig?.branches ?? new List<GameDataJSON.BranchEntry>())
            {
                if (b == null) continue;
                string eid = string.IsNullOrEmpty(b.EventId) ? "(空)" : b.EventId;
                if (string.IsNullOrEmpty(b.EventId)) issues.Add("分支表：存在EventId为空的行");
                else if (!seenEventIds.Add(b.EventId)) issues.Add($"分支表 {eid}：EventId 重复");

                if (!scheduleDays.Contains(b.ConditionDay)) issues.Add($"分支表 {eid}：条件日 \"{b.ConditionDay}\" 不在7天日程表中（应填 第N天）");
                if (!scheduleDays.Contains(b.EffectDay)) issues.Add($"分支表 {eid}：生效日 \"{b.EffectDay}\" 不在7天日程表中（应填 第N天）");
                int cnum = BranchResolver.ParseDayNumber(b.ConditionDay);
                int edayNum = BranchResolver.ParseDayNumber(b.EffectDay);
                if (cnum >= 1 && edayNum >= 1 && edayNum <= cnum) issues.Add($"分支表 {eid}：生效日 \"{b.EffectDay}\" 必须晚于条件日 \"{b.ConditionDay}\"");

                // 条件配对完整性
                string v1 = (b.Visitor1 ?? "").Trim(), r1 = (b.Verdict1 ?? "").Trim();
                string v2 = (b.Visitor2 ?? "").Trim(), r2 = (b.Verdict2 ?? "").Trim();
                string state = (b.DayState ?? "").Trim();
                if (v1.Length > 0 && r1.Length == 0) issues.Add($"分支表 {eid}：访客1填了 \"{v1}\" 但结果1没填");
                if (r1.Length > 0 && v1.Length == 0) issues.Add($"分支表 {eid}：结果1填了 \"{r1}\" 但访客1没填");
                if (v2.Length > 0 && r2.Length == 0) issues.Add($"分支表 {eid}：访客2填了 \"{v2}\" 但结果2没填");
                if (r2.Length > 0 && v2.Length == 0) issues.Add($"分支表 {eid}：结果2填了 \"{r2}\" 但访客2没填");
                if ((v2.Length > 0 || r2.Length > 0) && !(v1.Length > 0 && r1.Length > 0))
                    issues.Add($"分支表 {eid}：第二个条件不能脱离访客1+结果1单独存在");
                foreach (var pair in new[] { (v: v1, r: r1), (v: v2, r: r2) })
                {
                    if (pair.v.Length > 0 && !visitorIds.Contains(pair.v))
                        issues.Add($"分支表 {eid}：条件访客 \"{pair.v}\" 不在访客数据表中");
                    if (pair.r.Length > 0 && !validVerdicts.Contains(pair.r))
                        issues.Add($"分支表 {eid}：结果 \"{pair.r}\" 不合法（只能填 正确放行/错误放行/正确拦截/错误拦截/逼走/生气）");
                }
                if (state.Length > 0 && !validStates.Contains(state))
                    issues.Add($"分支表 {eid}：当天状态 \"{state}\" 不合法（只能填 当天有访客被逼走/当天有访客被惹生气/当天拥挤，留空=不要求）");
                if (v1.Length == 0 && v2.Length == 0 && state.Length == 0)
                    issues.Add($"分支表 {eid}：一个触发条件都没有（至少填 访客1+结果1 或 当天状态）");

                // 效果完整性：至少一列生效；报纸标题正文成对
                bool hasChat = b.ChatMessages != null && b.ChatMessages.Count > 0;
                bool hasNews = !string.IsNullOrEmpty((b.NewsTitle ?? "").Trim()) || !string.IsNullOrEmpty((b.NewsContent ?? "").Trim());
                bool hasVisitorFx = (b.AddVisitors != null && b.AddVisitors.Count > 0) || (b.RemoveVisitors != null && b.RemoveVisitors.Count > 0);
                bool hasFeedback = !string.IsNullOrEmpty((b.Feedback ?? "").Trim());
                if (!hasChat && !hasNews && !hasVisitorFx && !hasFeedback)
                    issues.Add($"分支表 {eid}：没有任何效果（新增/删除访客、新聊天、新报纸、值班记录提示至少填一项）");
                if (string.IsNullOrEmpty((b.NewsTitle ?? "").Trim()) != string.IsNullOrEmpty((b.NewsContent ?? "").Trim()))
                    issues.Add($"分支表 {eid}：新报纸的标题与正文必须成对填写（只填一个=报纸不生效）");

                // 新增/删除访客跨表引用
                foreach (var vid in b.AddVisitors ?? new List<string>())
                {
                    if (!visitorIds.Contains(vid))
                        issues.Add($"分支表 {eid}：新增访客 \"{vid}\" 不在访客数据表中");
                    if (baseDayVisitors.TryGetValue(b.EffectDay, out var dayIds) && dayIds.Contains(vid))
                        issues.Add($"分支表 {eid}：新增访客 \"{vid}\" 已在 {b.EffectDay} 原日程中（当天会重复出场，请确认）");
                }
                foreach (var vid in b.RemoveVisitors ?? new List<string>())
                {
                    if (!visitorIds.Contains(vid))
                        issues.Add($"分支表 {eid}：删除访客 \"{vid}\" 不在访客数据表中");
                }
            }
        }

        // ===== 分区：书籍与装配 =====

        private void DrawBooksSection(bool runtime = false)
        {
            SectionHeader(runtime ? "③ 书籍与玩家装配" : "③ 书籍与规则池（策划配置）");

            var books = runtime
                ? (GameDataManager.Instance != null ? GameDataManager.Instance.Books : new List<Book>())
                : BuildEditBooks();

            int initialBookCount = GameConstants.InitialBookCount;
            int confirmedCount = 0;

            for (int i = 0; i < books.Count; i++)
            {
                var book = books[i];
                var sb = new StringBuilder();
                sb.Append(runtime ? book.DisplayName : GetEditBookName(i));
                sb.Append($"（规则池 {book.AvailableRules.Count} 条：");
                sb.Append(string.Join("、", book.AvailableRules.Select(r => r.Name)));
                sb.Append("｜刷新次数 ");
                sb.Append(runtime ? book.RemainingRefreshCount : GetEditBookRefresh(i));
                sb.Append("）");

                if (runtime)
                {
                    if (i >= initialBookCount)
                    {
                        GUILayout.Label($"· [未展示] {sb}（常量\"初始书本数量\"={initialBookCount}，仅展示前{initialBookCount}本）", WrapLabel());
                        continue;
                    }
                    if (book.IsConfirmed)
                    {
                        var names = string.Join("、", book.ConfirmedRules.Select(r => r.Name));
                        GUILayout.Label($"· <color=#3C9A3C>《{sb}》→ 已装配：{names}</color>", WrapLabel());
                        confirmedCount++;
                    }
                    else
                    {
                        GUILayout.Label($"· {sb} → 未装配", WrapLabel());
                    }
                }
                else
                {
                    string note = i < initialBookCount ? "（玩家可见）" : "（常量\"初始书本数量\"之外，暂不展示）";
                    GUILayout.Label($"· {sb}{note}", WrapLabel());
                }
            }

            if (runtime)
            {
                var flow = SelectionFlowController.Instance;
                if (flow != null)
                {
                    GUILayout.Label($"装配进度：{confirmedCount}/{flow.SpawnedBooks.Count} 本　｜　" +
                        $"脑容量：{flow.GetUsedSlotsTotal()}/{GameConstants.InitialBrainSlots}　｜　" +
                        $"流程状态：{flow.CurrentState}", EditorStyles.boldLabel);
                }
                GUILayout.Label($"已装配书籍数（全部配置中）：{GameDataManager.Instance.Books.FindAll(b => b.IsConfirmed).Count}/{GameDataManager.Instance.Books.Count}", EditorStyles.miniLabel);
            }
        }

        private List<Book> editBooks;

        private List<Book> BuildEditBooks()
        {
            if (editBooks != null) return editBooks;
            editBooks = new List<Book>();
            var rules = (ruleConfig?.rules ?? new List<GameDataJSON.RuleEntry>())
                .Where(r => r != null && !string.IsNullOrEmpty(r.Id))
                .ToDictionary(r => r.Id, r => r.ToRule());
            foreach (var b in bookConfig?.books ?? new List<GameDataJSON.BookEntry>())
            {
                if (b == null) continue;
                var list = new List<Rule>();
                foreach (var rid in b.RuleIds ?? new List<string>())
                {
                    if (rules.TryGetValue(rid, out var rule)) list.Add(rule);
                }
                int refresh = b.MaxRefreshCount > 0 ? b.MaxRefreshCount : GameConstants.DefaultMaxRefreshCount;
                editBooks.Add(new Book(b.Id, b.DisplayName, list, refresh));
            }
            return editBooks;
        }

        private string GetEditBookName(int index)
        {
            var books = bookConfig?.books;
            if (books != null && index < books.Count && books[index] != null) return books[index].DisplayName;
            return "?";
        }

        private int GetEditBookRefresh(int index)
        {
            var books = bookConfig?.books;
            return books != null && index < books.Count && books[index] != null ? books[index].MaxRefreshCount : 0;
        }

        // ===== 分区：日程与访客出场顺序 =====

        private void DrawScheduleSection(bool runtime = false)
        {
            SectionHeader(runtime ? "④ 日程与访客出场顺序（当天）" : "④ 日程与访客出场顺序（7天）");

            if (runtime)
            {
                var dm = GameDataManager.Instance;
                var duty = UnityEngine.Object.FindObjectOfType<DutyFlowController>();
                int day = duty != null ? duty.DayNumber : (dm != null ? dm.CurrentDay : 1);
                var today = dm != null ? dm.GetDaySchedule(day) : null;
                if (today != null)
                {
                    DrawOneDay(today, dm, runtime: true, duty, dm.GetEffectiveDayPlan(day));
                    DrawBranchRuntimeInfo(dm, day);
                }
                else
                {
                    GUILayout.Label("当前场景没有值班流程（ReadNews/ChooseBook阶段）", EditorStyles.miniLabel);
                }
                return;
            }

            var schedules = scheduleConfig?.schedule ?? new List<GameDataJSON.ScheduleDay>();
            foreach (var day in schedules)
            {
                if (day == null) continue;
                DrawOneDay(day, null, runtime: false, null);
                EditorGUILayout.Space(4);
            }
        }

        private void DrawOneDay(GameDataJSON.ScheduleDay day, GameDataManager dm, bool runtime, DutyFlowController duty, EffectiveDayPlan plan = null)
        {
            GUILayout.Label($"【{day.Day}】{day.CoreContent}", EditorStyles.boldLabel);
            GUILayout.Label($"　任务：{(string.IsNullOrEmpty(day.PlayerTask) ? "—" : day.PlayerTask)}　｜　事件：{(string.IsNullOrEmpty(day.EventDescription) ? "—" : day.EventDescription)}　｜　特殊规则：{day.SpecialRule}", WrapLabel());

            // 聊天/报纸摘要（运行模式显示分支叠加后的生效内容）
            int chatCount = plan != null ? (plan.ChatMessages?.Count ?? 0) : (day.ChatMessages?.Count ?? 0);
            string news = plan != null
                ? (string.IsNullOrEmpty(plan.NewsTitle) ? "无" : plan.NewsTitle)
                : (string.IsNullOrEmpty(day.NewsTitle) ? "无" : day.NewsTitle);
            string mediaLabel = plan != null ? "生效资讯" : "ReadNews资讯";
            GUILayout.Label($"　{mediaLabel}：聊天{chatCount}条　｜　报纸：{news}", EditorLabel());

            // 出场顺序与访客特征（运行模式显示生效访客序列，含分支插入/移除）
            var visitorIds = plan != null ? plan.VisitorIds : day.VisitorIds;
            if (visitorIds != null && visitorIds.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("　出场顺序：");
                for (int i = 0; i < visitorIds.Count; i++)
                {
                    sb.Append($"{i + 1}.{visitorIds[i]}  ");
                }
                GUILayout.Label(sb.ToString(), WrapLabel());

                foreach (var vid in visitorIds)
                {
                    string line;
                    if (runtime && dm != null)
                    {
                        var v = dm.GetVisitorById(vid);
                        line = v == null
                            ? $"　　✗ {vid}：不存在（检查日程表）"
                            : $"　　· {v.Name}：[{string.Join("、", v.Attributes)}]　行为库{dm.GetBehaviours(vid).Count}条" + VisitorOutcomeSuffix(duty, vid);
                    }
                    else
                    {
                        var entry = (visitorConfig?.visitors ?? new List<GameDataJSON.VisitorEntry>())
                            .Find(x => x != null && x.Id == vid);
                        if (entry == null)
                        {
                            line = $"　　✗ {vid}：访客数据表中不存在";
                        }
                        else
                        {
                            var attrs = new List<string>(entry.Attributes ?? new List<string>());
                            if (!string.IsNullOrEmpty(entry.Expression) && entry.Expression != "无") attrs.Add(entry.Expression);
                            if (!string.IsNullOrEmpty(entry.BadgeAuthenticity) && entry.BadgeAuthenticity != "无") attrs.Add("工牌" + entry.BadgeAuthenticity);
                            if (!string.IsNullOrEmpty(entry.BadgeExpiry) && entry.BadgeExpiry != "无") attrs.Add("证件" + entry.BadgeExpiry);
                            int bhvCount = (behaviourConfig?.behaviours ?? new List<GameDataJSON.BehaviourEntry>())
                                .Count(x => x != null && x.VisitorId == vid);
                            line = $"　　· {entry.Name}：[{string.Join("、", attrs)}]　行为库{bhvCount}条";
                        }
                    }
                    GUILayout.Label(line, WrapLabel());
                }
            }
        }

        private static string VisitorOutcomeSuffix(DutyFlowController duty, string visitorId)
        {
            if (duty == null) return "";
            foreach (var r in duty.Results)
            {
                if (r != null && r.Visitor != null && r.Visitor.ID == visitorId)
                    return $"　→ <color=#{(r.Outcome == DutyOutcome.Released || r.Outcome == DutyOutcome.ReleasedByConsult ? "3C9A3C" : "B03A2E")}>{OutcomeText(r.Outcome)}</color>";
            }
            var current = duty.CurrentFrontVisitor;
            if (current != null && current.Data.ID == visitorId)
                return "　→ <color=#2EA3FF>正在处理…</color>";
            return "";
        }

        private static string OutcomeText(DutyOutcome outcome)
        {
            switch (outcome)
            {
                case DutyOutcome.Released: return "已放行";
                case DutyOutcome.Fled: return "逃跑（拦截成功）";
                case DutyOutcome.BlockedAlert: return "警戒拦截";
                case DutyOutcome.ReleasedByConsult: return "请示安抚后放行";
                case DutyOutcome.LeftAngry: return "愤怒离开（投诉）";
                default: return outcome.ToString();
            }
        }

        // ===== 运行模式：今日命中分支与结算历史 =====

        /// <summary>运行模式：当天命中的分支事件与已记录的按天结算历史（分支条件数据源）</summary>
        private void DrawBranchRuntimeInfo(GameDataManager dm, int day)
        {
            if (dm == null) return;
            var plan = dm.GetEffectiveDayPlan(day);

            if (plan.ActiveEvents.Count > 0)
            {
                GUILayout.Label($"　今日命中分支（{plan.ActiveEvents.Count}条，按表格行序）：", WrapLabel());
                foreach (var e in plan.ActiveEvents)
                {
                    GUILayout.Label($"　　· <color=#3C9A3C>{e.EventId}</color>{e.ConditionDay}【{e.DescribeConditions()}】→ {TruncateText(e.DescribeEffect(), 48)}", WrapLabel());
                }
            }
            else
            {
                GUILayout.Label("　今日命中分支：无（按日程表原样进行）", EditorLabel());
            }

            if (dm.DayHistories.Count > 0)
            {
                GUILayout.Label("　已记录结算历史（分支条件数据源）：", WrapLabel());
                foreach (var h in dm.DayHistories)
                {
                    if (h == null) continue;
                    var sb = new StringBuilder();
                    sb.Append($"　　· 第{h.Day}天 {h.Stars}星：");
                    bool first = true;
                    foreach (var r in h.Records)
                    {
                        if (r == null) continue;
                        if (!first) sb.Append("，");
                        first = false;
                        sb.Append($"{r.VisitorName}（{VerdictShortText(r)}）");
                    }
                    if (h.WasCrowded) sb.Append("　｜　当天拥挤");
                    GUILayout.Label(sb.ToString(), WrapLabel());
                }
            }
        }

        private static string VerdictShortText(DayHistoryRecord r)
        {
            if (r.Outcome == DutyOutcome.LeftAngry) return "错误拦截·逼走";
            string text;
            switch (r.Verdict)
            {
                case DutyVerdict.CorrectRelease: text = "正确放行"; break;
                case DutyVerdict.WrongRelease: text = "错误放行"; break;
                case DutyVerdict.CorrectBlock: text = "正确拦截"; break;
                default: text = "错误拦截"; break;
            }
            return r.WasAngered ? $"{text}·生气过" : text;
        }

        // ===== 权重表展示与判定模拟器（编辑模式）=====

        private Dictionary<VisitorAttribute, AttributeWeight> GetEditWeights()
        {
            if (editWeights != null) return editWeights;
            editWeights = new Dictionary<VisitorAttribute, AttributeWeight>();
            foreach (var entry in weightConfig?.weights ?? new List<GameDataJSON.AttributeWeightEntry>())
            {
                if (entry == null) continue;
                var weight = entry.ToAttributeWeight(); // 非法标签返回null并已在控制台告警
                if (weight != null) editWeights[weight.Tag] = weight;
            }
            return editWeights;
        }

        private Dictionary<string, Rule> GetEditRulesById()
        {
            if (editRulesById != null) return editRulesById;
            editRulesById = new Dictionary<string, Rule>();
            foreach (var entry in ruleConfig?.rules ?? new List<GameDataJSON.RuleEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                editRulesById[entry.Id] = entry.ToRule();
            }
            return editRulesById;
        }

        private Dictionary<string, List<VisitorBehaviour>> GetEditBehavioursById()
        {
            if (editBehavioursById != null) return editBehavioursById;
            editBehavioursById = new Dictionary<string, List<VisitorBehaviour>>();
            foreach (var entry in behaviourConfig?.behaviours ?? new List<GameDataJSON.BehaviourEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id) || string.IsNullOrEmpty(entry.VisitorId)) continue;
                var behaviour = entry.ToBehaviour();
                if (!editBehavioursById.TryGetValue(behaviour.VisitorID, out var list))
                {
                    list = new List<VisitorBehaviour>();
                    editBehavioursById[behaviour.VisitorID] = list;
                }
                list.Add(behaviour);
            }
            return editBehavioursById;
        }

        private string FindConstantValue(string key)
        {
            return (constantsConfig?.constants ?? new List<GameDataJSON.ConstantEntry>())
                .FirstOrDefault(c => c != null && c.key == key)?.value;
        }

        private void DrawWeightsSection()
        {
            SectionHeader("⑤ 属性权重表（权重制判定核心）");

            string releaseStr = FindConstantValue("放行阈值");
            string alertStr = FindConstantValue("警戒阈值");
            string unrecStr = FindConstantValue("未识别默认权重");
            GUILayout.Label(
                $"判定阈值（常量表）：放行 ≥ {releaseStr ?? $"(未配，默认{GameConstants.ReleaseThreshold})"}　｜　" +
                $"警戒拦截 ≤ {alertStr ?? $"(未配，默认{GameConstants.AlertThreshold})"}　｜　" +
                $"未识别默认权重 {unrecStr ?? $"(未配，默认{GameConstants.UnrecognizedDefaultWeight})"}（未识别且表内留空的标签取此值）",
                EditorLabel());

            var rulesById = GetEditRulesById();
            foreach (var kv in GetEditWeights())
            {
                var w = kv.Value;
                var sb = new StringBuilder();
                sb.Append($"· {w.Tag}：识别 {w.RecognizedWeight}　｜　");
                sb.Append(w.UnrecognizedWeight.HasValue ? $"未识别 {w.UnrecognizedWeight.Value}" : "未识别 留空→常量");
                if (w.RequiresLearning)
                {
                    sb.Append("　｜　来源：");
                    var parts = new List<string>();
                    foreach (var rid in w.AssemblySourceRuleIds)
                        parts.Add(rulesById.TryGetValue(rid, out var r) ? $"装配:{r.Name}" : $"装配:{rid}(规则不存在)");
                    foreach (var d in w.NewsSourceDays)
                        parts.Add($"新闻:第{d}天起");
                    foreach (var t in w.ComplaintTriggerTags)
                        parts.Add($"投诉:{t}访客处理不当");
                    sb.Append(string.Join("、", parts));
                }
                else
                {
                    sb.Append("　｜　天生认识（无需学习）");
                }
                GUILayout.Label(sb.ToString(), WrapLabel());
            }
            GUILayout.Label("（来源语法：装配:规则Id / 新闻:第N天 / 投诉:标签，分号分隔；识别状态决定取识别/未识别哪列权重）", EditorStyles.miniLabel);
        }

        // ===== 分区：分支事件表（编辑模式）=====

        private void DrawBranchesSection()
        {
            SectionHeader("⑥ 分支事件表（某天结算结果 → 另一天剧情变化，一行=一个完整分支）");

            var branches = branchConfig?.branches ?? new List<GameDataJSON.BranchEntry>();
            if (branches.Count == 0)
            {
                GUILayout.Label("BranchConfig.json 缺失或为空——请运行 Excel转Unity数据工具.py 生成分支事件表数据", EditorStyles.miniLabel);
                return;
            }

            foreach (var b in branches)
            {
                if (b == null) continue;
                var conds = new List<string>();
                if (!string.IsNullOrEmpty(b.Visitor1)) conds.Add($"{b.Visitor1}:{b.Verdict1}");
                if (!string.IsNullOrEmpty(b.Visitor2)) conds.Add($"{b.Visitor2}:{b.Verdict2}");
                if (!string.IsNullOrEmpty(b.DayState)) conds.Add(b.DayState);
                string condText = conds.Count > 0 ? string.Join("；", conds) : "（无条件）";

                var fx = new List<string>();
                if (b.AddVisitors != null && b.AddVisitors.Count > 0) fx.Add($"新增 {string.Join("、", b.AddVisitors)}");
                if (b.RemoveVisitors != null && b.RemoveVisitors.Count > 0) fx.Add($"删除 {string.Join("、", b.RemoveVisitors)}");
                if (b.ChatMessages != null && b.ChatMessages.Count > 0) fx.Add($"新聊天{b.ChatMessages.Count}条");
                if (!string.IsNullOrEmpty(b.NewsTitle)) fx.Add($"新报纸「{b.NewsTitle}」");
                if (!string.IsNullOrEmpty(b.Feedback)) fx.Add($"提示：{TruncateText(b.Feedback, 40)}");
                string fxText = fx.Count > 0 ? string.Join("；", fx) : "（无效果）";

                GUILayout.Label($"· {b.EventId}：{b.ConditionDay}【{condText}】→ {b.EffectDay}【{fxText}】", WrapLabel());
            }
            GUILayout.Label("（条件全为\"并且\"关系；效果=非空列生效：新增访客排当天最前/删除访客/新聊天整块替换/新报纸/值班提示；同日多行按表格行序生效，聊天/报纸后行覆盖前行）", EditorStyles.miniLabel);
        }

        private static string TruncateText(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "…";
        }

        private void DrawSimulatorSection()
        {
            SectionHeader("⑦ 权重判定模拟器（dry-run：勾选装配+选天数 → 预测当天结局）");

            int dayCount = scheduleConfig?.schedule?.Count ?? 0;
            if (dayCount == 0)
            {
                GUILayout.Label("无日程数据，无法模拟（先检查 ScheduleConfig.json）", EditorStyles.miniLabel);
                return;
            }
            simDayNumber = EditorGUILayout.IntSlider("模拟天数", simDayNumber, 1, dayCount);

            // 新闻自动授予的标签（生效日≤模拟天数，运行时由 DutyFlowController 发放）
            var newsLearned = new HashSet<VisitorAttribute>();
            foreach (var w in GetEditWeights().Values)
            {
                foreach (var d in w.NewsSourceDays)
                {
                    if (d <= simDayNumber) { newsLearned.Add(w.Tag); break; }
                }
            }

            // 模拟装配（按规则表顺序执行演出）
            GUILayout.Label("模拟装配的规则：", EditorStyles.boldLabel);
            foreach (var entry in ruleConfig?.rules ?? new List<GameDataJSON.RuleEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                bool on = simAssemblyRuleIds.Contains(entry.Id);
                bool toggled = GUILayout.Toggle(on, $"{entry.Id} {entry.Name}（请求：{entry.RequestType}）");
                if (toggled && !on) simAssemblyRuleIds.Add(entry.Id);
                else if (!toggled && on) simAssemblyRuleIds.Remove(entry.Id);
            }

            // 手动模拟"投诉学会"的标签（新闻自动项锁定显示，不可手动取消）
            GUILayout.Label("模拟已学会标签（新闻项自动锁定；投诉学会用勾选模拟）：", EditorStyles.boldLabel);
            foreach (var w in GetEditWeights().Values)
            {
                if (!w.RequiresLearning) continue;
                if (newsLearned.Contains(w.Tag))
                {
                    GUILayout.Label($"　✓ {w.Tag}（第≤{simDayNumber}天新闻已授予）", WrapLabel());
                    continue;
                }
                bool on = simLearnedTags.Contains(w.Tag);
                bool toggled = GUILayout.Toggle(on, $"　{w.Tag}（投诉学会）");
                if (toggled && !on) simLearnedTags.Add(w.Tag);
                else if (!toggled && on) simLearnedTags.Remove(w.Tag);
            }

            // 逐访客模拟
            var day = scheduleConfig.schedule.Find(s => s.Day == $"第{simDayNumber}天");
            if (day == null || day.VisitorIds == null || day.VisitorIds.Count == 0)
            {
                GUILayout.Label("当天日程无访客", EditorStyles.miniLabel);
                return;
            }
            GUILayout.Label($"【第{simDayNumber}天预测结果】", EditorStyles.boldLabel);
            foreach (var vid in day.VisitorIds)
            {
                DrawSimulatedVisitor(vid, newsLearned);
            }
            GUILayout.Label("（模拟口径与运行时判定引擎一致：请求触发行为藏标签/逃跑、懵逼与愤怒计数、磨人带重复请求均模拟；仅演出节奏与访客移动不模拟。分支插入的访客不参与模拟，按日程表原访客序列预测）", EditorStyles.miniLabel);
        }

        /// <summary>模拟单个访客的值班结局（复用运行时 DutyOutcomeSimulator，与"跳过到结算"同口径）：藏标签/逃跑 → 计分 → 三带结局预测</summary>
        private void DrawSimulatedVisitor(string visitorId, HashSet<VisitorAttribute> newsLearned)
        {
            var entry = (visitorConfig?.visitors ?? new List<GameDataJSON.VisitorEntry>())
                .Find(v => v != null && v.Id == visitorId);
            if (entry == null)
            {
                GUILayout.Label($"· <color=#B03A2E>✗ {visitorId}：访客数据表中不存在</color>", WrapLabel());
                return;
            }
            var visitor = entry.ToVisitor();
            GetEditBehavioursById().TryGetValue(visitorId, out var behvs);
            behvs = behvs ?? new List<VisitorBehaviour>();

            // 模拟装配（按规则表顺序构建，传入模拟器按此顺序执行请求）
            var assembled = new List<Rule>();
            foreach (var ruleEntry in ruleConfig?.rules ?? new List<GameDataJSON.RuleEntry>())
            {
                if (ruleEntry == null || !simAssemblyRuleIds.Contains(ruleEntry.Id)) continue;
                assembled.Add(GetEditRulesById().TryGetValue(ruleEntry.Id, out var r) ? r : ruleEntry.ToRule());
            }

            var learned = new HashSet<VisitorAttribute>(newsLearned);
            learned.UnionWith(simLearnedTags);
            var sim = DutyOutcomeSimulator.Simulate(visitor, behvs, assembled, GetEditWeights(), learned, simAssemblyRuleIds);

            if (sim.Outcome == DutyOutcome.Fled)
            {
                GUILayout.Label($"· {visitor.Name} → <color=#B03A2E>逃跑（拦截成功，不参与计分）</color>", WrapLabel());
                return;
            }

            var band = WeightEvaluator.GetBand(sim.TotalScore);
            var sb = new StringBuilder();
            sb.Append($"· {visitor.Name} → 总分 ");
            string color = band == DutyScoreBand.放行 ? "#3C9A3C" : (band == DutyScoreBand.警戒拦截 ? "#B03A2E" : "#cc7a00");
            string bandText = band == DutyScoreBand.放行 ? "放行" : (band == DutyScoreBand.警戒拦截 ? "警戒拦截" : "请示带→磨人收尾");
            sb.Append($"<color={color}><b>{sim.TotalScore}（{bandText}）</b></color>");
            sb.Append($"　结局预测：{OutcomeText(sim.Outcome)}");
            sb.Append("　计分：");
            bool first = true;
            foreach (var d in sim.ScoreDetails)
            {
                if (!first) sb.Append("，");
                first = false;
                sb.Append(d.Tag);
                if (!d.Recognized) sb.Append("(未识别)");
                sb.Append(d.Weight >= 0 ? $" +{d.Weight}" : $" {d.Weight}");
            }
            if (sim.HiddenTags.Count > 0)
                sb.Append($"　｜　被行为隐藏（不计分）：{string.Join("、", sim.HiddenTags)}");
            GUILayout.Label(sb.ToString(), WrapLabel());
        }

        // ===== 运行模式绘制 =====

        private void DrawRuntimeSections()
        {
            GUILayout.Label("<b>【运行中】</b>实时状态（每0.5秒自动刷新）", RichBold());

            DrawFlowOverview();
            GUILayout.Space(6);
            DrawConfigSummary(runtime: true);
            GUILayout.Space(6);
            DrawBooksSection(runtime: true);
            GUILayout.Space(6);
            DrawScheduleSection(runtime: true);
            GUILayout.Space(6);
            DrawDutyLiveSection();
            GUILayout.Space(6);
            DrawSkipToolSection();
        }

        private void DrawDutyLiveSection()
        {
            SectionHeader("⑧ 值班实时状态");

            var duty = UnityEngine.Object.FindObjectOfType<DutyFlowController>();
            if (duty == null)
            {
                GUILayout.Label("当前场景无值班流程（MainGameScene 中运行时显示）", EditorStyles.miniLabel);
                return;
            }

            GUILayout.Label($"第 {duty.DayNumber} 天　｜　场上访客 {duty.OnFieldVisitorCount} 人　｜　拥挤：{(duty.WasCrowdedToday ? "今天发生过" : "未发生")}　｜　" + (duty.IsSettlementShown ? "值班已结束，结算面板显示中" : "值班进行中"), EditorStyles.boldLabel);

            var current = duty.CurrentFrontVisitor;
            if (current != null)
            {
                GUILayout.Label($"正在处理/队首访客：{current.Data.Name}（状态：{current.State}　可见属性：{string.Join("、", current.GetVisibleAttributes())}）", WrapLabel());
                GUILayout.Label($"　收到请求 {current.RequestCount} 次　｜　懵逼 {current.DazeCount} 次　｜　愤怒：{(current.IsAngry ? "是" : "否")}", WrapLabel());
            }
            else
            {
                GUILayout.Label("当前场上无访客（等待生成）", EditorStyles.miniLabel);
            }

            GUILayout.Space(4);
            GUILayout.Label("已处理结果：", EditorStyles.boldLabel);
            if (duty.Results.Count == 0)
            {
                GUILayout.Label("　（尚无结果）", EditorStyles.miniLabel);
            }
            foreach (var r in duty.Results)
            {
                if (r == null) continue;
                GUILayout.Label($"　· {r.Visitor.Name}：{OutcomeText(r.Outcome)}", WrapLabel());
            }

            // 运行时常量（值班速度倍率等）
            GUILayout.Space(4);
            GUILayout.Label($"值班速度倍率：{GameConstants.DutySpeedMultiplier}　｜　判定节奏：{GameConstants.DutyStepInterval}s　｜　访客移动速度：{GameConstants.VisitorMoveSpeed}", EditorStyles.miniLabel);
            GUILayout.Label($"判定阈值：放行 ≥ {GameConstants.ReleaseThreshold}　｜　警戒拦截 ≤ {GameConstants.AlertThreshold}　｜　未识别默认权重 {GameConstants.UnrecognizedDefaultWeight}", EditorStyles.miniLabel);

            var dmLive = GameDataManager.Instance;
            if (dmLive != null)
            {
                GUILayout.Label($"当前装配规则：{(dmLive.ActiveAssemblyRuleIds.Count == 0 ? "（未注册，首位访客到达后生效）" : string.Join("、", dmLive.ActiveAssemblyRuleIds))}", EditorStyles.miniLabel);
                GUILayout.Label($"保安已学会标签：{(dmLive.LearnedTags.Count == 0 ? "（暂无）" : string.Join("、", dmLive.LearnedTags))}", EditorStyles.miniLabel);
            }
        }

        // ===== 分区：快速调试（跳过到结算）=====

        private void DrawSkipToolSection()
        {
            SectionHeader("⑨ 快速调试工具");

            // 主场景：直接跳过当天剩余演出结算
            var duty = UnityEngine.Object.FindObjectOfType<DutyFlowController>();
            if (duty != null)
            {
                if (duty.IsSettlementShown)
                {
                    GUILayout.Label("结算面板已显示：点游戏内\"下一天\"按钮继续7天循环", WrapLabel());
                }
                else
                {
                    if (GUILayout.Button("⏭ 跳过当天值班，直接结算（剩余访客按判定模拟口径瞬间裁决）", GUILayout.Height(30)))
                    {
                        duty.SkipToSettlementImmediately();
                        Debug.Log($"[流程面板] 已跳过第 {duty.DayNumber} 天到结算");
                    }
                }
                return;
            }

            // ReadNews/ChooseBook 阶段：一键自动推进（过资讯→自动装配→瞬间结算）
            var readNews = UnityEngine.Object.FindObjectOfType<ReadNewsSceneController>();
            var flow = SelectionFlowController.Instance;
            if (readNews != null || flow != null)
            {
                if (GUILayout.Button("⏭ 一键推进到当天结算（自动过资讯 → 自动装配 → 瞬间结算）", GUILayout.Height(30)))
                {
                    DebugSkipDriver.Launch();
                }
                GUILayout.Label("从当前阶段自动推进到结算面板；结算后可继续点游戏内\"下一天\"，逐日快速测试7天循环", EditorStyles.miniLabel);
                return;
            }

            GUILayout.Label("（在 ReadNews / ChooseBook / MainGameScene 场景中可用）", EditorStyles.miniLabel);
        }

        // ===== UI辅助 =====

        private static void SectionHeader(string title)
        {
            EditorGUILayout.Space(8);
            var rect = GUILayoutUtility.GetRect(new GUIContent(title), EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            GUI.Box(rect, title, EditorStyles.toolbarButton);
        }

        private static GUIStyle WrapLabel()
        {
            var style = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
            return style;
        }

        private static GUIStyle EditorLabel()
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { richText = true, wordWrap = true };
            return style;
        }

        private static GUIStyle RichBold()
        {
            var style = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 14 };
            return style;
        }
    }
}
