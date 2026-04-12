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

            // Register one handler for an arg count.
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

            // === Load ===
            AddN("Load", 1, 4, (state, rv, args) =>
            {
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
                    return GatherCore(rv, (NumericValue)args[1], ch, uniformOffset: offset);
                });
                // 4-offset variant, each corner gets its own offset
                if (has4Offset)
                {
                    AddN(gName, 6, 7, (state, rv, args) =>
                        GatherCore(rv, (NumericValue)args[1], ch,
                            cornerOffsets: new[] { (NumericValue)args[2], (NumericValue)args[3],
                                                   (NumericValue)args[4], (NumericValue)args[5] }));
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
                        comparisonSampler: (SamplerStateValue)args[0], comparisonValue: (NumericValue)args[2]);
                });
                if (has4Offset)
                {
                    AddN(gName, 7, 8, (state, rv, args) =>
                        GatherCore(rv, (NumericValue)args[1], ch,
                            cornerOffsets: new[] { (NumericValue)args[3], (NumericValue)args[4],
                                                   (NumericValue)args[5], (NumericValue)args[6] },
                            comparisonSampler: (SamplerStateValue)args[0], comparisonValue: (NumericValue)args[2]));
                }
            }

            // === GetDimensions ===
            AddN("GetDimensions", 1, 5, (state, rv, args) => GetDimensions(rv, args));

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
                Add($"Store{suffix}", 2, (state, rv, args) => { StoreN(rv, (NumericValue)args[0], (NumericValue)args[1], count); return ScalarValue.Null; });
            }

            // === RWByteAddressBuffer atomics ===
            AddN("InterlockedAdd", 2, 3, (state, rv, args) => { InterlockedRMW32(rv, args, (a, b) => a + b); return ScalarValue.Null; });
            AddN("InterlockedMin", 2, 3, (state, rv, args) => { InterlockedRMW32(rv, args, (a, b) => (int)a < (int)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedMax", 2, 3, (state, rv, args) => { InterlockedRMW32(rv, args, (a, b) => (int)a > (int)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedAnd", 2, 3, (state, rv, args) => { InterlockedRMW32(rv, args, (a, b) => a & b); return ScalarValue.Null; });
            AddN("InterlockedOr", 2, 3, (state, rv, args) => { InterlockedRMW32(rv, args, (a, b) => a | b); return ScalarValue.Null; });
            AddN("InterlockedXor", 2, 3, (state, rv, args) => { InterlockedRMW32(rv, args, (a, b) => a ^ b); return ScalarValue.Null; });
            AddN("InterlockedAdd64", 2, 3, (state, rv, args) => { InterlockedRMW64(rv, args, (a, b) => a + b); return ScalarValue.Null; });
            AddN("InterlockedMin64", 2, 3, (state, rv, args) => { InterlockedRMW64(rv, args, (a, b) => (long)a < (long)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedMax64", 2, 3, (state, rv, args) => { InterlockedRMW64(rv, args, (a, b) => (long)a > (long)b ? a : b); return ScalarValue.Null; });
            AddN("InterlockedAnd64", 2, 3, (state, rv, args) => { InterlockedRMW64(rv, args, (a, b) => a & b); return ScalarValue.Null; });
            AddN("InterlockedOr64", 2, 3, (state, rv, args) => { InterlockedRMW64(rv, args, (a, b) => a | b); return ScalarValue.Null; });
            AddN("InterlockedXor64", 2, 3, (state, rv, args) => { InterlockedRMW64(rv, args, (a, b) => a ^ b); return ScalarValue.Null; });
            Add("InterlockedExchange", 3, (state, rv, args) => { InterlockedRMW32(rv, args, (_, b) => b); return ScalarValue.Null; });
            Add("InterlockedExchange64", 3, (state, rv, args) => { InterlockedRMW64(rv, args, (_, b) => b); return ScalarValue.Null; });
            Add("InterlockedExchangeFloat", 3, (state, rv, args) => { InterlockedExchangeFloat(rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareStore", 3, (state, rv, args) => { InterlockedCmpStore32(rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareStore64", 3, (state, rv, args) => { InterlockedCmpStore64(rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareStoreFloatBitwise", 3, (state, rv, args) => { InterlockedCmpStoreFloat(rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareExchange", 4, (state, rv, args) => { InterlockedCmpStore32(rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareExchange64", 4, (state, rv, args) => { InterlockedCmpStore64(rv, args); return ScalarValue.Null; });
            Add("InterlockedCompareExchangeFloatBitwise", 4, (state, rv, args) => { InterlockedCmpStoreFloat(rv, args); return ScalarValue.Null; });

            // === Misc stuff ===
            Add("IncrementCounter", 0, (state, rv, args) => (NumericValue)(uint)rv.Counter++);
            Add("DecrementCounter", 0, (state, rv, args) => (NumericValue)(uint)--rv.Counter);
            Add("GetSamplePosition", 1, (state, rv, args) => VectorValue.FromScalars((ScalarValue)0.5f, (ScalarValue)0.5f));

            return table;
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

        public static HLSLValue Load(ResourceValue rv, NumericValue location, NumericValue offset = null)
            => LoadCore(rv, location, offset, hasMip: rv.IsTexture && !rv.IsWriteable);

        public static HLSLValue LoadMS(ResourceValue rv, NumericValue location, NumericValue offset = null)
            => LoadCore(rv, location, offset, hasMip: false);

        // Load N uints
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

        // Stores N uints
        private static void StoreN(ResourceValue rv, NumericValue byteOffset, NumericValue value, int count)
        {
            var scalarOff = CastToScalar(byteOffset.Cast(ScalarType.Int));
            var vec = CastToVector(value.Cast(ScalarType.Uint), count);
            int threadCount = scalarOff.ThreadCount;
            for (int thread = 0; thread < threadCount; thread++)
            {
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

        // Read-modify-write for 32-bit atomics.
        private static void InterlockedRMW32(ResourceValue rv, HLSLValue[] args, Func<uint, uint, uint> op)
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
                WriteUint32(rv, off, op(old, val));
            }
            if (originals is not null)
                ((ReferenceValue)args[2]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        // Read-modify-write for 64-bit atomics.
        private static void InterlockedRMW64(ResourceValue rv, HLSLValue[] args, Func<ulong, ulong, ulong> op)
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
                WriteUint64(rv, off, op(old, val));
            }
            if (originals is not null)
                ((ReferenceValue)args[2]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedExchangeFloat(ResourceValue rv, HLSLValue[] args)
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
                WriteUint32(rv, off, BitConverter.SingleToUInt32Bits(val));
            }
            if (args[2] is ReferenceValue outRef)
                outRef.Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedCmpStore32(ResourceValue rv, HLSLValue[] args)
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
                if (old == cmp) WriteUint32(rv, off, val);
            }
            if (originals is not null)
                ((ReferenceValue)args[3]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedCmpStore64(ResourceValue rv, HLSLValue[] args)
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
                if (old == cmp) WriteUint64(rv, off, val);
            }
            if (originals is not null)
                ((ReferenceValue)args[3]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        private static void InterlockedCmpStoreFloat(ResourceValue rv, HLSLValue[] args)
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
                if (oldBits == cmpBits) WriteUint32(rv, off, valBits);
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
                // Read spatial coordinate i (with optional offset).
                int CoordWithOffset(int i)
                {
                    int v = Convert.ToInt32(scalarLoc[i].GetThreadValue(threadIndex));
                    if (scalarOff != null)
                        v += Convert.ToInt32(scalarOff[i].GetThreadValue(threadIndex));
                    return v;
                }
                // Read a raw (non-offset) coordinate.
                int RawCoord(int i) => Convert.ToInt32(scalarLoc[i].GetThreadValue(threadIndex));

                int x = rv.Dimension >= 1 ? CoordWithOffset(0) : 0;
                int y = rv.Dimension >= 2 ? CoordWithOffset(1) : 0;
                int z = rv.Dimension >= 3 ? CoordWithOffset(2) : 0;
                int mip = 0;

                // Array slice follows the spatial dimensions, no offset applied.
                if (rv.IsArray)
                {
                    int slice = RawCoord(rv.Dimension);
                    // 1D arrays: slice goes into the y parameter of Get.
                    // 2D arrays: slice goes into the z parameter of Get.
                    if (rv.Dimension == 1) y = slice;
                    else z = slice;
                }

                // Mip level is the very last component (non-RW textures only).
                if (hasMip)
                    mip = RawCoord(rv.Dimension + (rv.IsArray ? 1 : 0));

                results[threadIndex] = rv.Get(x, y, z, 0, mip);
            }

            return HLSLValueUtils.MergeThreadValues(results);
        }

        public static NumericValue SampleLevel(ResourceValue rv, SamplerStateValue sampler, NumericValue location, NumericValue lod, NumericValue offset = null, Func<NumericValue, NumericValue> perTexel = null)
        {
            // Cube maps use face selection + per-face bilinear sampling; offset is not applicable.
            if (rv.IsCube)
                return SampleLevelCube(rv, location, lod, perTexel);

            int dim = rv.Dimension;
            var scalarLod = CastToScalar(lod);
            var size = VectorValue.FromScalars(rv.SizeX, rv.SizeY, rv.SizeZ).BroadcastToVector(dim) / (lod + 1);

            var texelPos = CastToVector(location, dim) * size - 0.5f;
            if (offset is not null)
                texelPos = texelPos + CastToVector(offset, dim);

            var basePos = Floor(texelPos).Cast(ScalarType.Int);
            var frac = (VectorValue)Frac(texelPos);

            NumericValue Fetch(NumericValue loadArgs)
            {
                var v = (NumericValue)Load(rv, loadArgs);
                return perTexel != null ? perTexel(v) : v;
            }

            if (dim == 1)
            {
                // Linear interpolation
                var p0 = (VectorValue)Clamp(basePos, 0, size - 1);
                var p1 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1), 0, size - 1);

                var c0 = Fetch(VectorValue.FromScalars(p0.x, scalarLod));
                var c1 = Fetch(VectorValue.FromScalars(p1.x, scalarLod));

                return Lerp(c0, c1, frac.x);
            }
            else if (dim == 2)
            {
                // Bilinear interpolation
                var p00 = (VectorValue)Clamp(basePos, 0, size - 1);
                var p10 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 0), 0, size - 1);
                var p01 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(0, 1), 0, size - 1);
                var p11 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 1), 0, size - 1);

                var c00 = Fetch(VectorValue.FromScalars(p00.x, p00.y, scalarLod));
                var c10 = Fetch(VectorValue.FromScalars(p10.x, p10.y, scalarLod));
                var c01 = Fetch(VectorValue.FromScalars(p01.x, p01.y, scalarLod));
                var c11 = Fetch(VectorValue.FromScalars(p11.x, p11.y, scalarLod));

                var cx0 = Lerp(c00, c10, frac.x);
                var cx1 = Lerp(c01, c11, frac.x);
                return Lerp(cx0, cx1, frac.y);
            }
            else
            {
                // Trilinear interpolation
                var p000 = (VectorValue)Clamp(basePos, 0, size - 1);
                var p100 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 0, 0), 0, size - 1);
                var p010 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(0, 1, 0), 0, size - 1);
                var p110 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 1, 0), 0, size - 1);
                var p001 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(0, 0, 1), 0, size - 1);
                var p101 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 0, 1), 0, size - 1);
                var p011 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(0, 1, 1), 0, size - 1);
                var p111 = (VectorValue)Clamp(basePos + VectorValue.FromScalars(1, 1, 1), 0, size - 1);

                var c000 = Fetch(VectorValue.FromScalars(p000.x, p000.y, p000.z, scalarLod));
                var c100 = Fetch(VectorValue.FromScalars(p100.x, p100.y, p100.z, scalarLod));
                var c010 = Fetch(VectorValue.FromScalars(p010.x, p010.y, p010.z, scalarLod));
                var c110 = Fetch(VectorValue.FromScalars(p110.x, p110.y, p110.z, scalarLod));
                var c001 = Fetch(VectorValue.FromScalars(p001.x, p001.y, p001.z, scalarLod));
                var c101 = Fetch(VectorValue.FromScalars(p101.x, p101.y, p101.z, scalarLod));
                var c011 = Fetch(VectorValue.FromScalars(p011.x, p011.y, p011.z, scalarLod));
                var c111 = Fetch(VectorValue.FromScalars(p111.x, p111.y, p111.z, scalarLod));

                var cx00 = Lerp(c000, c100, frac.x);
                var cx10 = Lerp(c010, c110, frac.x);
                var cx01 = Lerp(c001, c101, frac.x);
                var cx11 = Lerp(c011, c111, frac.x);

                var cxy0 = Lerp(cx00, cx10, frac.y);
                var cxy1 = Lerp(cx01, cx11, frac.y);

                return Lerp(cxy0, cxy1, frac.z);
            }
        }

        // Converts a float3 direction to a cube face index
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

        // Samples a cube map or cube map array using face selection and per-face bilinear interpolation.
        private static NumericValue SampleLevelCube(ResourceValue rv, NumericValue location, NumericValue lod, Func<NumericValue, NumericValue> perTexel = null)
        {
            var dir = CastToVector(location, rv.IsArray ? 4 : 3);
            var scalarLod = CastToScalar(lod);
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
                int baseX = (int)MathF.Floor(texelU), baseY = (int)MathF.Floor(texelV);
                float fracU = texelU - baseX, fracV = texelV - baseY;

                int x0 = Math.Clamp(baseX, 0, maxC), x1 = Math.Clamp(baseX + 1, 0, maxC);
                int y0 = Math.Clamp(baseY, 0, maxC), y1 = Math.Clamp(baseY + 1, 0, maxC);

                NumericValue Fetch(int x, int y)
                {
                    var raw = (NumericValue)rv.Get(x, y, face, arraySlice, mip);
                    return perTexel != null ? perTexel(raw) : raw;
                }

                var c00 = Fetch(x0, y0);
                var c10 = Fetch(x1, y0);
                var c01 = Fetch(x0, y1);
                var c11 = Fetch(x1, y1);

                var cx0 = Lerp(c00, c10, (ScalarValue)fracU);
                var cx1 = Lerp(c01, c11, (ScalarValue)fracU);
                results[thread] = Lerp(cx0, cx1, (ScalarValue)fracV);
            }

            return (NumericValue)HLSLValueUtils.MergeThreadValues(results);
        }

        public static NumericValue CalculateLevelOfDetail(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location)
        {
            var rho = CalculateRho(executionState, rv, location);
            float maxDim = MathF.Max(rv.SizeX, MathF.Max(rv.SizeY, rv.SizeZ));
            return Clamp(Log2(rho), 0.0f, MathF.Log(maxDim) / MathF.Log(2) + 1);
        }

        public static NumericValue CalculateLevelOfDetailUnclamped(HLSLExecutionState executionState, ResourceValue rv, SamplerStateValue sampler, NumericValue location)
        {
            return Log2(CalculateRho(executionState, rv, location));
        }

        // Computes rho from two gradient vectors already in texel space.
        private static NumericValue RhoFromGradients(VectorValue gradX, VectorValue gradY)
        {
            int dim = gradX.Size;
            NumericValue lengthX, lengthY;
            if (dim == 1)
            {
                lengthX = Abs(gradX.x);
                lengthY = Abs(gradY.x);
            }
            else if (dim == 2)
            {
                lengthX = Sqrt(gradX.x * gradX.x + gradX.y * gradX.y);
                lengthY = Sqrt(gradY.x * gradY.x + gradY.y * gradY.y);
            }
            else
            {
                lengthX = Sqrt(gradX.x * gradX.x + gradX.y * gradX.y + gradX.z * gradX.z);
                lengthY = Sqrt(gradY.x * gradY.x + gradY.y * gradY.y + gradY.z * gradY.z);
            }
            return Max(lengthX, lengthY);
        }

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

            float maxDim = MathF.Max(rv.SizeX, MathF.Max(rv.SizeY, rv.SizeZ));
            var lod = Clamp(Log2(RhoFromGradients(gradX, gradY)), 0.0f, MathF.Log(maxDim) / MathF.Log(2) + 1);
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
            (depth, cmp) = HLSLValueUtils.Promote(depth, cmp, false);
            return HLSLValueUtils.Map2(depth, cmp, (a, b) => CompareScalars(sampler, Convert.ToSingle(a), Convert.ToSingle(b)));
        }

        private static VectorValue GatherCore(
            ResourceValue rv, NumericValue location, int channelIndex,
            NumericValue uniformOffset = null,
            NumericValue[] cornerOffsets = null,
            SamplerStateValue comparisonSampler = null,
            NumericValue comparisonValue = null)
        {
            int spatialDim = rv.Dimension;
            int locComponents = spatialDim + (rv.IsArray ? 1 : 0);
            var loc = CastToVector(location.Cast(ScalarType.Float), locComponents);
            var scalarCmp = comparisonValue is not null ? CastToScalar(ToFloatLike(comparisonValue)) : null;

            int threadCount = loc.ThreadCount;
            HLSLValue[] results = new HLSLValue[threadCount];

            for (int thread = 0; thread < threadCount; thread++)
            {
                float u = Convert.ToSingle(loc[0].GetThreadValue(thread));
                float v = spatialDim >= 2 ? Convert.ToSingle(loc[1].GetThreadValue(thread)) : 0f;
                int arraySlice = rv.IsArray ? Convert.ToInt32(loc[spatialDim].GetThreadValue(thread)) : 0;

                // Bilinear footprint base coordinates (mip 0, no LOD scaling).
                int baseX = (int)MathF.Floor(u * rv.SizeX - 0.5f);
                int baseY = (int)MathF.Floor(v * rv.SizeY - 0.5f);

                // HLSL Gather output order: .x=(u0,v1), .y=(u1,v1), .z=(u1,v0), .w=(u0,v0)
                int[] cornerX = { baseX, baseX + 1, baseX + 1, baseX };
                int[] cornerY = { baseY + 1, baseY + 1, baseY, baseY };

                float cmpV = scalarCmp is not null ? Convert.ToSingle(scalarCmp.GetThreadValue(thread)) : 0f;

                float[] components = new float[4];
                for (int i = 0; i < 4; i++)
                {
                    // Resolve offset for this corner (per-corner or uniform).
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

                    // Clamp to texture bounds (clamp addressing mode).
                    int x = Math.Clamp(cornerX[i] + ox, 0, (int)rv.SizeX - 1);
                    int y, z;
                    if (rv.IsArray && spatialDim == 1) { y = arraySlice; z = 0; }
                    else { y = Math.Clamp(cornerY[i] + oy, 0, (int)rv.SizeY - 1); z = rv.IsArray ? arraySlice : 0; }

                    // Fetch the texel and extract the requested channel.
                    var texel = rv.Get(x, y, z, 0, 0);
                    float texelCh;
                    if (texel is VectorValue vv)
                    {
                        int idx = Math.Min(channelIndex, vv.Size - 1);
                        texelCh = Convert.ToSingle(vv[idx].GetThreadValue(thread));
                    }
                    else
                    {
                        texelCh = Convert.ToSingle(CastToScalar((NumericValue)texel).GetThreadValue(thread));
                    }

                    // Apply comparison for GatherCmp* (returns 1.0 if passes, 0.0 if fails).
                    if (comparisonSampler != null)
                        texelCh = CompareScalars(comparisonSampler, texelCh, cmpV);

                    components[i] = texelCh;
                }

                results[thread] = VectorValue.FromScalars(
                    (ScalarValue)components[0], (ScalarValue)components[1],
                    (ScalarValue)components[2], (ScalarValue)components[3]);
            }

            return (VectorValue)HLSLValueUtils.MergeThreadValues(results);
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

            // Element stride — StructuredBuffer types: size of the first template argument.
            if (rv.Type == PredefinedObjectType.StructuredBuffer ||
                rv.Type == PredefinedObjectType.RWStructuredBuffer ||
                rv.Type == PredefinedObjectType.AppendStructuredBuffer ||
                rv.Type == PredefinedObjectType.ConsumeStructuredBuffer ||
                rv.Type == PredefinedObjectType.RasterizerOrderedStructuredBuffer)
                Write((uint)HLSLValueUtils.GetTypeSize(rv.TemplateArguments[0]));

            // Mip level count — only when a mip input was given.
            if (hasMipInput)
                Write((uint)rv.MipCount);

            return ScalarValue.Null;
        }

        private static bool IsMSTexture(ResourceValue rv) =>
            rv.Type == PredefinedObjectType.Texture2DMS ||
            rv.Type == PredefinedObjectType.Texture2DMSArray;

        #endregion
    }
}
