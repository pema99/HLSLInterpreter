#include "HLSLTest.hlsl"

struct Bar
{
    int x;
    int y;
};

struct VecHolder
{
    float4 v;
};

void Increment(inout int val)
{
    val += 1;
}

void Increment1Thread(inout int val)
{
    if (WaveGetLaneIndex() == 0)
        val += 1;
}

void Swap(inout float2 v)
{
    v.xy = v.yx;
}

void AddWeird(inout float4 v)
{
    v.zyx.yx += float2(1, 2);
}

void Double(inout float2 v)
{
    v *= 2;
}

void Double(inout float4 v)
{
    v *= 2;
}

void Double1Thread(inout float2 v)
{
    if (WaveGetLaneIndex() == 0)
        v *= 2;
}

void SetXY(out int a, out int b)
{
    a = 10;
    b = 20;
}

void SetTo99(inout int val)
{
    val = 99;
}

void Chain(inout int val)
{
    SetTo99(val);
}

void ConditionalSet(inout int val, bool cond)
{
    if (cond)
    {
        val = 42;
    }
}

void AssignLaneIndex(inout int val)
{
    val = WaveGetLaneIndex();
}

void IncrementFloat(inout float val)
{
    val += 1;
}

[Test]
void InoutStructField_WritesBackToOriginalStruct()
{
    Bar b;
    b.x = 5;
    Increment(b.x);
    ASSERT(b.x == 6);
}

[Test]
void InoutArrayElement_WritesBackToOriginalArray()
{
    int arr[3];
    arr[0] = 1;
    arr[1] = 2;
    arr[2] = 3;
    Increment(arr[1]);
    ASSERT(arr[0] == 1);
    ASSERT(arr[1] == 3);
    ASSERT(arr[2] == 3);
}

[Test]
void InoutVectorSwizzle_WritesBackToOriginalVector()
{
    float4 v = float4(1, 2, 3, 4);
    Double(v.xy);
    ASSERT(v.x == 2);
    ASSERT(v.y == 4);
    ASSERT(v.z == 3);
    ASSERT(v.w == 4);
}

[Test]
void InoutVectorSwizzleInsideFunction_WritesBackToOriginalVector()
{
    float2 v = float2(1, 2);
    Swap(v);
    ASSERT(v.x == 2);
    ASSERT(v.y == 1);
}

[Test]
void InoutNestedVectorSwizzleInsideFunction_WritesBackToOriginalVector()
{
    float4 v = float4(1, 2, 3, 4);
    AddWeird(v);
    ASSERT(v.x == 1);
    ASSERT(v.y == 3);
    ASSERT(v.z == 5);
    ASSERT(v.w == 4);
}

[Test]
void InoutNestedVectorSwizzle_WritesBackToOriginalVector()
{
    float4 v = float4(1, 2, 3, 4);
    Double(v.zw.yx);
    ASSERT(v.x == 1);
    ASSERT(v.y == 2);
    ASSERT(v.z == 6);
    ASSERT(v.w == 8);
}

[Test]
void InoutNestedAccess_StructArrayField_WritesBack()
{
    Bar arr[2];
    arr[0].x = 7;
    arr[0].y = 0;
    Increment(arr[0].x);
    ASSERT(arr[0].x == 8);
}

[Test]
void OutMultipleNonSimpleLvalues_WritesBack()
{
    Bar b;
    SetXY(b.x, b.y);
    ASSERT(b.x == 10);
    ASSERT(b.y == 20);
}

// Case 1: Chaining inout — foo(inout a) calls bar(inout b) passing a through
[Test]
void InoutChained_WritebackPropagatesThroughChain()
{
    int x = 0;
    Chain(x);
    ASSERT(x == 99);
}

// Case 2: Varying control flow — only some threads take the branch
[Test]
[WarpSize(2, 1)]
void InoutVaryingControlFlow_OnlyActiveThreadsModified()
{
    int val = WaveGetLaneIndex(); // Thread 0: val=0, Thread 1: val=1
    ConditionalSet(val, WaveGetLaneIndex() == 0); // Only thread 0's branch fires
    ASSERT(WaveReadLaneAt(val, 0) == 42);
    ASSERT(WaveReadLaneAt(val, 1) == 1);
}

// Case 3: Varying RHS — the value assigned inside the function differs per thread
[Test]
[WarpSize(2, 1)]
void InoutVaryingRHS_EachThreadWritesOwnValue()
{
    int val = 0;
    AssignLaneIndex(val);
    ASSERT(WaveReadLaneAt(val, 0) == 0);
    ASSERT(WaveReadLaneAt(val, 1) == 1);
}

