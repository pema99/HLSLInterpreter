using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityShaderParser.HLSL;

namespace HLSL
{
    public class HLSLInterpreterContext
    {
        private sealed class Scope
        {
            public readonly bool IsFunction;
            public readonly string FunctionName;
            public readonly Dictionary<string, HLSLValue> Variables = new Dictionary<string, HLSLValue>();
            public readonly Dictionary<string, List<FunctionDefinitionNode>> Functions = new Dictionary<string, List<FunctionDefinitionNode>>();
            public readonly Dictionary<string, StructTypeNode> Structs = new Dictionary<string, StructTypeNode>();
            public readonly Dictionary<string, TypeNode> TypeAliases = new Dictionary<string, TypeNode>();
            public readonly HashSet<string> GroupsharedVars = new HashSet<string>();

            public Scope(bool isFunction, string functionName = null)
            {
                IsFunction = isFunction;
                FunctionName = functionName;
            }
        }

        public HLSLInterpreterContext()
        {
            globalScope = new Scope(false);
            environment = new Stack<Scope>();
            environment.Push(globalScope);
        }

        private Stack<Scope> environment;
        private Scope globalScope;

        private Stack<HLSLValue> returnStack = new Stack<HLSLValue>();
        private Stack<string> namespaceStack = new Stack<string>();

        public void EnterNamespace(string name)
        {
            namespaceStack.Push(name);
        }

        public void ExitNamespace()
        {
            namespaceStack.Pop();
        }

        public bool IsGlobalScope() => environment.Count <= 1;

        public void PushScope(bool isFunction = false, string functionName = null)
        {
            environment.Push(new Scope(isFunction, functionName));
        }

        public void PopScope()
        {
            environment.Pop();
        }

        public string[] GetCallStack()
        {
            return environment
                .Where(s => s.IsFunction && s.FunctionName != null)
                .Select(s => s.FunctionName)
                .ToArray();
        }

        // Returns per-frame local variable snapshots, innermost frame first.
        public Dictionary<string, HLSLValue>[] GetVariablesPerFrame()
        {
            var frames = new List<Dictionary<string, HLSLValue>>();
            var currentFrame = new Dictionary<string, HLSLValue>();

            foreach (var scope in environment.Take(environment.Count - 1))
            {
                // Innermost scope wins for same-name vars within a frame
                foreach (var kvp in scope.Variables)
                {
                    if (!currentFrame.ContainsKey(kvp.Key))
                        currentFrame[kvp.Key] = kvp.Value;
                }

                if (scope.IsFunction)
                {
                    frames.Add(currentFrame);
                    currentFrame = new Dictionary<string, HLSLValue>();
                }
            }

            return frames.ToArray();
        }

        public Dictionary<string, HLSLValue> GetGlobalVariables()
        {
            return globalScope.Variables;
        }

