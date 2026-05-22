using System.Text.Json.Serialization;

namespace HLSLInterpreter.Debugger.Execution;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextureFilter
{
    Point,
    Linear,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextureAddress
{
    Wrap,
    Clamp,
    Mirror,
}

public sealed class TextureBinding
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public byte[] Rgba8 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string PreviewDataUrl { get; set; }

    public TextureBinding Clone() => new()
    {
        Name = Name,
        FileName = FileName,
        Rgba8 = Rgba8,
        Width = Width,
        Height = Height,
        PreviewDataUrl = PreviewDataUrl,
    };
}

public sealed class PickedImage
{
    public string FileName { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Rgba8Base64 { get; set; }
    public string DataUrl { get; set; }

    public TextureBinding ToTextureBinding() => new()
    {
        FileName = FileName,
        Width = Width,
        Height = Height,
        Rgba8 = Convert.FromBase64String(Rgba8Base64 ?? ""),
        PreviewDataUrl = DataUrl,
    };
}

public sealed class SamplerBinding
{
    public string Name { get; set; } = "";
    public TextureFilter Filter { get; set; } = TextureFilter.Linear;
    public TextureAddress Address { get; set; } = TextureAddress.Wrap;

    public SamplerBinding Clone() => new()
    {
        Name = Name,
        Filter = Filter,
        Address = Address,
    };
}