// Single vector component via swizzle (v.x) and index (v[1]) passed as inout
[Test]
void InoutVectorSwizzleSingle_WritesBack()
{
    float4 v = float4(1, 2, 3, 4);
    Increment(v.x);
    ASSERT(v.x == 2);
    ASSERT(v.y == 2); // unchanged
}

[Test]
void InoutVectorIndex_WritesBack()
{
    float4 v = float4(1, 2, 3, 4);
    Increment(v[1]);
    ASSERT(v.x == 1); // unchanged
    ASSERT(v.y == 3);
}

[Test]
[WarpSize(2, 1)]
void InoutVectorVaryingIndex_ScatteredWriteBack()
{
    float4 v = float4(1, 2, 3, 4);
    // Thread 0 increments v[0], thread 1 increments v[1]
    Increment(v[WaveGetLaneIndex()]);
    // Each thread only saw its own write-back
    ASSERT(WaveReadLaneAt(v.x, 0) == 2); // thread 0 incremented v[0]: 1->2
    ASSERT(WaveReadLaneAt(v.y, 0) == 2); // thread 0 left v[1] unchanged
    ASSERT(WaveReadLaneAt(v.x, 1) == 1); // thread 1 left v[0] unchanged
    ASSERT(WaveReadLaneAt(v.y, 1) == 3); // thread 1 incremented v[1]: 2->3
}

// Swizzle nested inside a struct field passed as inout — MyStruct.MyVector.xz
[Test]
void InoutSwizzleNestedInStruct_WritesBack()
{
    VecHolder s;
    s.v = float4(1, 2, 3, 4);
    Double(s.v.xz);
    ASSERT(s.v.x == 2);
    ASSERT(s.v.y == 2); // unchanged
    ASSERT(s.v.z == 6);
    ASSERT(s.v.w == 4); // unchanged
}

// Matrix row passed as inout — write-back updates the original row in the matrix
[Test]
void InoutMatrixRow_WritesBackToOriginalMatrix()
{
    float4x4 m = float4x4(
        1, 2, 3, 4,
        5, 6, 7, 8,
        9, 10, 11, 12,
        13, 14, 15, 16
    );
    Double(m[0]);
    ASSERT(m[0][0] == 2);
    ASSERT(m[0][1] == 4);
    ASSERT(m[0][2] == 6);
    ASSERT(m[0][3] == 8);
    ASSERT(m[1][0] == 5); // other rows unchanged
}

// Case 5: Varying LHS — mat[threadIdx] as inout, each thread writes back to its own row
[Test]
[WarpSize(2, 1)]
void InoutMatrixVaryingRow_EachThreadWritesOwnRow()
{
    float4x4 m = float4x4(
        1, 2, 3, 4,
        5, 6, 7, 8,
        9, 10, 11, 12,
        13, 14, 15, 16
    );
    // Thread 0 doubles row 0, Thread 1 doubles row 1
    Double(m[WaveGetLaneIndex()]);
    ASSERT(WaveReadLaneAt(m[0][0], 0) == 2);
    ASSERT(WaveReadLaneAt(m[0][1], 0) == 4);
    ASSERT(WaveReadLaneAt(m[1][0], 0) == 5); // thread 0 left row 1 unchanged
    ASSERT(WaveReadLaneAt(m[1][0], 1) == 10);
    ASSERT(WaveReadLaneAt(m[1][1], 1) == 12);
    ASSERT(WaveReadLaneAt(m[0][0], 1) == 1); // thread 1 left row 0 unchanged
}

// Case 4: Varying LHS — arr[threadIdx] as inout, each thread writes back to its own slot
[Test]
[WarpSize(2, 1)]
void InoutScatteredWrite_VaryingIndex_EachThreadWritesOwnSlot()
{
    int arr[2];
    arr[0] = 10;
    arr[1] = 20;
    Increment(arr[WaveGetLaneIndex()]); // Thread 0: Increment(arr[0]), Thread 1: Increment(arr[1])
    ASSERT(WaveReadLaneAt(arr[0], 0) == 11);
    ASSERT(WaveReadLaneAt(arr[0], 1) == 10);
    ASSERT(WaveReadLaneAt(arr[1], 0) == 20);
    ASSERT(WaveReadLaneAt(arr[1], 1) == 21);
}

// =====================================================================
// Scenario 1: inout lvalue inside a divergent if-statement at the call site
// =====================================================================

