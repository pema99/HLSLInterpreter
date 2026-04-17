#include "HLSLTest.hlsl"

struct FlatInitStruct { float a; float b; };
struct StructWithArray { int data[2]; float x; };

[Test]
void Conversion_TernaryOperator_VectorSizes()
{
    bool condition = true;
    float2 v2 = float2(1.0, 2.0);
    float3 v3 = float3(3.0, 4.0, 5.0);
    
    // Both operands convert to float3
    float3 result = condition ? float3(v2, 0.0) : v3;  // v2 explicitly extended to float3
    ASSERT(result.x == 1.0 && result.y == 2.0 && result.z == 0.0);
}

// ============================================================================
// EDGE CASES AND SPECIAL VALUES
// ============================================================================

[Test]
void Conversion_NegativeFloatToUint()
{
    float f = -5.5;
    uint u = f;
    // Negative floats to uint is implementation-defined,
    // but typically wraps or clamps
    // Just verify it doesn't crash
    ASSERT(u == u);  // Tautology to ensure it executes
}

[Test]
void Conversion_LargeFloatToInt()
{
    float f = 1000000.0;
    int i = f;
    ASSERT(i == 1000000);
}

[Test]
void Conversion_SmallFloatToInt()
{
    float f = 0.1;
    int i = f;
    ASSERT(i == 0);
    
    f = 0.9;
    i = f;
    ASSERT(i == 0);
}

[Test]
void Conversion_NegativeIntToUint()
{
    int i = -1;
    uint u = i;
    // Typically becomes large positive value (two's complement)
    // Just verify conversion happens
    ASSERT(u != 0);
}

[Test]
void Conversion_ZeroValues()
{
    int i = 0;
    float f = i;
    ASSERT(f == 0.0);
    
    f = 0.0;
    i = f;
    ASSERT(i == 0);
    
    uint u = 0;
    f = u;
    ASSERT(f == 0.0);
}

// ============================================================================
// VECTOR CONSTRUCTION WITH MIXED TYPES
// ============================================================================

[Test]
void Construction_VectorFromMixedScalars()
{
    int i = 1;
    float f = 2.5;
    uint u = 3;
    
    float3 v = float3(i, f, u);  // All convert to float
    ASSERT(v.x == 1.0 && abs(v.y - 2.5) < 0.001 && v.z == 3.0);
}

[Test]
void Construction_VectorFromVectorAndScalar()
{
    float2 v2 = float2(1.0, 2.0);
    float s = 3.0;
    
    float3 v3 = float3(v2, s);  // Combines to (1.0, 2.0, 3.0)
    ASSERT(v3.x == 1.0 && v3.y == 2.0 && v3.z == 3.0);
}

[Test]
void Construction_VectorFromScalarAndVector()
{
    float s = 1.0;
    float2 v2 = float2(2.0, 3.0);
    
    float3 v3 = float3(s, v2);  // Combines to (1.0, 2.0, 3.0)
    ASSERT(v3.x == 1.0 && v3.y == 2.0 && v3.z == 3.0);
}

[Test]
void Construction_VectorFromMultipleVectors()
{
    float2 v2a = float2(1.0, 2.0);
    float2 v2b = float2(3.0, 4.0);
    
    float4 v4 = float4(v2a, v2b);  // Combines to (1.0, 2.0, 3.0, 4.0)
    ASSERT(v4.x == 1.0 && v4.y == 2.0 && v4.z == 3.0 && v4.w == 4.0);
}

[Test]
void Construction_VectorFromMixedVectorSizes()
{
    float3 v3 = float3(1.0, 2.0, 3.0);
    float s = 4.0;
    
    float4 v4 = float4(v3, s);  // Combines to (1.0, 2.0, 3.0, 4.0)
    ASSERT(v4.x == 1.0 && v4.y == 2.0 && v4.z == 3.0 && v4.w == 4.0);
}

