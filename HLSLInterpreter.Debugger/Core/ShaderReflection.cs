using HLSL;
using HLSLInterpreter.Debugger.Services;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public sealed record VertexInput(string SemanticBase, int SemanticIndex, int Dimensions);

public static class ShaderReflection
{
    public sealed record AssembledShader(string Source, string VertexEntry, IReadOnlyList<VertexInput>? VertexInputs);
    public static AssembledShader AssembleVertexShader(
        string userSource,
        string vertEntry,
        ShaderRenderMode mode,
        HLSLParserConfig config)
    {
        string DebuggerPreamble = @"
            cbuffer DebuggerGlobals : register(b0) {
                float2 _WarpSize;
                float2 _Resolution;
                float _Time;
                float4x4 _ViewProjection;
            };

            // Default vertex shader for GPU path when rendering fullscreen (pixel) mode.
            [shader(""vertex"")]
            float4 dbgVertex(uint vid : SV_VertexID) : SV_Position {
                float2 p = float2(float((vid << 1u) & 2u), float(vid & 2u));
                return float4(p * 2.0 - 1.0, 0.0, 1.0);
            }
        ";
        string source = DebuggerPreamble + "\n" + userSource;

        if (mode == ShaderRenderMode.Pixel)
            return new AssembledShader(source, "dbgVertex", null);

        var runner = new HLSLRunner();
        runner.ProcessCode(source, config);

        var vertFunc = runner.GetFunction(vertEntry)
            ?? throw new InvalidOperationException($"Vertex function '{vertEntry}' not found in shader.");

        var inputs = new List<VertexInput>();
        WalkParameters(runner, vertFunc, (type, semantic, dim, modifiers) =>
        {
            // System values come from pipeline state (@builtin in WGSL), not
            // the vertex buffer. Skip them in the layout enumeration.
            if (semantic.Base != null && semantic.Base.StartsWith("SV_", StringComparison.Ordinal)) return;
            if (semantic.Base == null)
                throw new InvalidOperationException("Vertex input leaf is missing a semantic.");
            inputs.Add(new VertexInput(semantic.Base, semantic.Index, dim));
        });
        return new AssembledShader(source, vertEntry, inputs);
    }

    #region General reflection
    public static bool TryAsStruct(TypeNode type, HLSLRunner runner, out StructTypeNode structType)
    {
        if (type is StructTypeNode inline && inline.Name != null)
        {
            structType = inline;
            return true;
        }
        if (type is UserDefinedNamedTypeNode named)
        {
            structType = runner.GetStructType(named.GetName());
            return structType != null;
        }
        structType = null;
        return false;
    }

    public static bool TryGetDimensions(TypeNode type, out int dimensions)
    {
        switch (type)
        {
            case ScalarTypeNode:
                dimensions = 1;
                return true;
            case VectorTypeNode v:
                dimensions = v.Dimension;
                return true;
            default:
                dimensions = 0;
                return false;
        }
    }

    public static bool TryGetSemantic(VariableDeclaratorNode declarator, out (string Base, int Index) semantic)
    {
        foreach (var q in declarator.Qualifiers)
        {
            if (q is SemanticNode sn)
            {
                semantic = CanonicalizeSemantic(sn.Name.Identifier);
                return true;
            }
        }
        semantic = default;
        return false;
    }

    // Function-level semantic, e.g. `float4 vert(...) : SV_POSITION`.
    public static bool TryGetSemantic(FunctionNode func, out (string Base, int Index) semantic)
    {
        if (func.Semantic != null)
        {
            semantic = CanonicalizeSemantic(func.Semantic.Name.Identifier);
            return true;
        }
        semantic = default;
        return false;
    }

    public static ScalarType GetScalarType(TypeNode type) => type switch
    {
        ScalarTypeNode s => s.Kind,
        VectorTypeNode v => v.Kind,
        _ => ScalarType.Float,
    };

    // Canonicalize semantic, eg: TEXCOORD2 -> (TEXCOORD, 2)
    private static (string Base, int Index) CanonicalizeSemantic(string raw)
    {
        int splitAt = raw.Length;
        while (splitAt > 0 && char.IsDigit(raw[splitAt - 1]))
        {
            splitAt--;
        }
        string baseName = raw.Substring(0, splitAt).ToUpperInvariant();
        int index = splitAt < raw.Length ? int.Parse(raw.Substring(splitAt)) : 0;
        return (baseName, index);
    }

    public static int LocateSemanticOffset(HLSLRunner runner, FunctionNode vertFunc, string semantic)
    {
        int cursor = 0;
        int found = -1;
        WalkReturn(runner, vertFunc, (type, otherSemantic, dim, modifiers) =>
        {
            if (found < 0 && otherSemantic.Base == semantic)
                found = cursor;
            cursor += dim;
        });
        return found;
    }

