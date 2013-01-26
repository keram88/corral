﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Boogie;

namespace Microsoft.Boogie
{
    public class LinearTypechecker : StandardVisitor
    {
        public int errorCount;
        CheckingContext checkingContext;
        public LinearTypechecker()
        {
            this.errorCount = 0;
            this.checkingContext = new CheckingContext(null);
        }
        private void Error(Absy node, string message)
        {
            checkingContext.Error(node, message);
            errorCount++;
        }

        public override Cmd VisitAssignCmd(AssignCmd node)
        {
            HashSet<Variable> rhsVars = new HashSet<Variable>();
            for (int i = 0; i < node.Lhss.Count; i++)
            {
                AssignLhs lhs = node.Lhss[i];
                string domainName = QKeyValue.FindStringAttribute(lhs.DeepAssignedVariable.Attributes, "linear");
                if (domainName == null) continue;
                SimpleAssignLhs salhs = lhs as SimpleAssignLhs;
                if (salhs == null)
                {
                    Error(node, "Only simple assignment allowed on linear sets");
                    continue;
                }
                IdentifierExpr rhs = node.Rhss[i] as IdentifierExpr;
                if (rhs == null)
                {
                    Error(node, "Only variable can be assigned to a linear set");
                    continue;
                }
                string rhsDomainName = QKeyValue.FindStringAttribute(rhs.Decl.Attributes, "linear");
                Formal formal = rhs.Decl as Formal;
                if (formal != null && formal.InComing)
                {
                    rhsDomainName = null;
                }
                if (rhsDomainName == null)
                {
                    Error(node, "Only linear variable can be assigned to another linear variable");
                    continue;
                }
                if (domainName != rhsDomainName)
                {
                    Error(node, "Variable of one domain being assigned to a variable of another domain");
                    continue;
                }
                if (rhsVars.Contains(rhs.Decl))
                {
                    Error(node, "A linear set can occur only once in the rhs");
                    continue;
                }
                rhsVars.Add(rhs.Decl);
            }

            return base.VisitAssignCmd(node);
        }

        public override Cmd VisitCallCmd(CallCmd node)
        {
            HashSet<Variable> inVars = new HashSet<Variable>();
            for (int i = 0; i < node.Proc.InParams.Length; i++)
            {
                Variable formal = node.Proc.InParams[i];
                string domainName = QKeyValue.FindStringAttribute(formal.Attributes, "linear");
                if (domainName == null) continue;
                IdentifierExpr actual = node.Ins[i] as IdentifierExpr;
                if (actual == null)
                {
                    Error(node, "Only variable can be assigned to a linear set");
                    continue;
                }
                string actualDomainName = QKeyValue.FindStringAttribute(actual.Decl.Attributes, "linear");
                Formal formal2 = actual.Decl as Formal;
                if (formal2 != null && formal2.InComing)
                {
                    actualDomainName = null;
                }
                if (actualDomainName == null)
                {
                    Error(node, "Only a linear argument can be passed to a linear parameter");
                    continue;
                }
                if (domainName != actualDomainName)
                {
                    Error(node, "The domains of formal and actual parameters must be the same");
                    continue;
                }
                if (inVars.Contains(actual.Decl))
                {
                    Error(node, "A linear set can occur only once as an in parameter");
                    continue;
                }
                inVars.Add(actual.Decl);
            }

            for (int i = 0; i < node.Proc.OutParams.Length; i++)
            {
                IdentifierExpr actual = node.Outs[i];
                string actualDomainName = QKeyValue.FindStringAttribute(actual.Decl.Attributes, "linear");
                if (actualDomainName == null) continue;
                Variable formal = node.Proc.OutParams[i];
                string domainName = QKeyValue.FindStringAttribute(formal.Attributes, "linear");
                if (domainName == null)
                {
                    Error(node, "Only a linear variable can be passed to a linear parameter");
                    continue;
                }
                if (domainName != actualDomainName)
                {
                    Error(node, "The domains of formal and actual parameters must be the same");
                }
            }
            return base.VisitCallCmd(node);
        }
    }

