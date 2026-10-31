// 值班结局模拟器：数据级复现保安判定流程（请求→行为藏标签/逃跑→权重计分→三带裁决，磨人带模拟重复请求），无演出无等待，供编辑器跳过结算与流程面板dry-run共用。
using System.Collections.Generic;

namespace SecurityGuardSimulator
{
    /// <summary>单访客模拟结果（结局/总分/计分明细/被藏标签/请求与懵逼计数）</summary>
    public class SimulatedVisitorResult
    {
        public DutyOutcome Outcome;
        public int TotalScore;
        public List<TagScoreDetail> ScoreDetails = new List<TagScoreDetail>();
        public List<VisitorAttribute> HiddenTags = new List<VisitorAttribute>();
        public int RequestCount;
        public int DazeCount;
        public bool IsAngry;
    }

    /// <summary>
    /// 值班结局模拟器（纯静态）：与 GuardBrain.ProcessVisitor + VisitorController.RespondToRequest 同口径的判定模拟，
    /// 逐装配规则发请求→行为库匹配（藏标签/逃跑/懵逼累计/愤怒触发）→可见标签权重计分→三带裁决；
    /// 中间请示带按磨人循环模拟（重复主请求至愤怒触发或达懵逼上限），不模拟移动与演出节奏。
    /// 供"跳过到结算"编辑器工具与流程面板dry-run共用，保证预测与真实判定一致。
    /// </summary>
    public static class DutyOutcomeSimulator
    {
        /// <summary>
        /// 模拟一位访客的值班结局。
        /// assembledRules 按执行顺序传入；weights/learnedTags/assemblyRuleIds 共同决定标签识别状态。
        /// </summary>
        public static SimulatedVisitorResult Simulate(
            Visitor visitor,
            List<VisitorBehaviour> behaviours,
            List<Rule> assembledRules,
            Dictionary<VisitorAttribute, AttributeWeight> weights,
            HashSet<VisitorAttribute> learnedTags,
            HashSet<string> assemblyRuleIds)
        {
            var result = new SimulatedVisitorResult();
            behaviours = behaviours ?? new List<VisitorBehaviour>();
            assembledRules = assembledRules ?? new List<Rule>();

            var visible = new HashSet<VisitorAttribute>(visitor.Attributes);
            bool fled = false, angry = false;
            int requestCount = 0, daze = 0;

            // 阶段1：按装配顺序逐条规则发请求（观察类"无"与"放行"不发请求），逃跑=硬终点
            foreach (var rule in assembledRules)
            {
                if (fled) break;
                if (rule.RequestType == GuardRequestType.无 || rule.RequestType == GuardRequestType.放行) continue;
                Respond(visitor, behaviours, rule.RequestType, visible, result, ref requestCount, ref daze, ref angry, ref fled);
            }

            if (fled)
            {
                result.Outcome = DutyOutcome.Fled;
                result.RequestCount = requestCount;
                result.DazeCount = daze;
                result.IsAngry = angry;
                return result;
            }

            // 阶段2：可见标签权重计分
            result.ScoreDetails = WeightEvaluator.Evaluate(weights, new List<VisitorAttribute>(visible),
                learnedTags, assemblyRuleIds, out int total);
            result.TotalScore = total;

            // 阶段3：三带裁决
            switch (WeightEvaluator.GetBand(total))
            {
                case DutyScoreBand.放行:
                    result.Outcome = DutyOutcome.Released;
                    break;
                case DutyScoreBand.警戒拦截:
                    result.Outcome = DutyOutcome.BlockedAlert;
                    break;
                default:
                    // 请示带磨人循环：重复主请求至愤怒触发或达懵逼上限（与运行时循环条件一致，中途逃跑不单独处理）
                    var primary = FindPrimaryRequest(assembledRules);
                    int repeats = 0;
                    while (!angry && repeats < GameConstants.DazeCountForAnger)
                    {
                        Respond(visitor, behaviours, primary, visible, result, ref requestCount, ref daze, ref angry, ref fled);
                        repeats++;
                    }
                    result.Outcome = angry ? DutyOutcome.LeftAngry : DutyOutcome.ReleasedByConsult;
                    break;
            }

            result.RequestCount = requestCount;
            result.DazeCount = daze;
            result.IsAngry = angry;
            return result;
        }

        /// <summary>模拟一次保安请求的访客响应：行为库匹配→藏标签/懵逼累计→愤怒触发检查。返回true=已逃跑</summary>
        private static bool Respond(Visitor visitor, List<VisitorBehaviour> behaviours, GuardRequestType request,
            HashSet<VisitorAttribute> visible, SimulatedVisitorResult result,
            ref int requestCount, ref int daze, ref bool angry, ref bool fled)
        {
            requestCount++;
            bool firedAny = false, fledNow = false;

            foreach (var b in behaviours)
            {
                if (b.TriggerRequest != request) continue;
                if (!MeetsCondition(visitor, b)) continue;

                firedAny = true;
                foreach (var hide in b.HideAttributes)
                {
                    if (visible.Remove(hide) && !result.HiddenTags.Contains(hide))
                        result.HiddenTags.Add(hide);
                }
                if (b.ActionName == "逃跑") fledNow = true;
            }

            if (!firedAny) daze++;
            if (fledNow)
            {
                fled = true;
                return true;
            }

            // 愤怒触发检查（TriggerRequest=无 的懵逼/请求计数条件行为，与 VisitorController.CheckAngryBehaviours 同口径）
            if (!angry)
            {
                foreach (var b in behaviours)
                {
                    if (b.TriggerRequest != GuardRequestType.无) continue;
                    bool hit = false;
                    if (b.TryGetDazeCountCondition(out int dazeNeed) && daze >= dazeNeed) hit = true;
                    if (b.TryGetRequestCountCondition(out int reqNeed) && requestCount >= reqNeed) hit = true;
                    if (hit) { angry = true; break; }
                }
            }
            return false;
        }

        private static bool MeetsCondition(Visitor visitor, VisitorBehaviour b)
        {
            foreach (var attr in b.RequiredAttributes())
            {
                if (!visitor.HasAttribute(attr)) return false;
            }
            return true;
        }

        /// <summary>找第一个会发请求的规则请求类型（无则默认请出示工牌），与 GuardBrain.FindPrimaryRequest 同口径</summary>
        private static GuardRequestType FindPrimaryRequest(List<Rule> assembledRules)
        {
            foreach (var rule in assembledRules)
            {
                if (rule.RequestType != GuardRequestType.无 && rule.RequestType != GuardRequestType.放行)
                    return rule.RequestType;
            }
            return GuardRequestType.请出示工牌;
        }
    }
}