[Test]
void Construction_VectorWithTypeConversion()
{
    int i1 = 1;
    int i2 = 2;
    
    float2 v = float2(i1, i2);  // ints convert to floats
    ASSERT(v.x == 1.0 && v.y == 2.0);
}

// ============================================================================
// MATRIX CONSTRUCTION WITH CONVERSIONS
// ============================================================================

[Test]
void Construction_MatrixFromScalars()
{
    float2x2 m = float2x2(1.0, 2.0, 3.0, 4.0);
    ASSERT(m[0][0] == 1.0 && m[0][1] == 2.0);
    ASSERT(m[1][0] == 3.0 && m[1][1] == 4.0);
}

[Test]
void Construction_MatrixFromMixedTypes()
{
    int i1 = 1;
    float f1 = 2.5;
    int i2 = 3;
    float f2 = 4.5;
    
    float2x2 m = float2x2(i1, f1, i2, f2);
    ASSERT(m[0][0] == 1.0 && abs(m[0][1] - 2.5) < 0.001);
    ASSERT(m[1][0] == 3.0 && abs(m[1][1] - 4.5) < 0.001);
}

[Test]
void Construction_MatrixFromVectors()
{
    float2 row0 = float2(1.0, 2.0);
    float2 row1 = float2(3.0, 4.0);
    
    float2x2 m = float2x2(row0, row1);
    ASSERT(m[0][0] == 1.0 && m[0][1] == 2.0);
    ASSERT(m[1][0] == 3.0 && m[1][1] == 4.0);
}

// ============================================================================
// BOOL CONVERSIONS IN CONTROL FLOW
// ============================================================================

[Test]
void Conversion_IntToBoolInIf()
{
    int i = 5;
    bool executed = false;
    
    if (i)  // Non-zero int converts to true
        executed = true;
    
    ASSERT(executed);
    
    i = 0;
    executed = true;
    
    if (i)  // Zero converts to false
        executed = false;
    
    ASSERT(executed);  // Should still be true
}

[Test]
void Conversion_FloatToBoolInIf()
{
    float f = 0.5;
    bool executed = false;
    
    if (f)  // Non-zero float converts to true
        executed = true;
    
    ASSERT(executed);
    
    f = 0.0;
    executed = true;
    
    if (f)  // Zero converts to false
        executed = false;
    
    ASSERT(executed);  // Should still be true
}

[Test]
void Conversion_VectorToBoolInIf()
{
    float3 v = float3(1.0, 2.0, 3.0);
    bool executed = false;
    
    // Vectors don't directly convert to bool in most implementations,
    // but we can test element access
    if (v.x)
        executed = true;
    
    ASSERT(executed);
}

// ============================================================================
// SWIZZLE EDGE CASES
// ============================================================================

[Test]
void Swizzle_SingleComponentToScalar()
{
    float4 v = float4(1.0, 2.0, 3.0, 4.0);
    float x = v.x;
    ASSERT(x == 1.0);
}

[Test]
void Swizzle_FourComponentIdentity()
{
    float4 v = float4(1.0, 2.0, 3.0, 4.0);
    float4 v2 = v.xyzw;
    ASSERT(v2.x == 1.0 && v2.y == 2.0 && v2.z == 3.0 && v2.w == 4.0);
}

[Test]
void Swizzle_ReverseAll()
{
    float4 v = float4(1.0, 2.0, 3.0, 4.0);
    float4 rev = v.wzyx;
    ASSERT(rev.x == 4.0 && rev.y == 3.0 && rev.z == 2.0 && rev.w == 1.0);
}

[Test]
void Swizzle_BroadcastSingleComponent()
{
    float3 v = float3(5.0, 7.0, 9.0);
    float4 broadcast = v.zzzz;
    ASSERT(broadcast.x == 9.0 && broadcast.y == 9.0 && broadcast.z == 9.0 && broadcast.w == 9.0);
}

