using System;
using System.Collections.Generic;
using System.Linq;
using UnityShaderParser.HLSL;

namespace UnityShaderParser.Test
{
    public static partial class HLSLIntrinsics
    {
        #region Texture and Buffer methods

        private delegate HLSLValue ResourceMethodHandler(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args);

        private static readonly Dictionary<string, List<(int ArgCount, ResourceMethodHandler Handler)>> ResourceMethodTable
            = BuildResourceMethodTable();

        private static bool IsMSTexture(ResourceValue rv) =>
            rv.Type == PredefinedObjectType.Texture2DMS     ||
            rv.Type == PredefinedObjectType.Texture2DMSArray;

        private static ResourceMethodHandler NotImplementedMethod(string methodName) =>
            (state, rv, args) => throw new NotImplementedException($"{rv.Type}.{methodName} is not yet implemented.");

        private static Dictionary<string, List<(int ArgCount, ResourceMethodHandler Handler)>> BuildResourceMethodTable()
        {
            var table = new Dictionary<string, List<(int, ResourceMethodHandler)>>(StringComparer.Ordinal);

            void Add(string name, int argCount, ResourceMethodHandler handler)
            {
                if (!table.TryGetValue(name, out var list))
                    table[name] = list = new List<(int, ResourceMethodHandler)>();
                list.Add((argCount, handler));
            }

            // Register one handler for each arg count in [minArgs, maxArgs].
            void AddN(string name, int minArgs, int maxArgs, ResourceMethodHandler handler)
            {
                for (int n = minArgs; n <= maxArgs; n++)
                    Add(name, n, handler);
            }

            void StubN(string name, int minArgs, int maxArgs) =>
                AddN(name, minArgs, maxArgs, NotImplementedMethod(name));

            void Stub(string name, int argCount) =>
                Add(name, argCount, NotImplementedMethod(name));

            // ==================== Load ====================
            // Load(intN location [, intN offset [, out uint status]])
            //   Non-MS: location encodes (x [,y [,z]] [, arraySlice] [, mipLevel]).
            //           Mip is the last component for non-RW textures only.
            //           Optional offset is applied to spatial dimensions only.
            // MS Load: Load(intN xy [, arraySlice], int sampleIndex [, intN offset [, out status]])
            //          Sample index is ignored — we don't simulate per-sample MSAA storage.
            AddN("Load", 1, 4, (state, rv, args) =>
            {
                if (IsMSTexture(rv))
                {
                    // args[1] = sampleIndex (ignored), args[2] = offset (optional), last = status (optional)
                    NumericValue msOffset = args.Length >= 3 && args[2] is not ReferenceValue
                        ? (NumericValue)args[2] : null;
                    var msResult = LoadMS(rv, (NumericValue)args[0], msOffset);
                    if (args.Length > 1 && args[args.Length - 1] is ReferenceValue msStatus)
                        msStatus.Set((NumericValue)(uint)0);
                    return msResult;
                }

                // Standard Load: args[1] is offset (non-ref) or status (ref); args[2] is status (ref).
                NumericValue offset = args.Length >= 2 && args[1] is not ReferenceValue
                    ? (NumericValue)args[1] : null;
                var result = Load(rv, (NumericValue)args[0], offset);
                if (args.Length > 1 && args[args.Length - 1] is ReferenceValue statusRef)
                    statusRef.Set((NumericValue)(uint)0);
                return result;
            });

            // ==================== Load2 / Load3 / Load4 ====================
            // ByteAddressBuffer / RWByteAddressBuffer: load 2/3/4 consecutive uints from a byte offset.
            // Each element occupies 4 bytes (DWORD-aligned).
            AddN("Load2", 1, 2, (state, rv, args) =>
            {
                var r = LoadN(rv, (NumericValue)args[0], 2);
                if (args.Length > 1 && args[1] is ReferenceValue sr) sr.Set((NumericValue)(uint)0);
                return r;
            });
            AddN("Load3", 1, 2, (state, rv, args) =>
            {
                var r = LoadN(rv, (NumericValue)args[0], 3);
                if (args.Length > 1 && args[1] is ReferenceValue sr) sr.Set((NumericValue)(uint)0);
                return r;
            });
            AddN("Load4", 1, 2, (state, rv, args) =>
            {
                var r = LoadN(rv, (NumericValue)args[0], 4);
                if (args.Length > 1 && args[1] is ReferenceValue sr) sr.Set((NumericValue)(uint)0);
                return r;
            });

            // ==================== Store / Store2 / Store3 / Store4 ====================
            // RWByteAddressBuffer: store value(s) at a byte offset.
            Stub("Store",  2);
            Stub("Store2", 2);
            Stub("Store3", 2);
            Stub("Store4", 2);

            // ==================== Sample ====================
            // Sample(sampler, uv [, offset [, clamp [, out status]]])
            AddN("Sample", 2, 5, (state, rv, args) =>
            {
                var offset = args.Length >= 3 ? (NumericValue)args[2] : null;
                var clamp  = args.Length >= 4 ? (NumericValue)args[3] : null;
                return Sample(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1], offset, clamp);
            });

