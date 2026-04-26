using System.Globalization;
using HLSL;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public static class HLSLValueFormatter
{
    public static string Format(HLSLValue? value, int threadIndex)
    {
        if (value is ReferenceValue rv) value = rv.Get();
        if (value == null) return "null";

        return value switch
        {
            ScalarValue sv => FormatScalar(sv, threadIndex),
            VectorValue vv => FormatVector(vv, threadIndex),
            MatrixValue mv => FormatMatrix(mv, threadIndex),
            StructValue sv2 => FormatStruct(sv2, threadIndex),
            ArrayValue av => $"[{av.Values.Length}]",
            PredefinedObjectValue pov => pov.ToString(),
            _ => value.GetType().Name
        };
    }

    public static string FormatScalar(ScalarValue sv, int threadIndex)
    {
        try
        {
            int idx = Math.Min(threadIndex, sv.ThreadCount - 1);
            return FormatRawValue(sv.Value.Get(idx), sv.Type);
        }
        catch { return "?"; }
    }

    public static string FormatVector(VectorValue vv, int threadIndex)
    {
        try
        {
            int idx = Math.Min(threadIndex, vv.ThreadCount - 1);
            RawValue[] comps = vv.Values.Get(idx);
            if (comps == null) return "(null)";
            return $"({string.Join(", ", comps.Select(c => FormatRawValue(c, vv.Type)))})";
        }
        catch { return "?"; }
    }

    public static string FormatMatrix(MatrixValue mv, int threadIndex)
    {
        return $"[{mv.Rows}×{mv.Columns}]";
    }

    public static string FormatStruct(StructValue sv, int threadIndex)
    {
        try
        {
            var parts = sv.Members.Take(4).Select(kv => $"{kv.Key}: {Format(kv.Value, threadIndex)}");
            string body = string.Join(", ", parts);
            if (sv.Members.Count > 4) body += ", ...";
            return "{" + body + "}";
        }
        catch { return "{struct}"; }
    }

    public static string FormatRawValue(RawValue rv, ScalarType type)
    {
        if (HLSLTypeUtils.IsInt(type)) return rv.Int.ToString(CultureInfo.InvariantCulture);
        if (HLSLTypeUtils.IsUint(type)) return rv.Uint.ToString(CultureInfo.InvariantCulture);
        if (type == ScalarType.Double) return rv.Double.ToString("G6", CultureInfo.InvariantCulture);
        if (HLSLTypeUtils.IsFloat(type)) return rv.Float.ToString("G6", CultureInfo.InvariantCulture);
        if (type == ScalarType.Bool) return rv.Bool.ToString(CultureInfo.InvariantCulture);
        if (type == ScalarType.Char) return rv.Char.ToString(CultureInfo.InvariantCulture);
        return rv.Int.ToString(CultureInfo.InvariantCulture);
    }
}