    public static RawValue[] FlattenReturnValue(HLSLRunner runner, FunctionNode func, HLSLValue value)
    {
        static void AppendStructRaws(HLSLRunner runner, StructTypeNode structType, StructValue value, List<RawValue> raws)
        {
            foreach (var field in structType.Fields)
            {
                foreach (var decl in field.Declarators)
                {
                    string name = decl.Name.Identifier;
                    if (!value.Members.TryGetValue(name, out var member))
                        throw new InvalidOperationException($"Struct member '{name}' missing from runtime value.");

                    var resolvedField = runner.ResolveType(field.Kind);
                    if (TryAsStruct(resolvedField, runner, out var nested))
                        AppendStructRaws(runner, nested!, (StructValue)member, raws);
                    else
                        AppendNumericRaws(member, raws);
                }
            }
        }

        static void AppendNumericRaws(HLSLValue value, List<RawValue> raws)
        {
            if (value is ScalarValue sv)
            {
                raws.Add(sv.Value.Get(0));
            }
            else if (value is VectorValue vv)
            {
                var comps = vv.Values.Get(0);
                for (int i = 0; i < vv.Size; i++)
                    raws.Add(comps[i]);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported leaf value type '{value?.GetType().Name}'.");
            }
        }

        var raws = new List<RawValue>();
        var resolved = runner.ResolveType(func.ReturnType);
        if (TryAsStruct(resolved, runner, out var structType))
            AppendStructRaws(runner, structType!, (StructValue)value, raws);
        else
            AppendNumericRaws(value, raws);
        return raws.ToArray();
    }
    #endregion

    #region Visitor
    public delegate void LeafVisitor(TypeNode type, (string Base, int Index) semantic, int dim, IList<BindingModifier> modifiers);

    public static void WalkParameters(HLSLRunner runner, FunctionNode func, LeafVisitor visitor)
    {
        foreach (var param in func.Parameters)
            WalkType(runner, param.ParamType, param.Declarator, param.Modifiers, visitor);
    }

    public static void WalkReturn(HLSLRunner runner, FunctionNode func, LeafVisitor visitor)
    {
        var resolved = runner.ResolveType(func.ReturnType);
        if (TryAsStruct(resolved, runner, out var structType))
        {
            WalkStruct(runner, structType!, visitor);
            return;
        }
        TryGetSemantic(func, out var semantic);
        if (!TryGetDimensions(resolved, out int dim))
            throw new InvalidOperationException("Function return must be a scalar, vector, or struct.");
        visitor(resolved, semantic, dim, Array.Empty<BindingModifier>());
    }

    private static void WalkType(HLSLRunner runner, TypeNode type, VariableDeclaratorNode declarator, IList<BindingModifier> modifiers, LeafVisitor visitor)
    {
        var resolved = runner.ResolveType(type);
        if (TryAsStruct(resolved, runner, out var structType))
        {
            WalkStruct(runner, structType!, visitor);
            return;
        }
        TryGetSemantic(declarator, out var semantic);
        if (!TryGetDimensions(resolved, out int dim))
            throw new InvalidOperationException(
                $"Unsupported leaf type for '{declarator.Name.Identifier}'. Only scalar and vector types are supported.");
        visitor(resolved, semantic, dim, modifiers);
    }

    private static void WalkStruct(HLSLRunner runner, StructTypeNode structType, LeafVisitor visitor)
    {
        foreach (var field in structType.Fields)
            foreach (var decl in field.Declarators)
                WalkType(runner, field.Kind, decl, field.Modifiers, visitor);
    }
    #endregion

    #region Visit producing HLSLValue
    public delegate HLSLValue LeafFactory(TypeNode type, (string Base, int Index) semantic, int dim, IList<BindingModifier> modifiers);

    public static HLSLValue[] BuildArgs(HLSLRunner runner, FunctionNode func, LeafFactory leafFactory)
    {
        var args = new HLSLValue[func.Parameters.Count];
        for (int p = 0; p < func.Parameters.Count; p++)
        {
            var param = func.Parameters[p];
            args[p] = BuildArg(runner, param.ParamType, param.Declarator, param.Modifiers, leafFactory);
        }
        return args;
    }

    private static HLSLValue BuildArg(HLSLRunner runner, TypeNode type, VariableDeclaratorNode declarator, IList<BindingModifier> modifiers, LeafFactory leafFactory)
    {
        var resolved = runner.ResolveType(type);
        if (TryAsStruct(resolved, runner, out var structType))
            return BuildStructArg(runner, structType!, leafFactory);

        TryGetSemantic(declarator, out var semantic);
        if (!TryGetDimensions(resolved, out int dim))
            throw new InvalidOperationException(
                $"Unsupported parameter type for '{declarator.Name.Identifier}'. Only scalar and vector types are supported.");
        return leafFactory(resolved, semantic, dim, modifiers);
    }

    private static HLSLValue BuildStructArg(HLSLRunner runner, StructTypeNode structType, LeafFactory leafFactory)
    {
        var members = new Dictionary<string, HLSLValue>();
        foreach (var field in structType.Fields)
        {
            foreach (var decl in field.Declarators)
            {
                string name = decl.Name.Identifier;
                var resolvedField = runner.ResolveType(field.Kind);
                HLSLValue val;
                if (TryAsStruct(resolvedField, runner, out var nested))
                {
                    val = BuildStructArg(runner, nested!, leafFactory);
                }
                else
                {
                    TryGetSemantic(decl, out var semantic);
                    if (!TryGetDimensions(resolvedField, out int dim))
                        throw new InvalidOperationException(
                            $"Unsupported struct member type for '{name}'.");
                    val = leafFactory(resolvedField, semantic, dim, field.Modifiers);
                }
                members[name] = val;
            }
        }
        return new StructValue(structType.Name.GetName(), members);
    }
    #endregion
}