            // ==================== SampleLevel ====================
            // SampleLevel(sampler, uv, lod [, offset [, out status]])
            AddN("SampleLevel", 3, 5, (state, rv, args) =>
            {
                var offset = args.Length >= 4 ? (NumericValue)args[3] : null;
                return SampleLevel(rv, (SamplerStateValue)args[0], (NumericValue)args[1], (NumericValue)args[2], offset);
            });

            // ==================== SampleGrad ====================
            // SampleGrad(sampler, uv, ddx, ddy [, offset [, clamp [, out status]]])
            AddN("SampleGrad", 4, 7, (state, rv, args) =>
            {
                var offset = args.Length >= 5 ? (NumericValue)args[4] : null;
                var clamp  = args.Length >= 6 ? (NumericValue)args[5] : null;
                return SampleGrad(rv, (SamplerStateValue)args[0], (NumericValue)args[1],
                                  (NumericValue)args[2], (NumericValue)args[3], offset, clamp);
            });

            // ==================== SampleBias ====================
            // SampleBias(sampler, uv, bias [, offset [, clamp [, out status]]])
            AddN("SampleBias", 3, 6, (state, rv, args) =>
            {
                var offset = args.Length >= 4 ? (NumericValue)args[3] : null;
                var clamp  = args.Length >= 5 ? (NumericValue)args[4] : null;
                return SampleBias(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1], (NumericValue)args[2], offset, clamp);
            });

            // ==================== SampleCmp family ====================
            // SampleCmp(sampler_cmp, uv, cmpValue [, offset [, clamp [, out status]]])
            StubN("SampleCmp",          3, 6);
            // SampleCmpLevel(sampler_cmp, uv, cmpValue, lod [, offset [, out status]])
            StubN("SampleCmpLevel",     4, 6);
            // SampleCmpLevelZero(sampler_cmp, uv, cmpValue [, offset [, out status]])
            StubN("SampleCmpLevelZero", 3, 5);
            // SampleCmpBias(sampler_cmp, uv, cmpValue, bias [, offset [, clamp [, out status]]])
            StubN("SampleCmpBias",      4, 7);
            // SampleCmpGrad(sampler_cmp, uv, cmpValue, ddx, ddy [, offset [, clamp [, out status]]])
            StubN("SampleCmpGrad",      5, 8);