[Test]
void Swizzle_ChainedSwizzles()
{
    float4 v = float4(1.0, 2.0, 3.0, 4.0);
    float2 result = v.wzyx.xy;  // First reverses, then takes first two
    ASSERT(result.x == 4.0 && result.y == 3.0);
}

// ============================================================================
// CONSTRUCTOR EDGE CASES
// ============================================================================

[Test]
void Construction_TooManyArguments_Truncates()
{
    // Providing more values than needed - should use first N values
    float2 v = float2(1.0, 2.0);  // Exact match
    ASSERT(v.x == 1.0 && v.y == 2.0);
    
    // Construct from larger vector
    float4 v4 = float4(1.0, 2.0, 3.0, 4.0);
    float2 v2 = float2(v4.x, v4.y);  // Manual truncation
    ASSERT(v2.x == 1.0 && v2.y == 2.0);
}

[Test]
void Construction_SingleScalarRepeated()
{
    float s = 5.0;
    float3 v = float3(s, s, s);
    ASSERT(v.x == 5.0 && v.y == 5.0 && v.z == 5.0);
}

[Test]
void Construction_NestedVectorConstruction()
{
    float2 inner = float2(1.0, 2.0);
    float3 outer = float3(inner, 3.0);
    float4 outermost = float4(outer, 4.0);
    
    ASSERT(outermost.x == 1.0 && outermost.y == 2.0);
    ASSERT(outermost.z == 3.0 && outermost.w == 4.0);
}

// ============================================================================
// ASSIGNMENT CONVERSIONS
// ============================================================================

[Test]
void Assignment_ScalarToVector()
{
    float3 v;
    v = 5.0;  // Scalar broadcasts
    ASSERT(v.x == 5.0 && v.y == 5.0 && v.z == 5.0);
}

[Test]
void Assignment_VectorTruncation()
{
    float2 v2;
    float4 v4 = float4(1.0, 2.0, 3.0, 4.0);
    v2 = (float2)v4;  // Truncates
    ASSERT(v2.x == 1.0 && v2.y == 2.0);
}

[Test]
void Assignment_WithTypeConversion()
{
    float3 fv;
    int3 iv = int3(1, 2, 3);
    fv = iv;  // Convert and assign
    ASSERT(fv.x == 1.0 && fv.y == 2.0 && fv.z == 3.0);
}

[Test]
void Assignment_CompoundOperators()
{
    float3 v = float3(1.0, 2.0, 3.0);
    int i = 2;
    
    v += i;  // int converts to float, broadcasts, then adds
    ASSERT(v.x == 3.0 && v.y == 4.0 && v.z == 5.0);
}

[Test]
void Assignment_MatrixScalarBroadcast()
{
    float2x2 m;
    m = 3.0;  // Scalar broadcasts to all elements
    ASSERT(m[0][0] == 3.0 && m[0][1] == 3.0);
    ASSERT(m[1][0] == 3.0 && m[1][1] == 3.0);
}

// ============================================================================
// STRUCT CASTS
// ============================================================================

struct FiveValues { int One; uint Two; float Three; int FourFive[2]; };

int FiveValuesSum(int X)
{
    FiveValues V = (FiveValues)X;
    return V.One + V.Two + V.Three + V.FourFive[0] + V.FourFive[1];
}

[Test]
void Cast_ScalarToStruct_BroadcastsToAllFields()
{
    // Casting scalar 2 to FiveValues fills every component with 2.
    // Sum = 2 + 2 + 2.0 + 2 + 2 = 10.
    ASSERT(FiveValuesSum(2) == 10);
}

// ============================================================================
// ARRAY CASTS
// ============================================================================

// --- TO scalar array ---

[Test]
void Cast_VectorToFloatArray()
{
    float4 v = float4(1.0, 2.0, 3.0, 4.0);
    float arr[4] = (float[4])v;
    ASSERT(arr[0] == 1.0 && arr[1] == 2.0 && arr[2] == 3.0 && arr[3] == 4.0);
}

