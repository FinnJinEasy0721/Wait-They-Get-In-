// 保安判定引擎：按装配顺序逐条执行规则演出，按可见标签×识别状态的权重总分三带（放行/警戒拦截/请示磨人）裁决；拥堵类观察规则按前台真实滞留人数联动演出。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>单个访客的值班处理结果</summary>
    public enum DutyOutcome
    {
        Released,            // 总分≥放行阈值 → 放行
        Fled,                // 访客逃跑（小偷）
        BlockedAlert,        // 总分≤警戒阈值 → 警戒拦截（不放行，移交处理）
        ReleasedByConsult,   // 请示带磨人未怒 → 安抚后放行
        LeftAngry            // 请示带被逼愤怒离开（第二天收到投诉，星级扣减）
    }

    /// <summary>
    /// 保安判定引擎（权重制）
    /// 流程：访客抵达停留点（过程入口）→ 按装配顺序逐条执行规则演出（发请求→访客反馈，可能藏标签/逃跑）→
    ///       全部执行完按可见标签×保安识别状态算总分（识别取识别权重，未识别取未识别权重，求和）→
    ///       三带裁决：≥放行阈值→放行；≤警戒阈值→警戒拦截；中间带→磨人循环（愤怒→离开投诉；磨到上限→安抚放行）
    /// </summary>
    public class GuardBrain
    {
        private readonly List<Rule> assembledRules;
        private readonly Text guardBubble;
        private readonly Func<int> onFieldCountProvider; // 前台真实滞留人数（拥堵类规则演出联动用，null=不联动）

        public GuardBrain(List<Rule> assembledRules, Text guardBubble, Func<int> onFieldCountProvider = null)
        {
            this.assembledRules = assembledRules ?? new List<Rule>();
            this.guardBubble = guardBubble;
            this.onFieldCountProvider = onFieldCountProvider;
        }

        private void Say(string text)
        {
            if (guardBubble != null) guardBubble.text = text;
        }

        private IEnumerator Wait(float seconds)
        {
            float scaled = seconds / Mathf.Max(0.01f, GameConstants.DutySpeedMultiplier);
            yield return new WaitForSeconds(scaled);
        }

        private IEnumerator Wait()
        {
            yield return Wait(GameConstants.DutyStepInterval);
        }

        /// <summary>
        /// 处理一个停留在停留点的访客，协程完成后通过回调返回结果
        /// </summary>
        public IEnumerator ProcessVisitor(VisitorController visitor, System.Action<DutyOutcome> onDone)
        {
            visitor.SetState(VisitorState.Processing);
            visitor.ShowBubble("");

            if (assembledRules.Count == 0)
            {
                Say("保安：我……我还没有任何规则，只能凭感觉判断……");
                yield return Wait();
            }

            // ===== 第一阶段：逐条规则演出（发请求→访客反馈，逃跑随时中断）=====
            foreach (var rule in assembledRules)
            {
                if (visitor.HasFled) break;

                Say($"保安执行规则：{rule.Name}");
                yield return Wait();

                if (rule.RequestType == GuardRequestType.无)
                {
                    if (onFieldCountProvider != null && rule.CheckAttribute != null && rule.CheckAttribute.Contains("滞留人数"))
                    {
                        // 拥堵类观察规则（如"拥堵请示"）：按前台真实滞留人数联动演出（只改台词，不影响判定计分）
                        int onField = onFieldCountProvider();
                        if (onField >= ParseCongestionThreshold(rule))
                        {
                            Say($"前台滞留 {onField} 人，已超过容纳上限，请示上级处理……");
                        }
                        else
                        {
                            Say($"保安观察中……（前台滞留 {onField} 人，未达上限）");
                        }
                    }
                    else
                    {
                        Say($"保安观察中……（{rule.CheckAttribute}）");
                    }
                    yield return Wait();
                }
                else
                {
                    // 保安根据规则向访客发出请求，访客按行为库响应（可能藏标签/逃跑/懵逼）
                    Say($"保安请求：{rule.RequestType}");
                    yield return Wait(0.6f);

                    visitor.RespondToRequest(rule.RequestType);
                    yield return Wait();

                    if (visitor.HasFled)
                    {
                        Say($"{visitor.Data.Name} 转身逃跑！规则书发挥了作用！");
                        break;
                    }
                }
                yield return Wait();
            }

            DutyOutcome outcome;

            // ===== 逃跑优先（硬终点，不参与计分）=====
            if (visitor.HasFled)
            {
                yield return Wait();
                outcome = DutyOutcome.Fled;
            }
            else
            {
                // ===== 第二阶段：权重计分（可见标签 × 保安识别状态）=====
                var visible = visitor.GetVisibleAttributes();
                GameDataManager.Instance.EvaluateScore(visible, out int totalScore);

                Say($"保安打量着访客……综合评估 {totalScore} 分。");
                yield return Wait();

                // ===== 第三阶段：三带裁决 =====
                switch (WeightEvaluator.GetBand(totalScore))
                {
                    case DutyScoreBand.放行:
                        Say("判定通过 → 放行！");
                        yield return Wait();
                        visitor.RespondToRequest(GuardRequestType.放行); // 触发"说谢谢"等行为
                        visitor.MarkReleased();
                        outcome = DutyOutcome.Released;
                        break;

                    case DutyScoreBand.警戒拦截:
                        Say($"警戒拦截：访客可疑程度过高（{totalScore} 分），不放行，移交上级处理。");
                        visitor.ShowBubble("（被拦下）");
                        yield return Wait();
                        outcome = DutyOutcome.BlockedAlert;
                        break;

                    default: // 请示带：情况暧昧，磨人循环收尾
                        Say("情况说不清楚……保安继续坚持要求……");
                        yield return Wait();

                        var primaryRequest = FindPrimaryRequest();
                        int repeats = 0;
                        while (!visitor.IsAngry && repeats < GameConstants.DazeCountForAnger)
                        {
                            Say($"保安重复请求：{primaryRequest}");
                            yield return Wait(0.6f);
                            visitor.RespondToRequest(primaryRequest);
                            yield return Wait();
                            repeats++;
                        }

                        if (visitor.IsAngry)
                        {
                            // 被逼愤怒：愤怒离开，第二天投诉
                            visitor.FireAngryLeaveBehaviour();
                            Say($"{visitor.Data.Name} 愤怒离开！");
                            yield return Wait();
                            outcome = DutyOutcome.LeftAngry;
                        }
                        else
                        {
                            // 磨到上限未怒：请示上级安抚后放行
                            Say("保安请示上级……");
                            yield return Wait();
                            Say("上级指示：特殊情形，安抚访客后予以放行。");
                            yield return Wait();
                            visitor.RespondToRequest(GuardRequestType.放行);
                            visitor.MarkReleased();
                            outcome = DutyOutcome.ReleasedByConsult;
                        }
                        break;
                }
            }

            if (guardBubble != null) guardBubble.text = "";
            onDone?.Invoke(outcome);
        }

        /// <summary>找第一个会发出请求的规则的请求类型作为重复请求（无则默认请出示工牌）</summary>
        private GuardRequestType FindPrimaryRequest()
        {
            foreach (var rule in assembledRules)
            {
                if (rule.RequestType != GuardRequestType.无 && rule.RequestType != GuardRequestType.放行)
                    return rule.RequestType;
            }
            return GuardRequestType.请出示工牌;
        }

        /// <summary>从"前台滞留人数 ≥ N"这类检查描述里解析人数阈值（解析失败按3）</summary>
        private static int ParseCongestionThreshold(Rule rule)
        {
            if (string.IsNullOrEmpty(rule.CheckAttribute)) return 3;
            int idx = rule.CheckAttribute.IndexOf('≥');
            if (idx >= 0 && int.TryParse(rule.CheckAttribute.Substring(idx + 1).Trim(), out int n) && n > 0)
                return n;
            return 3;
        }
    }
}