            // ==================== CalculateLevelOfDetail ====================
            // CalculateLevelOfDetail(sampler, uv)
            Add("CalculateLevelOfDetail", 2, (state, rv, args) =>
                CalculateLevelOfDetail(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1]));
            // CalculateLevelOfDetailUnclamped(sampler, uv)
            Add("CalculateLevelOfDetailUnclamped", 2, (state, rv, args) =>
                CalculateLevelOfDetailUnclamped(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1]));

            // ==================== Gather family ====================
            // Basic:      Gather(sampler, uv [, offset [, out status]])          — 2..4 args
            // 4-offsets:  Gather(sampler, uv, o0, o1, o2, o3 [, out status])    — 6..7 args
            // Comparison variants shift all counts up by 1 (adds compareValue).
            foreach (string gatherName in new[] { "Gather", "GatherRed", "GatherGreen", "GatherBlue", "GatherAlpha", "GatherRaw" })
                StubN(gatherName, 2, 7);
            foreach (string gatherName in new[] { "GatherCmp", "GatherCmpRed", "GatherCmpGreen", "GatherCmpBlue", "GatherCmpAlpha" })
                StubN(gatherName, 3, 8);

            // ==================== GetDimensions ====================
            // Arg count varies by resource type (1–5 args); resolved at runtime by rv type.
            AddN("GetDimensions", 1, 5, (state, rv, args) => GetDimensions(rv, args));

            // ==================== GetSamplePosition ====================
            // GetSamplePosition(int sampleIndex) — Texture2DMS / Texture2DMSArray only.
            Stub("GetSamplePosition", 1);

            // ==================== AppendStructuredBuffer / ConsumeStructuredBuffer ====================
            Stub("Append",  1);
            Stub("Consume", 0);

            // ==================== RWStructuredBuffer counter methods ====================
            Stub("IncrementCounter", 0);
            Stub("DecrementCounter", 0);

            // ==================== RWByteAddressBuffer Interlocked operations ====================
            // Basic:         InterlockedOp(byteOffset, value [, out original])
            foreach (string name in new[] {
                "InterlockedAdd", "InterlockedAdd64",
                "InterlockedMin", "InterlockedMin64",
                "InterlockedMax", "InterlockedMax64",
                "InterlockedAnd", "InterlockedAnd64",
                "InterlockedOr",  "InterlockedOr64",
                "InterlockedXor", "InterlockedXor64" })
            {
                StubN(name, 2, 3);
            }
            // Compare-store: InterlockedCompare*(byteOffset, compare, value)
            foreach (string name in new[] {
                "InterlockedCompareStore", "InterlockedCompareStore64", "InterlockedCompareStoreFloatBitwise" })
            {
                Stub(name, 3);
            }
            // Exchange:      Interlocked*Exchange*(byteOffset, value, out original)
            foreach (string name in new[] {
                "InterlockedExchange", "InterlockedExchange64", "InterlockedExchangeFloat" })
            {
                Stub(name, 3);
            }
            // Compare-exchange: InterlockedCompareExchange*(byteOffset, compare, value, out original)
            foreach (string name in new[] {
                "InterlockedCompareExchange", "InterlockedCompareExchange64", "InterlockedCompareExchangeFloatBitwise" })
            {
                Stub(name, 4);
            }

            return table;
        }

        // TODO: Argument checking
        public static bool TryInvokeResourceMethod(HLSLExecutionState executionState, ResourceValue rv, string name, HLSLValue[] args, out HLSLValue result)
        {
            if (ResourceMethodTable.TryGetValue(name, out var overloads))
            {
                foreach (var (argCount, handler) in overloads)
                {
                    if (argCount == args.Length)
                    {
                        result = handler(executionState, rv, args);
                        return true;
                    }
                }
            }
            result = null;
            return false;
        }

        // Public entry point for ordinary (non-MS) Load.
        // Non-RW textures carry a mip level as the last component of their location vector;
        // RW textures and buffers do not.
        public static HLSLValue Load(ResourceValue rv, NumericValue location, NumericValue offset = null)
            => LoadCore(rv, location, offset, hasMip: rv.IsTexture && !rv.IsWriteable);

        // Entry point for multisample Load. The caller strips out the sampleIndex arg;
        // MS textures never include a mip level in their location vector.
        public static HLSLValue LoadMS(ResourceValue rv, NumericValue location, NumericValue offset = null)
            => LoadCore(rv, location, offset, hasMip: false);

        // Load N consecutive uints starting at byteOffset (4 bytes apart).
        // Used by ByteAddressBuffer.Load2 / Load3 / Load4.
        private static NumericValue LoadN(ResourceValue rv, NumericValue byteOffset, int count)
        {
            var scalarOff = CastToScalar(byteOffset.Cast(ScalarType.Int));
            int threadCount = scalarOff.ThreadCount;
            var results = new HLSLValue[threadCount];

            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                int baseOff = Convert.ToInt32(scalarOff.GetThreadValue(threadIndex));
                var components = new ScalarValue[count];
                for (int i = 0; i < count; i++)
                {
                    var elem = (NumericValue)rv.Get(baseOff + i * 4, 0, 0, 0, 0);
                    components[i] = CastToScalar(elem.Cast(ScalarType.Uint));
                }
                results[threadIndex] = count == 1
                    ? (HLSLValue)components[0]
                    : VectorValue.FromScalars(components);
            }

            return (NumericValue)HLSLValueUtils.MergeThreadValues(results);
        }

        private static HLSLValue LoadCore(ResourceValue rv, NumericValue location, NumericValue offset, bool hasMip)
        {
            // Build the full coordinate vector.
            // Layout of the location vector:
            //   [x] [,y] [,z]          — spatial (rv.Dimension components)
            //   [,arraySlice]           — present when rv.IsArray
            //   [,mipLevel]             — present when hasMip (non-RW, non-MS textures)
            int coordCount = rv.Dimension + (rv.IsArray ? 1 : 0) + (hasMip ? 1 : 0);

            VectorValue vectorLoc = CastToVector(location.Cast(ScalarType.Int), coordCount);
            ScalarValue[] scalarLoc = vectorLoc.ToScalars();

            // Spatial offset (applies only to the first rv.Dimension components).
            VectorValue vectorOff = offset is not null
                ? CastToVector(offset.Cast(ScalarType.Int), rv.Dimension)
                : null;
            ScalarValue[] scalarOff = vectorOff?.ToScalars();

            int threadCount = vectorLoc.ThreadCount;
            HLSLValue[] results = new HLSLValue[threadCount];

            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                // Read spatial coordinate i (with optional offset).
                int S(int i)
                {
                    int v = Convert.ToInt32(scalarLoc[i].GetThreadValue(threadIndex));
                    if (scalarOff != null)
                        v += Convert.ToInt32(scalarOff[i].GetThreadValue(threadIndex));
                    return v;
                }
                // Read a raw (non-offset) coordinate.
                int R(int i) => Convert.ToInt32(scalarLoc[i].GetThreadValue(threadIndex));

                int x = rv.Dimension >= 1 ? S(0) : 0;
                int y = rv.Dimension >= 2 ? S(1) : 0;
                int z = rv.Dimension >= 3 ? S(2) : 0;
                int mip = 0;

                // Array slice follows the spatial dimensions; no offset applied.
                if (rv.IsArray)
                {
                    int slice = R(rv.Dimension);
                    // 1D arrays: slice goes into the y parameter of Get.
                    // 2D arrays: slice goes into the z parameter of Get.
                    if (rv.Dimension == 1) y = slice;
                    else                   z = slice;
                }

                // Mip level is the very last component (non-RW textures only).
                if (hasMip)
                    mip = R(rv.Dimension + (rv.IsArray ? 1 : 0));

                results[threadIndex] = rv.Get(x, y, z, 0, mip);
            }

            return HLSLValueUtils.MergeThreadValues(results);
        }

        // TODO: Texture Arrays
        // TODO: 1D, 3D texture
        public static NumericValue SampleLevel(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue lod, NumericValue offset = null)
        {
            var scalarLod = CastToScalar(lod);
            var size = VectorValue.FromScalars(rv.SizeX, rv.SizeY, rv.SizeZ).BroadcastToVector(rv.Dimension) / (lod + 1);

            var texelPos = CastToVector(location, rv.Dimension) * size - 0.5f;
            if (offset is not null)
                texelPos = (VectorValue)(texelPos + CastToVector(offset, rv.Dimension));

            var basePos = Floor(texelPos).Cast(ScalarType.Int);
            var frac = (VectorValue)Frac(texelPos);

            var p00 = (VectorValue)Clamp(basePos,                                 0, size - 1);
            var p10 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 0), 0, size - 1);
            var p01 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(0, 1), 0, size - 1);
            var p11 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 1), 0, size - 1);

            var c00 = (NumericValue)Load(rv, VectorValue.FromScalars(p00.x, p00.y, scalarLod));
            var c10 = (NumericValue)Load(rv, VectorValue.FromScalars(p10.x, p10.y, scalarLod));
            var c01 = (NumericValue)Load(rv, VectorValue.FromScalars(p01.x, p01.y, scalarLod));
            var c11 = (NumericValue)Load(rv, VectorValue.FromScalars(p11.x, p11.y, scalarLod));

            var cx0 = Lerp(c00, c10, frac.x);
            var cx1 = Lerp(c01, c11, frac.x);
            return Lerp(cx0, cx1, frac.y);
        }

        // TODO: This doesn't account for elliptical transform.
        // TODO: 1D, 3D texture
        public static NumericValue CalculateLevelOfDetail(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location)
        {
            var vecLoc = CastToVector(location, rv.Dimension);

            var du_dx = Ddx(executionState, vecLoc.x * rv.SizeX);
            var dv_dx = Ddx(executionState, vecLoc.y * rv.SizeY);
            var du_dy = Ddy(executionState, vecLoc.x * rv.SizeX);
            var dv_dy = Ddy(executionState, vecLoc.y * rv.SizeY);

            var lengthX = Sqrt(du_dx * du_dx + dv_dx * dv_dx);
            var lengthY = Sqrt(du_dy * du_dy + dv_dy * dv_dy);
            var rho = Max(lengthX, lengthY);

            return Clamp(Log2(rho), 0.0f, MathF.Log(MathF.Max(rv.SizeX, rv.SizeY)) / MathF.Log(2) + 1);
        }

        // Like CalculateLevelOfDetail but without clamping to [0, maxMip].
        // TODO: 1D, 3D texture
        public static NumericValue CalculateLevelOfDetailUnclamped(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location)
        {
            var vecLoc = CastToVector(location, rv.Dimension);

            var du_dx = Ddx(executionState, vecLoc.x * rv.SizeX);
            var dv_dx = Ddx(executionState, vecLoc.y * rv.SizeY);
            var du_dy = Ddy(executionState, vecLoc.x * rv.SizeX);
            var dv_dy = Ddy(executionState, vecLoc.y * rv.SizeY);

            var lengthX = Sqrt(du_dx * du_dx + dv_dx * dv_dx);
            var lengthY = Sqrt(du_dy * du_dy + dv_dy * dv_dy);
            var rho = Max(lengthX, lengthY);

            return Log2(rho);
        }

        public static NumericValue Sample(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue offset = null, NumericValue clamp = null)
        {
            var lod = CalculateLevelOfDetail(executionState, rv, sampler, location);
            if (clamp is not null)
                lod = Min(lod, ToFloatLike(clamp));
            return SampleLevel(rv, sampler, location, lod, offset);
        }

        // TODO: 1D, 3D texture
        public static NumericValue SampleGrad(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue ddx, NumericValue ddy, NumericValue offset = null, NumericValue clamp = null)
        {
            var vecDdx = CastToVector(ddx, rv.Dimension);
            var vecDdy = CastToVector(ddy, rv.Dimension);

            var du_dx = vecDdx.x * rv.SizeX;
            var dv_dx = vecDdx.y * rv.SizeY;
            var du_dy = vecDdy.x * rv.SizeX;
            var dv_dy = vecDdy.y * rv.SizeY;

            var lengthX = Sqrt(du_dx * du_dx + dv_dx * dv_dx);
            var lengthY = Sqrt(du_dy * du_dy + dv_dy * dv_dy);
            var rho = Max(lengthX, lengthY);

            var lod = Clamp(Log2(rho), 0.0f, MathF.Log(MathF.Max(rv.SizeX, rv.SizeY)) / MathF.Log(2) + 1);
            if (clamp is not null)
                lod = Min(lod, ToFloatLike(clamp));
            return SampleLevel(rv, sampler, location, lod, offset);
        }

        // TODO: 1D, 3D texture
        public static NumericValue SampleBias(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue bias, NumericValue offset = null, NumericValue clamp = null)
        {
            var lod = CalculateLevelOfDetail(executionState, rv, sampler, location) + ToFloatLike(bias);
            if (clamp is not null)
                lod = Min(lod, ToFloatLike(clamp));
            return SampleLevel(rv, sampler, location, lod, offset);
        }

        private static HLSLValue GetDimensions(ResourceValue rv, HLSLValue[] args)
        {
            int idx = 0;

            // The first argument is a mip-level input if it is not a ReferenceValue (out param).
            // Only non-RW textures support mip queries in GetDimensions.
            int mipLevel = 0;
            bool hasMipInput = args.Length > 0 && args[0] is not ReferenceValue
                && rv.IsTexture && !rv.IsWriteable;
            if (hasMipInput)
            {
                mipLevel = Convert.ToInt32(CastToScalar((NumericValue)args[0]).GetThreadValue(0));
                idx = 1;
            }

            float scale = MathF.Pow(2, mipLevel);
            uint w = (uint)Math.Max(1, (int)(rv.SizeX / scale));
            uint h = (uint)Math.Max(1, (int)(rv.SizeY / scale));
            uint d = (uint)Math.Max(1, (int)(rv.SizeZ / scale));

            void Write(uint value)
            {
                if (idx < args.Length && args[idx] is ReferenceValue r)
                    r.Set((NumericValue)value);
                idx++;
            }

            // Width — all resource types have a width.
            Write(w);

            // Height — 2D+ non-buffer resources.
            if (!rv.IsBuffer && rv.Dimension >= 2)
                Write(h);

            // Depth or array element count — 3D textures and array textures.
            if (rv.Dimension >= 3 || rv.IsArray)
                Write(d);

            // Sample count — MS textures (we report 1 since we don't track per-sample data).
            if (IsMSTexture(rv))
                Write(1);

            // Element stride — StructuredBuffer types (we don't track the element size).
            if (rv.Type == PredefinedObjectType.StructuredBuffer           ||
                rv.Type == PredefinedObjectType.RWStructuredBuffer          ||
                rv.Type == PredefinedObjectType.AppendStructuredBuffer      ||
                rv.Type == PredefinedObjectType.ConsumeStructuredBuffer     ||
                rv.Type == PredefinedObjectType.RasterizerOrderedStructuredBuffer)
                Write(0);

            // Mip level count — only when a mip input was given.
            if (hasMipInput)
                Write(1); // our implementation only supports 1 mip level.

            return ScalarValue.Null;
        }

        #endregion
    }
}
