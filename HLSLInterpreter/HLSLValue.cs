using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSL
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct RawValue : IEquatable<RawValue>
    {
        [FieldOffset(0)] public int Int;
        [FieldOffset(0)] public uint Uint;
        [FieldOffset(0)] public float Float;
        [FieldOffset(0)] public double Double;
        [FieldOffset(0)] public bool Bool;
        [FieldOffset(0)] public char Char;
        [FieldOffset(0)] public long Long;

        public static implicit operator RawValue(int v) { var r = default(RawValue); r.Int = v; return r; }
        public static implicit operator RawValue(uint v) { var r = default(RawValue); r.Uint = v; return r; }
        public static implicit operator RawValue(float v) { var r = default(RawValue); r.Float = v; return r; }
        public static implicit operator RawValue(double v) { var r = default(RawValue); r.Double = v; return r; }
        public static implicit operator RawValue(bool v) { var r = default(RawValue); r.Bool = v; return r; }
        public static implicit operator RawValue(char v) { var r = default(RawValue); r.Char = v; return r; }

        public bool Equals(RawValue other) => Long == other.Long;
        public override bool Equals(object obj) => obj is RawValue other && Long == other.Long;
        public override int GetHashCode() => Long.GetHashCode();
    }

    public readonly struct HLSLRegister<T>
    {
        public readonly T UniformValue;
        public readonly T[] VaryingValues;
        public readonly bool IsVarying;
        public bool IsUniform => !IsVarying;

        public HLSLRegister(T value)
        {
            VaryingValues = null;
            UniformValue = value;
            IsVarying = false;
        }

        public HLSLRegister(T[] values)
        {
            VaryingValues = values;
            UniformValue = default;
            IsVarying = true;
        }

        public HLSLRegister<T> Copy()
        {
            if (IsVarying)
            {
                T[] input = VaryingValues;
                return new HLSLRegister<T>(input.ToArray());
            }
            else
            {
                return new HLSLRegister<T>(UniformValue);
            }
        }

        public HLSLRegister<U> Map<U>(Func<T, U> mapper)
        {
            if (IsVarying)
            {
                T[] input = VaryingValues;
                return new HLSLRegister<U>(input.Select(mapper).ToArray());
            }
            else
            {
                return new HLSLRegister<U>(mapper(UniformValue));
            }
        }

        public HLSLRegister<U> MapThreads<U>(Func<T, int, U> mapper)
        {
            if (IsVarying)
            {
                T[] input = VaryingValues;
                return new HLSLRegister<U>(input.Select(mapper).ToArray());
            }
            else
            {
                return new HLSLRegister<U>(mapper(UniformValue, 0));
            }
        }

        public T Get(int threadIndex)
        {
            if (IsVarying)
                return VaryingValues[threadIndex];
            else
                return UniformValue;
        }

        public HLSLRegister<T> Set(int threadIndex, T value)
        {
            if (IsVarying)
            {
                var valCopy = VaryingValues.ToArray();
                valCopy[threadIndex] = value;
                return new HLSLRegister<T>(valCopy);
            }
            else
            {
                return new HLSLRegister<T>(value);
            }
        }

        public HLSLRegister<T> Vectorize(int threadCount)
        {
            if (IsVarying)
                return new HLSLRegister<T>(VaryingValues.ToArray());
            else
                return new HLSLRegister<T>(Enumerable.Repeat(UniformValue, threadCount).ToArray());
        }

        public HLSLRegister<T> Scalarize(int threadIndex)
        {
            return new HLSLRegister<T>(Get(threadIndex));
        }

        // If all lanes agree, collapse to scalar
        public HLSLRegister<T> Converge()
        {
            if (IsUniform)
                return new HLSLRegister<T>(Get(0));

            T first = Get(0);
            bool allSame = true;
            foreach (T value in VaryingValues)
            {
                if (!EqualityComparer<T>.Default.Equals(first, value))
                {
                    allSame = false;
                    break;
                }
            }

            if (allSame)
                return new HLSLRegister<T>(first);
            else
                return new HLSLRegister<T>(VaryingValues.ToArray());
        }

        public int Size => IsVarying ? VaryingValues.Length : 1;
    }

    public abstract class HLSLValue
    {
        public abstract int ThreadCount { get; }
        public abstract bool IsUniform { get; }
        public bool IsVarying => !IsUniform;

        public abstract HLSLValue Copy();
    }

    // Reference to another value, i.e. refcell (in/inout)
    public class ReferenceValue : HLSLValue
    {
        public readonly Func<HLSLValue> Get;
        public readonly Action<HLSLValue> Set;

        public override int ThreadCount => Get().ThreadCount;
        public override bool IsUniform => Get().IsUniform;

        public override HLSLValue Copy()
        {
            return new ReferenceValue(Get, Set);
        }

        public ReferenceValue(Func<HLSLValue> get, Action<HLSLValue> set)
        {
            Get = get;
            Set = set;
        }

        public override string ToString() => $"Ref({Get()})";
    }

    public sealed class StringValue : HLSLValue
    {
        public readonly string Value;

        public StringValue(string value) { Value = value; }

        public override int ThreadCount => 1;
        public override bool IsUniform => true;

        public override HLSLValue Copy() => new StringValue(Value);

        public override string ToString() => Value;
    }

    public abstract class NumericValue : HLSLValue
    {
        public readonly ScalarType Type;

        public NumericValue(ScalarType type)
        {
            Type = type;
        }

        public abstract (int rows, int columns) TensorSize { get; }

        public abstract VectorValue BroadcastToVector(int size);
        public abstract MatrixValue BroadcastToMatrix(int rows, int columns);
        public abstract NumericValue Cast(ScalarType type);
        public abstract NumericValue Map(Func<RawValue, RawValue> mapper);
        public abstract ScalarValue[] ToScalars();

        public abstract NumericValue Vectorize(int threadCount);
        public abstract NumericValue Scalarize(int threadIndex);

        public static NumericValue operator +(NumericValue left, NumericValue right) => HLSLOperators.Add(left, right);
        public static NumericValue operator -(NumericValue left, NumericValue right) => HLSLOperators.Sub(left, right);
        public static NumericValue operator *(NumericValue left, NumericValue right) => HLSLOperators.Mul(left, right);
        public static NumericValue operator /(NumericValue left, NumericValue right) => HLSLOperators.Div(left, right);
        public static NumericValue operator %(NumericValue left, NumericValue right) => HLSLOperators.Mod(left, right);
        public static NumericValue operator <(NumericValue left, NumericValue right) => HLSLOperators.Less(left, right);
        public static NumericValue operator >(NumericValue left, NumericValue right) => HLSLOperators.Greater(left, right);
        public static NumericValue operator <=(NumericValue left, NumericValue right) => HLSLOperators.LessEqual(left, right);
        public static NumericValue operator >=(NumericValue left, NumericValue right) => HLSLOperators.GreaterEqual(left, right);
        public static NumericValue operator ==(NumericValue left, NumericValue right) => HLSLOperators.Equal(left, right);
        public static NumericValue operator !=(NumericValue left, NumericValue right) => HLSLOperators.NotEqual(left, right);
        public static NumericValue operator ^(NumericValue left, NumericValue right) => HLSLOperators.BitXor(left, right);
        public static NumericValue operator |(NumericValue left, NumericValue right) => HLSLOperators.BitOr(left, right);
        public static NumericValue operator &(NumericValue left, NumericValue right) => HLSLOperators.BitAnd(left, right);
        public static NumericValue operator ~(NumericValue left) => HLSLOperators.BitNot(left);
        public static NumericValue operator !(NumericValue left) => HLSLOperators.BoolNegate(left);
        public static NumericValue operator -(NumericValue left) => HLSLOperators.Negate(left);

        public static implicit operator NumericValue(int v) => (ScalarValue)v;
        public static implicit operator NumericValue(uint v) => (ScalarValue)v;
        public static implicit operator NumericValue(float v) => (ScalarValue)v;
        public static implicit operator NumericValue(double v) => (ScalarValue)v;
        public static implicit operator NumericValue(bool v) => (ScalarValue)v;
        public static implicit operator NumericValue(char v) => (ScalarValue)v;
    }

    public sealed class StructValue : HLSLValue
    {
        public readonly string Name;
        public readonly Dictionary<string, HLSLValue> Members;

        public StructValue(string name, Dictionary<string, HLSLValue> members)
        {
            Name = name;
            Members = members;
        }

        public override int ThreadCount => Members.Max(x => x.Value.ThreadCount);
        public override bool IsUniform => Members.All(x => x.Value.IsUniform);

        public override HLSLValue Copy()
        {
            Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
            foreach (KeyValuePair<string, HLSLValue> member in Members)
            {
                members.Add(member.Key, member.Value.Copy());
            }
            return new StructValue(Name, members);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("struct ");
            sb.AppendLine(Name);
            sb.AppendLine("{");
            foreach (var kvp in Members)
            {
                sb.Append("    ");
                sb.Append(kvp.Key);
                sb.Append(": ");
                sb.Append(Convert.ToString(kvp.Value, CultureInfo.InvariantCulture));
                sb.AppendLine(";");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }

    public sealed class ScalarValue : NumericValue
    {
        public readonly HLSLRegister<RawValue> Value;

        public ScalarValue(ScalarType type, HLSLRegister<RawValue> value)
            : base(type)
        {
            Value = value;
        }

        public override (int rows, int columns) TensorSize => (1, 1);
        public override int ThreadCount => Value.Size;
        public override bool IsUniform => Value.IsUniform;

        public override ScalarValue[] ToScalars()
        {
            return new ScalarValue[] { (ScalarValue)Copy() };
        }

        public override HLSLValue Copy()
        {
            return new ScalarValue(Type, Value.Copy());
        }

        public override MatrixValue BroadcastToMatrix(int rows, int columns)
        {
            return new MatrixValue(Type, rows, columns, Value.Map(x =>
            {
                RawValue[] res = new RawValue[rows * columns];
                Array.Fill(res, x);
                return res;
            }));
        }

        public override VectorValue BroadcastToVector(int size)
        {
            return new VectorValue(Type, Value.Map(x =>
            {
                RawValue[] res = new RawValue[size];
                Array.Fill(res, x);
                return res;
            }));
        }

        public override NumericValue Cast(ScalarType type)
        {
            return new ScalarValue(type, Value.Map(x => HLSLTypeUtils.CastNumeric(type, Type, x)));
        }

        public int AsInt(int threadIndex = 0) => HLSLTypeUtils.CastNumeric(ScalarType.Int, Type, Value.Get(threadIndex)).Int;
        public uint AsUint(int threadIndex = 0) => HLSLTypeUtils.CastNumeric(ScalarType.Uint, Type, Value.Get(threadIndex)).Uint;
        public float AsFloat(int threadIndex = 0) => HLSLTypeUtils.CastNumeric(ScalarType.Float, Type, Value.Get(threadIndex)).Float;
        public double AsDouble(int threadIndex = 0) => HLSLTypeUtils.CastNumeric(ScalarType.Double, Type, Value.Get(threadIndex)).Double;
        public bool AsBool(int threadIndex = 0) => HLSLTypeUtils.CastNumeric(ScalarType.Bool, Type, Value.Get(threadIndex)).Bool;
        public char AsChar(int threadIndex = 0) => HLSLTypeUtils.CastNumeric(ScalarType.Char, Type, Value.Get(threadIndex)).Char;

        public RawValue GetThreadValue(int threadIndex) => Value.Get(threadIndex);

        public override NumericValue Map(Func<RawValue, RawValue> mapper)
        {
            return new ScalarValue(Type, Value.Map(mapper));
        }

        public ScalarValue MapThreads(Func<RawValue, int, RawValue> mapper)
        {
            return new ScalarValue(Type, Value.MapThreads(mapper));
        }

        public override NumericValue Scalarize(int threadIndex)
        {
            return new ScalarValue(Type, Value.Scalarize(threadIndex));
        }

        public override NumericValue Vectorize(int threadCount)
        {
            return new ScalarValue(Type, Value.Vectorize(threadCount));
        }

        public ScalarValue SetThreadValue(int threadIndex, RawValue set)
        {
            return new ScalarValue(Type, Value.Set(threadIndex, set));
        }

        public override string ToString()
        {
            if (Value.IsVarying)
            {
                string[] vals = new string[Value.VaryingValues.Length];
                for (int i = 0; i < vals.Length; i++)
                    vals[i] = $"{ToString(i)}";
                return $"Varying({string.Join(", ", vals)})";
            }
            else
            {
                return ToString(0);
            }
        }
        public string ToString(int threadIndex) => HLSLValueUtils.FormatRawValue(Value.Get(threadIndex), Type);

        public static ScalarValue operator +(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left + (NumericValue)right);
        public static ScalarValue operator -(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left - (NumericValue)right);
        public static ScalarValue operator *(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left * (NumericValue)right);
        public static ScalarValue operator /(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left / (NumericValue)right);
        public static ScalarValue operator %(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left % (NumericValue)right);
        public static ScalarValue operator <(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left < (NumericValue)right);
        public static ScalarValue operator >(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left > (NumericValue)right);
        public static ScalarValue operator <=(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left <= (NumericValue)right);
        public static ScalarValue operator >=(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left >= (NumericValue)right);
        public static ScalarValue operator ==(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left == (NumericValue)right);
        public static ScalarValue operator !=(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left != (NumericValue)right);
        public static ScalarValue operator ^(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left ^ (NumericValue)right);
        public static ScalarValue operator |(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left | (NumericValue)right);
        public static ScalarValue operator &(ScalarValue left, ScalarValue right) => (ScalarValue)((NumericValue)left & (NumericValue)right);
        public static ScalarValue operator ~(ScalarValue left) => (ScalarValue)(~(NumericValue)left);
        public static ScalarValue operator !(ScalarValue left) => (ScalarValue)(!(NumericValue)left);
        public static ScalarValue operator -(ScalarValue left) => (ScalarValue)(-(NumericValue)left);

        public static implicit operator ScalarValue(int v) => new ScalarValue(ScalarType.Int, new HLSLRegister<RawValue>(v));
        public static implicit operator ScalarValue(uint v) => new ScalarValue(ScalarType.Uint, new HLSLRegister<RawValue>(v));
        public static implicit operator ScalarValue(float v) => new ScalarValue(ScalarType.Float, new HLSLRegister<RawValue>(v));
        public static implicit operator ScalarValue(double v) => new ScalarValue(ScalarType.Double, new HLSLRegister<RawValue>(v));
        public static implicit operator ScalarValue(bool v) => new ScalarValue(ScalarType.Bool, new HLSLRegister<RawValue>(v));
        public static implicit operator ScalarValue(char v) => new ScalarValue(ScalarType.Char, new HLSLRegister<RawValue>(v));

        public static ScalarValue Null => new ScalarValue(ScalarType.Void, new HLSLRegister<RawValue>(0));
    }

    public sealed class VectorValue : NumericValue
    {
        public readonly HLSLRegister<RawValue[]> Values;

        public VectorValue(ScalarType type, HLSLRegister<RawValue[]> values)
            : base(type)
        {
            Values = values;
        }

        public int Size => Values.Get(0).Length;
        public override (int rows, int columns) TensorSize => (Size, 1);
        public override int ThreadCount => Values.Size;
        public override bool IsUniform => Values.IsUniform;

        public ScalarValue this[int channel]
        {
            get
            {
                if (IsVarying)
                {
                    RawValue[] perThreadValue = new RawValue[ThreadCount];
                    for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
                        perThreadValue[threadIndex] = Values.Get(threadIndex)[channel];
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarVGPR(perThreadValue));
                }
                else
                {
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarSGPR(Values.UniformValue[channel]));
                }
            }
        }
        public ScalarValue x => this[0];
        public ScalarValue y => this[1];
        public ScalarValue z => this[2];
        public ScalarValue w => this[3];

        public NumericValue Swizzle(string swizzle)
        {
            RawValue[][] perThreadSwizzle = new RawValue[ThreadCount][];
            for (int threadIndex = 0; threadIndex < perThreadSwizzle.Length; threadIndex++)
            {
                perThreadSwizzle[threadIndex] = new RawValue[swizzle.Length];
                for (int component = 0; component < swizzle.Length; component++)
                {
                    perThreadSwizzle[threadIndex][component] = Values.Get(threadIndex)[HLSLValueUtils.VectorSwizzleCharToIndex(swizzle[component])];
                }
            }
            if (ThreadCount == 1)
            {
                if (swizzle.Length == 1)
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarSGPR(perThreadSwizzle[0][0]));
                else
                    return new VectorValue(Type, HLSLValueUtils.MakeVectorSGPR(perThreadSwizzle[0]));
            }
            else
            {
                if (swizzle.Length == 1)
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarVGPR(perThreadSwizzle.Select(x => x[0])));
                else
                    return new VectorValue(Type, HLSLValueUtils.MakeVectorVGPR(perThreadSwizzle));
            }
        }

        public VectorValue SwizzleAssign(string swizzle, NumericValue value)
        {
            int maxThreadCount = Math.Max(ThreadCount, value.ThreadCount);
            RawValue[][] perThreadSwizzle = new RawValue[maxThreadCount][];
            for (int threadIndex = 0; threadIndex < perThreadSwizzle.Length; threadIndex++)
            {
                // Write current values
                perThreadSwizzle[threadIndex] = new RawValue[Size];
                for (int component = 0; component < Size; component++)
                {
                    perThreadSwizzle[threadIndex][component] = Values.Get(threadIndex)[component];
                }

                // Splat swizzle assign
                for (int component = 0; component < swizzle.Length; component++)
                {
                    perThreadSwizzle[threadIndex][HLSLValueUtils.VectorSwizzleCharToIndex(swizzle[component])] = HLSLValueUtils.GetScalarComponent(value, threadIndex, component);
                }
            }
            if (maxThreadCount == 1)
            {
                return new VectorValue(Type, HLSLValueUtils.MakeVectorSGPR(perThreadSwizzle[0]));
            }
            else
            {
                return new VectorValue(Type, HLSLValueUtils.MakeVectorVGPR(perThreadSwizzle));
            }
        }

        public VectorValue ChannelAssign(int channel, NumericValue value)
        {
            return SwizzleAssign(HLSLValueUtils.IndexToVectorSwizzleChar(channel).ToString(), value);
        }

        public static VectorValue FromScalars(params ScalarValue[] scalars)
        {
            var reg = HLSLValueUtils.RegisterFromScalars(scalars, out var promotedType);
            return new VectorValue(promotedType, reg);
        }

        public override ScalarValue[] ToScalars()
        {
            return HLSLValueUtils.RegisterToScalars(Type, Values);
        }

        public override HLSLValue Copy()
        {
            return new VectorValue(Type, Values.Copy());
        }

        public override MatrixValue BroadcastToMatrix(int rows, int columns)
        {
            throw new InvalidOperationException();
        }

        public override VectorValue BroadcastToVector(int size)
        {
            return new VectorValue(Type, Values.Map(x =>
            {
                int sizeDiff = size - x.Length;
                if (sizeDiff > 0) // Expansion
                {
                    RawValue[] res = new RawValue[size];
                    Array.Copy(x, res, x.Length);
                    for (int i = 0; i < sizeDiff; i++)
                        res[x.Length + i] = HLSLTypeUtils.GetZeroValue(Type);
                    return res;
                }
                else if (size < x.Length) // Truncation
                {
                    RawValue[] res = new RawValue[size];
                    Array.Copy(x, res, size);
                    return res;
                }
                else
                    return x;
            }));
        }

        public override NumericValue Cast(ScalarType type)
        {
            return new VectorValue(type, Values.Map(x =>
            {
                RawValue[] res = new RawValue[x.Length];
                for (int i = 0; i < res.Length; i++)
                    res[i] = HLSLTypeUtils.CastNumeric(type, Type, x[i]);
                return res;
            }));
        }

        public override NumericValue Map(Func<RawValue, RawValue> mapper)
        {
            return new VectorValue(Type, Values.Map(x =>
            {
                RawValue[] res = new RawValue[x.Length];
                for (int i = 0; i < res.Length; i++)
                    res[i] = mapper(x[i]);
                return res;
            }));
        }

        public VectorValue MapThreads(Func<RawValue[], int, RawValue[]> mapper)
        {
            return new VectorValue(Type, Values.MapThreads(mapper));
        }

        public override NumericValue Scalarize(int threadIndex)
        {
            return new VectorValue(Type, Values.Scalarize(threadIndex));
        }

        public override NumericValue Vectorize(int threadCount)
        {
            return new VectorValue(Type, Values.Vectorize(threadCount));
        }

        public RawValue[] GetThreadValue(int threadIndex) => Values.Get(threadIndex);

        public VectorValue SetThreadValue(int threadIndex, RawValue[] set)
        {
            return new VectorValue(Type, Values.Set(threadIndex, set));
        }

        public override string ToString()
        {
            if (Values.IsVarying)
            {
                string[] vals = new string[Values.VaryingValues.Length];
                for (int i = 0; i < vals.Length; i++)
                    vals[i] = $"{ToString(i)}";
                return $"Varying({string.Join(", ", vals)})";
            }
            else
            {
                return ToString(0);
            }
        }
        public string ToString(int threadIndex)
        {
            string type = PrintingUtil.GetEnumName(Type);
            return $"{type}{Size}({HLSLValueUtils.FormatRawValues(Values.Get(threadIndex), Type)})";
        }

        public static VectorValue operator +(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left + (NumericValue)right);
        public static VectorValue operator -(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left - (NumericValue)right);
        public static VectorValue operator *(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left * (NumericValue)right);
        public static VectorValue operator /(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left / (NumericValue)right);
        public static VectorValue operator %(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left % (NumericValue)right);
        public static VectorValue operator <(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left < (NumericValue)right);
        public static VectorValue operator >(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left > (NumericValue)right);
        public static VectorValue operator <=(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left <= (NumericValue)right);
        public static VectorValue operator >=(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left >= (NumericValue)right);
        public static VectorValue operator ==(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left == (NumericValue)right);
        public static VectorValue operator !=(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left != (NumericValue)right);
        public static VectorValue operator ^(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left ^ (NumericValue)right);
        public static VectorValue operator |(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left | (NumericValue)right);
        public static VectorValue operator &(VectorValue left, VectorValue right) => (VectorValue)((NumericValue)left & (NumericValue)right);
        public static VectorValue operator ~(VectorValue left) => (VectorValue)(~(NumericValue)left);
        public static VectorValue operator !(VectorValue left) => (VectorValue)(!(NumericValue)left);
        public static VectorValue operator -(VectorValue left) => (VectorValue)(-(NumericValue)left);
    }

    public sealed class MatrixValue : NumericValue
    {
        public readonly int Rows;
        public readonly int Columns;
        public readonly HLSLRegister<RawValue[]> Values;

        public MatrixValue(ScalarType type, int rows, int columns, HLSLRegister<RawValue[]> values)
            : base(type)
        {
            Rows = rows;
            Columns = columns;
            Values = values;
        }

        public override (int rows, int columns) TensorSize => (Rows, Columns);
        public override int ThreadCount => Values.Size;
        public override bool IsUniform => Values.IsUniform;

        public ScalarValue this[int channel]
        {
            get
            {
                if (IsVarying)
                {
                    RawValue[] perThreadValue = new RawValue[ThreadCount];
                    for (int threadIndex = 0; threadIndex < ThreadCount; threadIndex++)
                        perThreadValue[threadIndex] = Values.Get(threadIndex)[channel];
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarVGPR(perThreadValue));
                }
                else
                {
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarSGPR(Values.UniformValue[channel]));
                }
            }
        }

        public ScalarValue this[int row, int col] => this[row * Columns + col];

        public static MatrixValue FromScalars(int rows, int columns, params ScalarValue[] scalars)
        {
            var reg = HLSLValueUtils.RegisterFromScalars(scalars, out var promotedType);
            return new MatrixValue(promotedType, rows, columns, reg);
        }

        public NumericValue Swizzle(string swizzle)
        {
            int[] indices = HLSLValueUtils.MatrixSwizzleStringToIndices(swizzle, Columns);
            RawValue[][] perThreadSwizzle = new RawValue[ThreadCount][];
            for (int threadIndex = 0; threadIndex < perThreadSwizzle.Length; threadIndex++)
            {
                perThreadSwizzle[threadIndex] = new RawValue[indices.Length];
                for (int component = 0; component < indices.Length; component++)
                    perThreadSwizzle[threadIndex][component] = Values.Get(threadIndex)[indices[component]];
            }
            if (ThreadCount == 1)
            {
                if (indices.Length == 1)
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarSGPR(perThreadSwizzle[0][0]));
                else
                    return new VectorValue(Type, HLSLValueUtils.MakeVectorSGPR(perThreadSwizzle[0]));
            }
            else
            {
                if (indices.Length == 1)
                    return new ScalarValue(Type, HLSLValueUtils.MakeScalarVGPR(perThreadSwizzle.Select(x => x[0])));
                else
                    return new VectorValue(Type, HLSLValueUtils.MakeVectorVGPR(perThreadSwizzle));
            }
        }

        public MatrixValue SwizzleAssign(string swizzle, NumericValue value)
        {
            int[] indices = HLSLValueUtils.MatrixSwizzleStringToIndices(swizzle, Columns);
            int size = Rows * Columns;
            int maxThreadCount = Math.Max(ThreadCount, value.ThreadCount);
            RawValue[][] perThreadSwizzle = new RawValue[maxThreadCount][];
            for (int threadIndex = 0; threadIndex < perThreadSwizzle.Length; threadIndex++)
            {
                // Write current values
                perThreadSwizzle[threadIndex] = new RawValue[size];
                for (int component = 0; component < size; component++)
                    perThreadSwizzle[threadIndex][component] = Values.Get(threadIndex)[component];

                // Splat swizzle assign
                for (int component = 0; component < indices.Length; component++)
                    perThreadSwizzle[threadIndex][indices[component]] = HLSLValueUtils.GetScalarComponent(value, threadIndex, component);
            }
            if (maxThreadCount == 1)
                return new MatrixValue(Type, Rows, Columns, HLSLValueUtils.MakeVectorSGPR(perThreadSwizzle[0]));
            else
                return new MatrixValue(Type, Rows, Columns, HLSLValueUtils.MakeVectorVGPR(perThreadSwizzle));
        }

        public override ScalarValue[] ToScalars()
        {
            return HLSLValueUtils.RegisterToScalars(Type, Values);
        }

        public override HLSLValue Copy()
        {
            return new MatrixValue(Type, Rows, Columns, Values.Copy());
        }

        public override MatrixValue BroadcastToMatrix(int rows, int columns)
        {
            if (Rows != rows || Columns != columns)
            {
                var scalars = ToScalars();
                ScalarValue[] newScalars = new ScalarValue[rows * columns];
                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < columns; col++)
                    {
                        if (row < Rows && col < Columns)
                            newScalars[row * columns + col] = scalars[row * Columns + col];
                        else
                            newScalars[row * columns + col] = (ScalarValue)HLSLTypeUtils.GetZeroValue(scalars[0]);
                    }
                }
                return FromScalars(rows, columns, newScalars);
            }
            return (MatrixValue)Copy();
        }

        public override VectorValue BroadcastToVector(int size)
        {
            throw new InvalidOperationException();
        }

        public override NumericValue Cast(ScalarType type)
        {
            return new MatrixValue(type, Rows, Columns, Values.Map(x =>
            {
                RawValue[] res = new RawValue[x.Length];
                for (int i = 0; i < res.Length; i++)
                    res[i] = HLSLTypeUtils.CastNumeric(type, Type, x[i]);
                return res;
            }));
        }

        public override NumericValue Map(Func<RawValue, RawValue> mapper)
        {
            return new MatrixValue(Type, Rows, Columns, Values.Map(x =>
            {
                RawValue[] res = new RawValue[x.Length];
                for (int i = 0; i < res.Length; i++)
                    res[i] = mapper(x[i]);
                return res;
            }));
        }

        public MatrixValue MapThreads(Func<RawValue[], int, RawValue[]> mapper)
        {
            return new MatrixValue(Type, Rows, Columns, Values.MapThreads(mapper));
        }

        public override NumericValue Scalarize(int threadIndex)
        {
            return new MatrixValue(Type, Rows, Columns, Values.Scalarize(threadIndex));
        }

        public override NumericValue Vectorize(int threadCount)
        {
            return new MatrixValue(Type, Rows, Columns, Values.Vectorize(threadCount));
        }

        public RawValue[] GetThreadValue(int threadIndex) => Values.Get(threadIndex);

        public MatrixValue SetThreadValue(int threadIndex, RawValue[] set)
        {
            return new MatrixValue(Type, Rows, Columns, Values.Set(threadIndex, set));
        }

        public override string ToString()
        {
            if (Values.IsVarying)
            {
                string[] vals = new string[Values.VaryingValues.Length];
                for (int i = 0; i < vals.Length; i++)
                    vals[i] = $"{ToString(i)}";
                return $"Varying({string.Join(", ", vals)})";
            }
            else
            {
                return ToString(0);
            }
        }
        public string ToString(int threadIndex)
        {
            string type = PrintingUtil.GetEnumName(Type);
            return $"{type}{Rows}x{Columns}({HLSLValueUtils.FormatRawValues(Values.Get(threadIndex), Type)})";
        }

        public static MatrixValue operator +(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left + (NumericValue)right);
        public static MatrixValue operator -(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left - (NumericValue)right);
        public static MatrixValue operator *(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left * (NumericValue)right);
        public static MatrixValue operator /(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left / (NumericValue)right);
        public static MatrixValue operator %(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left % (NumericValue)right);
        public static MatrixValue operator <(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left < (NumericValue)right);
        public static MatrixValue operator >(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left > (NumericValue)right);
        public static MatrixValue operator <=(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left <= (NumericValue)right);
        public static MatrixValue operator >=(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left >= (NumericValue)right);
        public static MatrixValue operator ==(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left == (NumericValue)right);
        public static MatrixValue operator !=(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left != (NumericValue)right);
        public static MatrixValue operator ^(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left ^ (NumericValue)right);
        public static MatrixValue operator |(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left | (NumericValue)right);
        public static MatrixValue operator &(MatrixValue left, MatrixValue right) => (MatrixValue)((NumericValue)left & (NumericValue)right);
        public static MatrixValue operator ~(MatrixValue left) => (MatrixValue)(~(NumericValue)left);
        public static MatrixValue operator !(MatrixValue left) => (MatrixValue)(!(NumericValue)left);
        public static MatrixValue operator -(MatrixValue left) => (MatrixValue)(-(NumericValue)left);
    }

    public class PredefinedObjectValue : HLSLValue
    {
        public readonly PredefinedObjectType Type;
        public readonly TypeNode[] TemplateArguments;

        public PredefinedObjectValue(PredefinedObjectType type, TypeNode[] templateArguments)
        {
            Type = type;
            TemplateArguments = templateArguments;
        }
        public override int ThreadCount => 1;
        public override bool IsUniform => true;

        public override HLSLValue Copy()
        {
            return new PredefinedObjectValue(Type, TemplateArguments);
        }

        public override string ToString()
        {
            string type = PrintingUtil.GetEnumName(Type);
            if (TemplateArguments == null || TemplateArguments.Length == 0)
                return type;
            return $"{type}<{string.Join(", ", TemplateArguments.Select(x => x.GetPrettyPrintedCode()))}>";
        }
    }

    public delegate HLSLValue ResourceGetter(int x, int y, int z, int w, int mipLevel);
    public delegate void ResourceSetter(int x, int y, int z, int w, int mipLevel, HLSLValue value);

    public sealed class ResourceValue : PredefinedObjectValue
    {
        public readonly ResourceGetter Get;
        public readonly ResourceSetter Set;

        private readonly Func<int> GetSizeX;
        private readonly Func<int> GetSizeY;
        private readonly Func<int> GetSizeZ;
        private readonly Func<int> GetMipCount;

        public int SizeX    => GetSizeX();
        public int SizeY    => GetSizeY();
        public int SizeZ    => GetSizeZ();
        public int MipCount => GetMipCount();

        // For StructuredBuffer
        public readonly int Stride;

        // Atomic counter Append/Consume buffer
        public int Counter;

        public ResourceValue(PredefinedObjectType type, TypeNode[] templateArguments, int stride, Func<int> getSizeX, Func<int> getSizeY, Func<int> getSizeZ, Func<int> getMipCount, ResourceGetter get, ResourceSetter set)
            : base(type, templateArguments)
        {
            Stride = stride;
            GetSizeX = getSizeX;
            GetSizeY = getSizeY;
            GetSizeZ = getSizeZ;
            GetMipCount = getMipCount;
            Get = get;
            Set = set;
        }

        public ResourceValue(PredefinedObjectType type, TypeNode[] templateArguments, int stride, int sizeX, int sizeY, int sizeZ, int mipCount, ResourceGetter get, ResourceSetter set)
            : this(type, templateArguments, stride, () => sizeX, () => sizeY, () => sizeZ, () => mipCount, get, set) { }

        public override HLSLValue Copy()
        {
            return new ResourceValue(Type, TemplateArguments, Stride, GetSizeX, GetSizeY, GetSizeZ, GetMipCount, Get, Set);
        }

        public bool IsWriteable => HLSLSyntaxFacts.IsWriteable(Type);
        public bool IsArray => HLSLSyntaxFacts.IsArray(Type);
        public bool IsTexture => HLSLSyntaxFacts.IsTexture(Type);
        public bool IsBuffer => HLSLSyntaxFacts.IsBuffer(Type);
        public bool IsCube => Type == PredefinedObjectType.TextureCube || Type == PredefinedObjectType.SamplerCube || Type == PredefinedObjectType.TextureCubeArray;
        public int Dimension => HLSLSyntaxFacts.GetDimension(Type);
    }

    public sealed class SamplerStateValue : PredefinedObjectValue
    {
        public enum FilterMode
        {
            MinMagMipPoint,
            MinMagPointMipLinear,
            MinPointMagLinearMipPoint,
            MinPointMagMipLinear,
            MinLinearMagMipPoint,
            MinLinearMagPointMipLinear,
            MinMagLinearMipPoint,
            MinMagMipLinear,
            Anisotropic,
            ComparisonMinMagMipPoint,
            ComparisonMinMagPointMipLinear,
            ComparisonMinPointMagLinearMipPoint,
            ComparisonMinPointMagMipLinear,
            ComparisonMinLinearMagMipPoint,
            ComparisonMinLinearMagPointMipLinear,
            ComparisonMinMagLinearMipPoint,
            ComparisonMinMagMipLinear,
            ComparisonAnisotropic,
        }

        public enum TextureAddressMode
        {
            Wrap,
            Mirror,
            Clamp,
            Border,
            MirrorOnce
        }

        public enum ComparisonMode
        {
            Never,
            Less,
            Equal,
            LessEqual,
            Greater,
            NotEqual,
            GreaterEqual,
            Always
        }

        public FilterMode Filter { get; set; }
        public TextureAddressMode AddressU { get; set; }
        public TextureAddressMode AddressV { get; set; }
        public TextureAddressMode AddressW { get; set; }
        public float MinimumLod { get; set; } = 0f;
        public float MaximumLod { get; set; } = float.MaxValue;
        public float MipLodBias { get; set; } = 0f;
        public int MaximumAnisotropy { get; set; }
        public ComparisonMode Comparison { get; set; }
        public (float r, float g, float b, float a) BorderColor { get; set; }

        public SamplerStateValue(bool isComparison = false)
            : base(isComparison ? PredefinedObjectType.SamplerComparisonState : PredefinedObjectType.SamplerState, Array.Empty<TypeNode>())
        {
        }

        public override HLSLValue Copy()
        {
            return new SamplerStateValue(Type == PredefinedObjectType.SamplerComparisonState)
            {
                Filter = Filter,
                AddressU = AddressU,
                AddressV = AddressV,
                AddressW = AddressW,
                MinimumLod = MinimumLod,
                MaximumLod = MaximumLod,
                MipLodBias = MipLodBias,
                MaximumAnisotropy = MaximumAnisotropy,
                Comparison = Comparison,
                BorderColor = BorderColor,
            };
        }
    }

    public sealed class ArrayValue : HLSLValue
    {
        public readonly HLSLValue[] Values;

        public override int ThreadCount => Values.Max(x => x.ThreadCount);
        public override bool IsUniform => Values.All(x => x.IsUniform);

        public override HLSLValue Copy()
        {
            return new ArrayValue(Values.Select(x => x.Copy()).ToArray());
        }

        public ArrayValue(HLSLValue[] values)
        {
            Values = values;
        }

        public override string ToString()
        {
            return string.Join(", ", (IEnumerable<HLSLValue>)Values);
        }
    }

}
