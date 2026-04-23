using System;
using System.Collections.Generic;
using System.Linq;
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

        private HashSet<string> groupsharedVars = new HashSet<string>();

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

        private bool TryFindVariable(string name, out Dictionary<string, HLSLValue> resolvedScope, out string resolvedName, out HLSLValue resolvedValue, out bool isGlobal)
        {
            int count = environment.Count;
            int idx = 0;
            foreach (var scope in environment)
            {
                if (idx == count - 1)
                    break;

                if (scope.Variables.TryGetValue(name, out var val))
                {
                    resolvedScope = scope.Variables;
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
                    resolvedScope = globalVars;
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
                if (scope[resolvedName] is ReferenceValue refVal)
                    return refVal;
                else
                    return new ReferenceValue(() => scope[resolvedName], val => scope[resolvedName] = val);
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
                scope[resolvedName] = val;
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
                    groupsharedVars.Add(GetQualifiedName(name));
                return;
            }

            environment.Peek().Variables[name] = val;
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
            if (TryFindVariable(name, out _, out var resolvedName, out _, out bool isGlobal))
                return isGlobal && groupsharedVars.Contains(resolvedName);
            return false;
        }

        // Yields candidate qualified names for a given name under the current namespace stack,
        // from most-specific prefix to least-specific (unqualified).
        private IEnumerable<string> CandidateNames(string name)
        {
            if (namespaceStack.Count > 0)
            {
                var revNamespace = namespaceStack.Reverse().ToArray();
                for (int i = 0; i < namespaceStack.Count + 1; i++)
                {
                    int prefixLen = namespaceStack.Count - i;
                    string prefix = string.Join("::", revNamespace.Take(prefixLen));
                    yield return string.IsNullOrEmpty(prefix) ? name : $"{prefix}::{name}";
                }
            }
            else
            {
                yield return name;
            }
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

        public void PushReturn()
        {
            // We don't know the type yet, so just put a dummy object
            returnStack.Push(ScalarValue.Null);
        }

        public void SetReturn(int threadIndex, HLSLValue value)
        {
            var oldReturn = returnStack.Pop();
            // If this is the first return, just use it directly.
            if (oldReturn is ScalarValue sv && sv.Type == ScalarType.Void)
            {
                returnStack.Push(value);
            }
            // Otherwise splat the thread value
            else
            {
                var newReturn = HLSLValueUtils.SetThreadValue(oldReturn, threadIndex, value);
                returnStack.Push(newReturn);
            }
        }

        public HLSLValue PopReturn()
        {
            return returnStack.Pop();
        }
    }
}