        // Returns a snapshot of all variables visible from the current scope.
        public Dictionary<string, HLSLValue> GetVisibleVariables()
        {
            var result = new Dictionary<string, HLSLValue>();
            // Add global scope first (lowest priority)
            foreach (var kvp in globalScope.Variables)
            {
                result[kvp.Key] = kvp.Value;
            }
            // Add local scopes outermost-to-innermost
            var localScopes = environment.Take(environment.Count - 1).Reverse();
            foreach (var scope in localScopes)
            {
                foreach (var kvp in scope.Variables)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        private bool TryFindVariable(string name, out Scope resolvedScope, out string resolvedName, out HLSLValue resolvedValue, out bool isGlobal)
        {
            int count = environment.Count;
            int idx = 0;
            foreach (var scope in environment)
            {
                if (idx == count - 1)
                    break;

                if (scope.Variables.TryGetValue(name, out var val))
                {
                    resolvedScope = scope;
                    resolvedName = name;
                    resolvedValue = val;
                    isGlobal = false;
                    return true;
                }
                if (scope.IsFunction)
                    break;

                idx++;
            }

            var globalVars = globalScope.Variables;
            foreach (string candidate in CandidateNames(name))
            {
                if (globalVars.TryGetValue(candidate, out var val))
                {
                    resolvedScope = globalScope;
                    resolvedName = candidate;
                    resolvedValue = val;
                    isGlobal = true;
                    return true;
                }
            }

            resolvedScope = null;
            resolvedName = null;
            resolvedValue = null;
            isGlobal = false;
            return false;
        }

        public HLSLValue GetVariable(string name)
        {
            TryFindVariable(name, out _, out _, out HLSLValue value, out _);
            return value;
        }

        public ReferenceValue GetReference(string name)
        {
            if (TryFindVariable(name, out var scope, out var resolvedName, out _, out _))
            {
                if (scope.Variables[resolvedName] is ReferenceValue refVal)
                    return refVal;
                else
                    return new ReferenceValue(() => scope.Variables[resolvedName], val => scope.Variables[resolvedName] = val);
            }
            return null;
        }

        public bool TryGetVariable(string name, out HLSLValue variable)
        {
            variable = GetVariable(name);
            return variable != null;
        }

        public bool HasVariable(string name)
        {
            return GetVariable(name) != null;
        }

        public void SetVariable(string name, HLSLValue val)
        {
            if (environment.Count <= 1)
            {
                SetGlobalVariable(name, val);
                return;
            }

            if (TryFindVariable(name, out var scope, out var resolvedName, out _, out _))
            {
                scope.Variables[resolvedName] = val;
                return;
            }

            environment.Peek().Variables[name] = val;
        }

        // Like SetVariable but always add to top scope.
        public void AddVariable(string name, HLSLValue val, bool groupShared = false)
        {
            if (environment.Count <= 1)
            {
                SetGlobalVariable(name, val);
                if (groupShared)
                    globalScope.GroupsharedVars.Add(GetQualifiedName(name));
                return;
            }

            var scope = environment.Peek();
            scope.Variables[name] = val;
            if (groupShared)
                scope.GroupsharedVars.Add(name);
        }

        public string GetQualifiedName(string name)
        {
            if (namespaceStack.Count > 0)
                name = $"{string.Join("::", namespaceStack.Reverse())}::{name}";

            return name;
        }

        public void SetGlobalVariable(string name, HLSLValue type)
        {
            environment.Peek().Variables[GetQualifiedName(name)] = type;
        }

        public bool IsGroupShared(string name)
        {
            if (TryFindVariable(name, out var scope, out var resolvedName, out _, out _))
                return scope.GroupsharedVars.Contains(resolvedName);
            return false;
        }

        // Used to signficantly reduce allocs for the common no-namespace case.
        private readonly string[] singleCandidateHolder = new string[1];

        // Returns candidate qualified names for a given name under the current namespace stack,
        // from most-specific prefix to least-specific (unqualified).
        private string[] CandidateNames(string name)
        {
            // No namespace case.
            int nsCount = namespaceStack.Count;
            if (nsCount == 0)
            {
                singleCandidateHolder[0] = name;
                return singleCandidateHolder;
            }

            // Get all parts of the prefix. If we are in A::B::C, we will have { A, B, C }.
            var result = new string[nsCount + 1];
            string[] nsArr = new string[nsCount];
            int i = nsCount - 1;
            foreach (var ns in namespaceStack)
            {
                nsArr[i--] = ns;
            }

            // Get all prefixes. If we are in A::B::C, we will have { A::B::C::Name, A::B::Name, A::Name, Name }
            var sb = new StringBuilder();
            for (int prefixLen = nsCount; prefixLen > 0; prefixLen--)
            {
                sb.Clear();
                for (int p = 0; p < prefixLen; p++)
                {
                    if (p > 0)
                        sb.Append("::");
                    sb.Append(nsArr[p]);
                }
                sb.Append("::");
                sb.Append(name);
                result[nsCount - prefixLen] = sb.ToString();
            }
            result[nsCount] = name;
            return result;
        }

        public FunctionDefinitionNode GetFunction(HLSLExpressionEvaluator evaluator, string name, HLSLValue[] args)
        {
            foreach (var scope in environment)
            {
                foreach (string candidate in CandidateNames(name))
                {
                    if (scope.Functions.TryGetValue(candidate, out var funcs))
                    {
                        var overload = HLSLOverloadResolution.PickOverload(evaluator, this, funcs, args);
                        if (overload != null)
                            return overload;
                    }
                }
            }
            return null;
        }

        public FunctionDefinitionNode GetFunction(string name)
        {
            foreach (var scope in environment)
            {
                foreach (string candidate in CandidateNames(name))
                {
                    if (scope.Functions.TryGetValue(candidate, out var funcs) && funcs.Count > 0)
                    {
                        return funcs[0];
                    }
                }
            }
            return null;
        }

        public (string QualifiedName, FunctionDefinitionNode Func)[] GetFunctions()
        {
            return environment
                .SelectMany(s => s.Functions)
                .SelectMany(kvp => kvp.Value.Select(f => (kvp.Key, f)))
                .ToArray();
        }

        public void AddFunction(string name, FunctionDefinitionNode func)
        {
            name = GetQualifiedName(name);
            var functions = environment.Peek().Functions;
            if (!functions.TryGetValue(name, out var overloads))
            {
                overloads = new List<FunctionDefinitionNode>();
                functions[name] = overloads;
            }
            overloads.Add(func);
        }

        public StructTypeNode GetStructType(string name)
        {
            foreach (var scope in environment)
            {
                foreach (string candidate in CandidateNames(name))
                {
                    if (scope.Structs.TryGetValue(candidate, out var structType))
                        return structType;
                }
            }
            return null;
        }

        public string GetQualifiedStructName(string name)
        {
            foreach (var scope in environment)
            {
                foreach (string candidate in CandidateNames(name))
                {
                    if (scope.Structs.ContainsKey(candidate))
                        return candidate;
                }
            }
            return null;
        }

        public void AddStruct(string name, StructTypeNode structType)
        {
            environment.Peek().Structs[GetQualifiedName(name)] = structType;
        }

        public void AddTypeAlias(string name, TypeNode aliasedType)
        {
            environment.Peek().TypeAliases[GetQualifiedName(name)] = aliasedType;
        }

        private bool TryFindTypeAlias(string name, out TypeNode resolvedType)
        {
            // Search from innermost scope outward, stopping at function boundaries for local scopes.
            int count = environment.Count;
            int idx = 0;
            foreach (var scope in environment)
            {
                if (idx == count - 1)
                    break;

                if (scope.TypeAliases.TryGetValue(name, out resolvedType))
                    return true;
                if (scope.IsFunction)
                    break;

                idx++;
            }
            // Fall through to global scope with namespace resolution.
            foreach (string candidate in CandidateNames(name))
            {
                if (globalScope.TypeAliases.TryGetValue(candidate, out resolvedType))
                    return true;
            }
            resolvedType = null;
            return false;
        }

        public TypeNode ResolveType(TypeNode node)
        {
            int limit = 32; // guard against alias cycles
            while (limit-- > 0 && node is UserDefinedNamedTypeNode named)
            {
                string rawName = named.GetName();
                if (TryFindTypeAlias(rawName, out var resolved))
                    node = resolved;
                else
                    break;
            }
            return node;
        }

        public bool TryLookupTypeAlias(string name, out TypeNode resolvedType)
        {
            if (TryFindTypeAlias(name, out resolvedType))
            {
                resolvedType = ResolveType(resolvedType);
                return true;
            }
            resolvedType = null;
            return false;
        }

        public void PushReturn(HLSLValue value)
        {
            returnStack.Push(value);
        }

        public HLSLValue PeekReturn()
        {
            return returnStack.Peek();
        }

        public HLSLValue PopReturn()
        {
            return returnStack.Pop();
        }
    }
}
