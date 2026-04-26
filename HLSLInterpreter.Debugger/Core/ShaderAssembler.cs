using HLSL;
using HLSLInterpreter.Debugger.Services;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public sealed record VertexInput(string SemanticBase, int SemanticIndex, int Dimensions);

public static class ShaderAssembler
{
    public const string DebuggerPreamble = @"
cbuffer DebuggerGlobals : register(b0) {
    float2 _WarpSize;
    float2 _Resolution;
    float _Time;
    float4x4 _ViewProjection;
};

struct DbgVSOut { float4 pos : SV_Position; };

[shader(""vertex"")]
DbgVSOut dbgVertex(uint vid : SV_VertexID) {
    float2 p = float2(float((vid << 1u) & 2u), float(vid & 2u));
    DbgVSOut o;
    o.pos = float4(p * 2.0 - 1.0, 0.0, 1.0);
    return o;
}
";

    public sealed record AssembledShader(string Source, string VertexEntry, IReadOnlyList<VertexInput>? VertexInputs);

    public static AssembledShader Assemble(
        string userSource,
        string vertEntry,
        ShaderRenderMode mode,
        HLSLParserConfig config)
    {
        string source = DebuggerPreamble + "\n" + userSource;

        if (mode == ShaderRenderMode.Pixel)
            return new AssembledShader(source, "dbgVertex", null);

        var runner = new HLSLRunner();
        runner.ProcessCode(source, config);

        var vertFunc = runner.GetFunction(vertEntry)
            ?? throw new InvalidOperationException($"Vertex function '{vertEntry}' not found in shader.");

        var inputs = new List<VertexInput>();
        foreach (var param in vertFunc.Parameters)
        {
            FlattenParameter(param, runner, inputs);
        }

        return new AssembledShader(source, vertEntry, inputs);
    }

    private static void FlattenParameter(
        FormalParameterNode param,
        HLSLRunner runner,
        List<VertexInput> outList)
    {
        string label = param.Declarator.Name.Identifier;
        var resolvedType = runner.ResolveType(param.ParamType);
        bool isStruct = TryAsStruct(resolvedType, runner, out var structType);
        bool hasSemantic = TryGetSemantic(param.Declarator, out var semantic);

        if (hasSemantic && !isStruct)
        {
            AddLeaf(resolvedType, semantic, label, outList);
            return;
        }

        if (isStruct)
        {
            FlattenStruct(structType!, runner, outList);
            return;
        }

        throw new InvalidOperationException(
            $"Vertex parameter '{label}' has no semantic and is not a known struct type.");
    }

    private static void FlattenStruct(
        StructTypeNode structType,
        HLSLRunner runner,
        List<VertexInput> outList)
    {
        foreach (var field in structType.Fields)
        {
            foreach (var decl in field.Declarators)
            {
                string label = decl.Name.Identifier;
                var resolvedFieldType = runner.ResolveType(field.Kind);
                bool isStruct = TryAsStruct(resolvedFieldType, runner, out var nestedStruct);
                bool hasSemantic = TryGetSemantic(decl, out var semantic);

                if (hasSemantic && !isStruct)
                {
                    AddLeaf(resolvedFieldType, semantic, label, outList);
                    continue;
                }

                if (isStruct)
                {
                    FlattenStruct(nestedStruct!, runner, outList);
                    continue;
                }

                throw new InvalidOperationException($"Struct field '{label}' has no semantic and is not a nested struct.");
            }
        }
    }

    private static void AddLeaf(
        TypeNode type,
        (string Base, int Index) semantic,
        string label,
        List<VertexInput> outList)
    {
        // System values come from pipeline state (@builtin in WGSL), not from
        // the vertex buffer. Skip them in the layout enumeration.
        if (semantic.Base.StartsWith("SV_", StringComparison.Ordinal)) return;

        if (!TryGetDimensions(type, out int dim))
        {
            throw new InvalidOperationException($"Unsupported input type for '{label}'. Only scalar/vector float types are supported.");
        }

        outList.Add(new VertexInput(semantic.Base, semantic.Index, dim));
    }

    private static bool TryAsStruct(TypeNode type, HLSLRunner runner, out StructTypeNode? structType)
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

    private static bool TryGetDimensions(TypeNode type, out int dimensions)
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

    private static bool TryGetSemantic(VariableDeclaratorNode declarator, out (string Base, int Index) semantic)
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

    // Canonicalize semantic, eg:
    // TEXCOORD2 -> (TEXCOORD, 2)
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
}
