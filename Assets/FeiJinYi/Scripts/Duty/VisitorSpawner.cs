// 访客生成器：维护当日访客出场队列，只决定“下一个生成谁”，实例化与离场检测由 DutyFlowController 负责。
using System.Collections.Generic;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 访客生成器（无GameObject，只维护出场顺序）
    /// 严格串行：前一个访客完全离场后才生成下一个（走进画面的时间即为自然间隔）
    /// 只负责"下一个该生成谁"，实例化与离场检测由 DutyFlowController 完成
    /// </summary>
    public class VisitorSpawner
    {
        private readonly Queue<Visitor> pending = new Queue<Visitor>();

        /// <summary>是否还有待生成的访客</summary>
        public bool HasPending => pending.Count > 0;

        public VisitorSpawner(List<Visitor> visitorSequence)
        {
            if (visitorSequence == null) return;
            foreach (var v in visitorSequence)
            {
                if (v != null) pending.Enqueue(v);
            }
        }

        /// <summary>取出下一个待生成的访客</summary>
        public Visitor SpawnNext()
        {
            return pending.Dequeue();
        }
    }
}
