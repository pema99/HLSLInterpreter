#include "../HLSLTest.hlsl"

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

void SetXY(inout int x, inout int y)
{
    x = 10;
    y = 20;
}

[Test]
void FuncArg_InoutAlias_SameVarBothParams_SecondWriteWins()
{
    int v = 0;
    SetXY(v, v);
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
