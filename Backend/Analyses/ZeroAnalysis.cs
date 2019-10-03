using System;
using System.Linq;
using System.Collections.Generic;
using Backend.Model;
using Model.ThreeAddressCode.Values;
using static Backend.Analyses.ZeroAnalysis;
using Model.ThreeAddressCode.Visitor;
using Model.ThreeAddressCode.Instructions;
using Model.Types;

namespace Backend.Analyses
{
    public class ZeroAnalysis : ForwardDataFlowAnalysis<ISet<ZeroVariable>>
    {
        private TransferFunction transferFunction = new TransferFunction();
        public ZeroAnalysis(ControlFlowGraph cfg) : base(cfg) { }

        public enum ZeroSymbol
        {
            N = 0,
            Z = 1,
            NZ = 2,
            MZ = 3
        }
        
        public class ZeroVariable
        {
            public IVariable variable;
            public ZeroSymbol value;

            public string GetStringValue()
            {
                var s = value;
                if (s == ZeroSymbol.N)
                    return "N";
                if (s == ZeroSymbol.Z)
                    return "Z";
                if (s == ZeroSymbol.NZ)
                    return "NZ";
                if (s == ZeroSymbol.MZ)
                    return "MZ";
                throw new NotImplementedException();
            }

            private static int Compare(ZeroVariable zv1, ZeroVariable zv2)
            {
                if (zv1 == null && zv2 == null)
                    return 0;
                else if (zv1 == null)
                    return -1;
                else if (zv2 == null)
                    return 1;

                if (zv1.variable.Name == zv2.variable.Name)
                {
                    if (zv1.value == zv2.value || 
                        ((int)zv1.value == 1 && (int)zv2.value == 2) || 
                        ((int)zv1.value == 2 && (int)zv2.value == 1))
                        return 0;
                    else if ((int)zv1.value < (int)zv2.value)
                        return -1;
                    else
                        return 1;
                }
                else
                    return 0;
            }

            public int CompareTo(ZeroVariable other)
            {
                return Compare(this, other);
            }

            public static bool operator <(ZeroVariable zv1, ZeroVariable zv2)
            {
                return zv1.CompareTo(zv2) < 0;
            }

            public static bool operator >(ZeroVariable zv1, ZeroVariable zv2)
            {
                return zv1.CompareTo(zv2) > 0;
            }

            public ZeroVariable(IVariable variable, ZeroSymbol value)
            {
                this.variable = variable;
                this.value = value;
            }

            public override bool Equals(object obj)
            {
                var a = obj as ZeroVariable;

                if (a == null)
                    return false;

                return this.variable.Name == a.variable.Name;
            }

            public override int GetHashCode()
            {
                return this.variable.Name.GetHashCode();
            }
        }

        protected override bool Compare(ISet<ZeroVariable> oldValue, ISet<ZeroVariable> newValue)
        {
            foreach(var v in oldValue)
            {
                var rv = newValue.FirstOrDefault(x => x.variable == v.variable);
                if (rv == null || v.variable != rv.variable || v.value != rv.value)
                    return false;
            }
            foreach (var v in newValue)
            {
                var rv = oldValue.FirstOrDefault(x => x.variable == v.variable);
                if (rv == null || v.variable != rv.variable || v.value != rv.value)
                    return false;
            }
            return true;
        }

        protected override ISet<ZeroVariable> Flow(CFGNode node, ISet<ZeroVariable> input)
        {
            input = new HashSet<ZeroVariable>(input);
            var output = transferFunction.Evaluate(node, input);
            return output;
        }

        protected override ISet<ZeroVariable> InitialValue(CFGNode node)
        {
            return new HashSet<ZeroVariable>();
        }

        protected override ISet<ZeroVariable> Join(ISet<ZeroVariable> left, ISet<ZeroVariable> right)
        {
            var joinSet = new HashSet<ZeroVariable>();
            foreach(var v in left)
            {
                var rv = right.FirstOrDefault(x => x.variable.Name == v.variable.Name);
                if (rv == null)
                    continue;

                if (v > rv)
                    joinSet.Add(v);
                else if (v < rv)
                    joinSet.Add(rv);
                else if (v.value != rv.value && (v.value == ZeroSymbol.Z || v.value == ZeroSymbol.NZ))
                    joinSet.Add(new ZeroVariable(v.variable, ZeroSymbol.MZ));
                else
                    joinSet.Add(v);
            }
            return joinSet;
        }

        private class TransferFunction : InstructionVisitor
        {
            ISet<ZeroVariable> variables;

            public ISet<ZeroVariable> Evaluate(CFGNode node, ISet<ZeroVariable> input)
            {
                this.variables = input;
                Visit(node);
                var result = this.variables;
                this.variables = null;
                return result;
            }
            
            public override void Visit(DefinitionInstruction instruction)
            {
                var isIntOrDouble =
                    instruction.ModifiedVariables.Count > 0 &&
                    instruction.ModifiedVariables.First().Type is BasicType &&
                    (((BasicType)instruction.ModifiedVariables.First().Type).Name == "Int32" ||
                    ((BasicType)instruction.ModifiedVariables.First().Type).Name == "Double");

                var val = ZeroSymbol.MZ;
                var isZero = false;
                if (instruction is LoadInstruction)
                {
                    var loadInstruction = instruction as LoadInstruction;
                    var isConstant = loadInstruction.Operand is Constant;
                    if (isConstant)
                    {
                        isZero = isConstant && ((Constant)loadInstruction.Operand).Value.ToString() == "0";
                        var isNotZero = isConstant && !isZero;

                        if (isZero)
                            val = ZeroSymbol.Z;
                        if (isNotZero)
                            val = ZeroSymbol.NZ;
                    }
                    var isVariable = loadInstruction.Operand is LocalVariable;
                    if (isVariable)
                    {
                        var curVar = variables.FirstOrDefault(x => x.variable.Name ==
                            ((LocalVariable)loadInstruction.Operand).Name);
                        if (curVar != null)
                            val = curVar.value;
                    }
                }
                else if (instruction is BinaryInstruction)
                { 
                    isZero = true;
                    foreach (var v in instruction.UsedVariables)
                    {
                        var cv = variables.FirstOrDefault(x => x.variable.Name == v.Name);
                        isZero = isZero && (cv != null && cv.value == ZeroSymbol.Z);
                        if (!isZero)
                            break;
                    }
                    val = isZero ? ZeroSymbol.Z : ZeroSymbol.MZ;
                }

                foreach (var mv in instruction.ModifiedVariables)
                {
                    var cv = variables.FirstOrDefault(x => x.variable.Name == mv.Name);
                    if (cv != null)
                        variables.Remove(cv);
                    variables.Add(new ZeroVariable(mv, val));
                }
            }
        }
    }
}
