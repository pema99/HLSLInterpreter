using HLSL;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public static class ValueImageRenderer
{
    public static byte FloatToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    public static byte[]? RenderVariableImage(HLSLValue val, int wx, int wy)
    {
        int threadCount = wx * wy;
        if (val is VectorValue vv)
        {
            var asFloat = (VectorValue)vv.Cast(ScalarType.Float);
            int components = vv.Size;
            float peak = 1f;
            int rgbChannels = Math.Min(3, components);
            for (int i = 0; i < threadCount; i++)
            {
                var c = asFloat.Values.Get(i);
                for (int k = 0; k < rgbChannels; k++)
                    if (c[k].Float > peak) peak = c[k].Float;
            }
            float scale = 1f / peak;
            var rgba = new byte[threadCount * 4];
            for (int i = 0; i < threadCount; i++)
            {
                var c = asFloat.Values.Get(i);
                rgba[i * 4 + 0] = FloatToByte((components > 0 ? c[0].Float : 0f) * scale);
                rgba[i * 4 + 1] = FloatToByte((components > 1 ? c[1].Float : 0f) * scale);
                rgba[i * 4 + 2] = FloatToByte((components > 2 ? c[2].Float : 0f) * scale);
                rgba[i * 4 + 3] = FloatToByte(components > 3 ? c[3].Float : 1f);
            }
            return rgba;
        }
        if (val is ScalarValue sv && sv.Type != ScalarType.Void)
        {
            float peak = 1f;
            for (int i = 0; i < threadCount; i++)
            {
                float v = sv.AsFloat(i);
                if (v > peak) peak = v;
            }
            float scale = 1f / peak;
            var rgba = new byte[threadCount * 4];
            for (int i = 0; i < threadCount; i++)
            {
                byte gray = FloatToByte(sv.AsFloat(i) * scale);
                rgba[i * 4 + 0] = gray;
                rgba[i * 4 + 1] = gray;
                rgba[i * 4 + 2] = gray;
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }
        return null;
    }

    public static byte[]? TryExtractImage(HLSLValue result, int wx, int wy)
    {
        int threadCount = wx * wy;

        if (result is VectorValue vv)
        {
            int components = vv.Size;
            var pixels = new byte[threadCount * 4];
            var asFloat = (VectorValue)vv.Cast(ScalarType.Float);
            for (int i = 0; i < threadCount; i++)
            {
                var c = asFloat.Values.Get(i);
                pixels[i * 4 + 0] = FloatToByte(components > 0 ? c[0].Float : 0f);
                pixels[i * 4 + 1] = FloatToByte(components > 1 ? c[1].Float : 0f);
                pixels[i * 4 + 2] = FloatToByte(components > 2 ? c[2].Float : 0f);
                pixels[i * 4 + 3] = FloatToByte(components > 3 ? c[3].Float : 1f);
            }
            return pixels;
        }
        if (result is ScalarValue sv && sv.Type != ScalarType.Void)
        {
            var pixels = new byte[threadCount * 4];
            for (int i = 0; i < threadCount; i++)
            {
                byte gray = FloatToByte(sv.AsFloat(i));
                pixels[i * 4 + 0] = gray;
                pixels[i * 4 + 1] = gray;
                pixels[i * 4 + 2] = gray;
                pixels[i * 4 + 3] = 255;
            }
            return pixels;
        }
        return null;
    }
}
