using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityShaderParser.HLSL;

namespace HLSL
{
    public static partial class HLSLIntrinsics
    {
        #region Helpers
        public static bool TryInvokeIntrinsic(HLSLExecutionState executionState, string name, HLSLValue[] args, out HLSLValue result)
        {
            if (TryInvokeBasicIntrinsic(name, args, out result))
                return true;

            if (TryInvokeExecutionStateIntrinsic(executionState, name, args, out result))
                return true;

            return false;
        }

        private static void CheckArity(string name, HLSLValue[] args, int arity)
        {
            if (arity >= 0 && args.Length != arity)
                throw new ArgumentException($"Expected {arity} arguments for builtin '{name}', but got '{args.Length}'.");
        }

        private static void CheckNumeric(string name, HLSLValue[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                bool isNumeric = args[i] is NumericValue;
                bool isRefNumeric = args[i] is ReferenceValue refVal && refVal.Get() is NumericValue;
                if (!isNumeric && !isRefNumeric)
                    throw new ArgumentException($"Expected argument in position '{i}' to builtin '{name}' to be a numeric value.");
            }
        }

        private static ScalarValue CastToScalar(NumericValue v, int size = 1)
        {
            if (v is VectorValue vec)
                return vec.x;
            if (v is MatrixValue mat)
                return mat[0, 0];
            return (ScalarValue)v;
        }

        private static VectorValue CastToVector(NumericValue v, int size = 1)
        {
            if (v is VectorValue vec)
                return vec;
            else
                return v.BroadcastToVector(size);
        }

        private static MatrixValue CastToMatrix(NumericValue v, int rows = 1, int cols = 1)
        {
            if (v is MatrixValue mat)
                return mat;
            else
                return v.BroadcastToMatrix(rows, cols);
        }

        private static NumericValue ToFloatLike(NumericValue value)
        {
            if (value.Type == ScalarType.Float)
                return value;
            if (HLSLTypeUtils.IsFloat(value.Type) && value.Type != ScalarType.Double)
                return value;
            return value.Cast(ScalarType.Float);
        }

        private static RawValue Min(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return Math.Min(left.Float, right.Float);
            if (HLSLTypeUtils.IsInt(type)) return Math.Min(left.Int, right.Int);
            if (HLSLTypeUtils.IsUint(type)) return Math.Min(left.Uint, right.Uint);
            if (type == ScalarType.Double) return Math.Min(left.Double, right.Double);
            if (HLSLTypeUtils.IsFloat(type)) return Math.Min(left.Float, right.Float);
            if (type == ScalarType.Bool) return Math.Min(left.Bool ? 1 : 0, right.Bool ? 1 : 0) != 0;
            throw new InvalidOperationException();
        }

        private static RawValue Max(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return Math.Max(left.Float, right.Float);
            if (HLSLTypeUtils.IsInt(type)) return Math.Max(left.Int, right.Int);
            if (HLSLTypeUtils.IsUint(type)) return Math.Max(left.Uint, right.Uint);
            if (type == ScalarType.Double) return Math.Max(left.Double, right.Double);
            if (HLSLTypeUtils.IsFloat(type)) return Math.Max(left.Float, right.Float);
            if (type == ScalarType.Bool) return Math.Max(left.Bool ? 1 : 0, right.Bool ? 1 : 0) != 0;
            throw new InvalidOperationException();
        }
        #endregion

        #region Basic intrinsics
        private static readonly HashSet<string> unsupportedIntrinsics = new HashSet<string>()
        {
            "EvaluateAttributeCentroid",
            "EvaluateAttributeAtSample",
            "EvaluateAttributeSnapped",
            "Process2DQuadTessFactorsAvg",
            "Process2DQuadTessFactorsMax",
            "Process2DQuadTessFactorsMin",
            "ProcessIsolineTessFactors",
            "ProcessQuadTessFactorsAvg",
            "ProcessQuadTessFactorsMax",
            "ProcessQuadTessFactorsMin",
            "ProcessTriTessFactorsAvg",
            "ProcessTriTessFactorsMax",
            "ProcessTriTessFactorsMin",
        };

        public static bool IsUnsupportedIntrinsic(string name) => unsupportedIntrinsics.Contains(name);

        private delegate HLSLValue BasicIntrinsic(HLSLValue[] args);

        private static (int arity, BasicIntrinsic) N1(Func<NumericValue, NumericValue> fn) =>
            (1, args => fn((NumericValue)args[0]));

        private static (int arity, BasicIntrinsic) N2(Func<NumericValue, NumericValue, NumericValue> fn) =>
            (2, args => fn((NumericValue)args[0], (NumericValue)args[1]));

        private static (int arity, BasicIntrinsic) N3(Func<NumericValue, NumericValue, NumericValue, NumericValue> fn) =>
            (3, args => fn((NumericValue)args[0], (NumericValue)args[1], (NumericValue)args[2]));

        private static readonly Dictionary<string, (int arity, BasicIntrinsic fn)> basicIntrinsics = new Dictionary<string, (int arity, BasicIntrinsic fn)>()
        {

            // Need texture support:
            //tex1/2/3D/CUBE
            //tex1/2/3D/CUBEbias
            //tex1/2/3D/CUBEgrad
            //tex1/2/3D/CUBElod
            //tex1/2/3D/CUBEproj

            ["abs"] = N1(Abs),
            ["acos"] = N1(Acos),
            ["all"] = N1(All),
            ["any"] = N1(Any),
            ["asdouble"] = N2(Asdouble),
            ["asfloat"] = N1(Asfloat),
            ["asin"] = N1(Asin),
            ["asint"] = N1(Asint),
            ["atan"] = N1(Atan),
            ["atan2"] = N2(Atan2),
            ["ceil"] = N1(Ceil),
            ["CheckAccessFullyMapped"] = N1(CheckAccessFullyMapped),
            ["clamp"] = N3(Clamp),
            ["cos"] = N1(Cos),
            ["cosh"] = N1(Cosh),
            ["countbits"] = N1(Countbits),
            ["cross"] = N2(Cross),
            ["D3DCOLORtoUBYTE4"] = N1(D3DCOLORtoUBYTE4),
            ["degrees"] = N1(Degrees),
            ["determinant"] = N1(Determinant),
            ["distance"] = N2(Distance),
            ["dot"] = N2(Dot),
            ["dst"] = N2(Dst),
            ["exp"] = N1(Exp),
            ["exp2"] = N1(Exp2),
            ["f16tof32"] = N1(F16tof32),
            ["f32tof16"] = N1(F32tof16),
            ["faceforward"] = N3(Faceforward),
            ["firstbithigh"] = N1(Firstbithigh),
            ["firstbitlow"] = N1(Firstbitlow),
            ["floor"] = N1(Floor),
            ["fma"] = N3(Fma),
            ["fmod"] = N2(Fmod),
            ["frac"] = N1(Frac),
            ["isfinite"] = N1(Isfinite),
            ["isinf"] = N1(Isinf),
            ["isnan"] = N1(Isnan),
            ["isnormal"] = N1(Isnormal),
            ["ldexp"] = N2(Ldexp),
            ["length"] = N1(Length),
            ["lerp"] = N3(Lerp),
            ["lit"] = N3(Lit),
            ["log"] = N1(Log),
            ["log10"] = N1(Log10),
            ["log2"] = N1(Log2),
            ["mad"] = N3(Mad),
            ["max"] = N2(Max),
            ["min"] = N2(Min),
            ["msad4"] = N3(Msad4),
            ["mul"] = N2(Mul),
            ["noise"] = N1(Noise),
            ["normalize"] = N1(Normalize),
            ["pow"] = N2(Pow),
            ["radians"] = N1(Radians),
            ["rcp"] = N1(Rcp),
            ["reflect"] = N2(Reflect),
            ["refract"] = N3(Refract),
            ["reversebits"] = N1(Reversebits),
            ["round"] = N1(Round),
            ["rsqrt"] = N1(Rsqrt),
            ["saturate"] = N1(Saturate),
            ["sign"] = N1(Sign),
            ["sin"] = N1(Sin),
            ["sinh"] = N1(Sinh),
            ["smoothstep"] = N3(Smoothstep),
            ["sqrt"] = N1(Sqrt),
            ["step"] = N2(Step),
            ["tan"] = N1(Tan),
            ["tanh"] = N1(Tanh),
            ["transpose"] = N1(Transpose),
            ["trunc"] = N1(Trunc),

            // Inout intrinsics
            ["modf"] = (2, args => Modf((NumericValue)args[0], (ReferenceValue)args[1])),
            ["frexp"] = (2, args => Frexp((NumericValue)args[0], (ReferenceValue)args[1])),
            ["asuint"] = (-1, args =>
            {
                if (args.Length == 0 || args.Length > 3)
                    throw new ArgumentException($"Expected 1-3 arguments for builtin 'asuint', but got '{args.Length}'.");

                if (args.Length > 1)
                {
                    Asuint((NumericValue)args[0], (ReferenceValue)args[1], (ReferenceValue)args[2]);
                    return ScalarValue.Null;
                }
                return Asuint((NumericValue)args[0]);
            }),
            ["sincos"] = (3, args =>
            {
                Sincos((NumericValue)args[0], (ReferenceValue)args[1], (ReferenceValue)args[2]);
                return ScalarValue.Null;
            }),
        };

