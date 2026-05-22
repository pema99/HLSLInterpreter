using System.IO.Compression;
using System.Text;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Execution;

namespace HLSLInterpreter.Debugger.Utils;

public sealed record PermalinkSettings(
    string EntryPoint,
    int WarpX,
    int WarpY,
    int GroupOffsetX,
    int GroupOffsetY,
    bool GpuPreviewEnabled,
    ShaderRenderMode ShaderRenderMode,
    string VertexEntryPoint,
    CpuMode CpuMode);

public static class PermalinkCodec
{
    public static string CompressCode(string code)
    {
        var bytes = Encoding.UTF8.GetBytes(code);
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal))
            gz.Write(bytes, 0, bytes.Length);
        return Convert.ToBase64String(output.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string DecompressCode(string encoded)
    {
        encoded = encoded.Replace('-', '+').Replace('_', '/');
        switch (encoded.Length % 4)
        {
            case 2: encoded += "=="; break;
            case 3: encoded += "="; break;
        }
        var bytes = Convert.FromBase64String(encoded);
        using var input = new MemoryStream(bytes);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public static string GetQueryParam(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == key)
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    public static string BuildUrl(string baseUrl, string code, PermalinkSettings settings)
    {
        string encoded = CompressCode(code);
        return $"{baseUrl.TrimEnd('/')}/?c={encoded}"
            + $"&e={Uri.EscapeDataString(settings.EntryPoint)}"
            + $"&wx={settings.WarpX}&wy={settings.WarpY}"
            + $"&gx={settings.GroupOffsetX}&gy={settings.GroupOffsetY}"
            + $"&g={(settings.GpuPreviewEnabled ? 1 : 0)}"
            + $"&m={(settings.ShaderRenderMode == ShaderRenderMode.VertFrag ? "vf" : "p")}"
            + $"&ev={Uri.EscapeDataString(settings.VertexEntryPoint)}"
            + $"&cpu={CpuModeToCode(settings.CpuMode)}";
    }

    private static string CpuModeToCode(CpuMode m) => m switch
    {
        CpuMode.FullFrame => "ff",
        CpuMode.FullFrameWithMetrics => "ffm",
        _ => "sw",
    };

    private static CpuMode? CodeToCpuMode(string code) => code switch
    {
        "sw" => CpuMode.SingleWarp,
        "ff" => CpuMode.FullFrame,
        "ffm" => CpuMode.FullFrameWithMetrics,
        _ => null,
    };

    public static PermalinkSettings ApplyToSettings(string url, PermalinkSettings current)
    {
        var ep = GetQueryParam(url, "e");
        string entryPoint = !string.IsNullOrEmpty(ep) ? ep : current.EntryPoint;
        int warpX = (int.TryParse(GetQueryParam(url, "wx"), out var wx) && wx > 0) ? wx : current.WarpX;
        int warpY = (int.TryParse(GetQueryParam(url, "wy"), out var wy) && wy > 0) ? wy : current.WarpY;
        int groupOffsetX = (int.TryParse(GetQueryParam(url, "gx"), out var gx) && gx >= 0) ? gx : current.GroupOffsetX;
        int groupOffsetY = (int.TryParse(GetQueryParam(url, "gy"), out var gy) && gy >= 0) ? gy : current.GroupOffsetY;
        var g = GetQueryParam(url, "g");
        bool gpu = g == "1" ? true : g == "0" ? false : current.GpuPreviewEnabled;
        var m = GetQueryParam(url, "m");
        ShaderRenderMode mode = m == "vf" ? ShaderRenderMode.VertFrag : m == "p" ? ShaderRenderMode.Pixel : current.ShaderRenderMode;
        var evp = GetQueryParam(url, "ev");
        string vertEntry = !string.IsNullOrEmpty(evp) ? evp : current.VertexEntryPoint;
        CpuMode cpuMode = CodeToCpuMode(GetQueryParam(url, "cpu")) ?? current.CpuMode;
        return new PermalinkSettings(entryPoint, warpX, warpY, groupOffsetX, groupOffsetY, gpu, mode, vertEntry, cpuMode);
    }
}
