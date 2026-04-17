using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using HLSLInterpreter.Debugger;

namespace HLSLInterpreter.Debugger.Desktop;

public class WpfFileDialogService : FileDialogService
{
    private const string Filter = "HLSL Files (*.hlsl)|*.hlsl|All Files (*.*)|*.*";

    protected override void OnFileDropCallbackSet()
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var webView2 = FindDescendant<Microsoft.Web.WebView2.Wpf.WebView2>(Application.Current.MainWindow);
            if (webView2?.CoreWebView2 == null) return;
            webView2.CoreWebView2.WebMessageReceived += HandleFileDrop;
        });
    }

    private void HandleFileDrop(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (args.TryGetWebMessageAsString() != "FileDrop") return;
        foreach (var obj in args.AdditionalObjects)
        {
            if (obj is CoreWebView2File coreFile)
            {
                string path = coreFile.Path;
                string name = Path.GetFileName(path);
                try
                {
                    string content = File.ReadAllText(path);
                    Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (FileDropped != null)
                            await FileDropped(name, content, path);
                    });
                }
                catch { }
                break;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindDescendant<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    public override Task<(string? Path, string? Content)> OpenFile()
    {
        return Application.Current.Dispatcher.InvokeAsync<(string?, string?)>(() =>
        {
            var dialog = new OpenFileDialog { Filter = Filter };
            if (dialog.ShowDialog() == true)
                return (dialog.FileName, File.ReadAllText(dialog.FileName));
            return (null, null);
        }).Task;
    }

    public override Task<string?> SaveFile(string content, string? currentPath)
    {
        if (currentPath != null)
        {
            File.WriteAllText(currentPath, content);
            return Task.FromResult<string?>(currentPath);
        }
        return SaveFileAs(content);
    }

    public override Task<string?> SaveFileAs(string content)
    {
        return Application.Current.Dispatcher.InvokeAsync<string?>(() =>
        {
            var dialog = new SaveFileDialog { Filter = Filter, DefaultExt = "hlsl" };
            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, content);
                return dialog.FileName;
            }
            return null;
        }).Task;
    }
}
