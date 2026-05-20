using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Services;

// Connects PermalinkCodec to the store and the clipboard.
public sealed class PermalinkService
{
    private readonly AppStore _store;
    private readonly IBrowserInterop _browser;

    public PermalinkService(AppStore store, IBrowserInterop browser)
    {
        _store = store;
        _browser = browser;
    }

    public async Task CopyAsync(string code, string baseUrl)
    {
        string url = PermalinkCodec.BuildUrl(baseUrl, code, CurrentSettings());
        await _browser.CopyToClipboard(url);
    }

    public void ApplyUrlToStore(string url)
    {
        var applied = PermalinkCodec.ApplyToSettings(url, CurrentSettings());
        _store.UpdateActiveConfig(c => c with
        {
            FragmentEntryPoint = applied.EntryPoint,
            VertexEntryPoint = applied.VertexEntryPoint,
            WarpX = applied.WarpX,
            WarpY = applied.WarpY,
            GroupOffsetX = applied.GroupOffsetX,
            GroupOffsetY = applied.GroupOffsetY,
            RenderMode = applied.ShaderRenderMode,
            CpuMode = applied.CpuMode,
        });
        _store.SetGpuPreviewEnabled(applied.GpuPreviewEnabled);
    }

    private PermalinkSettings CurrentSettings()
    {
        var config = _store.State.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        return new PermalinkSettings(
            config.FragmentEntryPoint,
            config.WarpX,
            config.WarpY,
            config.GroupOffsetX,
            config.GroupOffsetY,
            _store.State.Run.GpuPreviewEnabled,
            config.RenderMode,
            config.VertexEntryPoint,
            config.CpuMode);
    }
}
