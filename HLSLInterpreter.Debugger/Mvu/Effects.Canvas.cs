using HLSLInterpreter.Debugger.Core;

namespace HLSLInterpreter.Debugger.Mvu;

// Canvas sync. update computes the full projection after every message and it is
// pushed to JS in one call. The mesh is the one payload large enough to be worth
// diffing, so it is pushed separately only when it changes.
public sealed partial class Effects
{
    private Mesh _cPushedMesh;
    private string _cTheme;

    public Cmd SyncCanvas(CanvasProjection projection) =>
        Cmd.OfTask(() => SyncCanvasImpl(projection));

    private async Task SyncCanvasImpl(CanvasProjection p)
    {
        if (!ReferenceEquals(_cPushedMesh, p.PickMesh))
        {
            try
            {
                await _canvas.SetMeshData(p.PickMesh?.Positions, p.PickMesh?.Indices);
                _cPushedMesh = p.PickMesh;
            }
            catch { }
        }
        try { await _canvas.SetState(p with { PickMesh = null }); }
        catch { }
        if (_cTheme != p.Theme)
        {
            try { await _editor.SetTheme(p.Theme); _cTheme = p.Theme; }
            catch { }
        }
    }
}
