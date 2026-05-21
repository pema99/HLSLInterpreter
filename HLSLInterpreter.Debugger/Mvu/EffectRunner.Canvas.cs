using HLSLInterpreter.Debugger.Core;

namespace HLSLInterpreter.Debugger.Mvu;

// Canvas sync. update computes the full projection after every message and the
// runner pushes only the fields that changed since the last sync. A field that
// fails to push (for example before its canvas is mounted) is left uncached so
// the next sync retries it.
public sealed partial class EffectRunner
{
    private (int, int)? _cWarp;
    private string _cRegularMode;
    private string _cDebugMode;
    private string _cPickMode;
    private string _cTheme;
    private (int, int)? _cDebugPixel;
    private (int, int)? _cCpuClick;
    private bool? _cDebugClick;
    private int[] _cThreadStates;
    private Mesh _cPushedMesh;

    private async Task SyncCanvasEffect(CanvasProjection p)
    {
        if (_cWarp != (p.WarpX, p.WarpY))
        {
            try { await _canvas.SetWarp(p.WarpX, p.WarpY); _cWarp = (p.WarpX, p.WarpY); }
            catch { }
        }
        if (_cRegularMode != p.RegularMode)
        {
            try { await _canvas.SetRegularMode(p.RegularMode); _cRegularMode = p.RegularMode; }
            catch { }
        }
        if (_cDebugMode != p.DebugMode)
        {
            try { await _canvas.SetDebugMode(p.DebugMode); _cDebugMode = p.DebugMode; }
            catch { }
        }
        if (_cDebugPixel != p.DebugPixel)
        {
            try { await _canvas.SetDebugPixel(p.DebugPixel?.X, p.DebugPixel?.Y); _cDebugPixel = p.DebugPixel; }
            catch { }
        }
        if (!ThreadStatesEqual(_cThreadStates, p.ThreadStates))
        {
            try { await _canvas.SetThreadStates(p.ThreadStates); _cThreadStates = p.ThreadStates; }
            catch { }
        }
        if (_cCpuClick != p.CpuClick)
        {
            try { await _canvas.SetCpuClickHandler(p.CpuClick?.X, p.CpuClick?.Y); _cCpuClick = p.CpuClick; }
            catch { }
        }
        if (_cDebugClick != p.DebugClick)
        {
            try { await _canvas.SetDebugClickHandler(p.DebugClick); _cDebugClick = p.DebugClick; }
            catch { }
        }
        if (_cPickMode != p.PickMode)
        {
            try { await _canvas.SetPickMode(p.PickMode); _cPickMode = p.PickMode; }
            catch { }
        }
        if (!ReferenceEquals(_cPushedMesh, p.PickMesh))
        {
            try
            {
                await _canvas.SetMeshData(p.PickMesh?.Positions, p.PickMesh?.Indices);
                _cPushedMesh = p.PickMesh;
            }
            catch { }
        }
        if (_cTheme != p.Theme)
        {
            try { await _editor.SetTheme(p.Theme); _cTheme = p.Theme; }
            catch { }
        }
    }

    private static bool ThreadStatesEqual(int[] a, int[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
