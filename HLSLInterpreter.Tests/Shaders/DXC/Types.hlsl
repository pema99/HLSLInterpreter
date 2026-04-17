#include "../HLSLTest.hlsl"

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/types/
// focusing on types/boolean, types/vector, types/matrix, types/cast

// ============================================================================
// BOOL TYPE OPERATIONS
// Inspired by DXC types/boolean/bool_stress.hlsl and boolComb.hlsl
// ============================================================================

[Test]
void Types_Bool_LiteralTrueAndFalse()
{
    bool t = true;
    bool f = false;
    ASSERT(t == true);
    ASSERT(f == false);
    ASSERT(t != f);
}

[Test]
void Types_Bool_ArithmeticConversion_TrueIsOne()
{
    bool t = true;
    int n = (int)t;
    ASSERT(n == 1);
}

[Test]
void Types_Bool_ArithmeticConversion_FalseIsZero()
{
    bool f = false;
    int n = (int)f;
    ASSERT(n == 0);
}

[Test]
void Types_Bool_IntToBool_NonzeroIsTrue()
{
    int i = 5;
    bool b = (bool)i;
    ASSERT(b == true);
}

[Test]
void Types_Bool_IntToBool_ZeroIsFalse()
{
    int i = 0;
    bool b = (bool)i;
    ASSERT(b == false);
}

[Test]
void Types_Bool_NegativeIntToBool_IsTrue()
{
    int i = -3;
    bool b = (bool)i;
    ASSERT(b == true);
}

[Test]
void Types_Bool_FloatToBool_NonzeroIsTrue()
{
    float f = 0.5;
    bool b = (bool)f;
    ASSERT(b == true);
}

[Test]
void Types_Bool_FloatToBool_ZeroIsFalse()
{
    float f = 0.0;
    bool b = (bool)f;
    ASSERT(b == false);
}

[Test]
void Types_Bool_Vector_ComponentWiseAnd()
{
    bool3 a = bool3(true, false, true);
    bool3 b = bool3(true, true, false);
    bool3 result = a && b;
    ASSERT(result.x == true);
    ASSERT(result.y == false);
    ASSERT(result.z == false);
}

[Test]
void Types_Bool_Vector_ComponentWiseOr()
{
    bool3 a = bool3(true, false, false);
    bool3 b = bool3(false, true, false);
    bool3 result = a || b;
    ASSERT(result.x == true);
    ASSERT(result.y == true);
    ASSERT(result.z == false);
}

[Test]
void Types_Bool_Vector_Not()
{
    bool3 v = bool3(true, false, true);
    bool3 result = !v;
    ASSERT(result.x == false);
    ASSERT(result.y == true);
    ASSERT(result.z == false);
}

[Test]
void Types_Bool_Vector_AllAny()
{
    bool3 allTrue  = bool3(true, true, true);
    bool3 someFalse = bool3(true, false, true);
    bool3 allFalse  = bool3(false, false, false);

    ASSERT(all(allTrue));
    ASSERT(!all(someFalse));
    ASSERT(!all(allFalse));

    ASSERT(any(allTrue));
    ASSERT(any(someFalse));
    ASSERT(!any(allFalse));
}

[Test]
void Types_Bool_UsedAsCondition_NonZeroTriggersBranch()
{
    float x = 0.0;
    bool cond = true;
    if (cond)
        x = 1.0;
    ASSERT(x == 1.0);
}

[Test]
void Types_Bool_InTernary()
{
    bool flag = true;
    int result = flag ? 10 : 20;
    ASSERT(result == 10);

    flag = false;
    result = flag ? 10 : 20;
    ASSERT(result == 20);
}

// ============================================================================
// BOOL VECTOR — SCALAR SWIZZLE
// Inspired by DXC types/boolean/bool_scalar_swizzle.hlsl
// ============================================================================

[Test]
void Types_Bool_Swizzle_SingleComponent()
{
    bool4 v = bool4(true, false, true, false);
    bool x = v.x;
    bool y = v.y;
    ASSERT(x == true);
    ASSERT(y == false);
}

[Test]
void Types_Bool_Swizzle_MultiComponent()
{
    bool4 v = bool4(true, false, true, false);
    bool2 xy = v.xy;
    bool2 zw = v.zw;
    ASSERT(xy.x == true && xy.y == false);
    ASSERT(zw.x == true && zw.y == false);
}

[Test]
void Types_Bool_Swizzle_Reorder()
{
    bool4 v = bool4(true, false, true, false);
    bool4 rev = v.wzyx;
    ASSERT(rev.x == false);
    ASSERT(rev.y == true);
    ASSERT(rev.z == false);
    ASSERT(rev.w == true);
}

// ============================================================================
// VECTOR TYPE OPERATIONS
// Inspired by DXC types/vector/
// ============================================================================

[Test]
void Types_Vector_ConstructFromScalars()
{
    float x = 1.0, y = 2.0, z = 3.0, w = 4.0;
    float4 v = float4(x, y, z, w);
    ASSERT(v.x == 1.0 && v.y == 2.0 && v.z == 3.0 && v.w == 4.0);
}