        public static bool IsIntrinsicInoutParameter(string name, int paramIndex)
        {
            switch (name)
            {
                case "modf" when paramIndex == 1: return true;
                case "frexp" when paramIndex == 1: return true;
                case "sincos" when paramIndex == 1 || paramIndex == 2: return true;
                case "asuint" when paramIndex == 1 || paramIndex == 2: return true;
                case "InterlockedAdd":
                case "InterlockedMin":
                case "InterlockedMax":
                case "InterlockedAnd":
                case "InterlockedOr":
                case "InterlockedXor":
                    return paramIndex == 0 || paramIndex == 2;
                case "InterlockedExchange":
                    return paramIndex == 0 || paramIndex == 2;
                case "InterlockedCompareStore":
                case "InterlockedCompareStoreFloatBitwise":
                    return paramIndex == 0;
                case "InterlockedCompareExchange":
                case "InterlockedCompareExchangeFloatBitwise":
                    return paramIndex == 0 || paramIndex == 3;
                default: return false;
            }
        }

        public static bool TryInvokeBasicIntrinsic(string name, HLSLValue[] args, out HLSLValue result)
        {
            if (!basicIntrinsics.TryGetValue(name, out var entry))
            {
                result = null;
                return false;
            }

            CheckArity(name, args, entry.arity);
            CheckNumeric(name, args);

            result = entry.fn(args);
            return true;
        }

        // https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-lerp
        public static NumericValue Lerp(NumericValue x, NumericValue y, NumericValue s)
        {
            return x * (1 - s) + y * s;
        }

        public static NumericValue Exp(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Exp(val.Float));
        }

