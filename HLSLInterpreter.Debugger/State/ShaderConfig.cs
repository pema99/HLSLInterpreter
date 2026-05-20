using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Services;

namespace HLSLInterpreter.Debugger.State;

// Per-document shader settings. Owned by the document that uses them, so a tab
// switch is just a change of active document, not a rewrite of global state.
public sealed record ShaderConfig
{
    public ShaderRenderMode RenderMode { get; init; } = ShaderRenderMode.Pixel;
    public string FragmentEntryPoint { get; init; } = "frag";
    public string VertexEntryPoint { get; init; } = "vert";
    public int WarpX { get; init; } = 16;
    public int WarpY { get; init; } = 16;
    public int GroupOffsetX { get; init; }
    public int GroupOffsetY { get; init; }
    public CpuMode CpuMode { get; init; } = CpuMode.SingleWarp;
    public DebugTarget DebugTarget { get; init; } = DebugTarget.Pixel;
    public Mesh Mesh { get; init; } = Mesh.CreateCube();
    public IReadOnlyList<TextureBinding> Textures { get; init; } = Array.Empty<TextureBinding>();
    public IReadOnlyList<SamplerBinding> Samplers { get; init; } = Array.Empty<SamplerBinding>();
}

public sealed record ShaderDocument
{
    public int Id { get; init; }
    public string Name { get; init; } = "new.hlsl";
    public string Path { get; init; }

    // Last-synced editor text. The active document's live text is in Monaco
    // and synced into here on demand (tab switch, run, save).
    public string Code { get; init; } = "";

    public ShaderConfig Config { get; init; } = new();
}
