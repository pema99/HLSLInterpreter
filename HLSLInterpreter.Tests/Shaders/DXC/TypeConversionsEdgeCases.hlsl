#include "../HLSLTest.hlsl"

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
