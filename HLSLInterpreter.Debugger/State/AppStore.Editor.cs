using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Services;

namespace HLSLInterpreter.Debugger.State;

public partial class AppStore
{
    public void SetDocuments(IReadOnlyList<ShaderDocument> documents, int activeIndex) =>
        Update(s => s with { Editor = s.Editor with {
            Documents = documents,
            ActiveIndex = Math.Clamp(activeIndex, 0, Math.Max(0, documents.Count - 1)),
        } });

    public void SetActiveDocument(int index) =>
        Update(s => index >= 0 && index < s.Editor.Documents.Count
            ? s with { Editor = s.Editor with { ActiveIndex = index } }
            : s);

    public void AddDocument(ShaderDocument document) =>
        Update(s =>
        {
            var documents = s.Editor.Documents.Append(document).ToArray();
            return s with { Editor = s.Editor with
            {
                Documents = documents,
                ActiveIndex = documents.Length - 1,
            } };
        });

    public void CloseDocument(int index) =>
        Update(s =>
        {
            var editor = s.Editor;
            if (editor.Documents.Count <= 1 || index < 0 || index >= editor.Documents.Count) return s;
            var documents = editor.Documents.ToList();
            documents.RemoveAt(index);
            int active = editor.ActiveIndex;
            if (index < active || active >= documents.Count) active--;
            return s with { Editor = editor with
            {
                Documents = documents,
                ActiveIndex = Math.Clamp(active, 0, documents.Count - 1),
            } };
        });

    public void MoveDocument(int from, int desired) =>
        Update(s =>
        {
            var editor = s.Editor;
            if (from < 0 || from >= editor.Documents.Count) return s;
            if (desired == from || desired == from + 1) return s;
            var documents = editor.Documents.ToList();
            var active = documents[editor.ActiveIndex];
            var moving = documents[from];
            documents.RemoveAt(from);
            documents.Insert(desired > from ? desired - 1 : desired, moving);
            return s with { Editor = editor with
            {
                Documents = documents,
                ActiveIndex = documents.IndexOf(active),
            } };
        });

    public void UpdateDocument(int index, Func<ShaderDocument, ShaderDocument> update) =>
        Update(s =>
        {
            if (index < 0 || index >= s.Editor.Documents.Count) return s;
            var documents = s.Editor.Documents.ToArray();
            documents[index] = update(documents[index]);
            return s with { Editor = s.Editor with { Documents = documents } };
        });

    public void UpdateActiveDocument(Func<ShaderDocument, ShaderDocument> update) =>
        UpdateDocument(State.Editor.ActiveIndex, update);

    public void UpdateActiveConfig(Func<ShaderConfig, ShaderConfig> update) =>
        Update(s => WithActiveConfig(s, update));

    public void SetActiveCode(string code) =>
        UpdateActiveDocument(d => d with { Code = code ?? "" });

    public void SetFontSize(int size) =>
        Update(s => s with { Editor = s.Editor with { FontSize = size } });

    public void SetRenderMode(ShaderRenderMode mode) =>
        UpdateActiveConfig(c => c with { RenderMode = mode });

    public void SetFragmentEntryPoint(string entry) =>
        UpdateActiveConfig(c => c with { FragmentEntryPoint = entry });

    public void SetVertexEntryPoint(string entry) =>
        UpdateActiveConfig(c => c with { VertexEntryPoint = entry });

    public void SetGroupOffset(int x, int y) =>
        UpdateActiveConfig(c => c with { GroupOffsetX = x, GroupOffsetY = y });

    public void SetCpuMode(CpuMode mode) =>
        UpdateActiveConfig(c => c with { CpuMode = mode });

    public void SetDebugTarget(DebugTarget target) =>
        UpdateActiveConfig(c => c with { DebugTarget = target });

    public void SetMesh(Mesh mesh) =>
        UpdateActiveConfig(c => c with { Mesh = mesh });

    public void SetTextures(IReadOnlyList<TextureBinding> textures) =>
        UpdateActiveConfig(c => c with { Textures = textures });

    public void SetSamplers(IReadOnlyList<SamplerBinding> samplers) =>
        UpdateActiveConfig(c => c with { Samplers = samplers });

    public void SetWarpSize(int warpX, int warpY) =>
        Update(s => WithClampedInspectedThread(
            WithActiveConfig(s, c => c with { WarpX = Math.Max(1, warpX), WarpY = Math.Max(1, warpY) })));

    private static AppState WithActiveConfig(AppState s, Func<ShaderConfig, ShaderConfig> update)
    {
        var doc = s.Editor.ActiveDocument;
        if (doc == null) return s;
        var documents = s.Editor.Documents.ToArray();
        documents[s.Editor.ActiveIndex] = doc with { Config = update(doc.Config) };
        return s with { Editor = s.Editor with { Documents = documents } };
    }
}
