using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSL
{
    public static class HLSLValueUtils
    {
        public static string FormatRawValue(RawValue rv, ScalarType type)
        {
            if (HLSLTypeUtils.IsInt(type)) return rv.Int.ToString(CultureInfo.InvariantCulture);
            if (HLSLTypeUtils.IsUint(type)) return rv.Uint.ToString(CultureInfo.InvariantCulture);
            if (HLSLTypeUtils.IsFloat(type)) return rv.Float.ToString(CultureInfo.InvariantCulture);
            if (type == ScalarType.Double) return rv.Double.ToString(CultureInfo.InvariantCulture);
            if (type == ScalarType.Bool) return rv.Bool.ToString(CultureInfo.InvariantCulture);
            if (type == ScalarType.Char) return rv.Char.ToString(CultureInfo.InvariantCulture);
            return rv.Int.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatRawValues(RawValue[] arr, ScalarType type)
        {
            var parts = new string[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                parts[i] = FormatRawValue(arr[i], type);
            return string.Join(", ", parts);
        }

        public static int VectorSwizzleCharToIndex(char c)
        {
            switch (c)
            {
                case 'x': case 'r': case 's': return 0;
                case 'y': case 'g': case 't': return 1;
                case 'z': case 'b': case 'p': return 2;
                case 'w': case 'a': case 'q': return 3;
                default: throw new InvalidOperationException($"Invalid swizzle character '{c}'");
            }
        }

        public static char IndexToVectorSwizzleChar(int index)
        {
            switch (index)
            {
                case 0: return 'x';
                case 1: return 'y';
                case 2: return 'z';
                case 3: return 'w';
                default: throw new InvalidOperationException($"Invalid component index {index}");
            }
        }

        public static int[] MatrixSwizzleStringToIndices(string swizzle, int columns)
        {
            bool zeroBased = swizzle.Length >= 2 && swizzle[1] == 'm';
            string[] parts = zeroBased
                ? swizzle.Split(new[] { "_m" }, StringSplitOptions.RemoveEmptyEntries)
                : swizzle.Split(new[] { "_"  }, StringSplitOptions.RemoveEmptyEntries);
            int offset = zeroBased ? 0 : 1;
            return parts.Select(p => (p[0] - '0' - offset) * columns + (p[1] - '0' - offset)).ToArray();
        }

        private static HLSLRegister<T> Map2Registers<T>(HLSLRegister<T> left, HLSLRegister<T> right, Func<T, T, T> mapper)
        {
            if (left.IsVarying && right.IsVarying)
            {
                T[] mapped = new T[left.VaryingValues.Length];
                for (int i = 0; i < mapped.Length; i++)
                    mapped[i] = mapper(left.VaryingValues[i], right.VaryingValues[i]);
                return new HLSLRegister<T>(mapped);
            }
            else if (!left.IsVarying && !right.IsVarying)
            {
                return new HLSLRegister<T>(mapper(left.UniformValue, right.UniformValue));
            }
            else
            {
                throw new InvalidOperationException("Cannot map varying and uniform register together.");
            }
        }

        public static NumericValue Map2(NumericValue left, NumericValue right, Func<RawValue, RawValue, RawValue> mapper)
        {
            if (left.TensorSize != right.TensorSize)
                throw new ArgumentException("Sizes of operands must match.");
            if (left is ScalarValue scalarLeft && right is ScalarValue scalarRight)
                return new ScalarValue(scalarLeft.Type, Map2Registers(scalarLeft.Value, scalarRight.Value, mapper));
            if (left is VectorValue vectorLeft && right is VectorValue vectorRight)
            {
                var mapped = Map2Registers(vectorLeft.Values, vectorRight.Values, (x, y) =>
                {
                    RawValue[] result = new RawValue[vectorLeft.Size];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = mapper(x[i], y[i]);
                    return result;
                });
                return new VectorValue(vectorLeft.Type, mapped);
            }
            if (left is MatrixValue matrixLeft && right is MatrixValue matrixRight)
            {
                var mapped = Map2Registers(matrixLeft.Values, matrixRight.Values, (x, y) =>
                {
                    RawValue[] result = new RawValue[x.Length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = mapper(x[i], y[i]);
                    return result;
                });
                return new MatrixValue(matrixLeft.Type, matrixLeft.Rows, matrixLeft.Columns, mapped);
            }
            throw new InvalidOperationException();
        }

        public static HLSLValue Scalarize(HLSLValue value, int threadIndex)
        {
            switch (value)
            {
                case NumericValue num:
                    return num.Scalarize(threadIndex);
                case StructValue str:
                    Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
                    foreach (var kvp in str.Members)
                        members.Add(kvp.Key, Scalarize(kvp.Value, threadIndex));
                    return new StructValue(str.Name, members);
                case ArrayValue arr:
                    HLSLValue[] vals = new HLSLValue[arr.Values.Length];
                    for (int i = 0; i < vals.Length; i++)
                        vals[i] = Scalarize(arr.Values[i], threadIndex);
                    return new ArrayValue(vals);
                default:
                    // StringValue and other non-shaped values have no per-thread representation.
                    return value;
            }
        }

        public static HLSLValue Vectorize(HLSLValue value, int threadCount)
        {
            switch (value)
            {
                case NumericValue num:
                    return num.Vectorize(threadCount);
                case StructValue str:
                    Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
                    foreach (var kvp in str.Members)
                        members.Add(kvp.Key, Vectorize(kvp.Value, threadCount));
                    return new StructValue(str.Name, members);
                case ArrayValue arr:
                    HLSLValue[] vals = new HLSLValue[arr.Values.Length];
                    for (int i = 0; i < vals.Length; i++)
                        vals[i] = Vectorize(arr.Values[i], threadCount);
                    return new ArrayValue(vals);
                default:
                    return value;
            }
        }

        public static HLSLValue SetThreadValue(HLSLValue allValue, int threadIndex, HLSLValue threadValue)
        {
            if (allValue is NumericValue numLeft && threadValue is NumericValue numRight)
            {
                (numLeft, numRight) = HLSLTypeUtils.Promote(numLeft, numRight, false);
                if (numLeft is ScalarValue svLeft && numRight is ScalarValue svRight)
                    return svLeft.SetThreadValue(threadIndex, svRight.Value.Get(threadIndex));
                if (numLeft is VectorValue vvLeft && numRight is VectorValue vvRight)
                    return vvLeft.SetThreadValue(threadIndex, vvRight.Values.Get(threadIndex));
                if (numLeft is MatrixValue mvLeft && numRight is MatrixValue mvRight)
                    return mvLeft.SetThreadValue(threadIndex, mvRight.Values.Get(threadIndex));
                throw new InvalidOperationException();
            }

            if (allValue is StructValue strLeft && threadValue is StructValue strRight)
            {
                Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
                foreach (var kvp in strLeft.Members)
                {
                    if (strRight.Members.TryGetValue(kvp.Key, out var rightV))
                        members.Add(kvp.Key, SetThreadValue(kvp.Value, threadIndex, rightV));
                }
                return new StructValue(strLeft.Name, members);
            }

            if (allValue is ArrayValue arrLeft && threadValue is ArrayValue arrRight)
            {
                HLSLValue[] vals = new HLSLValue[arrLeft.Values.Length];
                for (int i = 0; i < vals.Length; i++)
                    vals[i] = SetThreadValue(arrLeft.Values[i], threadIndex, arrRight.Values[i]);
                return new ArrayValue(vals);
            }

            throw new InvalidOperationException();
        }

        public static RawValue GetScalarComponent(NumericValue value, int threadIndex, int channel)
        {
            if (value is ScalarValue sv)
                return sv.Value.Get(threadIndex);
            if (value is VectorValue vv)
                return vv.Values.Get(threadIndex)[channel];
            if (value is MatrixValue mv)
                return mv.Values.Get(threadIndex)[channel];
            throw new InvalidOperationException();
        }

        public static HLSLRegister<RawValue> MakeScalarSGPR(RawValue val)
        {
            return new HLSLRegister<RawValue>(val);
        }

        public static HLSLRegister<RawValue> MakeScalarVGPR(IEnumerable<RawValue> val)
        {
            return new HLSLRegister<RawValue>(val.ToArray());
        }

        public static HLSLRegister<RawValue[]> MakeVectorSGPR(IEnumerable<RawValue> val)
        {
            return new HLSLRegister<RawValue[]>(val.ToArray());
        }

        public static HLSLRegister<RawValue[]> MakeVectorVGPR(IEnumerable<IEnumerable<RawValue>> val)
        {
            return new HLSLRegister<RawValue[]>(val.Select(x => x.ToArray()).ToArray());
        }

        public static HLSLRegister<RawValue[]> RegisterFromScalars(ScalarValue[] scalars)
        {
            return RegisterFromScalars(scalars, out _);
        }

        public static HLSLRegister<RawValue[]> RegisterFromScalars(ScalarValue[] scalars, out ScalarType promotedType)
        {
            ScalarType type = scalars[0].Type;
            foreach (var scalar in scalars)
            {
                type = HLSLTypeUtils.PromoteScalarType(type, scalar.Type);
            }
            promotedType = type;

            int maxThreadCount = scalars.Max(x => x.ThreadCount);
            RawValue[][] result = new RawValue[maxThreadCount][];
            for (int threadIndex = 0; threadIndex < maxThreadCount; threadIndex++)
            {
                result[threadIndex] = new RawValue[scalars.Length];
                for (int channel = 0; channel < scalars.Length; channel++)
                {
                    var scalar = scalars[channel];
                    if (scalar.Type != type)
                        scalar = (ScalarValue)scalar.Cast(type);
                    result[threadIndex][channel] = scalar.Value.Get(threadIndex);
                }
            }

            if (maxThreadCount == 1)
                return MakeVectorSGPR(result[0]);
            else
                return MakeVectorVGPR(result);
        }

        public static ScalarValue[] RegisterToScalars(ScalarType type, HLSLRegister<RawValue[]> scalars)
        {
            ScalarValue[] scalarValues = new ScalarValue[scalars.Get(0).Length];
            if (scalars.IsUniform)
            {
                for (int i = 0; i < scalarValues.Length; i++)
                {
                    scalarValues[i] = new ScalarValue(type, HLSLValueUtils.MakeScalarSGPR(scalars.UniformValue[i]));
                }
            }
            else
            {
                for (int i = 0; i < scalarValues.Length; i++)
                {
                    RawValue[] perThreadValues = new RawValue[scalars.Size];
                    for (int threadIndex = 0; threadIndex < scalars.Size; threadIndex++)
                    {
                        perThreadValues[threadIndex] = scalars.VaryingValues[threadIndex][i];
                    }
                    scalarValues[i] = new ScalarValue(type, HLSLValueUtils.MakeScalarVGPR(perThreadValues));
                }
            }
            return scalarValues;
        }

        public static HLSLValue MergeThreadValues(HLSLValue[] threadValues)
        {
            if (threadValues.Length == 1)
                return threadValues[0];

            HLSLValue result = Vectorize(threadValues[0], threadValues.Length);
            for (int threadIndex = 1; threadIndex < threadValues.Length; threadIndex++)
            {
                result = SetThreadValue(result, threadIndex, threadValues[threadIndex]);
            }
            return result;
        }

        public static ScalarValue[] FlattenToScalars(HLSLValue value)
        {
            if (value is NumericValue num)
                return num.ToScalars();
            if (value is ArrayValue arr)
                return arr.Values.SelectMany(x => FlattenToScalars(x)).ToArray();
            if (value is StructValue sv)
                return sv.Members.Values.SelectMany(x => FlattenToScalars(x)).ToArray();
            throw new InvalidOperationException();
        }

        public static HLSLValue PackScalars(HLSLInterpreterContext ctx, ScalarValue[] scalars, TypeNode targetType, IEnumerable<ArrayRankNode> arrayRanks = null)
        {
            targetType = ctx.ResolveType(targetType);
            int arrayLen = arrayRanks != null ? HLSLTypeUtils.GetDeclaratorArrayLength(arrayRanks) : 1;
            if (arrayLen > 1)
            {
                int elementSize = scalars.Length / arrayLen;
                var elements = new HLSLValue[arrayLen];
                for (int i = 0; i < arrayLen; i++)
                {
                    elements[i] = PackScalars(ctx, scalars[(i * elementSize)..((i + 1) * elementSize)], targetType);
                }
                return new ArrayValue(elements);
            }
            switch (targetType)
            {
                case ScalarTypeNode st:
                    return scalars[0].Cast(st.Kind);
                case VectorTypeNode vt:
                    return VectorValue.FromScalars(scalars[..vt.Dimension].Select(s => (ScalarValue)s.Cast(vt.Kind)).ToArray());
                case MatrixTypeNode mt:
                    int components = mt.FirstDimension * mt.SecondDimension;
                    return MatrixValue.FromScalars(mt.FirstDimension, mt.SecondDimension, scalars[..components].Select(s => (ScalarValue)s.Cast(mt.Kind)).ToArray());
                case StructTypeNode st:
                {
                    var members = new Dictionary<string, HLSLValue>();
                    int offset = 0;
                    foreach (var (kind, decl) in HLSLTypeUtils.GetStructFields(st, ctx))
                    {
                        int fieldSize = HLSLTypeUtils.GetTypeSizeDwords(ctx, kind, decl.ArrayRanks);
                        members[decl.Name] = PackScalars(ctx, scalars[offset..(offset + fieldSize)], kind, decl.ArrayRanks);
                        offset += fieldSize;
                    }
                    return new StructValue(ctx.GetQualifiedName(st.Name.GetName()), members);
                }
                case NamedTypeNode namedType:
                    return PackScalars(ctx, scalars, ctx.GetStructType(namedType.GetName()));
                case QualifiedNamedTypeNode qualType:
                    return PackScalars(ctx, scalars, ctx.GetStructType(qualType.GetName()));
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
