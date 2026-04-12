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
            // Universally applicable to all resource types. The Load() implementation
            // uses rv.Dimension/IsTexture/IsArray to infer which coordinates to extract.
            //   1 arg  — Load(intN location)
            //   2 args — Load(intN location, intN offset)       [offset ignored]
            //          — Load(intN location, out uint status)    [status written as 0]
            //          — MS: Load(int2 xy, int sampleIndex)      [stub]
            //   3 args — Load(intN location, intN offset, out uint status)
            //          — MS: Load(int2 xy, int sampleIndex, int2 offset) [stub]
            //   4 args — MS: Load(int2 xy, int sampleIndex, int2 offset, out uint status) [stub]
            AddN("Load", 1, 4, (state, rv, args) =>
            {
                if (IsMSTexture(rv) && args.Length >= 2 && args[1] is not ReferenceValue)
                    throw new NotImplementedException($"{rv.Type}.Load (multisample) is not yet implemented.");
                var result = Load(rv, (NumericValue)args[0]);
                if (args.Length > 1 && args[args.Length - 1] is ReferenceValue statusRef)
                    statusRef.Set((NumericValue)(uint)0);
                return result;
            });

            // ==================== Load2 / Load3 / Load4 ====================
            // ByteAddressBuffer / RWByteAddressBuffer: load 2/3/4 uints from a byte offset.
            StubN("Load2", 1, 2);
            StubN("Load3", 1, 2);
            StubN("Load4", 1, 2);

            // ==================== Store / Store2 / Store3 / Store4 ====================
            // RWByteAddressBuffer: store value(s) at a byte offset.
            Stub("Store",  2);
            Stub("Store2", 2);
            Stub("Store3", 2);
            Stub("Store4", 2);

            // ==================== Sample ====================
            // Sample(sampler, uv [, offset [, clamp [, out status]]])
            AddN("Sample", 2, 5, (state, rv, args) =>
                Sample(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1]));

            // ==================== SampleLevel ====================
            // SampleLevel(sampler, uv, lod [, offset [, out status]])
            AddN("SampleLevel", 3, 5, (state, rv, args) =>
                SampleLevel(rv, (SamplerStateValue)args[0], (NumericValue)args[1], (NumericValue)args[2]));

            // ==================== SampleGrad ====================
            // SampleGrad(sampler, uv, ddx, ddy [, offset [, clamp [, out status]]])
            AddN("SampleGrad", 4, 7, (state, rv, args) =>
                SampleGrad(rv, (SamplerStateValue)args[0], (NumericValue)args[1],
                           (NumericValue)args[2], (NumericValue)args[3]));

            // ==================== SampleBias ====================
            // SampleBias(sampler, uv, bias [, offset [, clamp [, out status]]])
            AddN("SampleBias", 3, 6, (state, rv, args) =>
                SampleBias(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1], (NumericValue)args[2]));

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

        public static HLSLValue Load(ResourceValue rv, NumericValue location)
        {
            // Number of int components in the location vector:
            // dimension + mip level (non-RW textures only) + array slice
            int coordCount = rv.Dimension + (rv.IsTexture ? 1 : 0) + (rv.IsArray ? 1 : 0);

            VectorValue vectorLoc = CastToVector(location.Cast(ScalarType.Int), coordCount);
            ScalarValue[] scalarLoc = vectorLoc.ToScalars();

            HLSLValue[] results = new HLSLValue[vectorLoc.ThreadCount];
            for (int threadIndex = 0; threadIndex < results.Length; threadIndex++)
            {
                if (coordCount == 1)
                {
                    results[threadIndex] = rv.Get(
                        Convert.ToInt32(scalarLoc[0].GetThreadValue(threadIndex)),
                        0, 0, 0, 0);
                }
                else if (coordCount == 2)
                {
                    results[threadIndex] = rv.Get(
                        Convert.ToInt32(scalarLoc[0].GetThreadValue(threadIndex)),
                        0, 0, 0,
                        Convert.ToInt32(scalarLoc[1].GetThreadValue(threadIndex)));
                }
                else if (coordCount == 3)
                {
                    results[threadIndex] = rv.Get(
                        Convert.ToInt32(scalarLoc[0].GetThreadValue(threadIndex)),
                        Convert.ToInt32(scalarLoc[1].GetThreadValue(threadIndex)),
                        0, 0,
                        Convert.ToInt32(scalarLoc[2].GetThreadValue(threadIndex)));
                }
                else if (coordCount == 4)
                {
                    results[threadIndex] = rv.Get(
                        Convert.ToInt32(scalarLoc[0].GetThreadValue(threadIndex)),
                        Convert.ToInt32(scalarLoc[1].GetThreadValue(threadIndex)),
                        Convert.ToInt32(scalarLoc[2].GetThreadValue(threadIndex)),
                        0,
                        Convert.ToInt32(scalarLoc[3].GetThreadValue(threadIndex)));
                }
            }

            return HLSLValueUtils.MergeThreadValues(results);
        }

        // TODO: Texture Arrays
        // TODO: 1D, 3D texture
        public static NumericValue SampleLevel(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue lod)
        {
            var scalarLod = CastToScalar(lod);
            var size = VectorValue.FromScalars(rv.SizeX, rv.SizeY, rv.SizeZ).BroadcastToVector(rv.Dimension) / (lod + 1);

            var texelPos = CastToVector(location, rv.Dimension) * size - 0.5f;

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

        public static NumericValue Sample(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location)
        {
            return SampleLevel(rv, sampler, location, CalculateLevelOfDetail(executionState, rv, sampler, location));
        }

        // TODO: 1D, 3D texture
        public static NumericValue SampleGrad(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue ddx, NumericValue ddy)
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

            return SampleLevel(rv, sampler, location, Clamp(Log2(rho), 0.0f, MathF.Log(MathF.Max(rv.SizeX, rv.SizeY)) / MathF.Log(2) + 1));
        }

        // TODO: 1D, 3D texture
        public static NumericValue SampleBias(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue bias)
        {
            var lod = CalculateLevelOfDetail(executionState, rv, sampler, location) + ToFloatLike(bias);
            return SampleLevel(rv, sampler, location, lod);
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
