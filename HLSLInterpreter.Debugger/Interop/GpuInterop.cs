using HLSLInterpreter.Debugger.Core;
using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

public sealed record GpuRenderRequest(
    string CanvasId,
    string Source,
    string FragmentEntryPoint,
    int WarpX,
    int WarpY,
    object DotNetRef,
    string Mode,
    string VertexEntryPoint,
    IReadOnlyList<VertexInput> VertexInputs,
    float[] MeshVertices,
    uint[] MeshIndices,
    float Time,
    IReadOnlyList<TextureBinding> Textures,
    IReadOnlyList<SamplerBinding> Samplers);

// Typed wrapper over the WebGPU preview loop in gpu.js.
public interface IGpuInterop
{
    ValueTask<bool> IsAvailable();
    ValueTask Render(GpuRenderRequest request);
    ValueTask Stop();
    ValueTask Pause();
    ValueTask Resume();
    ValueTask Restart();
    ValueTask<float[]> Snapshot();
    ValueTask<float[]> View();
    ValueTask<float[]> Projection(int canvasW, int canvasH);
    ValueTask<float[]> Mouse();
}

public sealed class GpuInterop : IGpuInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/gpu.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference _module;

    public GpuInterop(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> Module() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask<bool> IsAvailable()
    {
        try { return await (await Module()).InvokeAsync<bool>("gpuIsAvailable"); }
        catch { return false; }
    }

    public async ValueTask Render(GpuRenderRequest r) =>
        await (await Module()).InvokeVoidAsync("gpuRender", r.CanvasId, r.Source, r.FragmentEntryPoint,
            r.WarpX, r.WarpY, r.DotNetRef, r.Mode, r.VertexEntryPoint, r.VertexInputs,
            r.MeshVertices, r.MeshIndices, r.Time, r.Textures, r.Samplers);

    public async ValueTask Stop() => await (await Module()).InvokeVoidAsync("gpuStop");

    public async ValueTask Pause() => await (await Module()).InvokeVoidAsync("gpuPause");

    public async ValueTask Resume() => await (await Module()).InvokeVoidAsync("gpuResume");

    public async ValueTask Restart() => await (await Module()).InvokeVoidAsync("gpuRestart");

    public async ValueTask<float[]> Snapshot() => await (await Module()).InvokeAsync<float[]>("gpuSnapshot");

    public async ValueTask<float[]> View() => await (await Module()).InvokeAsync<float[]>("gpuView");

    public async ValueTask<float[]> Projection(int canvasW, int canvasH) =>
        await (await Module()).InvokeAsync<float[]>("gpuProjection", canvasW, canvasH);

    public async ValueTask<float[]> Mouse() => await (await Module()).InvokeAsync<float[]>("gpuMouse");
}