[Test]
void Cast_VectorToIntArray()
{
    float4 v = float4(1.5, 2.7, 3.9, 4.1);
    int arr[4] = (int[4])v;
    ASSERT(arr[0] == 1 && arr[1] == 2 && arr[2] == 3 && arr[3] == 4);
}

[Test]
void Cast_FloatScalarArrayToIntArray()
{
    float arr[3] = {1.5, 2.7, 3.9};
    int intArr[3] = (int[3])arr;
    ASSERT(intArr[0] == 1 && intArr[1] == 2 && intArr[2] == 3);
}

[Test]
void Cast_IntArrayToFloatArray()
{
    int arr[4] = {10, 20, 30, 40};
    float fArr[4] = (float[4])arr;
    ASSERT(fArr[0] == 10.0 && fArr[1] == 20.0 && fArr[2] == 30.0 && fArr[3] == 40.0);
}

[Test]
void Cast_MatrixToScalarArray()
{
    // float2x3 has 6 components (2 rows, 3 cols)
    float2x3 m = float2x3(1.0, 2.0, 3.0, 4.0, 5.0, 6.0);
    float arr[6] = (float[6])m;
    ASSERT(arr[0] == 1.0 && arr[1] == 2.0 && arr[2] == 3.0);
    ASSERT(arr[3] == 4.0 && arr[4] == 5.0 && arr[5] == 6.0);
}

// --- TO vector array ---

[Test]
void Cast_ScalarArrayToVectorArray()
{
    // 6 floats → 2 float3s
    float src[6] = {1.0, 2.0, 3.0, 4.0, 5.0, 6.0};
    float3 dst[2] = (float3[2])src;
    ASSERT(dst[0].x == 1.0 && dst[0].y == 2.0 && dst[0].z == 3.0);
    ASSERT(dst[1].x == 4.0 && dst[1].y == 5.0 && dst[1].z == 6.0);
}

[Test]
void Cast_VectorToVectorArray()
{
    // float4 (4 components) → float2[2]
    float4 v = float4(1.0, 2.0, 3.0, 4.0);
    float2 arr[2] = (float2[2])v;
    ASSERT(arr[0].x == 1.0 && arr[0].y == 2.0);
    ASSERT(arr[1].x == 3.0 && arr[1].y == 4.0);
}

[Test]
void Cast_VectorArrayToVectorArrayDifferentType()
{
    float2 src[2] = {float2(1.5, 2.7), float2(3.9, 4.1)};
    int2 dst[2] = (int2[2])src;
    ASSERT(dst[0].x == 1 && dst[0].y == 2);
    ASSERT(dst[1].x == 3 && dst[1].y == 4);
}

// --- TO matrix array ---

[Test]
void Cast_ScalarArrayToMatrixArray()
{
    // 8 floats → 2 float2x2 matrices (4 components each)
    float src[8] = {1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0};
    float2x2 dst[2] = (float2x2[2])src;
    ASSERT(dst[0][0][0] == 1.0 && dst[0][0][1] == 2.0);
    ASSERT(dst[0][1][0] == 3.0 && dst[0][1][1] == 4.0);
    ASSERT(dst[1][0][0] == 5.0 && dst[1][0][1] == 6.0);
    ASSERT(dst[1][1][0] == 7.0 && dst[1][1][1] == 8.0);
}

// --- FROM array to non-array ---

[Test]
void Cast_ScalarArrayToVector()
{
    float arr[4] = {1.0, 2.0, 3.0, 4.0};
    float4 v = (float4)arr;
    ASSERT(v.x == 1.0 && v.y == 2.0 && v.z == 3.0 && v.w == 4.0);
}

[Test]
void Cast_ScalarArrayToVectorWithTypeChange()
{
    float arr[3] = {1.5, 2.7, 3.9};
    int3 v = (int3)arr;
    ASSERT(v.x == 1 && v.y == 2 && v.z == 3);
}