    public class LinearSetTransform
    {
        private Program program;
        private Dictionary<Variable, string> varToDomainName;
        private Dictionary<string, LinearDomain> linearDomains;

        public LinearSetTransform(Program program)
        {
            this.program = program;
            this.varToDomainName = new Dictionary<Variable, string>();
            this.linearDomains = new Dictionary<string, LinearDomain>();
        }

        private void AddVariableToScope(Variable v, Dictionary<string, HashSet<Variable>> domainNameToScope)
        {
            var domainName = QKeyValue.FindStringAttribute(v.Attributes, "linear");
            if (domainName == null) return;            
            AddVariableToScopeHelper(v, domainName, domainNameToScope);
        }

        private void AddVariableToScope(Implementation impl, int outParamIndex, Dictionary<string, HashSet<Variable>> domainNameToScope)
        {
            var domainName = QKeyValue.FindStringAttribute(impl.Proc.OutParams[outParamIndex].Attributes, "linear");
            if (domainName == null) return;
            AddVariableToScopeHelper(impl.OutParams[outParamIndex], domainName, domainNameToScope);
        }

        private void AddVariableToScopeHelper(Variable v, string domainName, Dictionary<string, HashSet<Variable>> domainNameToScope)
        {
            if (!varToDomainName.ContainsKey(v))
            {
                varToDomainName[v] = domainName;
            }
            if (!linearDomains.ContainsKey(domainName))
            {
                var domain = new LinearDomain(program, v, domainName);
                linearDomains[domainName] = domain;
                varToDomainName[domain.allocator] = domainName;
            } 
            if (!domainNameToScope.ContainsKey(domainName))
            {
                HashSet<Variable> scope = new HashSet<Variable>();
                scope.Add(linearDomains[domainName].allocator);
                domainNameToScope[domainName] = scope;
            }
            domainNameToScope[domainName].Add(v);
        }