[Test]
[WarpSize(2, 1)]
void InoutNamedExpression_VaryingControlFlow_WritesBackOnlyOnActiveLanes()
{
    int a = 10;
    Increment1Thread(a);
    ASSERT(WaveReadLaneAt(a, 0) == 11); // thread 0 incremented
    ASSERT(WaveReadLaneAt(a, 1) == 10); // thread 1 did not
}

[Test]
[WarpSize(2, 1)]
void InoutArrayElement_VaryingControlFlow_WritesBackOnlyOnActiveLanes()
{
    int arr[2];
    arr[0] = 10;
    arr[1] = 10;
    Increment1Thread(arr[0]);
    ASSERT(WaveReadLaneAt(arr[0], 0) == 11); // thread 0 incremented
    ASSERT(WaveReadLaneAt(arr[0], 1) == 10); // thread 1 did not
}

[Test]
[WarpSize(2, 1)]
void InoutVectorElement_VaryingControlFlow_WritesBackOnlyOnActiveLanes()
{
    int4 v = int4(10, 20, 30, 40);
    Increment1Thread(v[0]);
    ASSERT(WaveReadLaneAt(v[0], 0) == 11); // thread 0 incremented
    ASSERT(WaveReadLaneAt(v[0], 1) == 10); // thread 1 did not
}

[Test]
[WarpSize(2, 1)]
void InoutMatrixElement_VaryingControlFlow_WritesBackOnlyOnActiveLanes()
{
    int4x4 m = int4x4(
        1, 2, 3, 4,
        5, 6, 7, 8,
        9, 10, 11, 12,
        13, 14, 15, 16
    );
    Increment1Thread(m[0][0]);
    ASSERT(WaveReadLaneAt(m[0][0], 0) == 2); // thread 0 incremented
    ASSERT(WaveReadLaneAt(m[0][0], 1) == 1); // thread 1 did not
}

[Test]
[WarpSize(2, 1)]
void InoutStructMember_VaryingControlFlow_WritesBackOnlyOnActiveLanes()
{
    Bar b;
    b.x = 5;
    Increment1Thread(b.x);
    ASSERT(WaveReadLaneAt(b.x, 0) == 6); // thread 0 incremented
    ASSERT(WaveReadLaneAt(b.x, 1) == 5); // thread 1 did not
}

[Test]
[WarpSize(2, 1)]
void InoutVectorSwizzle_VaryingControlFlow_WritesBackOnlyOnActiveLanes()
{
    float2 v = float2(1, 2);
    Double1Thread(v.xy);
    ASSERT(WaveReadLaneAt(v.x, 0) == 2.0); // thread 0 doubled
    ASSERT(WaveReadLaneAt(v.y, 0) == 4.0);
    ASSERT(WaveReadLaneAt(v.x, 1) == 1.0); // thread 1 unchanged
    ASSERT(WaveReadLaneAt(v.y, 1) == 2.0);
}

// =====================================================================
// Scenario 2: inout parameter is itself varying (VGPR) before the call
// =====================================================================

[Test]
[WarpSize(2, 1)]
void InoutNamedExpression_VaryingParam_WritesBackCorrectly()
{
    int val = WaveGetLaneIndex(); // VGPR: thread 0 -> 0, thread 1 -> 1
    Increment(val);
    ASSERT(WaveReadLaneAt(val, 0) == 1);
    ASSERT(WaveReadLaneAt(val, 1) == 2);
}

[Test]
[WarpSize(2, 1)]
void InoutArrayElement_VaryingParam_WritesBackCorrectly()
{
    int arr[1];
    arr[0] = WaveGetLaneIndex(); // VGPR: thread 0 -> 0, thread 1 -> 1
    Increment(arr[0]);
    ASSERT(WaveReadLaneAt(arr[0], 0) == 1);
    ASSERT(WaveReadLaneAt(arr[0], 1) == 2);
}

[Test]
[WarpSize(2, 1)]
void InoutVectorElement_VaryingParam_WritesBackCorrectly()
{
    int4 v = 0;
    v[0] = WaveGetLaneIndex();
    Increment(v[0]);
    ASSERT(WaveReadLaneAt(v[0], 0) == 1);
    ASSERT(WaveReadLaneAt(v[0], 1) == 2);
}