        public static NumericValue Exp2(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Pow(2.0f, val.Float));
        }

        public static NumericValue Abs(NumericValue x)
        {
            if (HLSLTypeUtils.IsInt(x.Type))
                return x.Map(val => Math.Abs(val.Int));

            if (HLSLTypeUtils.IsUint(x.Type))
                return x;

            return ToFloatLike(x).Map(val => Math.Abs(val.Float));
        }

        public static NumericValue Acos(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Acos(val.Float));
        }

        public static NumericValue All(NumericValue x)
        {
            var scalars = x.ToScalars();
            if (scalars.Length == 0) return true;

            var acc = scalars[0].Cast(ScalarType.Bool);
            foreach (var scalar in scalars)
            {
                acc = HLSLOperators.BoolAnd(acc, scalar.Cast(ScalarType.Bool));
            }
            return acc;
        }

        public static NumericValue Any(NumericValue x)
        {
            var scalars = x.ToScalars();
            if (scalars.Length == 0) return true;

            var acc = scalars[0].Cast(ScalarType.Bool);
            foreach (var scalar in scalars)
            {
                acc = HLSLOperators.BoolOr(acc, scalar.Cast(ScalarType.Bool));
            }
            return acc;
        }

        public static NumericValue Asdouble(NumericValue lowbits, NumericValue highbits)
        {
            ScalarValue lowbitsScalar = (ScalarValue)lowbits.Cast(ScalarType.Uint);
            ScalarValue highbitsScalar = (ScalarValue)highbits.Cast(ScalarType.Uint);

            ScalarValue result = (ScalarValue)0.0;
            return result.MapThreads((_, threadIndex) =>
            {
                var low = lowbitsScalar.AsUint(threadIndex);
                var high = highbitsScalar.AsUint(threadIndex);
                return BitConverter.ToDouble(BitConverter.GetBytes(low).Concat(BitConverter.GetBytes(high)).ToArray());
            });
        }

        public static NumericValue Asfloat(NumericValue x)
        {
            if (HLSLTypeUtils.IsInt(x.Type) || HLSLTypeUtils.IsUint(x.Type))
            {
                // Bit-reinterpret int/uint bits as float by re-tagging the register.
                if (x is ScalarValue sv) return new ScalarValue(ScalarType.Float, sv.Value);
                if (x is VectorValue vv) return new VectorValue(ScalarType.Float, vv.Values);
                if (x is MatrixValue mv) return new MatrixValue(ScalarType.Float, mv.Rows, mv.Columns, mv.Values);
                throw new InvalidOperationException();
            }
            return x.Cast(ScalarType.Float);
        }

        public static NumericValue Asint(NumericValue x)
        {
            if (HLSLTypeUtils.IsUint(x.Type) || x.Type == ScalarType.Float || x.Type == ScalarType.Half)
            {
                // Bit-reinterpret uint/float bits as int by re-tagging the register.
                if (x is ScalarValue sv) return new ScalarValue(ScalarType.Int, sv.Value);
                if (x is VectorValue vv) return new VectorValue(ScalarType.Int, vv.Values);
                if (x is MatrixValue mv) return new MatrixValue(ScalarType.Int, mv.Rows, mv.Columns, mv.Values);
                throw new InvalidOperationException();
            }
            return x.Cast(ScalarType.Int);
        }

        public static NumericValue Asuint(NumericValue x)
        {
            if (HLSLTypeUtils.IsInt(x.Type) || x.Type == ScalarType.Float || x.Type == ScalarType.Half)
            {
                // Bit-reinterpret int/float bits as uint by re-tagging the register.
                if (x is ScalarValue sv) return new ScalarValue(ScalarType.Uint, sv.Value);
                if (x is VectorValue vv) return new VectorValue(ScalarType.Uint, vv.Values);
                if (x is MatrixValue mv) return new MatrixValue(ScalarType.Uint, mv.Rows, mv.Columns, mv.Values);
                throw new InvalidOperationException();
            }
            return x.Cast(ScalarType.Uint);
        }

        public static NumericValue Asin(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Asin(val.Float));
        }

        public static NumericValue Atan(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Atan(val.Float));
        }

        public static NumericValue Atan2(NumericValue y, NumericValue x)
        {
            x = ToFloatLike(x);
            y = ToFloatLike(y);

            return
            Select(x > 0, Atan(y / x),
            Select(HLSLOperators.BoolAnd(x < 0, y >= 0), Atan(y / x) + MathF.PI,
            Select(HLSLOperators.BoolAnd(x < 0, y < 0), Atan(y / x) - MathF.PI,
            Select(HLSLOperators.BoolAnd(x == 0, y > 0), MathF.PI / 2.0f,
            Select(HLSLOperators.BoolAnd(x == 0, y < 0), -MathF.PI / 2.0f,
            0)))));
        }

        public static NumericValue Select(NumericValue cond, NumericValue a, NumericValue b)
        {
            // HLSL allows non-bool cond (e.g. (0.5f ? a : b)); coerce so .Bool reads are safe.
            cond = cond.Cast(ScalarType.Bool);

            // Match thread count all args
            (cond, a) = HLSLTypeUtils.PromoteThreadCount(cond, a);
            (cond, b) = HLSLTypeUtils.PromoteThreadCount(cond, b);

            // Match shape of all args
            {
                bool needMatrix = cond is MatrixValue || a is MatrixValue || b is MatrixValue;
                bool needVector = cond is VectorValue || a is VectorValue || b is VectorValue;

                if (needMatrix)
                {
                    (int condRows, int condColumns) = cond.TensorSize;
                    (int aRows, int aColumns) = a.TensorSize;
                    (int bRows, int bColumns) = b.TensorSize;
                    int newRows = Math.Max(condRows, Math.Max(aRows, bRows));
                    int newColumns = Math.Max(condColumns, Math.Max(aColumns, bColumns));
                    if (condRows != newRows || condColumns != newColumns)
                        cond = cond.BroadcastToMatrix(newRows, newColumns);
                    if (aRows != newRows || aColumns != newColumns)
                        a = a.BroadcastToMatrix(newRows, newColumns);
                    if (bRows != newRows || bColumns != newColumns)
                        b = b.BroadcastToMatrix(newRows, newColumns);
                }

                else if (needVector)
                {
                    (int condSize, _) = cond.TensorSize;
                    (int aSize, _) = a.TensorSize;
                    (int bSize, _) = b.TensorSize;
                    int newSize = Math.Max(condSize, Math.Max(aSize, bSize));
                    if (condSize != newSize)
                        cond = cond.BroadcastToVector(newSize);
                    if (aSize != newSize)
                        a = a.BroadcastToVector(newSize);
                    if (bSize != newSize)
                        b = b.BroadcastToVector(newSize);
                }
            }

            // Match types of branches
            (a, b) = HLSLTypeUtils.PromoteType(a, b, false);

            ScalarType resultType = a.Type;
            if (cond is ScalarValue condS)
            {
                var aS = (ScalarValue)a;
                var bS = (ScalarValue)b;
                return new ScalarValue(resultType, condS.Value.MapThreads((c, i) => c.Bool ? aS.GetThreadValue(i) : bS.GetThreadValue(i)));
            }
            if (cond is VectorValue condV)
            {
                var aV = (VectorValue)a;
                var bV = (VectorValue)b;
                return new VectorValue(resultType, condV.Values.MapThreads((c, i) =>
                {
                    var aArr = aV.GetThreadValue(i);
                    var bArr = bV.GetThreadValue(i);
                    var r = new RawValue[c.Length];
                    for (int j = 0; j < c.Length; j++)
                        r[j] = c[j].Bool ? aArr[j] : bArr[j];
                    return r;
                }));
            }
            if (cond is MatrixValue condM)
            {
                var aM = (MatrixValue)a;
                var bM = (MatrixValue)b;
                return new MatrixValue(resultType, condM.Rows, condM.Columns, condM.Values.MapThreads((c, i) =>
                {
                    var aArr = aM.GetThreadValue(i);
                    var bArr = bM.GetThreadValue(i);
                    var r = new RawValue[c.Length];
                    for (int j = 0; j < c.Length; j++)
                        r[j] = c[j].Bool ? aArr[j] : bArr[j];
                    return r;
                }));
            }
            throw new InvalidOperationException();
        }

        public static NumericValue Sqrt(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Sqrt(val.Float));
        }

        public static NumericValue Step(NumericValue y, NumericValue x)
        {
            return Select(x >= y, 1, 0);
        }

        public static NumericValue Ceil(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Ceiling(val.Float));
        }

        public static NumericValue CheckAccessFullyMapped(NumericValue x)
        {
            return (NumericValue)true;
        }


        public static NumericValue Cos(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Cos(val.Float));
        }

        public static NumericValue Cosh(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Cosh(val.Float));
        }

        public static NumericValue Countbits(NumericValue x)
        {
            var u = x.Cast(ScalarType.Uint);
            return u.Map(y =>
            {
                uint bits = y.Uint;
                uint count = 0;
                for (int i = 0; i < 32; i++)
                {
                    if ((bits & (1 << i)) != 0)
                        count++;
                }
                return count;
            });
        }

        public static NumericValue Cross(NumericValue a, NumericValue b)
        {
            var vecA = ToFloatLike(a).BroadcastToVector(3);
            var vecB = ToFloatLike(b).BroadcastToVector(3);
            return VectorValue.FromScalars(
                vecA.y * vecB.z - vecA.z * vecB.y,
                vecA.z * vecB.x - vecA.x * vecB.z,
                vecA.x * vecB.y - vecA.y * vecB.x
            );
        }

        public static NumericValue D3DCOLORtoUBYTE4(NumericValue x)
        {
            var vec = CastToVector(Trunc(x.Cast(ScalarType.Float) * 255.001953f));
            return VectorValue.FromScalars(vec.z, vec.y, vec.x, vec.w);
        }

        public static NumericValue Degrees(NumericValue x)
        {
            return x * (180f / MathF.PI);
        }

        public static NumericValue Determinant(NumericValue x)
        {
            x = ToFloatLike(x);
            var mat = CastToMatrix(x);

            if (mat.Rows == 2) return Det2x2(mat);
            if (mat.Rows == 3) return Det3x3(mat);
            if (mat.Rows == 4) return Det4x4(mat);
            return mat[0, 0];

            // Determinant of a 2x2 matrix
            NumericValue Det2x2(MatrixValue m)
            {
                return m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0];
            }

            // Determinant of a 3x3 matrix
            NumericValue Det3x3(MatrixValue m)
            {
                return
                    m[0, 0] * (m[1, 1] * m[2, 2] - m[1,2] * m[2,1]) -
                    m[0, 1] * (m[1, 0] * m[2, 2] - m[1,2] * m[2,0]) +
                    m[0, 2] * (m[1, 0] * m[2, 1] - m[1,1] * m[2,0]);
            }

            // Determinant of a 4x4 matrix
            NumericValue Det4x4(MatrixValue m)
            {
                return
                    m[0,0] * (
                        m[1,1] * (m[2,2] * m[3,3] - m[2,3] * m[3,2]) -
                        m[1,2] * (m[2,1] * m[3,3] - m[2,3] * m[3,1]) +
                        m[1,3] * (m[2,1] * m[3,2] - m[2,2] * m[3,1])
                    ) -
                    m[0,1] * (
                        m[1,0] * (m[2,2] * m[3,3] - m[2,3] * m[3,2]) -
                        m[1,2] * (m[2,0] * m[3,3] - m[2,3] * m[3,0]) +
                        m[1,3] * (m[2,0] * m[3,2] - m[2,2] * m[3,0])
                    ) +
                    m[0,2] * (
                        m[1,0] * (m[2,1] * m[3,3] - m[2,3] * m[3,1]) -
                        m[1,1] * (m[2,0] * m[3,3] - m[2,3] * m[3,0]) +
                        m[1,3] * (m[2,0] * m[3,1] - m[2,1] * m[3,0])
                    ) -
                    m[0,3] * (
                        m[1,0] * (m[2,1] * m[3,2] - m[2,2] * m[3,1]) -
                        m[1,1] * (m[2,0] * m[3,2] - m[2,2] * m[3,0]) +
                        m[1,2] * (m[2,0] * m[3,1] - m[2,1] * m[3,0])
                    );
            }
        }

        public static NumericValue Distance(NumericValue x, NumericValue y)
        {
            return Length(y - x);
        }

        public static NumericValue Sign(NumericValue x)
        {
            var zero = HLSLTypeUtils.GetZeroValue(x);
            return Select(x == zero, zero, Select(x > zero, 1, -1)).Cast(ScalarType.Int);
        }

        private static uint F32tof16Bits(uint f)
        {
            // Extract sign, exponent, and mantissa from float32
            uint sign = (f >> 31) & 0x1;
            uint exponent = (f >> 23) & 0xFF;
            uint mantissa = f & 0x7FFFFF;

            if (exponent == 0xFF) // Inf or NaN
                return (sign << 15) | 0x7C00 | (mantissa != 0 ? 0x200u : 0);
            if (exponent == 0) // Zero or denormalized
                return sign << 15; // Just preserve sign, flush denorms to zero

            // Rebias exponent from float32 (bias 127) to float16 (bias 15)
            int newExp = (int)(exponent) - 127 + 15;
            if (newExp >= 31) // Overflow to infinity
                return (sign << 15) | 0x7C00;
            if (newExp <= 0) // Underflow to zero
                return sign << 15;

            // Normal case: construct half float
            return (sign << 15) | ((uint)(newExp) << 10) | (mantissa >> 13);
        }

        private static uint F16tof32Bits(uint half)
        {
            // Extract sign, exponent, and mantissa from float16
            uint sign = (half >> 15) & 0x1;
            uint exponent = (half >> 10) & 0x1F;
            uint mantissa = half & 0x3FF;

            if (exponent == 0x1F) // Inf or NaN
            {
                // Preserve inf/nan, expand mantissa
                return (sign << 31) | 0x7F800000 | (mantissa << 13);
            }
            if (exponent == 0) // Zero or denormalized
            {
                if (mantissa == 0) // Zero
                    return sign << 31;

                // Denormalized - convert to normalized float32
                // Find the leading 1 bit
                exponent = 1;
                while ((mantissa & 0x400) == 0)
                {
                    mantissa <<= 1;
                    exponent--;
                }
                mantissa &= 0x3FF; // Remove leading 1
            }

            // Rebias exponent from float16 (bias 15) to float32 (bias 127)
            uint newExp = exponent + 127 - 15;
            return (sign << 31) | (newExp << 23) | (mantissa << 13);
        }

        public static NumericValue F32tof16(NumericValue x)
        {
            var floatVal = x.Cast(ScalarType.Float);
            if (floatVal is ScalarValue sv)
                return new ScalarValue(ScalarType.Uint, sv.Value.Map(rv => (RawValue)F32tof16Bits(rv.Uint)));
            if (floatVal is VectorValue vv)
                return new VectorValue(ScalarType.Uint, vv.Values.Map(arr =>
                {
                    var res = new RawValue[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        res[i] = F32tof16Bits(arr[i].Uint);
                    return res;
                }));
            if (floatVal is MatrixValue mv)
                return new MatrixValue(ScalarType.Uint, mv.Rows, mv.Columns, mv.Values.Map(arr =>
                {
                    var res = new RawValue[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        res[i] = F32tof16Bits(arr[i].Uint);
                    return res;
                }));
            throw new InvalidOperationException();
        }

        public static NumericValue F16tof32(NumericValue x)
        {
            var uintVal = x.Cast(ScalarType.Uint);
            if (uintVal is ScalarValue sv)
                return new ScalarValue(ScalarType.Float, sv.Value.Map(rv => (RawValue)F16tof32Bits(rv.Uint)));
            if (uintVal is VectorValue vv)
                return new VectorValue(ScalarType.Float, vv.Values.Map(arr =>
                {
                    var res = new RawValue[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        res[i] = F16tof32Bits(arr[i].Uint);
                    return res;
                }));
            if (uintVal is MatrixValue mv)
                return new MatrixValue(ScalarType.Float, mv.Rows, mv.Columns, mv.Values.Map(arr =>
                {
                    var res = new RawValue[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        res[i] = F16tof32Bits(arr[i].Uint);
                    return res;
                }));
            throw new InvalidOperationException();
        }

        public static NumericValue Faceforward(NumericValue n, NumericValue i, NumericValue ng)
        {
            return -n * Sign(Dot(i, ng));
        }

        public static NumericValue Firstbithigh(NumericValue x)
        {
            // Per DXC: uint<> firstbithigh(in any_int<> x) — returns uint regardless of signed/unsigned input.
            // Gets the location of the first set bit starting from the highest order bit and working downward, per component.
            NumericValue result;
            if (HLSLTypeUtils.IsInt(x.Type))
            {
                if (x.Type != ScalarType.Int)
                    x = x.Cast(ScalarType.Int);
                result = x.Map(y =>
                {
                    int signed = y.Int;
                    for (int i = 0; i < 32; i++)
                    {
                        int bitPos = 1 << (31 - i);
                        // For a negative signed integer, firstbithigh returns the position of the first bit set to 0.
                        if (signed < 0)
                        {
                            if ((signed & bitPos) == 0)
                                return (31 - i);
                        }
                        else if ((signed & bitPos) != 0)
                            return (31 - i);
                    }
                    return -1;
                });
            }
            else
            {
                if (x.Type != ScalarType.Uint)
                    x = x.Cast(ScalarType.Uint);
                result = x.Map(y =>
                {
                    uint unsigned = y.Uint;
                    for (int i = 0; i < 32; i++)
                    {
                        uint bitPos = 1u << (31 - i);
                        if ((unsigned & bitPos) != 0)
                            return (31 - i);
                    }
                    return -1;
                });
            }
            // Re-tag result as uint (bit patterns are equivalent: -1 == 0xFFFFFFFFu).
            if (result is ScalarValue sv) return new ScalarValue(ScalarType.Uint, sv.Value);
            if (result is VectorValue vv) return new VectorValue(ScalarType.Uint, vv.Values);
            if (result is MatrixValue mv) return new MatrixValue(ScalarType.Uint, mv.Rows, mv.Columns, mv.Values);
            throw new InvalidOperationException();
        }

        public static NumericValue Firstbitlow(NumericValue x)
        {
            x = x.Cast(ScalarType.Uint);

            return x.Map(y =>
            {
                // Returns the location of the first set bit starting from the lowest order bit and working upward, per component.
                uint unsigned = y.Uint;
                for (int i = 0; i < 32; i++)
                {
                    uint bitPos = 1u << i;
                    if ((unsigned & bitPos) != 0)
                        return (uint)i;
                }
                return 0xFFFFFFFFu;
            });
        }

        public static NumericValue Floor(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Floor(val.Float));
        }

        public static NumericValue Fma(NumericValue a, NumericValue b, NumericValue c)
        {
            return a * b + c;
        }

        public static NumericValue Fmod(NumericValue a, NumericValue b)
        {
            a = ToFloatLike(a);
            b = ToFloatLike(b);
            var c = Frac(Abs(a/b))*Abs(b);
            return Select(a < 0, -c, c);
        }

        public static NumericValue Frac(NumericValue x)
        {
            return Abs(ToFloatLike(x).Map(val => val.Float % 1.0f));
        }

        public static NumericValue Isnan(NumericValue x)
        {
            return x.Cast(ScalarType.Float).Map(val => float.IsNaN(val.Float)).Cast(ScalarType.Bool);
        }

        public static NumericValue Isfinite(NumericValue x)
        {
            return x.Cast(ScalarType.Float).Map(val => float.IsFinite(val.Float)).Cast(ScalarType.Bool);
        }

        public static NumericValue Isinf(NumericValue x)
        {
            return x.Cast(ScalarType.Float).Map(val => float.IsInfinity(val.Float)).Cast(ScalarType.Bool);
        }

        public static NumericValue Isnormal(NumericValue x)
        {
            return x.Cast(ScalarType.Float).Map(val => float.IsNormal(val.Float)).Cast(ScalarType.Bool);
        }

        public static NumericValue Ldexp(NumericValue x, NumericValue exp)
        {
            return x * Exp2(exp);
        }

        public static NumericValue Lit(NumericValue nDotL, NumericValue nDotH, NumericValue m)
        {
            nDotL = ToFloatLike(nDotL);
            nDotH = ToFloatLike(nDotH);
            m = ToFloatLike(m);

            var diffuse = Max(nDotL, 0.0f);
            var specular = Select(nDotL > 0.0f, Pow(Max(nDotH, 0.0f), m), 0.0f);

            ScalarValue one = (ScalarValue)(NumericValue)1.0f;
            return VectorValue.FromScalars(one, (ScalarValue)diffuse, (ScalarValue)specular, one);
        }

        public static NumericValue Log(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Log(val.Float));
        }

        public static NumericValue Log10(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Log(val.Float) / MathF.Log(10));
        }

        public static NumericValue Log2(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Log(val.Float) / MathF.Log(2));
        }

        public static NumericValue Mad(NumericValue m, NumericValue a, NumericValue b)
        {
            return m * a + b;
        }

        public static NumericValue Noise(NumericValue x)
        {
            return 1; // Not supported since SM2.0
        }

        public static NumericValue Pow(NumericValue x, NumericValue y)
        {
            x = ToFloatLike(x);
            y = ToFloatLike(y);
            (x, y) = HLSLTypeUtils.Promote(x, y, false);
            return HLSLValueUtils.Map2(x, y, (fx, fy) => MathF.Exp(fy.Float * MathF.Log(fx.Float)));
        }

        public static NumericValue Radians(NumericValue x)
        {
            return x / (180f / MathF.PI);
        }

        public static NumericValue Rcp(NumericValue x)
        {
            return 1.0f / x;
        }

        public static NumericValue Reflect(NumericValue i, NumericValue n)
        {
            return i - 2.0f * n * Dot(n,i);
        }

        public static NumericValue Refract(NumericValue i, NumericValue n, NumericValue eta)
        {
            var cosi = Dot(-i, n);
            var cost2 = 1.0f - eta * eta * (1.0f - cosi*cosi);
            var t = eta*i + ((eta*cosi - Sqrt(Abs(cost2))) * n);
            return t * (cost2 > 0);
        }

        public static NumericValue Reversebits(NumericValue x)
        {
            return x.Cast(ScalarType.Uint).Map(v =>
            {
                uint a = v.Uint;
                uint r = 0;
                for (int i = 0; i < 32; i++)
                {
                    r <<= 1;
                    r |= (a & 1);
                    a >>= 1;
                }
                return r;
            });
        }

        public static NumericValue Round(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Round(val.Float, MidpointRounding.ToEven));
        }

        public static NumericValue Rsqrt(NumericValue x)
        {
            return 1.0f / Sqrt(x);
        }

        public static NumericValue Sin(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Sin(val.Float));
        }

        public static NumericValue Sinh(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Sinh(val.Float));
        }

        // https://developer.download.nvidia.com/cg/smoothstep.html
        public static NumericValue Smoothstep(NumericValue a, NumericValue b, NumericValue x)
        {
            var t = Saturate((x - a)/(b - a));
            return t*t*(3.0f - (2.0f*t));
        }

        public static NumericValue Tan(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Tan(val.Float));
        }

        public static NumericValue Tanh(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Tanh(val.Float));
        }

        public static NumericValue Transpose(NumericValue m)
        {
            var mat = CastToMatrix(m);
            int rows = mat.Rows;
            int cols = mat.Columns;
            var reg = mat.Values.Map(x =>
            {
                RawValue[] values = new RawValue[x.Length];
                for (int col = 0; col < cols; col++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        values[col * rows + row] = x[row * cols + col];
                    }
                }
                return values;
            });
            return new MatrixValue(mat.Type, mat.Columns, mat.Rows, reg);
        }

        public static NumericValue Trunc(NumericValue x)
        {
            return ToFloatLike(x).Map(val => MathF.Truncate(val.Float));
        }

        public static NumericValue Dot(NumericValue x, NumericValue y)
        {
            (x, y) = HLSLTypeUtils.Promote(x, y, false);
            VectorValue vx = CastToVector(x);
            VectorValue vy = CastToVector(y);
            ScalarType t = vx.Type;
            int size = vx.Size;

            // Uniform case
            if (vx.Values.IsUniform)
            {
                var ax = vx.Values.UniformValue;
                var ay = vy.Values.UniformValue;
                RawValue acc = default;
                for (int i = 0; i < size; i++)
                {
                    acc = HLSLOperators.Add(t, acc, HLSLOperators.Mul(t, ax[i], ay[i]));
                }
                return new ScalarValue(t, new HLSLRegister<RawValue>(acc));
            }

            int threadCount = vx.ThreadCount;
            RawValue[] result = new RawValue[threadCount];
            for (int thread = 0; thread < threadCount; thread++)
            {
                var ax = vx.Values.Get(thread);
                var ay = vy.Values.Get(thread);
                RawValue acc = default;
                for (int i = 0; i < size; i++)
            {
                    acc = HLSLOperators.Add(t, acc, HLSLOperators.Mul(t, ax[i], ay[i]));
                }
                result[thread] = acc;
            }
            return new ScalarValue(t, new HLSLRegister<RawValue>(result));
        }

        public static NumericValue Dst(NumericValue src0, NumericValue src1)
        {
            var src0vec = CastToVector(ToFloatLike(src0));
            var src1vec = CastToVector(ToFloatLike(src1));
            return VectorValue.FromScalars((ScalarValue)(NumericValue)1.0f, src0vec.y * src1vec.y, src0vec.z, src1vec.w);
        }

        public static NumericValue Length(NumericValue x)
        {
            return Sqrt(Dot(x, x));
        }

        public static NumericValue Normalize(NumericValue x)
        {
            return x / Length(x);
        }

        public static NumericValue Min(NumericValue x, NumericValue y)
        {
            (x, y) = HLSLTypeUtils.Promote(x, y, false);
            ScalarType t = x.Type;
            return HLSLValueUtils.Map2(x, y, (l, r) => Min(t, l, r));
        }

        public static NumericValue Msad4(NumericValue reference, NumericValue source, NumericValue accum)
        {
            ScalarValue referenceU = (ScalarValue)reference.Cast(ScalarType.Uint); // uint
            VectorValue sourceU = CastToVector(source.Cast(ScalarType.Uint), 2); // uint2
            VectorValue accumU = CastToVector(accum.Cast(ScalarType.Uint), 4); // uint4

            // Unpack reference bytes
            var r0 = referenceU & 0xFF;
            var r1 = (HLSLOperators.BitSHR(referenceU, 8)) & 0xFF;
            var r2 = (HLSLOperators.BitSHR(referenceU, 16)) & 0xFF;
            var r3 = (HLSLOperators.BitSHR(referenceU, 24)) & 0xFF;

            // Unpack source.x bytes
            var s0 = sourceU.x & 0xFF;
            var s1 = (HLSLOperators.BitSHR(sourceU.x, 8)) & 0xFF;
            var s2 = (HLSLOperators.BitSHR(sourceU.x, 16)) & 0xFF;
            var s3 = (HLSLOperators.BitSHR(sourceU.x, 24)) & 0xFF;

            // Unpack source.y bytes
            var t0 = sourceU.y & 0xFF;
            var t1 = (HLSLOperators.BitSHR(sourceU.y, 8)) & 0xFF;
            var t2 = (HLSLOperators.BitSHR(sourceU.y, 16)) & 0xFF;
            var t3 = (HLSLOperators.BitSHR(sourceU.y, 24)) & 0xFF;

            ScalarValue x = (ScalarValue)(
                  Abs(r0.Cast(ScalarType.Int) - s0.Cast(ScalarType.Int))
                + Abs(r1.Cast(ScalarType.Int) - s1.Cast(ScalarType.Int))
                + Abs(r2.Cast(ScalarType.Int) - s2.Cast(ScalarType.Int))
                + Abs(r3.Cast(ScalarType.Int) - s3.Cast(ScalarType.Int)));
            ScalarValue y = (ScalarValue)(
                  Abs(r0.Cast(ScalarType.Int) - s1.Cast(ScalarType.Int))
                + Abs(r1.Cast(ScalarType.Int) - s2.Cast(ScalarType.Int))
                + Abs(r2.Cast(ScalarType.Int) - s3.Cast(ScalarType.Int))
                + Abs(r3.Cast(ScalarType.Int) - t0.Cast(ScalarType.Int)));
            ScalarValue z = (ScalarValue)(
                  Abs(r0.Cast(ScalarType.Int) - s2.Cast(ScalarType.Int))
                + Abs(r1.Cast(ScalarType.Int) - s3.Cast(ScalarType.Int))
                + Abs(r2.Cast(ScalarType.Int) - t0.Cast(ScalarType.Int))
                + Abs(r3.Cast(ScalarType.Int) - t1.Cast(ScalarType.Int)));
            ScalarValue w = (ScalarValue)(
                  Abs(r0.Cast(ScalarType.Int) - s3.Cast(ScalarType.Int))
                + Abs(r1.Cast(ScalarType.Int) - t0.Cast(ScalarType.Int))
                + Abs(r2.Cast(ScalarType.Int) - t1.Cast(ScalarType.Int))
                + Abs(r3.Cast(ScalarType.Int) - t2.Cast(ScalarType.Int)));

            return accum + VectorValue.FromScalars(x,y,z,w);
        }

        // https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-mul#type-description
        public static NumericValue Mul(NumericValue x, NumericValue y)
        {
            bool xIsScalar = x is ScalarValue;
            bool yIsScalar = y is ScalarValue;

            // Covers case 1, 2, 3, 4, 7 - scalar mul
            if (xIsScalar || yIsScalar)
                return x * y;

            bool xIsVector = x is VectorValue;
            bool yIsVector = y is VectorValue;

            // Case 5 - dot
            if (xIsVector && yIsVector)
                return Dot(x, y);

            bool xIsMatrix = x is MatrixValue;
            bool yIsMatrix = y is MatrixValue;

            // Case 6 - row vector mul
            if (xIsVector && yIsMatrix)
            {
                var xVec = (VectorValue)x;
                var yMat = (MatrixValue)y;
                ScalarValue[] result = new ScalarValue[yMat.Columns];
                for (int col = 0; col < yMat.Columns; col++)
                {
                    ScalarValue[] colVec = new ScalarValue[yMat.Rows];
                    for (int row = 0; row < yMat.Rows; row++)
                        colVec[row] = yMat[row, col];
                    result[col] = (ScalarValue)Dot(VectorValue.FromScalars(colVec), xVec);
                }
                return VectorValue.FromScalars(result);
            }

            // Case 8 - column vector mul
            if (xIsMatrix && yIsVector)
            {
                var xMat = (MatrixValue)x;
                var yVec = (VectorValue)y;
                ScalarValue[] result = new ScalarValue[xMat.Rows];
                for (int row = 0; row < xMat.Rows; row++)
                {
                    ScalarValue[] rowVec = new ScalarValue[xMat.Columns];
                    for (int col = 0; col < xMat.Columns; col++)
                        rowVec[col] = xMat[row, col];
                    result[row] = (ScalarValue)Dot(VectorValue.FromScalars(rowVec), yVec);
                }
                return VectorValue.FromScalars(result);
            }

            // Case 9 - full matmul
            if (xIsMatrix && yIsMatrix)
            {
                var xMat = (MatrixValue)x;
                var yMat = (MatrixValue)y;
                ScalarValue[] result = new ScalarValue[xMat.Rows*yMat.Columns];
                for (int row = 0; row < xMat.Rows; row++)
                {
                    for (int col = 0; col < yMat.Columns; col++)
                    {
                        // get row 'row' from xMat
                        ScalarValue[] rowVec = new ScalarValue[xMat.Columns];
                        for (int i = 0; i < xMat.Columns; i++)
                            rowVec[i] = xMat[row, i];

                        // get col 'col' from yMat
                        ScalarValue[] colVec = new ScalarValue[yMat.Rows];
                        for (int i = 0; i < yMat.Rows; i++)
                            colVec[i] = yMat[i, col];

                        result[row * xMat.Rows + col] = (ScalarValue)Dot(VectorValue.FromScalars(rowVec), VectorValue.FromScalars(colVec));
                    }
                }
                return MatrixValue.FromScalars(xMat.Rows, yMat.Columns, result);
            }

            return x;
        }

        public static NumericValue Max(NumericValue x, NumericValue y)
        {
            (x, y) = HLSLTypeUtils.Promote(x, y, false);
            ScalarType t = x.Type;
            return HLSLValueUtils.Map2(x, y, (l, r) => Max(t, l, r));
        }

        public static NumericValue Clamp(NumericValue x, NumericValue min, NumericValue max)
        {
            return Min(Max(x, min), max);
        }

        public static NumericValue Saturate(NumericValue x)
        {
            return Clamp(x, 0f, 1f);
        }
        #endregion

        #region Intrinsics with out parameters
        public static NumericValue Modf(NumericValue x, ReferenceValue i)
        {
            i.Set(Trunc(x));
            return x - (NumericValue)i.Get();
        }

        public static void Sincos(NumericValue a, ReferenceValue s, ReferenceValue c)
        {
            s.Set(Sin(a));
            c.Set(Cos(a));
        }

        public static NumericValue Frexp(NumericValue x, ReferenceValue e)
        {
            var bits = Asuint(x);
            var biased_exp = (HLSLOperators.BitSHR(bits, 23) & 0xFF).Cast(ScalarType.Int);
            var mantissa_bits = (bits & 0x807FFFFFu) | (126u << 23);
            e.Set(Select(x == 0, 0, biased_exp - 126));
            return Select(x == 0, x, Asfloat(mantissa_bits));
        }

        public static void Asuint(NumericValue value, ReferenceValue lowbits, ReferenceValue highbits)
        {
            var d = (ScalarValue)value.Cast(ScalarType.Double);
            if (d.IsUniform)
            {
                long bits = d.Value.UniformValue.Long;
                lowbits.Set((ScalarValue)(uint)(bits & 0xFFFFFFFFu));
                highbits.Set((ScalarValue)(uint)((bits >> 32) & 0xFFFFFFFFu));
                return;
            }

            ScalarValue retLow = 0u;
            retLow = (ScalarValue)retLow.Vectorize(d.ThreadCount);
            ScalarValue retHigh = 0u;
            retHigh = (ScalarValue)retHigh.Vectorize(d.ThreadCount);
            for (int threadIndex = 0; threadIndex < d.ThreadCount; threadIndex++)
            {
                long bits = d.Value.Get(threadIndex).Long;
                uint low = (uint)(bits & 0xFFFFFFFFu);
                uint high = (uint)((bits >> 32) & 0xFFFFFFFFu);
                retLow = (ScalarValue)retLow.SetThreadValue(threadIndex, low);
                retHigh = (ScalarValue)retHigh.SetThreadValue(threadIndex, high);
            }
            lowbits.Set(retLow);
            highbits.Set(retHigh);
        }
        #endregion

        #region Special intrinsics that touch execution state
        public static bool TryInvokeExecutionStateIntrinsic(HLSLExecutionState executionState, string name, HLSLValue[] args, out HLSLValue result)
        {
            switch (name)
            {
                case "printf":
                case "errorf":
                    Printf(executionState, args);
                    result = ScalarValue.Null;
                    return true;

                case "QuadReadAcrossDiagonal":
                    result = QuadReadAcrossDiagonal(executionState, (NumericValue)args[0]);
                    return true;
                case "QuadReadLaneAt":
                    result = QuadReadLaneAt(executionState, (NumericValue)args[0], (ScalarValue)args[1]);
                    return true;
                case "QuadReadAcrossX":
                    result = QuadReadAcrossX(executionState, (NumericValue)args[0]);
                    return true;
                case "QuadReadAcrossY":
                    result = QuadReadAcrossY(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveAllEqual":
                    result = WaveActiveAllEqual(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveBitAnd":
                    result = WaveActiveBitAnd(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveBitOr":
                    result = WaveActiveBitOr(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveBitXor":
                    result = WaveActiveBitXor(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveCountBits":
                    result = WaveActiveCountBits(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveMax":
                    result = WaveActiveMax(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveMin":
                    result = WaveActiveMin(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveProduct":
                    result = WaveActiveProduct(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveSum":
                    result = WaveActiveSum(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveAllTrue":
                    result = WaveActiveAllTrue(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveAnyTrue":
                    result = WaveActiveAnyTrue(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveActiveBallot":
                    result = WaveActiveBallot(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveGetLaneCount":
                    result = WaveGetLaneCount(executionState);
                    return true;
                case "WaveGetLaneIndex":
                    result = WaveGetLaneIndex(executionState);
                    return true;
                case "WaveIsFirstLane":
                    result = WaveIsFirstLane(executionState);
                    return true;
                case "WavePrefixCountBits":
                    result = WavePrefixCountBits(executionState, (NumericValue)args[0]);
                    return true;
                case "WavePrefixProduct":
                    result = WavePrefixProduct(executionState, (NumericValue)args[0]);
                    return true;
                case "WavePrefixSum":
                    result = WavePrefixSum(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveReadLaneFirst":
                    result = WaveReadLaneFirst(executionState, (NumericValue)args[0]);
                    return true;
                case "WaveReadLaneAt":
                    result = WaveReadLaneAt(executionState, (NumericValue)args[0], (ScalarValue)args[1]);
                    return true;

                case "ddx":
                case "ddx_coarse":
                    result = Ddx(executionState, (NumericValue)args[0]);
                    return true;
                case "ddy":
                case "ddy_coarse":
                    result = Ddy(executionState, (NumericValue)args[0]);
                    return true;
                case "ddx_fine":
                    result = DdxFine(executionState, (NumericValue)args[0]);
                    return true;
                case "ddy_fine":
                    result = DdyFine(executionState, (NumericValue)args[0]);
                    return true;
                case "fwidth":
                    result = Fwidth(executionState, (NumericValue)args[0]);
                    return true;

                case "clip":
                    Clip(executionState, (NumericValue)args[0]);
                    result = ScalarValue.Null;
                    return true;
                case "abort":
                    Abort(executionState);
                    result = ScalarValue.Null;
                    return true;

                case "AllMemoryBarrier":
                case "DeviceMemoryBarrier":
                case "GroupMemoryBarrier":
                case "AllMemoryBarrierWithGroupSync":
                case "DeviceMemoryBarrierWithGroupSync":
                case "GroupMemoryBarrierWithGroupSync":
                    System.Threading.Thread.MemoryBarrier();
                    result = ScalarValue.Null;
                    return true;

                case "InterlockedAdd":
                    InterlockedRMW(executionState, args, (a, b) => a + b);
                    result = ScalarValue.Null;
                    return true;
                case "InterlockedAnd":
                    InterlockedRMW(executionState, args, (a, b) => HLSLOperators.BitAnd(a, b));
                    result = ScalarValue.Null;
                    return true;
                case "InterlockedOr":
                    InterlockedRMW(executionState, args, (a, b) => HLSLOperators.BitOr(a, b));
                    result = ScalarValue.Null;
                    return true;
                case "InterlockedXor":
                    InterlockedRMW(executionState, args, (a, b) => HLSLOperators.BitXor(a, b));
                    result = ScalarValue.Null;
                    return true;
                case "InterlockedMin":
                    InterlockedRMW(executionState, args, (a, b) => Min(a, b));
                    result = ScalarValue.Null;
                    return true;
                case "InterlockedMax":
                    InterlockedRMW(executionState, args, (a, b) => Max(a, b));
                    result = ScalarValue.Null;
                    return true;
                case "InterlockedExchange":
                    InterlockedRMW(executionState, args, (_, b) => b);
                    result = ScalarValue.Null;
                    return true;
                case "InterlockedCompareStore":
                case "InterlockedCompareStoreFloatBitwise":
                case "InterlockedCompareExchange":
                case "InterlockedCompareExchangeFloatBitwise":
                    InterlockedCAS(executionState, args);
                    result = ScalarValue.Null;
                    return true;

                case "GetRenderTargetSampleCount":
                    result = (ScalarValue)1u;
                    return true;
                case "GetRenderTargetSamplePosition":
                    result = VectorValue.FromScalars(0.5, 0.5f);
                    return true;

                // Legacy combined-sampler tex* intrinsics from dx9
                case "tex1D" when args.Length == 2:
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1]);
                    return true;
                case "tex1D" when args.Length == 4:
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "tex1Dbias":
                    result = SampleBias(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "tex1Dgrad":
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "tex1Dlod":
                    result = SampleLevel((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "tex1Dproj":
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1] / ((VectorValue)args[1]).w);
                    return true;

                case "tex2D" when args.Length == 2:
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1]);
                    return true;
                case "tex2D" when args.Length == 4:
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "tex2Dbias":
                    result = SampleBias(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "tex2Dgrad":
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "tex2Dlod":
                    result = SampleLevel((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "tex2Dproj":
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1] / ((VectorValue)args[1]).w);
                    return true;

                case "tex3D" when args.Length == 2:
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1]);
                    return true;
                case "tex3D" when args.Length == 4:
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "tex3Dbias":
                    result = SampleBias(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "tex3Dgrad":
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "tex3Dlod":
                    result = SampleLevel((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "tex3Dproj":
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1] / ((VectorValue)args[1]).w);
                    return true;

                case "texCUBE" when args.Length == 2:
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1]);
                    return true;
                case "texCUBE" when args.Length == 4:
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "texCUBEbias":
                    result = SampleBias(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "texCUBEgrad":
                    result = SampleGrad((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], (NumericValue)args[2], (NumericValue)args[3]);
                    return true;
                case "texCUBElod":
                    result = SampleLevel((ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1], ((VectorValue)args[1]).w);
                    return true;
                case "texCUBEproj":
                    result = Sample(executionState, (ResourceValue)args[0], new SamplerStateValue(), (NumericValue)args[1] / ((VectorValue)args[1]).w);
                    return true;

                default:
                    result = null;
                    return false;
            }
        }

        // Interlocked op helper
        private static void InterlockedOp(HLSLExecutionState state, HLSLValue[] args, int outArgIndex, Func<NumericValue, int, NumericValue> computeNewValue)
        {
            var refVal = (ReferenceValue)args[0];
            bool hasOut = outArgIndex < args.Length && args[outArgIndex] is ReferenceValue;
            int threadCount = state.GetThreadCount();
            var originals = hasOut ? new HLSLValue[threadCount] : null;

            for (int thread = 0; thread < threadCount; thread++)
            {
                var cur = (NumericValue)HLSLValueUtils.Scalarize(refVal.Get(), thread);
                if (hasOut) originals[thread] = cur;
                if (!state.IsThreadActive(thread))
                    continue;

                var newVal = computeNewValue(cur, thread);
                if (newVal is not null)
                    refVal.Set(HLSLTypeUtils.CastForAssignment(cur, newVal));
            }

            if (hasOut)
                ((ReferenceValue)args[outArgIndex]).Set((NumericValue)HLSLValueUtils.MergeThreadValues(originals));
        }

        // Read-modify-write
        private static void InterlockedRMW(HLSLExecutionState state, HLSLValue[] args, Func<NumericValue, NumericValue, NumericValue> op)
        {
            var operand = (NumericValue)args[1];
            InterlockedOp(state, args, 2, (cur, t) =>
            {
                return op(cur, operand.Scalarize(t));
            });
        }

        // Compare-and-swap
        private static void InterlockedCAS(HLSLExecutionState state, HLSLValue[] args)
        {
            var compare = (NumericValue)args[1];
            var value = (NumericValue)args[2];
            InterlockedOp(state, args, 3, (cur, t) =>
            {
                return ((ScalarValue)(cur == compare.Scalarize(t))).AsBool() ? value.Scalarize(t) : null;
            });
        }

        public static void Printf(HLSLExecutionState executionState, HLSLValue[] args)
        {
            if (args.Length > 0)
            {
                int maxThreadCount = args.Max(x => x.ThreadCount);

                bool scalarizeLoop = executionState.IsVaryingExecution() || maxThreadCount > 1;
                int numThreads = scalarizeLoop ? Math.Max(maxThreadCount, executionState.GetThreadCount()) : 1;

                for (int threadIndex = 0; threadIndex < numThreads; threadIndex++)
                {
                    if (scalarizeLoop && !executionState.IsThreadActive(threadIndex))
                        continue;

                    if (scalarizeLoop && args.Length == 1)
                    {
                        Console.WriteLine(Convert.ToString(HLSLValueUtils.Scalarize(args[0], threadIndex), CultureInfo.InvariantCulture));
                        continue;
                    }

                    string formatString = args[0].ToString();
                    StringBuilder sb = new StringBuilder();
                    if (scalarizeLoop)
                        sb.Append($"[Thread {threadIndex}] ");
                    int argCounter = 1;
                    for (int j = 0; j < formatString.Length; j++)
                    {
                        if (formatString[j] == '%')
                        {
                            j++;
                            var arg = args[argCounter++];
                            sb.Append(Convert.ToString(HLSLValueUtils.Scalarize(arg, threadIndex), CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(formatString[j]);
                        }
                    }
                    Console.WriteLine(sb.ToString());
                }
            }
        }

        public static ScalarValue WaveGetLaneIndex(HLSLExecutionState executionState)
        {
            return new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarVGPR(Enumerable.Range(0, executionState.GetThreadCount()).Select(i => (RawValue)(uint)i)));
        }

        public static ScalarValue WaveGetLaneCount(HLSLExecutionState executionState)
        {
            return (ScalarValue)executionState.GetThreadCount();
        }

        public static ScalarValue WaveIsFirstLane(HLSLExecutionState executionState)
        {
            var perLaneIsFirst = new RawValue[executionState.GetThreadCount()];
            for (int threadIdx = 0; threadIdx < executionState.GetThreadCount(); threadIdx++)
            {
                if (executionState.IsThreadActive(threadIdx))
                {
                    perLaneIsFirst[threadIdx] = true;
                    break;
                }
            }
            return new ScalarValue(ScalarType.Bool, HLSLValueUtils.MakeScalarVGPR(perLaneIsFirst));
        }

        public static NumericValue WaveReadLaneAt(HLSLExecutionState executionState, NumericValue expr, ScalarValue laneIndex)
        {
            if (laneIndex.IsUniform)
                return expr.Scalarize(laneIndex.AsInt());

            int threadCount = laneIndex.ThreadCount;
            if (expr is ScalarValue scalarExpr)
            {
                RawValue[] perLaneValue = new RawValue[threadCount];
                for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                    perLaneValue[threadIndex] = scalarExpr.Value.Get(laneIndex.AsInt(threadIndex));
                return new ScalarValue(expr.Type, HLSLValueUtils.MakeScalarVGPR(perLaneValue));
            }
            if (expr is VectorValue vectorExpr)
            {
                RawValue[][] perLaneValue = new RawValue[threadCount][];
                for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                    perLaneValue[threadIndex] = vectorExpr.Values.Get(laneIndex.AsInt(threadIndex));
                return new VectorValue(expr.Type, HLSLValueUtils.MakeVectorVGPR(perLaneValue));
            }
            if (expr is MatrixValue matrixExpr)
            {
                RawValue[][] perLaneValue = new RawValue[threadCount][];
                for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                    perLaneValue[threadIndex] = matrixExpr.Values.Get(laneIndex.AsInt(threadIndex));
                return new MatrixValue(expr.Type, matrixExpr.Rows, matrixExpr.Columns, HLSLValueUtils.MakeVectorVGPR(perLaneValue));
            }
            throw new InvalidOperationException();
        }

        public static NumericValue QuadReadAcrossDiagonal(HLSLExecutionState executionState, NumericValue expr)
        {
            var laneIndex = HLSLValueUtils.MakeScalarVGPR(new RawValue[] { 3u, 2u, 1u, 0u });
            return WaveReadLaneAt(executionState, expr, new ScalarValue(ScalarType.Uint, laneIndex));
        }

        public static NumericValue QuadReadLaneAt(HLSLExecutionState executionState, NumericValue expr, ScalarValue laneIndex)
        {
            return WaveReadLaneAt(executionState, expr, laneIndex);
        }

        public static NumericValue QuadReadAcrossX(HLSLExecutionState executionState, NumericValue expr)
        {
            var laneIndex = HLSLValueUtils.MakeScalarVGPR(new RawValue[] { 1u, 0u, 3u, 2u });
            return WaveReadLaneAt(executionState, expr, new ScalarValue(ScalarType.Uint, laneIndex));
        }

        public static NumericValue QuadReadAcrossY(HLSLExecutionState executionState, NumericValue expr)
        {
            var laneIndex = HLSLValueUtils.MakeScalarVGPR(new RawValue[] { 2u, 3u, 0u, 1u });
            return WaveReadLaneAt(executionState, expr, new ScalarValue(ScalarType.Uint, laneIndex));
        }

        public static NumericValue WaveActiveAllEqual(HLSLExecutionState executionState, NumericValue expr)
        {
            NumericValue exprFirst = null;
            NumericValue retVal = null;

            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                if (exprFirst is null)
                {
                    exprFirst = expr.Scalarize(threadIndex);
                    #pragma warning disable CS1718 // Comparison made to same variable
                    retVal = exprFirst == exprFirst;
                    #pragma warning restore CS1718
                }
                else
                {
                    retVal = HLSLOperators.BoolAnd(retVal, exprFirst == expr.Scalarize(threadIndex));
                }
            }

            return retVal;
        }

        private static NumericValue WaveActiveReduce(HLSLExecutionState executionState, NumericValue expr, Func<NumericValue, NumericValue, NumericValue> op)
        {
            NumericValue acc = null;
            for (int i = 0; i < executionState.GetThreadCount(); i++)
            {
                if (!executionState.IsThreadActive(i))
                    continue;

                var lane = expr.Scalarize(i);
                if (acc is null)
                    acc = lane;
                else
                    acc = op(acc, lane);
            }
            return acc;
        }

        public static NumericValue WaveActiveBitAnd(HLSLExecutionState executionState, NumericValue expr) =>
            WaveActiveReduce(executionState, expr, (a, b) => a & b);

        public static NumericValue WaveActiveBitOr(HLSLExecutionState executionState, NumericValue expr) =>
            WaveActiveReduce(executionState, expr, (a, b) => a | b);

        public static NumericValue WaveActiveBitXor(HLSLExecutionState executionState, NumericValue expr) =>
            WaveActiveReduce(executionState, expr, (a, b) => a ^ b);

        public static NumericValue WaveActiveCountBits(HLSLExecutionState executionState, NumericValue expr)
        {
            var exprS = (ScalarValue)expr.Cast(ScalarType.Bool);

            uint count = 0;

            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                if (exprS.AsBool(threadIndex))
                    count++;
            }

            return count;
        }

        public static NumericValue WaveActiveMax(HLSLExecutionState executionState, NumericValue expr) =>
            WaveActiveReduce(executionState, expr, Max);

        public static NumericValue WaveActiveMin(HLSLExecutionState executionState, NumericValue expr) =>
            WaveActiveReduce(executionState, expr, Min);

        public static NumericValue WaveActiveProduct(HLSLExecutionState executionState, NumericValue expr) =>
            WaveActiveReduce(executionState, expr, (a, b) => a * b);

        public static NumericValue WaveActiveSum(HLSLExecutionState executionState, NumericValue expr) =>
            WaveActiveReduce(executionState, expr, (a, b) => a + b);

        public static NumericValue WaveActiveAllTrue(HLSLExecutionState executionState, NumericValue expr)
        {
            var exprS = (ScalarValue)expr.Cast(ScalarType.Bool);

            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                if (!exprS.AsBool(threadIndex))
                    return false;
            }

            return true;
        }

        public static NumericValue WaveActiveAnyTrue(HLSLExecutionState executionState, NumericValue expr)
        {
            var exprS = (ScalarValue)expr.Cast(ScalarType.Bool);

            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                if (exprS.AsBool(threadIndex))
                    return true;
            }

            return false;
        }

        public static NumericValue WaveActiveBallot(HLSLExecutionState executionState, NumericValue expr)
        {
            var exprS = (ScalarValue)expr.Cast(ScalarType.Bool);
            bool[] perLane = new bool[executionState.GetThreadCount()];

            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                if (exprS.AsBool(threadIndex))
                    perLane[threadIndex] = true;
            }

            ScalarValue[] uints = new ScalarValue[4];
            for (int i = 0; i < 4; i++)
            {
                uint res = 0;
                for (int j = 0; j < 32; j++)
                {
                    int idx = i * 32 + j;
                    if (idx < perLane.Length && perLane[idx])
                        res |= 1u << j;
                }
                uints[i] = res;
            }
            return VectorValue.FromScalars(uints);
        }

        public static NumericValue WavePrefixCountBits(HLSLExecutionState executionState, NumericValue expr)
        {
            return WavePrefixSum(executionState, expr.Cast(ScalarType.Bool).Cast(ScalarType.Uint));
        }

        public static NumericValue WavePrefixProduct(HLSLExecutionState executionState, NumericValue expr)
        {
            NumericValue exprFirst = null;
            NumericValue sum = null;

            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                if (exprFirst is null)
                {
                    sum = HLSLTypeUtils.GetOneValue(expr).Scalarize(threadIndex);
                    exprFirst = (NumericValue)HLSLValueUtils.SetThreadValue(expr.Vectorize(executionState.GetThreadCount()), threadIndex, sum);
                    sum = expr.Scalarize(threadIndex);
                }
                else
                {
                    exprFirst = (NumericValue)HLSLValueUtils.SetThreadValue(exprFirst, threadIndex, sum);
                    sum = sum * expr.Scalarize(threadIndex);
                }
            }

            return exprFirst;
        }

        public static NumericValue WavePrefixSum(HLSLExecutionState executionState, NumericValue expr)
        {
            NumericValue exprFirst = null;
            NumericValue sum = null;

            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                if (exprFirst is null)
                {
                    sum = HLSLTypeUtils.GetZeroValue(expr).Scalarize(threadIndex);
                    exprFirst = (NumericValue)HLSLValueUtils.SetThreadValue(expr.Vectorize(executionState.GetThreadCount()), threadIndex, sum);
                    sum = sum + expr.Scalarize(threadIndex);
                }
                else
                {
                    exprFirst = (NumericValue)HLSLValueUtils.SetThreadValue(exprFirst, threadIndex, sum);
                    sum = sum + expr.Scalarize(threadIndex);
                }
            }

            return exprFirst;
        }

        public static NumericValue WaveReadLaneFirst(HLSLExecutionState executionState, NumericValue expr)
        {
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (!executionState.IsThreadActive(threadIndex))
                    continue;

                return expr.Scalarize(threadIndex);
            }

            return expr.Scalarize(0);
        }

        public static NumericValue DdxFine(HLSLExecutionState executionState, NumericValue val)
        {
            if (val.IsUniform)
                return val - val;

            NumericValue Compute(int threadIndex)
            {
                (int x, int y) = executionState.GetThreadPosition(threadIndex);
                int offset = (x % 2 == 0) ? 1 : -1;

                var me = val.Scalarize(threadIndex);
                var other = val.Scalarize(executionState.GetThreadIndex(x + offset, y));
                return (other - me) * offset;
            }

            if (val is ScalarValue sv) return sv.MapThreads((_, threadIndex) => ((ScalarValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is VectorValue vv) return vv.MapThreads((_, threadIndex) => ((VectorValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is MatrixValue mv) return mv.MapThreads((_, threadIndex) => ((MatrixValue)Compute(threadIndex)).GetThreadValue(0));
            throw new InvalidOperationException();
        }

        public static NumericValue DdyFine(HLSLExecutionState executionState, NumericValue val)
        {
            if (val.IsUniform)
                return val - val;

            NumericValue Compute(int threadIndex)
            {
                (int x, int y) = executionState.GetThreadPosition(threadIndex);
                int offset = (y % 2 == 0) ? 1 : -1;

                var me = val.Scalarize(threadIndex);
                var other = val.Scalarize(executionState.GetThreadIndex(x, y + offset));
                return (other - me) * offset;
            }

            if (val is ScalarValue sv) return sv.MapThreads((_, threadIndex) => ((ScalarValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is VectorValue vv) return vv.MapThreads((_, threadIndex) => ((VectorValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is MatrixValue mv) return mv.MapThreads((_, threadIndex) => ((MatrixValue)Compute(threadIndex)).GetThreadValue(0));
            throw new InvalidOperationException();
        }

        public static NumericValue Ddx(HLSLExecutionState executionState, NumericValue val)
        {
            if (val.IsUniform)
                return val - val;

            NumericValue Compute(int threadIndex)
            {
                (int x, int y) = executionState.GetThreadPosition(threadIndex);
                y -= (y & 1);
                threadIndex = executionState.GetThreadIndex(x, y);
                int offset = (x % 2 == 0) ? 1 : -1;

                var me = val.Scalarize(threadIndex);
                var other = val.Scalarize(executionState.GetThreadIndex(x + offset, y));
                return (other - me) * offset;
            }

            if (val is ScalarValue sv) return sv.MapThreads((_, threadIndex) => ((ScalarValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is VectorValue vv) return vv.MapThreads((_, threadIndex) => ((VectorValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is MatrixValue mv) return mv.MapThreads((_, threadIndex) => ((MatrixValue)Compute(threadIndex)).GetThreadValue(0));
            throw new InvalidOperationException();
        }

        public static NumericValue Ddy(HLSLExecutionState executionState, NumericValue val)
        {
            if (val.IsUniform)
                return val - val;

            NumericValue Compute(int threadIndex)
            {
                (int x, int y) = executionState.GetThreadPosition(threadIndex);
                x -= (x & 1);
                threadIndex = executionState.GetThreadIndex(x, y);
                int offset = (y % 2 == 0) ? 1 : -1;

                var me = val.Scalarize(threadIndex);
                var other = val.Scalarize(executionState.GetThreadIndex(x, y + offset));
                return (other - me) * offset;
            }

            if (val is ScalarValue sv) return sv.MapThreads((_, threadIndex) => ((ScalarValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is VectorValue vv) return vv.MapThreads((_, threadIndex) => ((VectorValue)Compute(threadIndex)).GetThreadValue(0));
            if (val is MatrixValue mv) return mv.MapThreads((_, threadIndex) => ((MatrixValue)Compute(threadIndex)).GetThreadValue(0));
            throw new InvalidOperationException();
        }

        public static NumericValue Fwidth(HLSLExecutionState executionState, NumericValue val) =>
            Abs(Ddx(executionState, val)) + Abs(Ddy(executionState, val));

        public static void Clip(HLSLExecutionState executionState, NumericValue x)
        {
            var cond = (ScalarValue)(x < 0);
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (executionState.IsThreadActive(threadIndex))
                {
                    if (cond.AsBool(threadIndex))
                        executionState.KillThreadGlobally(threadIndex);
                }
            }
        }

        public static void Abort(HLSLExecutionState executionState)
        {
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                executionState.KillThreadGlobally(threadIndex);
        }
        #endregion

    }
}