        public void Transform()
        {
            foreach (var decl in program.TopLevelDeclarations)
            {
                Implementation impl = decl as Implementation;
                if (impl == null) continue;

                Dictionary<string, HashSet<Variable>> domainNameToScope = new Dictionary<string, HashSet<Variable>>();
                foreach (Variable v in program.GlobalVariables())
                {
                    AddVariableToScope(v, domainNameToScope);
                }
                for (int i = 0; i < impl.OutParams.Length; i++)
                {
                    AddVariableToScope(impl, i, domainNameToScope);
                }
                foreach (Variable v in impl.LocVars)
                {
                    AddVariableToScope(v, domainNameToScope);
                }

                Dictionary<Variable, LocalVariable> copies = new Dictionary<Variable, LocalVariable>();
                foreach (Block b in impl.Blocks)
                {
                    CmdSeq newCmds = new CmdSeq();
                    for (int i = 0; i < b.Cmds.Length; i++)
                    {
                        Cmd cmd = b.Cmds[i];
                        if (cmd is AssignCmd)
                        {
                            TransformAssignCmd(newCmds, (AssignCmd)cmd, copies);
                        }
                        else if (cmd is HavocCmd)
                        {
                            TransformHavocCmd(newCmds, (HavocCmd)cmd, copies, domainNameToScope);
                        }
                        else if (cmd is CallCmd)
                        {
                            TransformCallCmd(newCmds, (CallCmd)cmd, copies, domainNameToScope);
                        }
                        else
                        {
                            newCmds.Add(cmd);
                        }
                    }
                    if (b.TransferCmd is ReturnCmd)
                    {
                        foreach (Variable v in impl.LocVars)
                        {
                            if (varToDomainName.ContainsKey(v)) 
                                Drain(newCmds, v, varToDomainName[v]);
                        }
                    }
                    b.Cmds = newCmds;
                }

                foreach (var v in copies.Values)
                {
                    impl.LocVars.Add(v);
                }

                {
                    // Loops
                    impl.PruneUnreachableBlocks();
                    impl.ComputePredecessorsForBlocks();
                    GraphUtil.Graph<Block> g = Program.GraphFromImpl(impl);
                    g.ComputeLoops();
                    if (g.Reducible)
                    {
                        foreach (Block header in g.Headers)
                        {
                            CmdSeq newCmds = new CmdSeq();
                            foreach (string domainName in domainNameToScope.Keys)
                            {
                                newCmds.Add(new AssumeCmd(Token.NoToken, DisjointnessExpr(domainName, domainNameToScope[domainName])));
                            }
                            newCmds.AddRange(header.Cmds);
                            header.Cmds = newCmds;
                        }
                    }
                }

                {
                    // Initialization
                    CmdSeq newCmds = new CmdSeq();
                    foreach (Variable v in impl.OutParams)
                    {
                        if (varToDomainName.ContainsKey(v))
                            Initialize(newCmds, v);
                    }
                    foreach (Variable v in impl.LocVars)
                    {
                        if (varToDomainName.ContainsKey(v))
                            Initialize(newCmds, v);
                    }
                    newCmds.AddRange(impl.Blocks[0].Cmds);
                    impl.Blocks[0].Cmds = newCmds;
                }
            }

            foreach (var decl in program.TopLevelDeclarations)
            {
                Implementation impl = decl as Implementation;
                if (impl == null) continue;
                Procedure proc = impl.Proc;

                Dictionary<string, HashSet<Variable>> domainNameToScope = new Dictionary<string, HashSet<Variable>>();
                foreach (Variable v in program.GlobalVariables())
                {
                    AddVariableToScope(v, domainNameToScope);
                }
                foreach (string domainName in domainNameToScope.Keys)
                {
                    proc.Requires.Add(new Requires(true, DisjointnessExpr(domainName, domainNameToScope[domainName])));
                }
                Dictionary<string, HashSet<Variable>> domainNameToInParams = new Dictionary<string, HashSet<Variable>>();
                foreach (Variable v in proc.InParams)
                {
                    var domainName = QKeyValue.FindStringAttribute(v.Attributes, "linear");
                    if (domainName == null) continue;
                    if (!linearDomains.ContainsKey(domainName))
                    {
                        linearDomains[domainName] = new LinearDomain(program, v, domainName);
                    } 
                    if (!domainNameToInParams.ContainsKey(domainName))
                    {
                        domainNameToInParams[domainName] = new HashSet<Variable>();
                    }
                    domainNameToInParams[domainName].Add(v);
                    var domain = linearDomains[domainName];
                    Expr e = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapImpBool), new ExprSeq(new IdentifierExpr(Token.NoToken, v), new IdentifierExpr(Token.NoToken, domain.allocator)));
                    e = Expr.Binary(BinaryOperator.Opcode.Eq, e, new NAryExpr(Token.NoToken, new FunctionCall(domain.mapConstBool), new ExprSeq(Expr.True)));
                    proc.Requires.Add(new Requires(true, e));
                }
                foreach (var domainName in domainNameToInParams.Keys)
                {
                    proc.Requires.Add(new Requires(true, DisjointnessExpr(domainName, domainNameToInParams[domainName])));
                }
            }

