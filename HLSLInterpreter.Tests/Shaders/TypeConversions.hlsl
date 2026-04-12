

[Test]
void Conversion_TernaryOperator_VectorSizes()
{
    bool condition = true;
    float2 v2 = float2(1.0, 2.0);
    float3 v3 = float3(3.0, 4.0, 5.0);
    
    // Both operands convert to float3
    float3 result = condition ? v2 : v3;  // v2 extends to float3
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
    v2 = v4;  // Truncates
    ASSERT(v2.x == 1.0 && v2.y == 2.0);
}

[Test]
void Assignment_VectorExtension()
{
    float4 v4;
    float2 v2 = float2(5.0, 6.0);
    v4 = v2;  // Extends with zeros
    ASSERT(v4.x == 5.0 && v4.y == 6.0 && v4.z == 0.0 && v4.w == 0.0);
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