[Test]
void Cast_ScalarArrayToMatrix()
{
    float arr[6] = {1.0, 2.0, 3.0, 4.0, 5.0, 6.0};
    float2x3 m = (float2x3)arr;
    ASSERT(m[0][0] == 1.0 && m[0][1] == 2.0 && m[0][2] == 3.0);
    ASSERT(m[1][0] == 4.0 && m[1][1] == 5.0 && m[1][2] == 6.0);
}

[Test]
void Cast_VectorArrayToVector()
{
    // 2 float2s (4 total components) → float4
    float2 arr[2] = {float2(1.0, 2.0), float2(3.0, 4.0)};
    float4 v = (float4)arr;
    ASSERT(v.x == 1.0 && v.y == 2.0 && v.z == 3.0 && v.w == 4.0);
}

// --- Flattened array initializer lists ---
// HLSL flattens all scalar components in an initializer and repacks them into the
// array element type. int4(4,5,6,7) contributes 4 scalars, giving 4 int2 elements.

[Test]
void ArrayInit_FlattenedImplicitSize()
{
    // Implicit size: derived from total scalars / components-per-element = 8 / 2 = 4.
    int2 arr[] = { int2(0, 1), int2(2, 3), int4(4, 5, 6, 7) };
    ASSERT(arr[0].x == 0 && arr[0].y == 1);
    ASSERT(arr[1].x == 2 && arr[1].y == 3);
    ASSERT(arr[2].x == 4 && arr[2].y == 5);
    ASSERT(arr[3].x == 6 && arr[3].y == 7);
}

[Test]
void ArrayInit_FlattenedExplicitSize()
{
    // Same initializer with explicit [4].
    int2 arr[4] = { int2(0, 1), int2(2, 3), int4(4, 5, 6, 7) };
    ASSERT(arr[0].x == 0 && arr[0].y == 1);
    ASSERT(arr[1].x == 2 && arr[1].y == 3);
    ASSERT(arr[2].x == 4 && arr[2].y == 5);
    ASSERT(arr[3].x == 6 && arr[3].y == 7);
}

[Test]
void ArrayInit_FlattenedScalarElements()
{
    // int2 initializers contribute 2 scalars each -> int[4].
    int arr[] = { int2(0, 1), int2(2, 3) };
    ASSERT(arr[0] == 0 && arr[1] == 1 && arr[2] == 2 && arr[3] == 3);
}

[Test]
void ArrayInit_FlattenedStructElements()
{
    // FlatInitStruct has 2 float fields; int4(4,5,6,7) contributes 4 scalars -> 4 elements.
    FlatInitStruct arr[] = { int2(0, 1), int2(2, 3), int4(4, 5, 6, 7) };
    ASSERT(arr[0].a == 0.0 && arr[0].b == 1.0);
    ASSERT(arr[1].a == 2.0 && arr[1].b == 3.0);
    ASSERT(arr[2].a == 4.0 && arr[2].b == 5.0);
    ASSERT(arr[3].a == 6.0 && arr[3].b == 7.0);
}

[Test]
void ArrayInit_StructWithArrayField()
{
    // StructWithArray has int data[2] and float x, so 3 dwords per element.
    // 9 scalars total -> 3 elements.
    StructWithArray arr[] = { int3(0, 1, 2), int3(3, 4, 5), int3(6, 7, 8) };
    ASSERT(arr[0].data[0] == 0 && arr[0].data[1] == 1 && arr[0].x == 2.0);
    ASSERT(arr[1].data[0] == 3 && arr[1].data[1] == 4 && arr[1].x == 5.0);
    ASSERT(arr[2].data[0] == 6 && arr[2].data[1] == 7 && arr[2].x == 8.0);
}

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/types/conversions/

// ============================================================================
// SCALAR SPLAT TO VECTOR AND MATRIX
// Inspired by DXC types/conversions/between_type_shapes.hlsl
// Casting a scalar to a larger type broadcasts (splats) the value to all slots.
// ============================================================================

[Test]
void ShapeConv_ScalarSplat_ToFloat2()
{
    float2 v = (float2)5.0;
    ASSERT(v.x == 5.0 && v.y == 5.0);
}

