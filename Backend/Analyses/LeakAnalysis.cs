using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Backend.Model;
using Model.ThreeAddressCode.Instructions;
using Model.ThreeAddressCode.Values;
using Model.ThreeAddressCode.Visitor;
using Model.Types;

namespace Backend.Analyses
{
    public class LeakAnalysis : ForwardDataFlowAnalysis<LeakVariables>
    {
        private MethodDefinition method;
        private TransferFunction transferFunction;
        private LeakVariables initialLeakVariables;

        public delegate LeakVariables ProcessMethodCallDelegate(IMethodReference caller, MethodCallInstruction methodCall, LeakVariables input);
        public ProcessMethodCallDelegate ProcessMethodCall
        {
            get { return transferFunction.ProcessMethodCall; }
            set { transferFunction.ProcessMethodCall = value; }
        }

        public LeakAnalysis(ControlFlowGraph cfg, MethodDefinition method) : base(cfg)
        {
            this.method = method;
            this.transferFunction = new TransferFunction(method);
        }
        
        protected override bool Compare(LeakVariables lv1, LeakVariables lv2)
        {
            return lv1.Equals(lv2);
        }

        public override DataFlowAnalysisResult<LeakVariables>[] Analyze()
        {
            this.initialLeakVariables = new LeakVariables();
            foreach (var p in method.Body.Parameters)
                this.initialLeakVariables.Variables[p] = new LeakVariableInfo(LeakSymbol.Bottom);
            return base.Analyze();
        }

        public DataFlowAnalysisResult<LeakVariables>[] Analyze(LeakVariables lv)
        {
            this.initialLeakVariables = new LeakVariables(lv);
            return base.Analyze();
        }

        protected override LeakVariables Flow(CFGNode node, LeakVariables input)
        {
            input = new LeakVariables(input);
            var output = transferFunction.Evaluate(node, input);
            return output;
        }

        protected override LeakVariables InitialValue(CFGNode node)
        {
            return initialLeakVariables;
        }

        protected override LeakVariables Join(LeakVariables left, LeakVariables right)
        {
            var joinSet = new LeakVariables(left);
            joinSet.Union(right);
            return joinSet;
        }

        private class TransferFunction : InstructionVisitor
        {
            private IMethodReference method;
            LeakVariables variables;
            public ProcessMethodCallDelegate ProcessMethodCall;

            public TransferFunction(IMethodReference method)
            {
                this.method = method;
            }

            public LeakVariables Evaluate(CFGNode node, LeakVariables input)
            {
                this.variables = input;
                Visit(node);
                var result = this.variables;
                this.variables = null;
                return result;
            }

            public override void Visit(ConditionalBranchInstruction instruction)
            {
                variables.LakedVariables = variables.LakedVariables ||
                (instruction.UsedVariables.Any(x => 
                variables.Variables[x].Sensibility == LeakSymbol.Top || 
                variables.Variables[x].Sensibility == LeakSymbol.High));
            }

            //public override void Visit(LoadInstruction instruction)
            //{
            //    base.Visit(instruction);
            //}

            //public override void Visit(BinaryInstruction instruction)
            //{
            //    base.Visit(instruction);
            //}

            //public override void Visit(MethodCallInstruction instruction)
            //{
            //    base.Visit(instruction);
            //}

            public override void Visit(DefinitionInstruction instruction)
            {
                base.Visit(instruction);

                LeakVariableInfo info = new LeakVariableInfo();

                if (instruction is LoadInstruction)
                {
                    var isConstant = ((LoadInstruction)instruction).Operand is Constant;
                    var isVariable = ((LoadInstruction)instruction).Operand is IVariable;
                    if (isConstant)
                        info.Sensibility = LeakSymbol.Low;
                    else if (isVariable)
                        variables.Variables.TryGetValue((IVariable)((LoadInstruction)instruction).Operand, out info);
                    else
                        throw new NotImplementedException();
                }
                else if (instruction is BinaryInstruction)
                {
                    foreach (var v in instruction.UsedVariables)
                    {
                        if (variables.Variables.ContainsKey(v))
                        {
                            var cv = variables.Variables[v];
                            info.Sensibility = LeakVariables.GetMinMax(info.Sensibility, cv.Sensibility);
                        }
                        else
                            throw new NotImplementedException();
                    }
                }
                else if (instruction is MethodCallInstruction)
                {
                    // Particular cases
                    if (((MethodCallInstruction)instruction).Method.Name == "Sensible")
                        variables.Variables[((MethodCallInstruction)instruction).Arguments[0]].Sensibility = LeakSymbol.High;
                    else if (((MethodCallInstruction)instruction).Method.Name == "Sanitiza")
                        variables.Variables[((MethodCallInstruction)instruction).Arguments[0]].Sensibility = LeakSymbol.Low;
                    else
                    {
                        LeakVariables output = null;
                        if (ProcessMethodCall != null)
                            output = ProcessMethodCall(method, (MethodCallInstruction)instruction, variables);

                        if (output == null)
                        { 
                            if (((MethodCallInstruction)instruction).Arguments.Any(x => 
                            variables.Variables[x].Sensibility == LeakSymbol.High || variables.Variables[x].Sensibility == LeakSymbol.Top))
                                variables.LakedVariables = true;
                        }
                        else
                        {
                            variables = output;
                            return;
                        }
                    }
                }
                else
                    ;

                foreach (var mv in instruction.ModifiedVariables)
                    variables.Variables[mv] = info;
            }

            public override void Visit(ReturnInstruction instruction)
            {
                if (instruction.HasOperand)
                    variables.ReturnVariable = instruction.Operand;
            }
        }
    }
}
