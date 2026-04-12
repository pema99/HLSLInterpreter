using System;
using System.Collections.Generic;
using System.Linq;
using UnityShaderParser.HLSL;

namespace UnityShaderParser.Test
{
    public class HLSLInterpreterContext
    {
        private sealed class Scope
        {
            public readonly bool IsFunction;
            public readonly Dictionary<string, HLSLValue> Variables = new Dictionary<string, HLSLValue>();
            public readonly Dictionary<string, List<FunctionDefinitionNode>> Functions = new Dictionary<string, List<FunctionDefinitionNode>>();
            public readonly Dictionary<string, StructTypeNode> Structs = new Dictionary<string, StructTypeNode>();

            public Scope(bool isFunction) => IsFunction = isFunction;
        }

        private Stack<Scope> environment = new Stack<Scope>(new[] { new Scope(false) });
        private Stack<HLSLValue> returnStack = new Stack<HLSLValue>();
        private Stack<string> namespaceStack = new Stack<string>();

        private Dictionary<string, TypeNode> typeAliases = new Dictionary<string, TypeNode>();
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

        public void PushScope(bool isFunction = false)
        {
            environment.Push(new Scope(isFunction));
        }

        public void PopScope()
        {
            environment.Pop();
        }

        private bool TryFindVariable(string name, out Dictionary<string, HLSLValue> resolvedScope, out string resolvedName, out HLSLValue resolvedValue, out bool isGlobal)
        {
            // Local scope: search all scopes except the global one, stopping at a function boundary.
            var localScopes = environment.Take(environment.Count - 1);
            foreach (var scope in localScopes)
            {
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
            }

            // Not in local scope, try global scope with namespace resolution.
            var globalVars = environment.Last().Variables;
            if (namespaceStack.Count > 0)
            {
                var reverseNamespace = namespaceStack.Reverse().ToArray();
                for (int i = 0; i < namespaceStack.Count + 1; i++)
                {
                    int prefixLength = namespaceStack.Count - i;
                    string currPrefix = string.Join("::", reverseNamespace.Take(prefixLength));
                    string qualifiedName = string.IsNullOrEmpty(currPrefix) ? name : $"{currPrefix}::{name}";
                    if (globalVars.TryGetValue(qualifiedName, out var val))
                    {
                        resolvedScope = globalVars;
                        resolvedName = qualifiedName;
                        resolvedValue = val;
                        isGlobal = true;
                        return true;
                    }
                }
            }
            else
            {
                if (globalVars.TryGetValue(name, out var val))
                {
                    resolvedScope = globalVars;
                    resolvedName = name;
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
                        var overload = HLSLValueUtils.PickOverload(evaluator, funcs, args);
                        if (overload != null)
                            return overload;
                    }
                }
            }
            return null;
        }

        public FunctionDefinitionNode[] GetFunctions()
        {
            return environment.SelectMany(s => s.Functions.Values).SelectMany(x => x).ToArray();
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

        public StructTypeNode GetStruct(string name)
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

        public void AddStruct(string name, StructTypeNode structType)
        {
            environment.Peek().Structs[GetQualifiedName(name)] = structType;
        }

        public void AddTypeAlias(string name, TypeNode aliasedType)
        {
            typeAliases[GetQualifiedName(name)] = aliasedType;
        }

        public TypeNode ResolveType(TypeNode node)
        {
            int limit = 32; // guard against alias cycles
            while (limit-- > 0 && node is UserDefinedNamedTypeNode named)
            {
                string rawName = named.GetName();
                string qualName = GetQualifiedName(rawName);
                if (typeAliases.TryGetValue(qualName, out var resolved) ||
                    typeAliases.TryGetValue(rawName, out resolved))
                    node = resolved;
                else
                    break;
            }
            return node;
        }

        public bool TryLookupTypeAlias(string name, out TypeNode resolvedType)
        {
            string qualName = GetQualifiedName(name);
            if (typeAliases.TryGetValue(qualName, out resolvedType) ||
                typeAliases.TryGetValue(name, out resolvedType))
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