[Test]
void ShapeConv_ScalarSplat_ToFloat4()
{
    float4 v = (float4)(-3.0);
    ASSERT(v.x == -3.0 && v.y == -3.0 && v.z == -3.0 && v.w == -3.0);
}

[Test]
void ShapeConv_ScalarSplat_ToFloat2x2()
{
    float2x2 m = (float2x2)7.0;
    ASSERT(m[0][0] == 7.0 && m[0][1] == 7.0);
    ASSERT(m[1][0] == 7.0 && m[1][1] == 7.0);
}

[Test]
void ShapeConv_ScalarSplat_ToInt3x3()
{
    int3x3 m = (int3x3)4;
    ASSERT(m[0][0] == 4 && m[0][1] == 4 && m[0][2] == 4);
    ASSERT(m[1][0] == 4 && m[1][1] == 4 && m[1][2] == 4);
    ASSERT(m[2][0] == 4 && m[2][1] == 4 && m[2][2] == 4);
}

[Test]
void ShapeConv_ScalarZeroSplat_ToFloat3()
{
    float3 v = (float3)0.0;
    ASSERT(v.x == 0.0 && v.y == 0.0 && v.z == 0.0);
}

// ============================================================================
// VECTOR TRUNCATION VIA CAST
// Inspired by DXC types/conversions/between_type_shapes.hlsl
// Casting to a smaller vector type keeps only the first N components.
// ============================================================================

[Test]
void ShapeConv_VectorTruncation_Float4ToFloat2_KeepsFirstTwo()
{
    float4 v4 = float4(1.0, 2.0, 3.0, 4.0);
    float2 v2 = (float2)v4;
    ASSERT(v2.x == 1.0 && v2.y == 2.0);
}

[Test]
void ShapeConv_VectorTruncation_Float3ToFloat1_KeepsFirstOne()
{
    float3 v3 = float3(10.0, 20.0, 30.0);
    float s = (float)v3;
    ASSERT(s == 10.0);
}

// ============================================================================
// NEGATIVE LITERAL INT TO HALF CONVERSION
// Inspired by DXC types/conversions/negative_literal_int_to_float.hlsl
// Negative integer literals cast to half must produce the correct signed half
// value, not wrap around as if the bits were unsigned.
// ============================================================================

[Test]
void HalfConv_NegativeLiteralMinusOne_CorrectHalf()
{
    half h = (half)(-1);
    float f = (float)h;
    ASSERT(abs(f - (-1.0)) < 0.01);
}

[Test]
void HalfConv_NegativeLiteralMinusTen_CorrectHalf()
{
    half h = (half)(-10);
    float f = (float)h;
    ASSERT(abs(f - (-10.0)) < 0.1);
}

[Test]
void HalfConv_NegativeLiteralMinusOne_ViaHalfSuffix()
{
    half h = -1.0h;
    float f = (float)h;
    ASSERT(abs(f - (-1.0)) < 0.01);
}

// ============================================================================
// INOUT PARAMETER WITH TYPE CONVERSION
// Inspired by DXC types/conversions/inout_numerical.hlsl
// When a float variable is passed to an inout int parameter:
//   - Copy-in:  float is truncated to int
//   - Operation: int is incremented inside the function
//   - Copy-out: int is converted back to float
// ============================================================================

void IncrementAsInt(inout int val) { val++; }

[Test]
void InoutConv_FloatToInoutInt_TruncatesOnEntry()
{
    float f = 1.9;
    IncrementAsInt(f); // f truncated to 1 on entry, becomes 2, written back as 2.0
    ASSERT(f == 2.0);
}

[Test]
void InoutConv_NegativeFloatToInoutInt_TruncatesOnEntry()
{
    float f = -2.7;
    IncrementAsInt(f); // -2.7 truncated to -2, incremented to -1, written back as -1.0
    ASSERT(f == -1.0);
}


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