            foreach (LinearDomain domain in linearDomains.Values)
            {
                program.TopLevelDeclarations.Add(domain.allocator);
            }
        }

        void Initialize(CmdSeq newCmds, Variable v)
        {
            var domainName = varToDomainName[v];
            var domain = linearDomains[domainName];
            List<AssignLhs> lhss = new List<AssignLhs>();
            lhss.Add(new SimpleAssignLhs(Token.NoToken, new IdentifierExpr(Token.NoToken, v)));
            List<Expr> rhss = new List<Expr>();
            rhss.Add(new NAryExpr(Token.NoToken, new FunctionCall(domain.mapConstBool), new ExprSeq(Expr.False)));
            newCmds.Add(new AssignCmd(Token.NoToken, lhss, rhss));
        }

        void Drain(CmdSeq newCmds, Variable v, string domainName)
        {
            var domain = linearDomains[domainName];
            List<AssignLhs> lhss = MkAssignLhss(domain.allocator);
            List<Expr> rhss = MkExprs(new NAryExpr(Token.NoToken, new FunctionCall(domain.mapOrBool), new ExprSeq(new IdentifierExpr(Token.NoToken, domain.allocator), new IdentifierExpr(Token.NoToken, v))));
            newCmds.Add(new AssignCmd(Token.NoToken, lhss, rhss));
        }

        List<AssignLhs> MkAssignLhss(params Variable[] args)
        {
            List<AssignLhs> lhss = new List<AssignLhs>();
            foreach (Variable arg in args)
            {
                lhss.Add(new SimpleAssignLhs(Token.NoToken, new IdentifierExpr(Token.NoToken, arg)));
            }
            return lhss;
        }

        List<Expr> MkExprs(params Expr[] args)
        {
            return new List<Expr>(args);
        }

        void TransformHavocCmd(CmdSeq newCmds, HavocCmd havocCmd, Dictionary<Variable, LocalVariable> copies, Dictionary<string, HashSet<Variable>> domainNameToScope)
        {
            Dictionary<string, HashSet<Variable>> domainNameToHavocVars = new Dictionary<string, HashSet<Variable>>();
            foreach (IdentifierExpr ie in havocCmd.Vars)
            {
                Variable v = ie.Decl;
                if (!varToDomainName.ContainsKey(v)) continue;
                var domainName = varToDomainName[v];
                if (!domainNameToHavocVars.ContainsKey(domainName))
                {
                    domainNameToHavocVars[domainName] = new HashSet<Variable>();
                }
                domainNameToHavocVars[domainName].Add(v);
                if (!copies.ContainsKey(v))
                {
                    copies[v] = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("copy_{0}", v.Name), v.TypedIdent.Type));
                }
                var allocator = linearDomains[domainName].allocator;
                domainNameToHavocVars[domainName].Add(allocator);
                if (!copies.ContainsKey(allocator))
                {
                    copies[allocator] = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("copy_{0}", allocator.Name), allocator.TypedIdent.Type));
                }
            }
            foreach (var domainName in domainNameToHavocVars.Keys)
            {
                List<AssignLhs> lhss = new List<AssignLhs>();
                List<Expr> rhss = new List<Expr>();
                foreach (var v in domainNameToHavocVars[domainName])
                {
                    lhss.Add(new SimpleAssignLhs(Token.NoToken, new IdentifierExpr(Token.NoToken, copies[v])));
                    rhss.Add(new IdentifierExpr(Token.NoToken, v));
                }
                newCmds.Add(new AssignCmd(Token.NoToken, lhss, rhss));
                havocCmd.Vars.Add(new IdentifierExpr(Token.NoToken, linearDomains[domainName].allocator));
            }
            newCmds.Add(havocCmd);
            foreach (string domainName in domainNameToHavocVars.Keys)
            {
                var domain = linearDomains[domainName];
                Expr oldExpr = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapConstBool), new ExprSeq(Expr.False));
                Expr expr = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapConstBool), new ExprSeq(Expr.False));
                foreach (var v in domainNameToHavocVars[domainName])
                {
                    oldExpr = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapOrBool), new ExprSeq(new IdentifierExpr(Token.NoToken, copies[v]), oldExpr));
                    expr = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapOrBool), new ExprSeq(new IdentifierExpr(Token.NoToken, v), expr));
                }
                newCmds.Add(new AssumeCmd(Token.NoToken, Expr.Binary(BinaryOperator.Opcode.Eq, oldExpr, expr)));
                newCmds.Add(new AssumeCmd(Token.NoToken, DisjointnessExpr(domainName, domainNameToScope[domainName])));
            }
        }

        void TransformCallCmd(CmdSeq newCmds, CallCmd callCmd, Dictionary<Variable, LocalVariable> copies, Dictionary<string, HashSet<Variable>> domainNameToScope)
        {
            List<Expr> newIns = new List<Expr>();
            for (int i = 0; i < callCmd.Ins.Count; i++)
            {
                IdentifierExpr ie = callCmd.Ins[i] as IdentifierExpr;
                if (ie == null)
                {
                    newIns.Add(callCmd.Ins[i]);
                    continue;
                }
                Variable source = ie.Decl;
                Variable target = callCmd.Proc.InParams[i];
                if (!varToDomainName.ContainsKey(source) || !varToDomainName.ContainsKey(target))
                {
                    newIns.Add(ie);
                    continue;
                }
                if (!copies.ContainsKey(source))
                {
                    copies[source] = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("copy_{0}", source.Name), source.TypedIdent.Type));
                }
                newIns.Add(new IdentifierExpr(Token.NoToken, copies[source]));
                newCmds.Add(new AssignCmd(Token.NoToken, MkAssignLhss(copies[source], source), MkExprs(new IdentifierExpr(Token.NoToken, source), new NAryExpr(Token.NoToken, new FunctionCall(linearDomains[varToDomainName[source]].mapConstBool), new ExprSeq(Expr.False)))));
            }
            HashSet<Variable> drainedVars = new HashSet<Variable>();
            foreach (IdentifierExpr ie in callCmd.Outs)
            {
                Variable v = ie.Decl;
                if (!varToDomainName.ContainsKey(v)) continue;
                if (!copies.ContainsKey(v))
                {
                    copies[v] = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("copy_{0}", v.Name), v.TypedIdent.Type));
                }
                newCmds.Add(new AssignCmd(Token.NoToken, MkAssignLhss(copies[v]), MkExprs(new IdentifierExpr(Token.NoToken, v))));
                drainedVars.Add(v);
            }
            callCmd.Ins = newIns;
            newCmds.Add(callCmd);
            foreach (Variable v in drainedVars)
            {
                Drain(newCmds, copies[v], varToDomainName[v]);
            }
            foreach (Variable v in drainedVars)
            {
                string domainName = varToDomainName[v];
                newCmds.Add(new AssumeCmd(Token.NoToken, DisjointnessExpr(domainName, domainNameToScope[domainName])));
            }
        }

        void TransformAssignCmd(CmdSeq newCmds, AssignCmd assignCmd, Dictionary<Variable, LocalVariable> copies)
        {
            List<Expr> newRhss = new List<Expr>();
            for (int i = 0; i < assignCmd.Rhss.Count; i++)
            {
                IdentifierExpr ie = assignCmd.Rhss[i] as IdentifierExpr;
                if (ie == null)
                {
                    newRhss.Add(assignCmd.Rhss[i]);
                    continue;
                }
                Variable source = ie.Decl;
                Variable target = assignCmd.Lhss[i].DeepAssignedVariable;
                if (!varToDomainName.ContainsKey(source) || !varToDomainName.ContainsKey(target))
                {
                    newRhss.Add(ie);
                    continue;
                }
                if (!copies.ContainsKey(source))
                {
                    copies[source] = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("copy_{0}", source.Name), source.TypedIdent.Type));
                }
                newRhss.Add(new IdentifierExpr(Token.NoToken, copies[source]));
                newCmds.Add(new AssignCmd(Token.NoToken, MkAssignLhss(copies[source], source), MkExprs(new IdentifierExpr(Token.NoToken, source), new NAryExpr(Token.NoToken, new FunctionCall(linearDomains[varToDomainName[source]].mapConstBool), new ExprSeq(Expr.False)))));
            }
            HashSet<Variable> drainedVars = new HashSet<Variable>();
            foreach (AssignLhs lhs in assignCmd.Lhss)
            {
                Variable v = lhs.DeepAssignedVariable;
                if (!varToDomainName.ContainsKey(v)) continue;
                if (!copies.ContainsKey(v))
                {
                    copies[v] = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("copy_{0}", v.Name), v.TypedIdent.Type));
                } 
                newCmds.Add(new AssignCmd(Token.NoToken, MkAssignLhss(copies[v]), MkExprs(new IdentifierExpr(Token.NoToken, v))));
                drainedVars.Add(v);
            }
            assignCmd.Rhss = newRhss;
            newCmds.Add(assignCmd);
            foreach (Variable v in drainedVars)
            {
                Drain(newCmds, copies[v], varToDomainName[v]);
            }
        }

        Expr DisjointnessExpr(string domainName, HashSet<Variable> scope)
        {
            LinearDomain domain = linearDomains[domainName];
            BoundVariable partition = new BoundVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("partition_{0}", domainName), new MapType(Token.NoToken, new TypeVariableSeq(), new TypeSeq(domain.elementType), Microsoft.Boogie.Type.Int)));
            Expr disjointExpr = Expr.True;
            int count = 0;
            foreach (Variable var in scope)
            {
                Expr e = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapConstInt), new ExprSeq(new LiteralExpr(Token.NoToken, Microsoft.Basetypes.BigNum.FromInt(count++))));
                e = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapEqInt), new ExprSeq(new IdentifierExpr(Token.NoToken, partition), e));
                e = new NAryExpr(Token.NoToken, new FunctionCall(domain.mapImpBool), new ExprSeq(new IdentifierExpr(Token.NoToken, var), e));
                e = Expr.Binary(BinaryOperator.Opcode.Eq, e, new NAryExpr(Token.NoToken, new FunctionCall(domain.mapConstBool), new ExprSeq(Expr.True)));
                disjointExpr = Expr.Binary(BinaryOperator.Opcode.And, e, disjointExpr);
            }
            return new ExistsExpr(Token.NoToken, new VariableSeq(partition), disjointExpr);
        }
    }

    class LinearDomain
    {
        public GlobalVariable allocator;
        public Microsoft.Boogie.Type elementType;
        public Function mapEqInt;
        public Function mapConstInt;
        public Function mapOrBool;
        public Function mapImpBool;
        public Function mapConstBool;

        public LinearDomain(Program program, Variable var, string domainName)
        {
            this.allocator = new GlobalVariable(Token.NoToken, new TypedIdent(Token.NoToken, string.Format("allocator_{0}", domainName), var.TypedIdent.Type));
            this.elementType = ((MapType)var.TypedIdent.Type).Arguments[0];
            foreach (var decl in program.TopLevelDeclarations)
            {
                Function func = decl as Function;
                if (func == null) continue;
                var name = QKeyValue.FindStringAttribute(func.Attributes, "builtin");
                if (name == null) continue;
                MapType mapType = func.OutParams[0].TypedIdent.Type as MapType;
                if (mapType == null) continue;
                if (mapType.Arguments.Length > 1) continue;
                if (!mapType.Arguments[0].Equals(elementType)) continue;
                if (mapType.Result.Equals(Microsoft.Boogie.Type.Bool))
                {
                    if (name == "MapOr")
                        this.mapOrBool = func;
                    else if (name == "MapImp")
                        this.mapImpBool = func;
                    else if (name == "MapConst")
                        this.mapConstBool = func;
                    else if (name == "MapEq")
                        this.mapEqInt = func;
                }
                else if (mapType.Result.Equals(Microsoft.Boogie.Type.Int))
                {
                    if (name == "MapConst")
                        this.mapConstInt = func;
                }
            }
        }
    }
}
