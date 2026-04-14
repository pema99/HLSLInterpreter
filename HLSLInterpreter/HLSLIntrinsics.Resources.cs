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

        private static Dictionary<string, List<(int ArgCount, ResourceMethodHandler Handler)>> BuildResourceMethodTable()
        {
            var table = new Dictionary<string, List<(int, ResourceMethodHandler)>>(StringComparer.Ordinal);

            void Add(string name, int argCount, ResourceMethodHandler handler)
            {
                if (!table.TryGetValue(name, out var list))
                    table[name] = list = new List<(int, ResourceMethodHandler)>();
                list.Add((argCount, handler));
            }

            void AddN(string name, int minArgs, int maxArgs, ResourceMethodHandler handler)
            {
                for (int n = minArgs; n <= maxArgs; n++)
                    Add(name, n, handler);
            }

            // === Load ===
            AddN("Load", 1, 4, (state, rv, args) =>
            {
                // ByteAddressBuffer.Load(uint ByteOffset) returns a raw uint, not a texel.
                if (rv.Type == PredefinedObjectType.ByteAddressBuffer ||
                    rv.Type == PredefinedObjectType.RWByteAddressBuffer ||
                    rv.Type == PredefinedObjectType.RasterizerOrderedByteAddressBuffer)
                    return LoadN(rv, (NumericValue)args[0], 1);

                if (IsMSTexture(rv))
                {
                    NumericValue msOffset = args.Length >= 3 && args[2] is not ReferenceValue
                        ? (NumericValue)args[2] : null;
                    var msResult = LoadMS(rv, (NumericValue)args[0], msOffset);
                    if (args.Length > 1 && args[args.Length - 1] is ReferenceValue msStatus)
                        msStatus.Set((NumericValue)(uint)0);
                    return msResult;
                }

                NumericValue offset = args.Length >= 2 && args[1] is not ReferenceValue
                    ? (NumericValue)args[1] : null;
                var result = Load(rv, (NumericValue)args[0], offset);
                if (args.Length > 1 && args[args.Length - 1] is ReferenceValue statusRef)
                    statusRef.Set((NumericValue)(uint)0);
                return result;
            });

            // === Sample family ===
            AddN("Sample", 2, 5, (state, rv, args) =>
            {
                var offset = args.Length >= 3 ? (NumericValue)args[2] : null;
                var clamp = args.Length >= 4 ? (NumericValue)args[3] : null;
                return Sample(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1], offset, clamp);
            });
            AddN("SampleLevel", 3, 5, (state, rv, args) =>
            {
                var offset = args.Length >= 4 ? (NumericValue)args[3] : null;
                return SampleLevel(rv, (SamplerStateValue)args[0], (NumericValue)args[1], (NumericValue)args[2], offset);
            });
            AddN("SampleGrad", 4, 7, (state, rv, args) =>
            {
                var offset = args.Length >= 5 ? (NumericValue)args[4] : null;
                var clamp = args.Length >= 6 ? (NumericValue)args[5] : null;
                return SampleGrad(rv, (SamplerStateValue)args[0], (NumericValue)args[1],
                                  (NumericValue)args[2], (NumericValue)args[3], offset, clamp);
            });
            AddN("SampleBias", 3, 6, (state, rv, args) =>
            {
                var offset = args.Length >= 4 ? (NumericValue)args[3] : null;
                var clamp = args.Length >= 5 ? (NumericValue)args[4] : null;
                return SampleBias(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1], (NumericValue)args[2], offset, clamp);
            });

            // === SampleCmp family ===
            AddN("SampleCmp", 3, 6, (state, rv, args) =>
            {
                var sampler = (SamplerStateValue)args[0];
                var cmpVal = (NumericValue)args[2];
                var offset = args.Length >= 4 ? (NumericValue)args[3] : null;
                var clamp = args.Length >= 5 ? (NumericValue)args[4] : null;
                return Sample(state, rv, sampler, (NumericValue)args[1], offset, clamp, v => ApplyComparison(sampler, v, cmpVal));
            });
            AddN("SampleCmpLevel", 4, 6, (state, rv, args) =>
            {
                var sampler = (SamplerStateValue)args[0];
                var cmpVal = (NumericValue)args[2];
                var offset = args.Length >= 5 ? (NumericValue)args[4] : null;
                return SampleLevel(rv, sampler, (NumericValue)args[1], (NumericValue)args[3], offset, v => ApplyComparison(sampler, v, cmpVal));
            });
            AddN("SampleCmpLevelZero", 3, 5, (state, rv, args) =>
            {
                var sampler = (SamplerStateValue)args[0];
                var cmpVal = (NumericValue)args[2];
                var offset = args.Length >= 4 ? (NumericValue)args[3] : null;
                return SampleLevel(rv, sampler, (NumericValue)args[1], (ScalarValue)0.0f, offset, v => ApplyComparison(sampler, v, cmpVal));
            });
            AddN("SampleCmpBias", 4, 7, (state, rv, args) =>
            {
                var sampler = (SamplerStateValue)args[0];
                var cmpVal = (NumericValue)args[2];
                var offset = args.Length >= 5 ? (NumericValue)args[4] : null;
                var clamp = args.Length >= 6 ? (NumericValue)args[5] : null;
                return SampleBias(state, rv, sampler, (NumericValue)args[1], (NumericValue)args[3], offset, clamp, v => ApplyComparison(sampler, v, cmpVal));
            });
            AddN("SampleCmpGrad", 5, 8, (state, rv, args) =>
            {
                var sampler = (SamplerStateValue)args[0];
                var cmpVal = (NumericValue)args[2];
                var offset = args.Length >= 6 ? (NumericValue)args[5] : null;
                var clamp = args.Length >= 7 ? (NumericValue)args[6] : null;
                return SampleGrad(rv, sampler, (NumericValue)args[1], (NumericValue)args[3], (NumericValue)args[4], offset, clamp, v => ApplyComparison(sampler, v, cmpVal));
            });

            // === LOD calculation ===
            Add("CalculateLevelOfDetail", 2, (state, rv, args) => CalculateLevelOfDetail(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1]));
            Add("CalculateLevelOfDetailUnclamped", 2, (state, rv, args) => CalculateLevelOfDetailUnclamped(state, rv, (SamplerStateValue)args[0], (NumericValue)args[1]));

            // === Gather family ===
            foreach ((string gName, int gCh, bool has4Offset) in new (string, int, bool)[]
            {
                ("Gather",      0, false),
                ("GatherRed",   0, true),
                ("GatherGreen", 1, true),
                ("GatherBlue",  2, true),
                ("GatherAlpha", 3, true),
            })
            {
                int ch = gCh;
                AddN(gName, 2, 4, (state, rv, args) =>
                {
                    var offset = args.Length >= 3 && args[2] is not ReferenceValue ? (NumericValue)args[2] : null;
                    return GatherCore(rv, (NumericValue)args[1], ch, uniformOffset: offset, sampler: (SamplerStateValue)args[0]);
                });
                if (has4Offset)
                {
                    AddN(gName, 6, 7, (state, rv, args) =>
                        GatherCore(rv, (NumericValue)args[1], ch,
                            cornerOffsets: new[] { (NumericValue)args[2], (NumericValue)args[3],
                                                   (NumericValue)args[4], (NumericValue)args[5] },
                            sampler: (SamplerStateValue)args[0]));
                }
            }

            // === GatherCmp family ===
            foreach ((string gName, int gCh, bool has4Offset) in new (string, int, bool)[]
            {
                ("GatherCmp",      0, false),
                ("GatherCmpRed",   0, true),
                ("GatherCmpGreen", 1, true),
                ("GatherCmpBlue",  2, true),
                ("GatherCmpAlpha", 3, true),
            })
            {
                int ch = gCh;
                AddN(gName, 3, 5, (state, rv, args) =>
                {
                    var offset = args.Length >= 4 && args[3] is not ReferenceValue ? (NumericValue)args[3] : null;
                    return GatherCore(rv, (NumericValue)args[1], ch, uniformOffset: offset,
                        sampler: (SamplerStateValue)args[0], comparisonValue: (NumericValue)args[2]);
                });
                if (has4Offset)
                {
                    AddN(gName, 7, 8, (state, rv, args) =>
                        GatherCore(rv, (NumericValue)args[1], ch,
                            cornerOffsets: new[] { (NumericValue)args[3], (NumericValue)args[4],
                                                   (NumericValue)args[5], (NumericValue)args[6] },
                            sampler: (SamplerStateValue)args[0], comparisonValue: (NumericValue)args[2]));
                }
            }

            // === GetDimensions ===
            AddN("GetDimensions", 1, 5, (state, rv, args) => GetDimensions(state, rv, args));

            // === AppendStructuredBuffer / ConsumeStructuredBuffer functions ===
            Add("Append", 1, (state, rv, args) =>
            {
                rv.Set(rv.Counter++, 0, 0, 0, 0, args[0]);
                return ScalarValue.Null;
            });
            Add("Consume", 0, (state, rv, args) =>
            {
                int index = rv.Counter--;
                return rv.Get(index, 0, 0, 0, 0);
            });

            // === ByteAddressBuffer functions ===
            foreach (int n in new[] { 2, 3, 4 })
            {
                int count = n;
                AddN($"Load{count}", 1, 2, (state, rv, args) =>
                {
                    var r = LoadN(rv, (NumericValue)args[0], count);
                    if (args.Length > 1 && args[1] is ReferenceValue sr) sr.Set((NumericValue)(uint)0);
                    return r;
                });
            }
            foreach (int n in new[] { 1, 2, 3, 4 })
            {
                int count = n;
                string suffix = count == 1 ? "" : count.ToString();
                Add($"Store{suffix}", 2, (state, rv, args) => { StoreN(state, rv, (NumericValue)args[0], (NumericValue)args[1], count); return ScalarValue.Null; });
            }

            // === RWByteAddressBuffer atomics ===
            AddN("InterlockedAdd", 2, 3, (state, rv, args) => { InterlockedRMW32(state, rv, args, (a, b) => a + b); return ScalarValue.Null; });
            AddN("InterlockedMin", 2, 3, (state, rv, args) => { InterlockedRMW32(state, rv, args, (a, b) => (int)a < (int)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedMax", 2, 3, (state, rv, args) => { InterlockedRMW32(state, rv, args, (a, b) => (int)a > (int)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedAnd", 2, 3, (state, rv, args) => { InterlockedRMW32(state, rv, args, (a, b) => a & b); return ScalarValue.Null; });
            AddN("InterlockedOr", 2, 3, (state, rv, args) => { InterlockedRMW32(state, rv, args, (a, b) => a | b); return ScalarValue.Null; });
            AddN("InterlockedXor", 2, 3, (state, rv, args) => { InterlockedRMW32(state, rv, args, (a, b) => a ^ b); return ScalarValue.Null; });
            AddN("InterlockedAdd64", 2, 3, (state, rv, args) => { InterlockedRMW64(state, rv, args, (a, b) => a + b); return ScalarValue.Null; });
            AddN("InterlockedMin64", 2, 3, (state, rv, args) => { InterlockedRMW64(state, rv, args, (a, b) => (long)a < (long)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedMax64", 2, 3, (state, rv, args) => { InterlockedRMW64(state, rv, args, (a, b) => (long)a > (long)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedAnd64", 2, 3, (state, rv, args) => { InterlockedRMW64(state, rv, args, (a, b) => a & b); return ScalarValue.Null; });
            AddN("InterlockedOr64", 2, 3, (state, rv, args) => { InterlockedRMW64(state, rv, args, (a, b) => a | b); return ScalarValue.Null; });
            AddN("InterlockedXor64", 2, 3, (state, rv, args) => { InterlockedRMW64(state, rv, args, (a, b) => a ^ b); return ScalarValue.Null; });
            Add("InterlockedExchange", 3, (state, rv, args) => { InterlockedRMW32(state, rv, args, (_, b) => b); return ScalarValue.Null; });
            Add("InterlockedExchange64", 3, (state, rv, args) => { InterlockedRMW64(state, rv, args, (_, b) => b); return ScalarValue.Null; });
            Add("InterlockedExchangeFloat", 3, (state, rv, args) => { InterlockedExchangeFloat(state, rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareStore", 3, (state, rv, args) => { InterlockedCmpStore32(state, rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareStore64", 3, (state, rv, args) => { InterlockedCmpStore64(state, rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareStoreFloatBitwise", 3, (state, rv, args) => { InterlockedCmpStoreFloat(state, rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareExchange", 4, (state, rv, args) => { InterlockedCmpStore32(state, rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareExchange64", 4, (state, rv, args) => { InterlockedCmpStore64(state, rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareExchangeFloatBitwise", 4, (state, rv, args) => { InterlockedCmpStoreFloat(state, rv, args); return ScalarValue.Null; });

            // === Misc stuff ===
            Add("IncrementCounter", 0, (state, rv, args) => (NumericValue)(uint)rv.Counter++);
            Add("DecrementCounter", 0, (state, rv, args) => (NumericValue)(uint)--rv.Counter);
            Add("GetSamplePosition", 1, (state, rv, args) => VectorValue.FromScalars((ScalarValue)0.5f, (ScalarValue)0.5f));

            return table;
        }

        public static bool IsResourceMethodInoutParameter(ResourceValue rv, string methodName, int argCount, int paramIndex)
        {
            if (methodName == "GetDimensions")
            {
                // For read-only (non-RW, non-MS) textures the mip+levels overload has arg 0
                // as an INPUT mip level; every other arg is an OUT param.
                // For RW textures, buffers, and MS textures every arg is OUT.
                if (rv.IsTexture && !rv.IsWriteable && !IsMSTexture(rv))
                {
                    // For cube textures GetDimensions outputs width + height (2 values), not Dimension (3).
                    int outOnlyCount = (rv.IsCube ? 2 : rv.Dimension) + (rv.IsArray ? 1 : 0);
                    if (argCount > outOnlyCount)
                        return paramIndex > 0;   // arg 0 is the mip input; the rest are OUT
                }
                return true;  // RW / buffer / MS or out-only overload, all args are OUT
            }

            if (paramIndex == argCount - 1)
            {
                switch (methodName)
                {
                    case "Load":
                        return argCount >= 2;
                    case "Load2":
                    case "Load3":
                    case "Load4":
                        return argCount == 2;
                    case "Sample":
                    case "SampleLevel":
                    case "SampleCmpLevelZero":
                        return argCount == 5;
                    case "SampleBias":
                    case "SampleCmp":
                    case "SampleCmpLevel":
                        return argCount == 6;
                    case "SampleGrad":
                    case "SampleCmpBias":
                        return argCount == 7;
                    case "SampleCmpGrad":
                        return argCount == 8;
                    case "Gather":
                    case "GatherRed":
                    case "GatherGreen":
                    case "GatherBlue":
                    case "GatherAlpha":
                        return argCount == 4 || argCount == 7 || (argCount == 3 && rv.IsCube);
                    case "GatherCmp":
                    case "GatherCmpRed":
                    case "GatherCmpGreen":
                    case "GatherCmpBlue":
                    case "GatherCmpAlpha":
                        return argCount == 5 || argCount == 8 || (argCount == 4 && rv.IsCube);
                }
            }

            switch (methodName)
            {
                case "InterlockedAdd":
                case "InterlockedMin":
                case "InterlockedMax":
                case "InterlockedAnd":
                case "InterlockedOr":
                case "InterlockedXor":
                case "InterlockedAdd64":
                case "InterlockedMin64":
                case "InterlockedMax64":
                case "InterlockedAnd64":
                case "InterlockedOr64":
                case "InterlockedXor64":
                case "InterlockedExchange":
                case "InterlockedExchange64":
                case "InterlockedExchangeFloat":
                    return paramIndex == 2;
                case "InterlockedCompareExchange":
                case "InterlockedCompareExchange64":
                case "InterlockedCompareExchangeFloatBitwise":
                    return paramIndex == 3;
            }

            return false;
        }

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

        public static HLSLValue ResourceSubscriptRead(ResourceValue rv, NumericValue coord)
        {
            int dim = rv.Dimension;
            int coordCount = dim + (rv.IsArray ? 1 : 0);
            var vecCoord = CastToVector(coord.Cast(ScalarType.Int), coordCount);
            var scalarCoord = vecCoord.ToScalars();
            int threadCount = vecCoord.ThreadCount;
            var results = new HLSLValue[threadCount];
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                int x = dim >= 1 ? Convert.ToInt32(scalarCoord[0].GetThreadValue(threadIndex)) : 0;
                int y = dim >= 2 ? Convert.ToInt32(scalarCoord[1].GetThreadValue(threadIndex)) : 0;
                int z = dim >= 3 ? Convert.ToInt32(scalarCoord[2].GetThreadValue(threadIndex)) : 0;
                if (rv.IsArray)
                {
                    int slice = Convert.ToInt32(scalarCoord[dim].GetThreadValue(threadIndex));
                    if (dim == 1) y = slice; else z = slice;
                }
                results[threadIndex] = HLSLValueUtils.Scalarize(rv.Get(x, y, z, 0, 0), threadIndex);
            }
            return HLSLValueUtils.MergeThreadValues(results);
        }

        public static void ResourceSubscriptWrite(HLSLExecutionState executionState, ResourceValue rv, NumericValue coord, NumericValue value)
        {
            int dim = rv.Dimension;
            int coordCount = dim + (rv.IsArray ? 1 : 0);
            var vecCoord = CastToVector(coord.Cast(ScalarType.Int), coordCount);
            var scalarCoord = vecCoord.ToScalars();
            int threadCount = Math.Max(vecCoord.ThreadCount, value.ThreadCount);
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex)) continue;
                int x = dim >= 1 ? Convert.ToInt32(scalarCoord[0].GetThreadValue(threadIndex)) : 0;
                int y = dim >= 2 ? Convert.ToInt32(scalarCoord[1].GetThreadValue(threadIndex)) : 0;
                int z = dim >= 3 ? Convert.ToInt32(scalarCoord[2].GetThreadValue(threadIndex)) : 0;
                if (rv.IsArray)
                {
                    int slice = Convert.ToInt32(scalarCoord[dim].GetThreadValue(threadIndex));
                    if (dim == 1) y = slice; else z = slice;
                }
                rv.Set(x, y, z, 0, 0, HLSLValueUtils.Scalarize(value, threadIndex));
            }
        }

        public static HLSLValue Load(ResourceValue rv, NumericValue location, NumericValue offset = null)
            => LoadCore(rv, location, offset, hasMip: rv.IsTexture && !rv.IsWriteable);

        public static HLSLValue LoadMS(ResourceValue rv, NumericValue location, NumericValue offset = null)
            => LoadCore(rv, location, offset, hasMip: false);

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
                    ? components[0]
                    : VectorValue.FromScalars(components);
            }

            return (NumericValue)HLSLValueUtils.MergeThreadValues(results);
        }

        private static void StoreN(HLSLExecutionState state, ResourceValue rv, NumericValue byteOffset, NumericValue value, int count)
        {
            var scalarOff = CastToScalar(byteOffset.Cast(ScalarType.Int));
            var vec = CastToVector(value.Cast(ScalarType.Uint), count);
            int threadCount = scalarOff.ThreadCount;
            for (int thread = 0; thread < threadCount; thread++)
            {
                if (!state.IsThreadActive(thread)) continue;
                int baseOff = Convert.ToInt32(scalarOff.GetThreadValue(thread));
                for (int i = 0; i < count; i++)
                {
                    var raw = vec[i].GetThreadValue(thread);
                    rv.Set(baseOff + i * 4, 0, 0, 0, 0, new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR(raw)));
                }
            }
        }

        private static uint ReadUint32(ResourceValue rv, int byteOffset)
        {
            var v = (NumericValue)rv.Get(byteOffset, 0, 0, 0, 0);
            return Convert.ToUInt32(CastToScalar(v.Cast(ScalarType.Uint)).GetThreadValue(0));
        }

        private static void WriteUint32(ResourceValue rv, int byteOffset, uint value)
        {
            rv.Set(byteOffset, 0, 0, 0, 0, new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR(value)));
        }

        private static ulong ReadUint64(ResourceValue rv, int byteOffset)
        {
            ulong lo = ReadUint32(rv, byteOffset);
            ulong hi = ReadUint32(rv, byteOffset + 4);
            return lo | (hi << 32);
        }

        private static void WriteUint64(ResourceValue rv, int byteOffset, ulong value)
        {
            WriteUint32(rv, byteOffset, (uint)(value & 0xFFFFFFFF));
            WriteUint32(rv, byteOffset + 4, (uint)(value >> 32));
        }

        private static void InterlockedRMW32(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args, Func<uint, uint, uint> op)
        {
            var scalarOff = CastToScalar(((NumericValue)args[0]).Cast(ScalarType.Int));
            var scalarVal = CastToScalar(((NumericValue)args[1]).Cast(ScalarType.Uint));
            int threadCount = scalarOff.ThreadCount;
            bool hasOut = args.Length > 2 && args[2] is ReferenceValue;
            var originals = hasOut ? new HLSLValue[threadCount] : null;
            for (int thread = 0; thread < threadCount; thread++)
            {
                int off = Convert.ToInt32(scalarOff.GetThreadValue(thread));
                uint old = ReadUint32(rv, off);
                uint val = Convert.ToUInt32(scalarVal.GetThreadValue(thread));
                if (originals is not null) originals[thread] = new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR(old));
                if (state.IsThreadActive(thread))
                    WriteUint32(rv, off, op(old, val));
            }
            if (originals is not null)
                ((ReferenceValue)args[2]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedRMW64(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args, Func<ulong, ulong, ulong> op)
        {
            var scalarOff = CastToScalar(((NumericValue)args[0]).Cast(ScalarType.Int));
            var valVec = CastToVector(((NumericValue)args[1]).Cast(ScalarType.Uint), 2);
            int threadCount = scalarOff.ThreadCount;
            bool hasOut = args.Length > 2 && args[2] is ReferenceValue;
            var originals = hasOut ? new HLSLValue[threadCount] : null;
            for (int thread = 0; thread < threadCount; thread++)
            {
                int off = Convert.ToInt32(scalarOff.GetThreadValue(thread));
                ulong old = ReadUint64(rv, off);
                ulong val = Convert.ToUInt32(valVec[0].GetThreadValue(thread)) |
                             ((ulong)Convert.ToUInt32(valVec[1].GetThreadValue(thread)) << 32);
                if (originals is not null)
                    originals[thread] = VectorValue.FromScalars(
                        new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR((uint)(old & 0xFFFFFFFF))),
                        new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR((uint)(old >> 32))));
                if (state.IsThreadActive(thread))
                    WriteUint64(rv, off, op(old, val));
            }
            if (originals is not null)
                ((ReferenceValue)args[2]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedExchangeFloat(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args)
        {
            var scalarOff = CastToScalar(((NumericValue)args[0]).Cast(ScalarType.Int));
            var scalarVal = CastToScalar(((NumericValue)args[1]).Cast(ScalarType.Float));
            int threadCount = scalarOff.ThreadCount;
            var originals = new HLSLValue[threadCount];
            for (int thread = 0; thread < threadCount; thread++)
            {
                int off = Convert.ToInt32(scalarOff.GetThreadValue(thread));
                float old = BitConverter.UInt32BitsToSingle(ReadUint32(rv, off));
                float val = Convert.ToSingle(scalarVal.GetThreadValue(thread));
                originals[thread] = new ScalarValue(ScalarType.Float, HLSLValueUtils.MakeScalarSGPR(old));
                if (state.IsThreadActive(thread))
                    WriteUint32(rv, off, BitConverter.SingleToUInt32Bits(val));
            }
            if (args[2] is ReferenceValue outRef)
                outRef.Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedCmpStore32(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args)
        {
            var scalarOff = CastToScalar(((NumericValue)args[0]).Cast(ScalarType.Int));
            var scalarCmp = CastToScalar(((NumericValue)args[1]).Cast(ScalarType.Uint));
            var scalarVal = CastToScalar(((NumericValue)args[2]).Cast(ScalarType.Uint));
            int threadCount = scalarOff.ThreadCount;
            bool hasOut = args.Length > 3 && args[3] is ReferenceValue;
            var originals = hasOut ? new HLSLValue[threadCount] : null;
            for (int thread = 0; thread < threadCount; thread++)
            {
                int off = Convert.ToInt32(scalarOff.GetThreadValue(thread));
                uint old = ReadUint32(rv, off);
                uint cmp = Convert.ToUInt32(scalarCmp.GetThreadValue(thread));
                uint val = Convert.ToUInt32(scalarVal.GetThreadValue(thread));
                if (originals is not null) originals[thread] = new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR(old));
                if (state.IsThreadActive(thread) && old == cmp) WriteUint32(rv, off, val);
            }
            if (originals is not null)
                ((ReferenceValue)args[3]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedCmpStore64(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args)
        {
            var scalarOff = CastToScalar(((NumericValue)args[0]).Cast(ScalarType.Int));
            var cmpVec = CastToVector(((NumericValue)args[1]).Cast(ScalarType.Uint), 2);
            var valVec = CastToVector(((NumericValue)args[2]).Cast(ScalarType.Uint), 2);
            int threadCount = scalarOff.ThreadCount;
            bool hasOut = args.Length > 3 && args[3] is ReferenceValue;
            var originals = hasOut ? new HLSLValue[threadCount] : null;
            for (int thread = 0; thread < threadCount; thread++)
            {
                int off = Convert.ToInt32(scalarOff.GetThreadValue(thread));
                ulong old = ReadUint64(rv, off);
                ulong cmp = Convert.ToUInt32(cmpVec[0].GetThreadValue(thread)) | ((ulong)Convert.ToUInt32(cmpVec[1].GetThreadValue(thread)) << 32);
                ulong val = Convert.ToUInt32(valVec[0].GetThreadValue(thread)) | ((ulong)Convert.ToUInt32(valVec[1].GetThreadValue(thread)) << 32);
                if (originals is not null)
                    originals[thread] = VectorValue.FromScalars(
                        new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR((uint)(old & 0xFFFFFFFF))),
                        new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR((uint)(old >> 32))));
                if (state.IsThreadActive(thread) && old == cmp) WriteUint64(rv, off, val);
            }
            if (originals is not null)
                ((ReferenceValue)args[3]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedCmpStoreFloat(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args)
        {
            var scalarOff = CastToScalar(((NumericValue)args[0]).Cast(ScalarType.Int));
            var scalarCmp = CastToScalar(((NumericValue)args[1]).Cast(ScalarType.Float));
            var scalarVal = CastToScalar(((NumericValue)args[2]).Cast(ScalarType.Float));
            int threadCount = scalarOff.ThreadCount;
            bool hasOut = args.Length > 3 && args[3] is ReferenceValue;
            var originals = hasOut ? new HLSLValue[threadCount] : null;
            for (int thread = 0; thread < threadCount; thread++)
            {
                int off = Convert.ToInt32(scalarOff.GetThreadValue(thread));
                uint oldBits = ReadUint32(rv, off);
                uint cmpBits = BitConverter.SingleToUInt32Bits(Convert.ToSingle(scalarCmp.GetThreadValue(thread)));
                uint valBits = BitConverter.SingleToUInt32Bits(Convert.ToSingle(scalarVal.GetThreadValue(thread)));
                if (originals is not null)
                    originals[thread] = new ScalarValue(ScalarType.Float, HLSLValueUtils.MakeScalarSGPR(BitConverter.UInt32BitsToSingle(oldBits)));
                if (state.IsThreadActive(thread) && oldBits == cmpBits) WriteUint32(rv, off, valBits);
            }
            if (originals is not null)
                ((ReferenceValue)args[3]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static HLSLValue LoadCore(ResourceValue rv, NumericValue location, NumericValue offset, bool hasMip)
        {
            // Layout of the location vector:
            //   [x] [,y] [,z] - spatial (rv.Dimension components)
            //   [,arraySlice] - present when rv.IsArray
            //   [,mipLevel]   - present when hasMip (non-RW, non-MS textures)
            int coordCount = rv.Dimension + (rv.IsArray ? 1 : 0) + (hasMip ? 1 : 0);

            VectorValue vectorLoc = CastToVector(location.Cast(ScalarType.Int), coordCount);
            ScalarValue[] scalarLoc = vectorLoc.ToScalars();

            VectorValue vectorOff = offset is not null
                ? CastToVector(offset.Cast(ScalarType.Int), rv.Dimension)
                : null;
            ScalarValue[] scalarOff = vectorOff?.ToScalars();

            int threadCount = vectorLoc.ThreadCount;
            HLSLValue[] results = new HLSLValue[threadCount];

            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                int CoordWithOffset(int i)
                {
                    int v = Convert.ToInt32(scalarLoc[i].GetThreadValue(threadIndex));
                    if (scalarOff != null)
                        v += Convert.ToInt32(scalarOff[i].GetThreadValue(threadIndex));
                    return v;
                }
                int RawCoord(int i) => Convert.ToInt32(scalarLoc[i].GetThreadValue(threadIndex));

                int x = rv.Dimension >= 1 ? CoordWithOffset(0) : 0;
                int y = rv.Dimension >= 2 ? CoordWithOffset(1) : 0;
                int z = rv.Dimension >= 3 ? CoordWithOffset(2) : 0;
                int mip = 0;

                if (rv.IsArray)
                {
                    int slice = RawCoord(rv.Dimension);
                    if (rv.Dimension == 1) y = slice;
                    else z = slice;
                }

                if (hasMip)
                    mip = RawCoord(rv.Dimension + (rv.IsArray ? 1 : 0));

                results[threadIndex] = rv.Get(x, y, z, 0, mip);
            }

            return HLSLValueUtils.MergeThreadValues(results);
        }

        private static bool IsLinearFilter(SamplerStateValue.FilterMode filter) => filter switch
        {
            SamplerStateValue.FilterMode.MinMagMipPoint => false,
            SamplerStateValue.FilterMode.MinMagPointMipLinear => false,
            SamplerStateValue.FilterMode.ComparisonMinMagMipPoint => false,
            SamplerStateValue.FilterMode.ComparisonMinMagPointMipLinear => false,
            _ => true,
        };

        private static NumericValue ApplyAddressMode(
            NumericValue uv,
            SamplerStateValue.TextureAddressMode mode,
            ref NumericValue borderMask)
        {
            switch (mode)
            {
                case SamplerStateValue.TextureAddressMode.Wrap:
                    return Frac(uv);
                case SamplerStateValue.TextureAddressMode.Mirror:
                    return 1.0f - Abs(Frac(uv * 0.5f) * 2.0f - 1.0f);
                case SamplerStateValue.TextureAddressMode.MirrorOnce:
                    return Clamp(Abs(uv), 0f, 1f);
                case SamplerStateValue.TextureAddressMode.Border:
                    borderMask = borderMask * ToFloatLike((uv >= 0.0f) * (uv <= 1.0f));
                    return Clamp(uv, 0f, 1f);
                default:
                    return Clamp(uv, 0f, 1f);
            }
        }

        public static NumericValue SampleLevel(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue lod, NumericValue offset = null, Func<NumericValue, NumericValue> perTexel = null)
        {
            lod = ToFloatLike(lod) + sampler.MipLodBias;
            lod = Clamp(lod, sampler.MinimumLod, sampler.MaximumLod);

            if (rv.IsCube)
                return SampleLevelCube(rv, sampler, location, lod, perTexel);

            int dim = rv.Dimension;
            var scalarLod = CastToScalar(lod);
            var size = VectorValue.FromScalars(rv.SizeX, rv.SizeY, rv.SizeZ).BroadcastToVector(dim) / (lod + 1);

            // For array textures the location vector is (spatial_coords..., arraySlice).
            ScalarValue arrSlice = null;
            NumericValue spatialLocation = location;
            if (rv.IsArray)
            {
                var locVec = CastToVector(location, dim + 1);
                var locScalars = locVec.ToScalars();
                arrSlice = locScalars[dim];
                spatialLocation = locVec.BroadcastToVector(dim);
            }

            var addressModes = new[]
            {
                sampler.AddressU,
                sampler.AddressV,
                sampler.AddressW,
            };
            NumericValue borderMask = (NumericValue)1.0f;
            var uvVec = CastToVector(spatialLocation, dim);
            ScalarValue[] wrappedUV = new ScalarValue[dim];
            for (int i = 0; i < dim; i++)
                wrappedUV[i] = (ScalarValue)ApplyAddressMode(uvVec[i], addressModes[i], ref borderMask);

            spatialLocation = dim == 1 ? wrappedUV[0]
                            : dim == 2 ? VectorValue.FromScalars(wrappedUV[0], wrappedUV[1])
                            : VectorValue.FromScalars(wrappedUV[0], wrappedUV[1], wrappedUV[2]);

            var texelPos = CastToVector(spatialLocation, dim).BroadcastToVector(dim) * size - 0.5f;
            if (offset is not null)
                texelPos = texelPos + CastToVector(offset, dim);

            NumericValue FetchAt(ScalarValue x, ScalarValue y, ScalarValue z)
            {
                NumericValue coord = dim switch
                {
                    1 => rv.IsArray ? VectorValue.FromScalars(x, arrSlice, scalarLod) : VectorValue.FromScalars(x, scalarLod),
                    2 => rv.IsArray ? VectorValue.FromScalars(x, y, arrSlice, scalarLod) : VectorValue.FromScalars(x, y, scalarLod),
                    _ => VectorValue.FromScalars(x, y, z, scalarLod),
                };
                var v = (NumericValue)Load(rv, coord);
                return perTexel != null ? perTexel(v) : v;
            }

            bool linear = IsLinearFilter(sampler.Filter);
            NumericValue result;
            ScalarValue zero = (ScalarValue)0;

            if (!linear)
            {
                var p = (VectorValue)Clamp(Round(texelPos).Cast(ScalarType.Int), 0, size - 1);
                result = FetchAt(p.x, dim >= 2 ? p.y : zero, dim >= 3 ? p.z : zero);
            }
            else
            {
                var basePos = (VectorValue)Floor(texelPos).Cast(ScalarType.Int);
                var sizeVec = (VectorValue)size;
                var frac = (VectorValue)Frac(texelPos);

                NumericValue WrapU(NumericValue x) => WrapTexelCoord(x, sizeVec[0], sampler.AddressU);
                NumericValue WrapV(NumericValue y) => WrapTexelCoord(y, sizeVec[1], sampler.AddressV);
                NumericValue WrapW(NumericValue z) => WrapTexelCoord(z, sizeVec[2], sampler.AddressW);

                var x0 = (ScalarValue)WrapU(basePos.x); var x1 = (ScalarValue)WrapU(basePos.x + 1);
                if (dim == 1)
                {
                    result = Lerp(FetchAt(x0, zero, zero), FetchAt(x1, zero, zero), frac.x);
                }
                else if (dim == 2)
                {
                    var y0 = (ScalarValue)WrapV(basePos.y); var y1 = (ScalarValue)WrapV(basePos.y + 1);
                    result = Lerp(
                        Lerp(FetchAt(x0, y0, zero), FetchAt(x1, y0, zero), frac.x),
                        Lerp(FetchAt(x0, y1, zero), FetchAt(x1, y1, zero), frac.x),
                        frac.y);
                }
                else
                {
                    var y0 = (ScalarValue)WrapV(basePos.y); var y1 = (ScalarValue)WrapV(basePos.y + 1);
                    var z0 = (ScalarValue)WrapW(basePos.z); var z1 = (ScalarValue)WrapW(basePos.z + 1);
                    result = Lerp(
                        Lerp(Lerp(FetchAt(x0, y0, z0), FetchAt(x1, y0, z0), frac.x), Lerp(FetchAt(x0, y1, z0), FetchAt(x1, y1, z0), frac.x), frac.y),
                        Lerp(Lerp(FetchAt(x0, y0, z1), FetchAt(x1, y0, z1), frac.x), Lerp(FetchAt(x0, y1, z1), FetchAt(x1, y1, z1), frac.x), frac.y),
                        frac.z);
                }
            }

            bool hasBorder = sampler.AddressU == SamplerStateValue.TextureAddressMode.Border
                || (dim >= 2 && sampler.AddressV == SamplerStateValue.TextureAddressMode.Border)
                || (dim >= 3 && sampler.AddressW == SamplerStateValue.TextureAddressMode.Border);
            if (hasBorder)
                result = result * borderMask;

            return result;
        }

        private static void ProjectCubeDirection(float x, float y, float z, out int face, out float u, out float v)
        {
            float ax = MathF.Abs(x), ay = MathF.Abs(y), az = MathF.Abs(z);
            float sc, tc, ma;
            if (ax >= ay && ax >= az)
            {
                ma = ax;
                if (x >= 0) { face = 0; sc = -z; tc = -y; }
                else { face = 1; sc = z; tc = -y; }
            }
            else if (ay >= az)
            {
                ma = ay;
                if (y >= 0) { face = 2; sc = x; tc = z; }
                else { face = 3; sc = x; tc = -z; }
            }
            else
            {
                ma = az;
                if (z >= 0) { face = 4; sc = x; tc = -y; }
                else { face = 5; sc = -x; tc = -y; }
            }
            u = (sc / ma + 1f) * 0.5f;
            v = (tc / ma + 1f) * 0.5f;
        }

        private static NumericValue SampleLevelCube(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue lod, Func<NumericValue, NumericValue> perTexel = null)
        {
            var dir = CastToVector(location, rv.IsArray ? 4 : 3);
            var scalarLod = CastToScalar(lod);
            bool linear = IsLinearFilter(sampler.Filter);
            int threadCount = dir.ThreadCount;
            HLSLValue[] results = new HLSLValue[threadCount];

            for (int thread = 0; thread < threadCount; thread++)
            {
                ProjectCubeDirection(
                    Convert.ToSingle(dir.x.GetThreadValue(thread)),
                    Convert.ToSingle(dir.y.GetThreadValue(thread)),
                    Convert.ToSingle(dir.z.GetThreadValue(thread)),
                    out int face, out float u, out float v);

                float lodClamped = MathF.Max(0f, Convert.ToSingle(scalarLod.GetThreadValue(thread)));
                float faceSize = MathF.Max(1f, rv.SizeX / MathF.Pow(2f, lodClamped));
                int mip = (int)lodClamped;
                int maxC = (int)faceSize - 1;
                int arraySlice = rv.IsArray ? Convert.ToInt32(dir[3].GetThreadValue(thread)) : 0;

                float texelU = u * faceSize - 0.5f;
                float texelV = v * faceSize - 0.5f;

                NumericValue Fetch(int x, int y)
                {
                    var raw = (NumericValue)rv.Get(x, y, face, arraySlice, mip);
                    return perTexel != null ? perTexel(raw) : raw;
                }

                if (!linear)
                {
                    int nx = Math.Clamp((int)MathF.Round(texelU, MidpointRounding.AwayFromZero), 0, maxC);
                    int ny = Math.Clamp((int)MathF.Round(texelV, MidpointRounding.AwayFromZero), 0, maxC);
                    results[thread] = Fetch(nx, ny);
                }
                else
                {
                    int baseX = (int)MathF.Floor(texelU), baseY = (int)MathF.Floor(texelV);
                    float fracU = texelU - baseX, fracV = texelV - baseY;
                    int x0 = Math.Clamp(baseX, 0, maxC), x1 = Math.Clamp(baseX + 1, 0, maxC);
                    int y0 = Math.Clamp(baseY, 0, maxC), y1 = Math.Clamp(baseY + 1, 0, maxC);

                    var c00 = Fetch(x0, y0);
                    var c10 = Fetch(x1, y0);
                    var c01 = Fetch(x0, y1);
                    var c11 = Fetch(x1, y1);

                    var cx0 = Lerp(c00, c10, (ScalarValue)fracU);
                    var cx1 = Lerp(c01, c11, (ScalarValue)fracU);
                    results[thread] = Lerp(cx0, cx1, (ScalarValue)fracV);
                }
            }

            return (NumericValue)HLSLValueUtils.MergeThreadValues(results);
        }

        public static NumericValue CalculateLevelOfDetailUnclamped(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location)
            => Log2(CalculateRho(executionState, rv, location));

        public static NumericValue CalculateLevelOfDetail(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location)
            => Clamp(CalculateLevelOfDetailUnclamped(executionState, rv, sampler, location), 0.0f, MaxLodForResource(rv));

        // Computes rho from two gradient vectors already in texel space.
        private static NumericValue RhoFromGradients(VectorValue gradX, VectorValue gradY)
            => Max(Length(gradX), Length(gradY));

        // Upper bound on meaningful LOD for a resource, used to clamp computed LODs.
        private static float MaxLodForResource(ResourceValue rv)
            => MathF.Log2(MathF.Max(rv.SizeX, MathF.Max(rv.SizeY, rv.SizeZ))) + 1;

        // Returns face-local UV in [0, 1] per thread. Used by CalculateRho to get face-space derivatives.
        private static VectorValue CubeDirectionToFaceUV(VectorValue dir)
        {
            int threadCount = dir.ThreadCount;
            HLSLValue[] results = new HLSLValue[threadCount];
            for (int thread = 0; thread < threadCount; thread++)
            {
                ProjectCubeDirection(
                    Convert.ToSingle(dir.x.GetThreadValue(thread)),
                    Convert.ToSingle(dir.y.GetThreadValue(thread)),
                    Convert.ToSingle(dir.z.GetThreadValue(thread)),
                    out _, out float u, out float v);
                results[thread] = VectorValue.FromScalars(u, v);
            }
            return (VectorValue)HLSLValueUtils.MergeThreadValues(results);
        }

        // Computes rho for LOD calculation.
        private static NumericValue CalculateRho(HLSLExecutionState executionState, ResourceValue rv, NumericValue location)
        {
            VectorValue scaledUV;
            if (rv.IsCube)
            {
                scaledUV = (VectorValue)(CubeDirectionToFaceUV(CastToVector(location, 3)) * rv.SizeX);
            }
            else
            {
                var sizeVec = VectorValue.FromScalars(rv.SizeX, rv.SizeY, rv.SizeZ).BroadcastToVector(rv.Dimension);
                scaledUV = CastToVector(location, rv.Dimension) * sizeVec;
            }
            var gradX = (VectorValue)Ddx(executionState, scaledUV);
            var gradY = (VectorValue)Ddy(executionState, scaledUV);
            return RhoFromGradients(gradX, gradY);
        }

        public static NumericValue Sample(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue offset = null, NumericValue clamp = null, Func<NumericValue, NumericValue> perTexel = null)
        {
            var lod = CalculateLevelOfDetail(executionState, rv, sampler, location);
            if (clamp is not null)
                lod = Min(lod, ToFloatLike(clamp));
            return SampleLevel(rv, sampler, location, lod, offset, perTexel);
        }

        public static NumericValue SampleGrad(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue ddx, NumericValue ddy, NumericValue offset = null, NumericValue clamp = null, Func<NumericValue, NumericValue> perTexel = null)
        {
            var sizeVec = VectorValue.FromScalars(rv.SizeX, rv.SizeY, rv.SizeZ).BroadcastToVector(rv.Dimension);
            var gradX = CastToVector(ddx, rv.Dimension) * sizeVec;
            var gradY = CastToVector(ddy, rv.Dimension) * sizeVec;

            var lod = Clamp(Log2(RhoFromGradients(gradX, gradY)), 0.0f, MaxLodForResource(rv));
            if (clamp is not null)
                lod = Min(lod, ToFloatLike(clamp));
            return SampleLevel(rv, sampler, location, lod, offset, perTexel);
        }

        public static NumericValue SampleBias(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue bias, NumericValue offset = null, NumericValue clamp = null, Func<NumericValue, NumericValue> perTexel = null)
        {
            var lod = CalculateLevelOfDetail(executionState, rv, sampler, location) + ToFloatLike(bias);
            if (clamp is not null)
                lod = Min(lod, ToFloatLike(clamp));
            return SampleLevel(rv, sampler, location, lod, offset, perTexel);
        }

        // returns 1.0 if texelValue passes the sampler's comparison against cmpValue, else 0.0.
        private static float CompareScalars(SamplerStateValue sampler, float texelValue, float cmpValue)
        {
            bool pass = sampler.Comparison switch
            {
                SamplerStateValue.ComparisonMode.Never => false,
                SamplerStateValue.ComparisonMode.Less => texelValue < cmpValue,
                SamplerStateValue.ComparisonMode.Equal => texelValue == cmpValue,
                SamplerStateValue.ComparisonMode.LessEqual => texelValue <= cmpValue,
                SamplerStateValue.ComparisonMode.Greater => texelValue > cmpValue,
                SamplerStateValue.ComparisonMode.NotEqual => texelValue != cmpValue,
                SamplerStateValue.ComparisonMode.GreaterEqual => texelValue >= cmpValue,
                SamplerStateValue.ComparisonMode.Always => true,
                _ => throw new NotImplementedException($"Unknown comparison mode: {sampler.Comparison}")
            };
            return pass ? 1.0f : 0.0f;
        }

        // Applies the sampler's comparison function to a sampled depth value vs a reference value
        private static NumericValue ApplyComparison(SamplerStateValue sampler, NumericValue sampledValue, NumericValue cmpVal)
        {
            var depth = ToFloatLike(CastToScalar(sampledValue));
            var cmp = ToFloatLike(CastToScalar(cmpVal));
            (depth, cmp) = HLSLTypeUtils.Promote(depth, cmp, false);
            return HLSLValueUtils.Map2(depth, cmp, (a, b) => CompareScalars(sampler, Convert.ToSingle(a), Convert.ToSingle(b)));
        }

        private static NumericValue WrapTexelCoord(NumericValue coord, NumericValue mipSize, SamplerStateValue.TextureAddressMode mode)
        {
            var sizeI = Floor(mipSize).Cast(ScalarType.Int);
            switch (mode)
            {
                case SamplerStateValue.TextureAddressMode.Wrap:
                    return ((coord % sizeI) + sizeI) % sizeI;
                case SamplerStateValue.TextureAddressMode.MirrorOnce:
                    return Clamp(Abs(coord), 0, sizeI - 1);
                case SamplerStateValue.TextureAddressMode.Mirror:
                    {
                        var period = sizeI * 2;
                        var c = ((coord % period) + period) % period;
                        var inLower = ToFloatLike(c < sizeI);
                        return (c * inLower + (period - 1 - c) * (1.0f - inLower)).Cast(ScalarType.Int);
                    }
                default: // Clamp, Border
                    return Clamp(coord, 0, sizeI - 1);
            }
        }

        private static int WrapTexelCoord(int coord, int size, SamplerStateValue.TextureAddressMode mode)
        {
            switch (mode)
            {
                case SamplerStateValue.TextureAddressMode.Wrap:
                    return ((coord % size) + size) % size;
                case SamplerStateValue.TextureAddressMode.Mirror:
                    {
                        int period = 2 * size;
                        int c = ((coord % period) + period) % period;
                        return c < size ? c : period - 1 - c;
                    }
                case SamplerStateValue.TextureAddressMode.MirrorOnce:
                    return Math.Clamp(Math.Abs(coord), 0, size - 1);
                case SamplerStateValue.TextureAddressMode.Border:
                    return (coord < 0 || coord >= size) ? -1 : coord;
                default: // Clamp
                    return Math.Clamp(coord, 0, size - 1);
            }
        }

        private static VectorValue GatherCore(
            ResourceValue rv, NumericValue location, int channelIndex,
            NumericValue uniformOffset = null,
            NumericValue[] cornerOffsets = null,
            SamplerStateValue sampler = null,
            NumericValue comparisonValue = null)
        {
            int spatialDim = rv.Dimension;
            int locComponents = spatialDim + (rv.IsArray ? 1 : 0);
            var loc = CastToVector(location.Cast(ScalarType.Float), locComponents);
            var scalarCmp = comparisonValue is not null ? CastToScalar(ToFloatLike(comparisonValue)) : null;

            var addrU = sampler?.AddressU ?? SamplerStateValue.TextureAddressMode.Clamp;
            var addrV = sampler?.AddressV ?? SamplerStateValue.TextureAddressMode.Clamp;

            int threadCount = loc.ThreadCount;
            HLSLValue[] results = new HLSLValue[threadCount];

            for (int thread = 0; thread < threadCount; thread++)
            {
                float u = Convert.ToSingle(loc[0].GetThreadValue(thread));
                float v = spatialDim >= 2 ? Convert.ToSingle(loc[1].GetThreadValue(thread)) : 0f;
                int arraySlice = rv.IsArray ? Convert.ToInt32(loc[spatialDim].GetThreadValue(thread)) : 0;

                int baseX = (int)MathF.Floor(u * rv.SizeX - 0.5f);
                int baseY = (int)MathF.Floor(v * rv.SizeY - 0.5f);

                // Gather output order: .x=(u0,v1), .y=(u1,v1), .z=(u1,v0), .w=(u0,v0)
                int[] cornerX = { baseX, baseX + 1, baseX + 1, baseX };
                int[] cornerY = { baseY + 1, baseY + 1, baseY, baseY };

                float cmpV = scalarCmp is not null ? Convert.ToSingle(scalarCmp.GetThreadValue(thread)) : 0f;

                float[] components = new float[4];
                for (int i = 0; i < 4; i++)
                {
                    NumericValue offSrc = cornerOffsets is not null ? cornerOffsets[i] : uniformOffset;
                    int ox = 0, oy = 0;
                    if (offSrc is not null)
                    {
                        if (spatialDim >= 2)
                        {
                            var ov = CastToVector(offSrc.Cast(ScalarType.Int), 2);
                            ox = Convert.ToInt32(ov.x.GetThreadValue(thread));
                            oy = Convert.ToInt32(ov.y.GetThreadValue(thread));
                        }
                        else
                        {
                            ox = Convert.ToInt32(CastToScalar(offSrc.Cast(ScalarType.Int)).GetThreadValue(thread));
                        }
                    }

                    int x = WrapTexelCoord(cornerX[i] + ox, rv.SizeX, addrU);
                    int y, z;
                    if (rv.IsArray && spatialDim == 1) { y = arraySlice; z = 0; }
                    else { y = WrapTexelCoord(cornerY[i] + oy, rv.SizeY, addrV); z = rv.IsArray ? arraySlice : 0; }

                    if (x == -1 || y == -1)
                    {
                        components[i] = comparisonValue is not null ? CompareScalars(sampler, 0f, cmpV) : 0f;
                        continue;
                    }

                    var texel = rv.Get(x, y, z, 0, 0);
                    float texelCh = texel is VectorValue vv
                        ? Convert.ToSingle(vv[Math.Min(channelIndex, vv.Size - 1)].GetThreadValue(thread))
                        : Convert.ToSingle(CastToScalar((NumericValue)texel).GetThreadValue(thread));

                    if (comparisonValue is not null)
                        texelCh = CompareScalars(sampler, texelCh, cmpV);

                    components[i] = texelCh;
                }

                results[thread] = VectorValue.FromScalars(
                    (ScalarValue)components[0], (ScalarValue)components[1],
                    (ScalarValue)components[2], (ScalarValue)components[3]);
            }

            return (VectorValue)HLSLValueUtils.MergeThreadValues(results);
        }

        private static HLSLValue GetDimensions(HLSLExecutionState state, ResourceValue rv, HLSLValue[] args)
        {
            // The first argument is a mip-level input if it is not a ReferenceValue (out param).
            // Only non-RW textures support mip queries in GetDimensions.
            bool hasMipInput = args.Length > 0 && args[0] is not ReferenceValue
                && rv.IsTexture && !rv.IsWriteable;

            ScalarValue scalarMip = null;
            int firstOutArgIdx = 0;
            int threadCount = 1;
            if (hasMipInput)
            {
                scalarMip = CastToScalar((NumericValue)args[0]);
                threadCount = scalarMip.ThreadCount;
                firstOutArgIdx = 1;
            }

            // Count the out params and allocate per-thread storage.
            int outCount = args.Length - firstOutArgIdx;
            var collected = new uint[outCount][];
            for (int o = 0; o < outCount; o++)
                collected[o] = new uint[threadCount];

            // For cube textures, GetDimensions reports face width/height (2D), not a 3D dimension.
            int outDim = rv.IsCube ? 2 : rv.Dimension;

            for (int thread = 0; thread < threadCount; thread++)
            {
                int mipLevel = hasMipInput ? Convert.ToInt32(scalarMip.GetThreadValue(thread)) : 0;
                float scale = MathF.Pow(2, mipLevel);
                uint w = (uint)Math.Max(1, (int)(rv.SizeX / scale));
                uint h = (uint)Math.Max(1, (int)(rv.SizeY / scale));
                uint d = (uint)Math.Max(1, (int)(rv.SizeZ / scale));

                int o = 0;
                // Width, all resource types have a width.
                collected[o++][thread] = w;
                // Height, 2D+ non-buffer resources.
                if (!rv.IsBuffer && outDim >= 2) collected[o++][thread] = h;
                // Depth or array element count, 3D textures and array textures.
                if (outDim >= 3 || rv.IsArray) collected[o++][thread] = d;
                // Sample count, MS textures (we report 1 since we don't track per-sample data).
                if (IsMSTexture(rv)) collected[o++][thread] = 1;
                // Element stride, StructuredBuffer types: size of the first template argument.
                if (rv.Type == PredefinedObjectType.StructuredBuffer ||
                    rv.Type == PredefinedObjectType.RWStructuredBuffer ||
                    rv.Type == PredefinedObjectType.AppendStructuredBuffer ||
                    rv.Type == PredefinedObjectType.ConsumeStructuredBuffer ||
                    rv.Type == PredefinedObjectType.RasterizerOrderedStructuredBuffer)
                    collected[o++][thread] = (uint)rv.Stride;
                // Mip level count, only when a mip input was given.
                if (hasMipInput) collected[o++][thread] = (uint)rv.MipCount;
            }

            // Write per-thread values to each out param.
            for (int o = 0; o < outCount; o++)
            {
                int argIdx = firstOutArgIdx + o;
                if (argIdx < args.Length && args[argIdx] is ReferenceValue r)
                {
                    var perThread = new HLSLValue[threadCount];
                    for (int thread = 0; thread < threadCount; thread++)
                        perThread[thread] = new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarSGPR(collected[o][thread]));
                    r.Set((NumericValue)HLSLValueUtils.MergeThreadValues(perThread));
                }
            }

            return ScalarValue.Null;
        }

        private static bool IsMSTexture(ResourceValue rv) =>
            rv.Type == PredefinedObjectType.Texture2DMS ||
            rv.Type == PredefinedObjectType.Texture2DMSArray;

        #endregion
    }
}