[Test]
[WarpSize(2, 1)]
void InoutMatrixElement_VaryingParam_WritesBackCorrectly()
{
    float4x4 m = float4x4(
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0
    );
    m[0][1] = (float)WaveGetLaneIndex(); // VGPR: thread 0 -> 0.0, thread 1 -> 1.0
    IncrementFloat(m[0][1]);
    ASSERT(WaveReadLaneAt(m[0][1], 0) == 1.0);
    ASSERT(WaveReadLaneAt(m[0][1], 1) == 2.0);
}

[Test]
[WarpSize(2, 1)]
void InoutStructMember_VaryingParam_WritesBackCorrectly()
{
    Bar b;
    b.x = WaveGetLaneIndex(); // VGPR: thread 0 -> 0, thread 1 -> 1
    Increment(b.x);
    ASSERT(WaveReadLaneAt(b.x, 0) == 1);
    ASSERT(WaveReadLaneAt(b.x, 1) == 2);
}

[Test]
[WarpSize(2, 1)]
void InoutVectorSwizzle_VaryingParam_WritesBackCorrectly()
{
    float2 v;
    v.x = (float)WaveGetLaneIndex(); // VGPR: thread 0 -> 0.0, thread 1 -> 1.0
    v.y = 0;
    Double(v.xy);
    ASSERT(WaveReadLaneAt(v.x, 0) == 0.0); // 0 * 2 = 0
    ASSERT(WaveReadLaneAt(v.x, 1) == 2.0); // 1 * 2 = 2
}


// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/functions/arguments/

// ============================================================================
// OUT / INOUT PARAMETER BASICS
// ============================================================================

void SetThree(out int a, out int b, out int c)
{
    a = 1;
    b = 2;
    c = 3;
}

[Test]
void FuncArg_OutParam_ThreeOutParams_AllSet()
{
    int a = 0, b = 0, c = 0;
    SetThree(a, b, c);
    ASSERT(a == 1 && b == 2 && c == 3);
}

// ============================================================================
// INOUT ALIASING (copyin-copyout semantics)
// Inspired by copyin-copyout.hlsl: passing the same variable to multiple
// inout parameters. Both params alias the same memory slot, so the last
// write wins.
// ============================================================================

void AliasSetXY(inout int x, inout int y)
{
    x = 10;
    y = 20;
}

[Test]
void FuncArg_InoutAlias_SameVarBothParams_SecondWriteWins()
{
    int v = 0;
    AliasSetXY(v, v);
    ASSERT(v == 20);
}

void IncrementBoth(inout int x, inout int y)
{
    x = x + 1;
    y = y + 1;
}

[Test]
void FuncArg_InoutAlias_IncrementSameVar_AliasedReadsAndWrites()
{
    // DXC uses copy-in/copy-out: both x and y copy v=0 on entry.
    // x = x + 1 → x=1, y = y + 1 → y=1. On exit, y's copy-out (v=1) wins.
    int v = 0;
    IncrementBoth(v, v);
    ASSERT(v == 1);
}

void IncrementBothF(inout float x, inout float y)
{
    x = x + 1.0;
    y = y + 1.0;
}

[Test]
void FuncArg_InoutAlias_Float_AliasedReadsAndWrites()
{
    // x is a reference to v (v=5→6), y is a copy of the original 5 (y=6), copy-out: v=6
    float v = 5.0;
    IncrementBothF(v, v);
    ASSERT(v == 6.0);
}

// ============================================================================
// INOUT ALIASING WITH STRUCT AND STRUCT FIELD
// Inspired by copyin-copyout-struct.hlsl
// ============================================================================

struct Pup
{
    float X;
};

void SetFieldAndStruct(inout float f, inout Pup p)
{
    f = 4.0;
    p.X = 5.0;
}

[Test]
void FuncArg_InoutAlias_DifferentVars_BothModified()
{
    float x = 0.0;
    Pup p;
    p.X = 0.0;
    SetFieldAndStruct(x, p);
    ASSERT(x == 4.0);
    ASSERT(p.X == 5.0);
}

[Test]
void FuncArg_InoutAlias_StructFieldAndStruct_CopyoutWins()
{
    // p.X is passed as the float ref (f), p is passed as inout struct (aliased -> copy).
    // f=4.0 writes directly to p.X via reference; p.X=5.0 writes to the copy.
    // Copy-out restores p from the copy, so p.X ends up as 5.0.
    Pup p;
    p.X = 0.0;
    SetFieldAndStruct(p.X, p);
    ASSERT(p.X == 5.0);
}

// ============================================================================
// ARRAY OUT / INOUT PARAMETERS
// Inspired by outputArray.hlsl: arrays passed as out/inout params and
// modifications propagate back to the caller.
// ============================================================================

