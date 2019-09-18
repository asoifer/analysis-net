using Backend.Model;
using Backend.Utils;
using Model.ThreeAddressCode.Values;
using Model.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Backend.Analyses
{
    public enum LeakSymbol
    {
        Bottom = 0,
        Low = 1,
        High = 2,
        Top = 3
    }

    public class InterLeakAnalysisInfo : DataFlowAnalysisResult<ISet<LeakVariable>>
    {
        public DataFlowAnalysisResult<ISet<LeakVariable>>[] IntraLeakAnalysisInfo { get; set; }
    }

    public class LeakVariable
    {
        public IVariable variable;
        public LeakSymbol value;

        public string GetStringValue()
        {
            var s = value;
            if (s == LeakSymbol.Bottom)
                return "Bottom";
            if (s == LeakSymbol.Low)
                return "Low";
            if (s == LeakSymbol.High)
                return "High";
            if (s == LeakSymbol.Top)
                return "Top";
            throw new NotImplementedException();
        }

        private static int Compare(LeakVariable lv1, LeakVariable lv2)
        {
            if (lv1 == null && lv2 == null)
                return 0;
            else if (lv1 == null)
                return -1;
            else if (lv2 == null)
                return 1;

            if (lv1.variable.Name == lv2.variable.Name)
            {
                if (lv1.value == lv2.value ||
                    ((int)lv1.value == 1 && (int)lv2.value == 2) ||
                    ((int)lv1.value == 2 && (int)lv2.value == 1))
                    return 0;
                else if ((int)lv1.value < (int)lv2.value)
                    return -1;
                else
                    return 1;
            }
            else
                return 0;
        }

        public int CompareTo(LeakVariable other)
        {
            return Compare(this, other);
        }

        public static bool operator <(LeakVariable lv1, LeakVariable lv2)
        {
            return lv1.CompareTo(lv2) < 0;
        }

        public static bool operator >(LeakVariable lv1, LeakVariable lv2)
        {
            return lv1.CompareTo(lv2) > 0;
        }

        public LeakVariable(IVariable variable, LeakSymbol value)
        {
            this.variable = variable;
            this.value = value;
        }

        public override bool Equals(object obj)
        {
            var a = obj as LeakVariable;

            if (a == null)
                return false;

            return this.variable.Name == a.variable.Name;
        }

        public override int GetHashCode()
        {
            return this.variable.Name.GetHashCode();
        }
    }

    public class InterLeakAnalysis
    {
        private CallGraph callGraph;
        private ProgramAnalysisInfo programInfo;
        public Func<MethodDefinition, ControlFlowGraph> OnReachableMethodFound;
        //public Func<IMethodReference, bool> OnUnknownMethodFound;
        //public ProcessUnknownMethodCallDelegate ProcessUnknownMethodCall;

        public const string INFO_CG = "CG";
        public const string INFO_CFG = "CFG";
        public const string INFO_PTA = "PTA";
        public const string INFO_IPTA_RESULT = "IPTA_RESULT";

        public InterLeakAnalysis(ProgramAnalysisInfo programInfo)
        {
            this.programInfo = programInfo;
        }

        public CallGraph Analyze(MethodDefinition method)
        {
            callGraph = new CallGraph();
            callGraph.Add(method);
            programInfo.Add(INFO_CG, callGraph);

            var methodInfo = programInfo.GetOrAdd(method);

            var cfg = OnReachableMethodFound(method);
            // TODO: Don't create unknown nodes when doing the inter PT analysis
            var pta = new PointsToAnalysis(cfg, method);
            pta.ProcessMethodCall = ProcessMethodCall;

            if (IsScalarType != null)
            {
                pta.IsScalarType = IsScalarType;
            }

            methodInfo.Add(INFO_PTA, pta);

            var info = new InterPointsToInfo();
            methodInfo.Add(INFO_IPTA_RESULT, info);
            info.IntraPointsToInfo = pta.Result;

            var result = pta.Analyze();

            var ptg = result[ControlFlowGraph.ExitNodeId].Output;
            info.Output = ptg;

            //callStack.Pop();

            // TODO: Remove INFO_PTA from all method infos.
            return callGraph;
        }

        protected virtual ControlFlowGraph DefaultReachableMethodFound(MethodDefinition method)
        {
            ControlFlowGraph cfg;
            var methodInfo = programInfo.GetOrAdd(method);
            var ok = methodInfo.TryGet(INFO_CFG, out cfg);

            if (!ok)
            {
                if (method.Body.Kind == MethodBodyKind.Bytecode)
                {
                    throw new NotImplementedException();

                    //var disassembler = new Disassembler(method);
                    //var body = disassembler.Execute();

                    //method.Body = body;
                }

                var cfa = new ControlFlowAnalysis(method.Body);
                cfg = cfa.GenerateNormalControlFlow();

                var splitter = new WebAnalysis(cfg);
                splitter.Analyze();
                splitter.Transform();

                method.Body.UpdateVariables();

                var typeAnalysis = new TypeInferenceAnalysis(cfg, method.ReturnType);
                typeAnalysis.Analyze();

                methodInfo.Add(INFO_CFG, cfg);
            }

            return cfg;
        }
    }
}
