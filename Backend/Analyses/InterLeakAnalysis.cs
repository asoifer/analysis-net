using Backend.Model;
using Backend.Transformations;
using Backend.Utils;
using Model.ThreeAddressCode.Instructions;
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

    public class InterLeakAnalysisInfo : DataFlowAnalysisResult<LeakVariables>
    {
        public DataFlowAnalysisResult<LeakVariables>[] IntraLeakAnalysisInfo { get; set; }
    }

    public class InterLeakAnalysis
    {
        private CallGraph callGraph;
        private ProgramAnalysisInfo programInfo;
        public Func<MethodDefinition, ControlFlowGraph> OnReachableMethodFound;

        public const string INFO_CFG = "CFG";
        public const string INFO_LA = "LA";
        public const string INFO_ILA_RESULT = "ILA_RESULT";

        public InterLeakAnalysis(ProgramAnalysisInfo programInfo, CallGraph callGraph)
        {
            this.programInfo = programInfo;
            this.callGraph = callGraph;
            this.OnReachableMethodFound = this.DefaultReachableMethodFound;
        }

        public CallGraph Analyze(MethodDefinition method)
        {
            var methodInfo = programInfo.GetOrAdd(method);

            var cfg = OnReachableMethodFound(method);
            // TODO: Don't create unknown nodes when doing the inter PT analysis
            var la = new LeakAnalysis(cfg, method);
            la.ProcessMethodCall = ProcessMethodCall;
            methodInfo.Add(INFO_LA, la);

            var info = new InterLeakAnalysisInfo();
            methodInfo.Add(INFO_ILA_RESULT, info);
            info.IntraLeakAnalysisInfo = la.Result;

            var result = la.Analyze();

            info.Output = result[ControlFlowGraph.ExitNodeId].Output;
            return callGraph;
        }

        protected virtual LeakVariables ProcessMethodCall(IMethodReference caller, MethodCallInstruction methodCall, LeakVariables input)
        {
            LeakVariables output = null;
            var possibleCallees = ResolvePossibleCallees(caller, methodCall, input);
            foreach (var callee in possibleCallees)
            {
                var method = callee.ResolvedMethod;
                var isUnknownMethod = method == null || method.IsExternal;

                // Si no es conocido el método retornamos null por ahora
                if (isUnknownMethod)
                    return output;

                InterLeakAnalysisInfo info;
                var processCallee = true;
                var methodInfo = programInfo.GetOrAdd(callee);
                var ok = methodInfo.TryGet(INFO_ILA_RESULT, out info);

                if (!ok)
                {
                    if (isUnknownMethod)
                        throw new NotImplementedException();
                    else
                    {
                        var cfg = OnReachableMethodFound(method);
                        var la = new LeakAnalysis(cfg, method);
                        la.ProcessMethodCall = ProcessMethodCall;
                        methodInfo.Add(INFO_LA, la);

                        info = new InterLeakAnalysisInfo();
                        methodInfo.Add(INFO_ILA_RESULT, info);
                        info.IntraLeakAnalysisInfo = la.Result;
                    }
                }

                if (processCallee)
                {
                    IList<IVariable> parameters;
                    if (isUnknownMethod)
                        throw new NotImplementedException();
                    else
                        parameters = method.Body.Parameters;

                    var binding = GetCallerCalleeBinding(methodCall.Arguments, parameters);
                    var lv = NewMapping(input, binding);
                    
                    var oldInput = info.Input;
                    var inputChanged = true;

                    if (oldInput != null)
                    {
                        inputChanged = !oldInput.Equals(lv);

                        if (inputChanged)
                        {
                            lv.Union(oldInput);
                            // Even when the graphs were different,
                            // it could be the case that one (ptg)
                            // is a subgraph of the other (oldInput)
                            // so the the result of the union of both
                            // graphs is exactly the same oldInput graph.
                            inputChanged = !oldInput.Equals(lv);
                        }
                    }

                    if (inputChanged)
                    {
                        info.Input = lv;

                        if (isUnknownMethod)
                            throw new NotImplementedException();
                        else
                        {
                            var la = methodInfo.Get<LeakAnalysis>(INFO_LA);
                            var result = la.Analyze(lv);

                            lv = result[ControlFlowGraph.ExitNodeId].Output;
                        }

                        info.Output = lv;
                    }
                    else
                    {
                        if (isUnknownMethod)
                            throw new NotImplementedException();
                        else
                        {
                            // We cannot use info.Output here because it could be a recursive call
                            // and info.Output is assigned after analyzing the callee.
                            lv = info.IntraLeakAnalysisInfo[ControlFlowGraph.ExitNodeId].Output;

                            if (info.Output != null)
                                lv.Union(info.Output);

                            info.Output = lv;
                        }
                    }

                    //if (methodCall.HasResult && lv.ReturnVariable != null)
                    //    binding.Add(lv.ReturnVariable, methodCall.Result);
                    //lv = RestoreMapping(input, lv, binding);

                    var maybeLakedVariables = lv.LakedVariables;
                    if (methodCall.HasResult && lv.ReturnVariable != null)
                    {
                        var vrv = lv.Variables[lv.ReturnVariable];
                        lv = input;
                        lv.Add(methodCall.Result, vrv);
                    }
                    else
                        lv = input;
                    lv.LakedVariables = lv.LakedVariables || maybeLakedVariables;

                    if (lv != null)
                    {
                        if (output == null)
                            output = lv;
                        else
                            output.Union(lv);
                    }
                }
            }

            return output;
        }

        private LeakVariables NewMapping(LeakVariables input, IDictionary<IVariable, IVariable> binding)
        {
            var lv = new LeakVariables();
            lv.LakedVariables = input.LakedVariables;

            foreach (var entry in binding)
            {
                var parameter = entry.Key;
                var argument = entry.Value;

                if (!input.Variables.ContainsKey(argument))
                    throw new Exception("Input not found");

                lv.Add(parameter, input.Variables[argument]);
            }

            return lv;
        }

        private LeakVariables RestoreMapping(LeakVariables input, LeakVariables output, IDictionary<IVariable, IVariable> binding)
        {
            throw new NotImplementedException();
        }

        // binding: callee parameter -> caller argument
        private IDictionary<IVariable, IVariable> GetCallerCalleeBinding(IList<IVariable> arguments, IList<IVariable> parameters)
        {
            var binding = new Dictionary<IVariable, IVariable>();

#if DEBUG
            if (arguments.Count != parameters.Count)
                throw new Exception("Different ammount of parameters and arguments");
#endif

            for (var i = 0; i < arguments.Count; ++i)
            {
                var argument = arguments[i];
                var parameter = parameters[i];

                binding.Add(parameter, argument);
            }

            return binding;
        }

        // binding: callee variable -> caller variable
        private IDictionary<IVariable, IVariable> GetCalleeCallerBinding(IVariable callerResult, IVariable calleeResult)
        {
            var binding = new Dictionary<IVariable, IVariable>();

            if (calleeResult != null && callerResult != null)
                binding.Add(calleeResult, callerResult);

            return binding;
        }

        private IEnumerable<IMethodReference> ResolvePossibleCallees(IMethodReference caller, MethodCallInstruction methodCall, LeakVariables lv)
        {
            var inv = this.callGraph.GetInvocation(caller, methodCall.Label);
            return inv.PossibleCallees;
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
                    var disassembler = new Disassembler(method);
                    var body = disassembler.Execute();

                    method.Body = body;
                }

                var cfa = new ControlFlowAnalysis(method.Body);
                cfg = cfa.GenerateNormalControlFlow();

                // From here: for getting the types
                var splitter = new WebAnalysis(cfg);
                splitter.Analyze();
                splitter.Transform();

                method.Body.UpdateVariables();

                var typeAnalysis = new TypeInferenceAnalysis(cfg, method.ReturnType);
                typeAnalysis.Analyze();
                // Until here: for getting the types

                methodInfo.Add(INFO_CFG, cfg);
            }

            return cfg;
        }
    }
}