void FillArray(out float arr[3])
{
    arr[0] = 10.0;
    arr[1] = 20.0;
    arr[2] = 30.0;
}

[Test]
void FuncArg_ArrayOut_FillArray_CallerSeesNewValues()
{
    float arr[3];
    arr[0] = 0.0;
    arr[1] = 0.0;
    arr[2] = 0.0;
    FillArray(arr);
    ASSERT(arr[0] == 10.0);
    ASSERT(arr[1] == 20.0);
    ASSERT(arr[2] == 30.0);
}

void DoubleArrayInout(inout float arr[4])
{
    for (int i = 0; i < 4; i++)
        arr[i] *= 2.0;
}

[Test]
void FuncArg_ArrayInout_ElementsDoubled_CallerSeesNewValues()
{
    float arr[4] = { 1.0, 2.0, 3.0, 4.0 };
    DoubleArrayInout(arr);
    ASSERT(arr[0] == 2.0);
    ASSERT(arr[1] == 4.0);
    ASSERT(arr[2] == 6.0);
    ASSERT(arr[3] == 8.0);
}

void SumArray(float arr[3], out float result)
{
    result = arr[0] + arr[1] + arr[2];
}

[Test]
void FuncArg_ArrayByRef_PassedToOutParam_SumComputed()
{
    float arr[3] = { 5.0, 10.0, 15.0 };
    float sum = 0.0;
    SumArray(arr, sum);
    ASSERT(sum == 30.0);
}

// ============================================================================
// FUNCTION RETURNING STRUCT
// Inspired by copyin-copyout-struct.hlsl / structArray.hlsl
// ============================================================================

struct Pair
{
    int first;
    int second;
};

Pair MakePair(int a, int b)
{
    Pair p;
    p.first = a;
    p.second = b;
    return p;
}

[Test]
void FuncArg_ReturnStruct_HasCorrectFields()
{
    Pair p = MakePair(7, 13);
    ASSERT(p.first == 7);
    ASSERT(p.second == 13);
}

[Test]
void FuncArg_ReturnStruct_UsedInExpression()
{
    Pair p = MakePair(3, 4);
    ASSERT(p.first + p.second == 7);
}

// ============================================================================
// FUNCTION PARAMETER: STRUCT INOUT
// ============================================================================

void SwapPair(inout Pair p)
{
    int tmp = p.first;
    p.first = p.second;
    p.second = tmp;
}

[Test]
void FuncArg_InoutStruct_SwapFields_CallerSeesSwap()
{
    Pair p = MakePair(1, 2);
    SwapPair(p);
    ASSERT(p.first == 2);
    ASSERT(p.second == 1);
}

// ============================================================================
// PARAMETER TYPES: scalar, vector, matrix, struct
// Inspired by parameter_types.hlsl
// ============================================================================

struct TypesStruct
{
    float a;
    float4 b;
};

float SumTypeParams(float a, float4 b, TypesStruct t, float2x2 m)
{
    return a + t.a + b.x + m[0][0];
}

[Test]
void FuncArg_MixedParameterTypes_ComputedCorrectly()
{
    TypesStruct ts;
    ts.a = 1.0;
    ts.b = float4(0, 0, 0, 0);

    float2x2 m = float2x2(2.0, 0.0, 0.0, 0.0);
    float result = SumTypeParams(10.0, float4(5.0, 0.0, 0.0, 0.0), ts, m);
    ASSERT(result == 18.0); // 10 + 1 + 5 + 2
}

// ============================================================================
// CHAINED INOUT CALLS
// Inspired by copyin-copyout.hlsl: function passing its own inout param
// to another function.
// ============================================================================

void AddTen(inout int x)
{
    x += 10;
}

void AddTwenty(inout int x)
{
    AddTen(x);
    AddTen(x);
}

[Test]
void FuncArg_ChainedInout_AddedTwice()
{
    int v = 5;
    AddTwenty(v);
    ASSERT(v == 25);
}

// ============================================================================
// MULTIPLE OUT PARAMS WITH STRUCT RETURN
// ============================================================================

void DecomposePair(Pair p, out int first, out int second)
{
    first = p.first;
    second = p.second;
}

[Test]
void FuncArg_DecomposeStruct_OutParams_HaveCorrectValues()
{
    Pair p = MakePair(42, 99);
    int a, b;
    DecomposePair(p, a, b);
    ASSERT(a == 42);
    ASSERT(b == 99);
}
