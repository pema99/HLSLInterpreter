using HLSLInterpreter.Debugger.Execution;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Services;

public sealed class ImageLibraryService
{
    private Task _examplesLoad;

    public List<TextureBinding> Examples { get; } = new();
    public List<TextureBinding> RecentUploads { get; } = new();

    public Task EnsureExamplesLoadingAsync() => _examplesLoad ??= LoadExamplesAsync();

    private async Task LoadExamplesAsync()
    {
        const string baseUrl = "_content/HLSLInterpreter.Debugger/ExampleTextures/";
        string index;
        try { index = await BrowserInterop.FetchText(baseUrl + "index.txt"); }
        catch (Exception ex) { Console.WriteLine($"[examples-tex] index fetch failed: {ex.Message}"); return; }

        foreach (var line in index.Split('\n'))
        {
            var name = line.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            Examples.Add(new TextureBinding { FileName = name, PreviewDataUrl = baseUrl + name });
        }
    }

    private readonly Dictionary<string, Task> _bytesInFlight = new();

    public Task EnsureBytesAsync(TextureBinding tex)
    {
        if (tex == null || string.IsNullOrEmpty(tex.PreviewDataUrl)) return Task.CompletedTask;
        if (tex.Rgba8 != null && tex.Rgba8.Length > 0) return Task.CompletedTask;
        var url = tex.PreviewDataUrl;
        if (_bytesInFlight.TryGetValue(url, out var existing)) return existing;
        var task = FetchBytesAsync(tex, url);
        _bytesInFlight[url] = task;
        return task;
    }

    private async Task FetchBytesAsync(TextureBinding tex, string url)
    {
        try
        {
            var picked = await BrowserInterop.FetchImage(url);
            if (picked == null) return;
            var img = picked.ToTextureBinding();
            if (img.Rgba8 == null || img.Rgba8.Length == 0) return;
            tex.Rgba8 = img.Rgba8;
            tex.Width = img.Width;
            tex.Height = img.Height;
        }
        catch (Exception ex) { Console.WriteLine($"[image-library] ensure bytes for {url}: {ex.Message}"); }
        finally { _bytesInFlight.Remove(url); }
    }

    public void AddRecent(TextureBinding tex)
    {
        if (tex == null || tex.Rgba8 == null) return;
        var key = tex.FileName ?? "";
        var existing = RecentUploads.FindIndex(t => (t.FileName ?? "") == key);
        if (existing >= 0)
        {
            Revoke(RecentUploads[existing].PreviewDataUrl);
            RecentUploads.RemoveAt(existing);
        }
        RecentUploads.Insert(0, tex);
        while (RecentUploads.Count > 20)
        {
            Revoke(RecentUploads[^1].PreviewDataUrl);
            RecentUploads.RemoveAt(RecentUploads.Count - 1);
        }
    }

    private void Revoke(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        _ = BrowserInterop.RevokeBlobUrl(url);
    }
}
