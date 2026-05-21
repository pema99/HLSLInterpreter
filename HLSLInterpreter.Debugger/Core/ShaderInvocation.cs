using HLSL;
using HLSLInterpreter.Debugger.State;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public sealed record ShaderInvocation(
    ShaderRenderMode Mode,
    string FragmentEntryPoint,
    string VertexEntryPoint,
    Mesh Mesh,
    int WarpX,
    int WarpY,
    int GroupOffsetX,
    int GroupOffsetY,
    int CanvasW,
    int CanvasH,
    float Time,
    float[] View,
    float[] Projection,
    float[] Mouse,
    int DebugVertexIndex,
    IReadOnlyList<TextureBinding> Textures,
    IReadOnlyList<SamplerBinding> Samplers)
{
    public Action OnTextureFetch { get; set; }

    public void SetUniforms(HLSLRunner runner)
    {
        runner.SetVariable("_WarpSize", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([(float)WarpX, (float)WarpY])));
        runner.SetVariable("_Resolution", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([(float)CanvasW, (float)CanvasH])));
        runner.SetVariable("_Time", new ScalarValue(ScalarType.Float, new HLSLRegister<RawValue>(Time)));
        runner.SetVariable("_Mouse", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([Mouse[0], Mouse[1], Mouse[2], Mouse[3]])));
        if (Mode == ShaderRenderMode.VertFrag && View != null && Projection != null)
        {
            runner.SetVariable("_View", BuildMatrix(View));
            runner.SetVariable("_Projection", BuildMatrix(Projection));
        }
    }

    private void BindTexturesAndSamplers(HLSLRunner runner)
    {
        var texByName = new Dictionary<string, TextureBinding>();
        if (Textures != null)
            foreach (var t in Textures)
                if (!string.IsNullOrEmpty(t.Name)) texByName[t.Name] = t;

        var sampByName = new Dictionary<string, SamplerBinding>();
        if (Samplers != null)
            foreach (var s in Samplers)
                if (!string.IsNullOrEmpty(s.Name)) sampByName[s.Name] = s;

        foreach (var kvp in runner.GetGlobalVariables().ToList())
        {
            if (kvp.Value is ResourceValue tv && tv.IsTexture)
            {
                if (texByName.TryGetValue(kvp.Key, out var tex) && tex.Rgba8 != null && tex.Width > 0 && tex.Height > 0)
                    runner.SetVariable(kvp.Key, BuildTextureResource(tv, tex, OnTextureFetch));
                else
                    runner.SetVariable(kvp.Key, new ResourceValue(tv.Type, tv.TemplateArguments, tv.Stride,
                        sizeX: 1, sizeY: 1, sizeZ: 1, mipCount: 1,
                        get: (x, y, z, sample, mip) => { OnTextureFetch?.Invoke(); return new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([1f, 0f, 1f, 1f])); },
                        set: null));
            }
            else if (kvp.Value is SamplerStateValue)
            {
                runner.SetVariable(kvp.Key, sampByName.TryGetValue(kvp.Key, out var s) ? BuildSampler(s) : new SamplerStateValue
                {
                    Filter = SamplerStateValue.FilterMode.MinMagMipLinear,
                    AddressU = SamplerStateValue.TextureAddressMode.Wrap,
                    AddressV = SamplerStateValue.TextureAddressMode.Wrap,
                    AddressW = SamplerStateValue.TextureAddressMode.Wrap,
                });
            }
        }
    }

    private static ResourceValue BuildTextureResource(ResourceValue template, TextureBinding tex, Action onFetch)
    {
        int w = tex.Width, h = tex.Height;
        byte[] data = tex.Rgba8;
        ResourceGetter get = (x, y, z, sample, mip) =>
        {
            onFetch?.Invoke();
            int xc = Math.Clamp(x, 0, w - 1);
            int yc = Math.Clamp(y, 0, h - 1);
            int o = (yc * w + xc) * 4;
            float r = data[o + 0] / 255f;
            float g = data[o + 1] / 255f;
            float b = data[o + 2] / 255f;
            float a = data[o + 3] / 255f;
            return new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([r, g, b, a]));
        };
        return new ResourceValue(template.Type, template.TemplateArguments, template.Stride,
            sizeX: w, sizeY: h, sizeZ: 1, mipCount: 1, get: get, set: null);
    }

    private static SamplerStateValue BuildSampler(SamplerBinding s)
    {
        var addr = s.Address switch
        {
            TextureAddress.Wrap => SamplerStateValue.TextureAddressMode.Wrap,
            TextureAddress.Clamp => SamplerStateValue.TextureAddressMode.Clamp,
            TextureAddress.Mirror => SamplerStateValue.TextureAddressMode.Mirror,
            _ => SamplerStateValue.TextureAddressMode.Wrap,
        };
        var filter = s.Filter == TextureFilter.Linear
            ? SamplerStateValue.FilterMode.MinMagMipLinear
            : SamplerStateValue.FilterMode.MinMagMipPoint;
        return new SamplerStateValue
        {
            Filter = filter,
            AddressU = addr,
            AddressV = addr,
            AddressW = addr,
        };
    }

    private static MatrixValue BuildMatrix(float[] m)
    {
        var raws = new RawValue[16];
        for (int i = 0; i < 16; i++) raws[i] = m[i];
        return new MatrixValue(ScalarType.Float, 4, 4, new HLSLRegister<RawValue[]>(raws));
    }

    public HLSLValue Execute(HLSLRunner runner)
    {
        BindTexturesAndSamplers(runner);
        int threadCount = WarpX * WarpY;
        if (Mode == ShaderRenderMode.VertFrag)
        {
            if (DebugVertexIndex >= 0)
            {
                int batchStart = (DebugVertexIndex / threadCount) * threadCount;
                int batchSize = Math.Min(threadCount, Mesh.VertexCount - batchStart);
                return SoftwareRenderer.RunVertOnly(runner, Mesh, VertexEntryPoint, WarpX, WarpY, batchStart, batchSize)[0];
            }
            else
            {
                return SoftwareRenderer.RunVertFrag(
                    runner, Mesh, VertexEntryPoint, FragmentEntryPoint,
                    WarpX, WarpY, CanvasW, CanvasH,
                    GroupOffsetX, GroupOffsetY);
            }
        }
        else
        {
            var fragFunc = runner.GetFunction(FragmentEntryPoint) ?? throw new InvalidOperationException($"Fragment function '{FragmentEntryPoint}' not found.");
            var fragArgs = ShaderReflection.BuildArgs(runner, fragFunc, (type, semantic, dim, modifiers) =>
            {
                ScalarType scalarType = ShaderReflection.GetScalarType(type);
                bool isPosition = semantic.Base == "SV_POSITION";

                var perThread = new RawValue[threadCount][];
                for (int threadIdx = 0; threadIdx < threadCount; threadIdx++)
                {
                    var row = new RawValue[dim];
                    if (isPosition)
                    {
                        if (dim > 0) row[0] = (float)(threadIdx % WarpX + GroupOffsetX * WarpX) + 0.5f;
                        if (dim > 1) row[1] = (float)(threadIdx / WarpX + GroupOffsetY * WarpY) + 0.5f;
                        if (dim > 2) row[2] = 0f;
                        if (dim > 3) row[3] = 1f;
                    }
                    perThread[threadIdx] = row;
                }

                if (dim == 1)
                {
                    var scalars = new RawValue[threadCount];
                    for (int threadIdx = 0; threadIdx < threadCount; threadIdx++)
                        scalars[threadIdx] = perThread[threadIdx][0];
                    return new ScalarValue(scalarType, HLSLValueUtils.MakeScalarVGPR(scalars));
                }
                return new VectorValue(scalarType, HLSLValueUtils.MakeVectorVGPR(perThread));
            });
            return runner.CallFunction(FragmentEntryPoint, fragArgs);
        }
    }
}
