using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Backend.Model;

namespace Backend.Analyses
{
    public class LeakAnalysis : ForwardDataFlowAnalysis<ISet<LeakVariable>>
    {
        public LeakAnalysis(ControlFlowGraph cfg) : base(cfg) { }

        protected override bool Compare(ISet<LeakVariable> oldValue, ISet<LeakVariable> newValue)
        {
            throw new NotImplementedException();
        }

        protected override ISet<LeakVariable> Flow(CFGNode node, ISet<LeakVariable> input)
        {
            throw new NotImplementedException();
        }

        protected override ISet<LeakVariable> InitialValue(CFGNode node)
        {
            throw new NotImplementedException();
        }

        protected override ISet<LeakVariable> Join(ISet<LeakVariable> left, ISet<LeakVariable> right)
        {
            throw new NotImplementedException();
        }
    }
}
