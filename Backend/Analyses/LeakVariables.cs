using Model.ThreeAddressCode.Values;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Backend.Analyses
{
    public class LeakVariableInfo
    {
        public LeakSymbol Sensibility;

        public LeakVariableInfo()
        {
            Sensibility = LeakSymbol.Bottom;
        }

        public LeakVariableInfo(LeakSymbol sensibility)
        {
            this.Sensibility = sensibility;
        }
    }

    public class LeakVariables
    {
        public IDictionary<IVariable, LeakVariableInfo> Variables { get; set; }
        public IVariable ReturnVariable { get; set; }
        public bool LakedVariables { get; set; }

        public LeakVariables()
        {
            Variables = new Dictionary<IVariable, LeakVariableInfo>();
            ReturnVariable = null;
            LakedVariables = false;
        }

        public LeakVariables(LeakVariables other)
        {
            Variables = other.Variables;
            ReturnVariable = other.ReturnVariable;
            LakedVariables = other.LakedVariables;
        }

        public void Add(IVariable v, LeakVariableInfo l)
        {
            Variables.Add(v, l);
        }

        public void Union(LeakVariables other)
        {
            foreach(var e2 in other.Variables)
            {
                if (Variables.ContainsKey(e2.Key))
                {
                    var e = Variables[e2.Key];
                    var c = CompareLeakSymbol(e.Sensibility, e2.Value.Sensibility);
                    // If c == 1, we let the previous value in the dictionary
                    if (c == -1)
                        Variables[e2.Key] = e2.Value;
                    // c could be 0
                    else if (e != e2.Value && (e.Sensibility == LeakSymbol.Low || e.Sensibility == LeakSymbol.High))
                        Variables[e2.Key].Sensibility = LeakSymbol.Top;
                }
                else
                    Variables.Add(e2);
            }
            this.LakedVariables = this.LakedVariables || other.LakedVariables;
        }

        public static int CompareLeakSymbol (LeakSymbol ls1, LeakSymbol ls2)
        {
            var e = (int)ls1;
            var e2 = (int)ls2;
            if (e == e2 ||
                    ((int)e == 1 && (int)e2 == 2) ||
                    ((int)e == 2 && (int)e2 == 1))
                return 0;
            else if ((int)e < (int)e2)
                return -1;
            else
                return 1;
        }

        public static LeakSymbol GetMinMax(LeakSymbol ls1, LeakSymbol ls2)
        {
            var c = CompareLeakSymbol(ls1, ls2);
            if (c == -1)
                return ls2;
            if (c == 1)
                return ls1;
            if (ls1 != ls2)
                return LeakSymbol.Top;
            return ls1;
        }

        public override bool Equals(object obj)
        {
            var a = obj as LeakVariables;

            if (a == null)
                return false;

            return Variables == a.Variables && LakedVariables == a.LakedVariables;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public string GetStringValue(LeakSymbol s)
        {
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
    }
}