[Test]
void Types_Vector_int4_Operations()
{
    int4 a = int4(1, 2, 3, 4);
    int4 b = int4(4, 3, 2, 1);
    int4 sum = a + b;
    ASSERT(sum.x == 5 && sum.y == 5 && sum.z == 5 && sum.w == 5);
}

[Test]
void Types_Vector_uint4_BitwiseOps()
{
    uint4 a = uint4(0xF0, 0x0F, 0xFF, 0x00);
    uint4 b = uint4(0xFF, 0xFF, 0x00, 0xFF);
    uint4 andResult = a & b;
    uint4 orResult  = a | b;
    ASSERT(andResult.x == 0xF0 && andResult.y == 0x0F);
    ASSERT(andResult.z == 0x00 && andResult.w == 0x00);
    ASSERT(orResult.x  == 0xFF && orResult.y  == 0xFF);
    ASSERT(orResult.z  == 0xFF && orResult.w  == 0xFF);
}

[Test]
void Types_Vector_ComparisonReturnsBooolVector()
{
    int4 a = int4(1, 5, 3, 7);
    int4 b = int4(2, 4, 3, 6);
    bool4 lt = a < b;
    bool4 eq = a == b;
    ASSERT(lt.x == true  && lt.y == false && lt.z == false && lt.w == false);
    ASSERT(eq.x == false && eq.y == false && eq.z == true  && eq.w == false);
}

// ============================================================================
// MATRIX TYPE OPERATIONS
// Inspired by DXC types/matrix/
// ============================================================================

[Test]
void Types_Matrix_RowAccess()
{
    float3x3 m = float3x3(1, 2, 3, 4, 5, 6, 7, 8, 9);
    float3 row0 = m[0];
    float3 row1 = m[1];
    float3 row2 = m[2];
    ASSERT(row0.x == 1 && row0.y == 2 && row0.z == 3);
    ASSERT(row1.x == 4 && row1.y == 5 && row1.z == 6);
    ASSERT(row2.x == 7 && row2.y == 8 && row2.z == 9);
}

[Test]
void Types_Matrix_ElementWrite_OtherElementsUnchanged()
{
    float2x2 m = float2x2(1, 2, 3, 4);
    m[0][1] = 99.0;
    ASSERT(m[0][0] == 1.0 && m[0][1] == 99.0);
    ASSERT(m[1][0] == 3.0 && m[1][1] == 4.0);
}

[Test]
void Types_Matrix_ScalarBroadcast()
{
    float2x2 m = (float2x2)5.0;
    ASSERT(m[0][0] == 5.0 && m[0][1] == 5.0);
    ASSERT(m[1][0] == 5.0 && m[1][1] == 5.0);
}

// ============================================================================
// TYPE CASTING
// Inspired by DXC types/cast/
// ============================================================================

[Test]
void Types_Cast_FloatToInt_Truncates()
{
    float f = 3.9;
    int i = (int)f;
    ASSERT(i == 3);

    f = -2.7;
    i = (int)f;
    ASSERT(i == -2);
}

[Test]
void Types_Cast_IntToFloat_Exact()
{
    int i = 42;
    float f = (float)i;
    ASSERT(f == 42.0);
}

[Test]
void Types_Cast_FloatToUint_Truncates()
{
    float f = 7.99;
    uint u = (uint)f;
    ASSERT(u == 7);
}

[Test]
void Types_Cast_UintToInt_SmallValues()
{
    uint u = 100;
    int i = (int)u;
    ASSERT(i == 100);
}

[Test]
void Types_Cast_VectorTruncation()
{
    float4 v4 = float4(1.0, 2.0, 3.0, 4.0);
    float2 v2 = (float2)v4;
    ASSERT(v2.x == 1.0 && v2.y == 2.0);
}

[Test]
void Types_Cast_IntVectorToFloatVector()
{
    int3 iv = int3(1, 2, 3);
    float3 fv = (float3)iv;
    ASSERT(fv.x == 1.0 && fv.y == 2.0 && fv.z == 3.0);
}

// ============================================================================
// HALF TYPE (stored as float internally in interpreter)
// HLSL 'half' is a 16-bit float; the interpreter stores it as float.
// ============================================================================

[Test]
void Types_Half_BasicArithmetic()
{
    half a = 1.5h;
    half b = 2.5h;
    half c = a + b;
    ASSERT(abs((float)c - 4.0) < 0.01);
}

[Test]
void Types_Half_ConversionToFloat()
{
    half h = 3.0h;
    float f = (float)h;
    ASSERT(abs(f - 3.0) < 0.001);
}

[Test]
void Types_Half_Vector()
{
    half2 v = half2(1.0h, 2.0h);
    ASSERT(abs((float)v.x - 1.0) < 0.01);
    ASSERT(abs((float)v.y - 2.0) < 0.01);
}
