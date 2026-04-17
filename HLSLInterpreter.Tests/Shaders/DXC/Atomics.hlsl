#include "../HLSLTest.hlsl"

// Tests for atomic (Interlocked) operations on non-scalar targets:
// array elements and struct member fields in groupshared memory.
// Inspired by DXC tests that target swizzled/member-accessed groupshared slots.

// ============================================================================
// INTERLOCKED OPS ON GROUPSHARED ARRAY ELEMENTS
// The first argument to an Interlocked intrinsic is an inout reference.
// Passing arr[i] must produce a stable lvalue that reads and writes back
// to the correct slot, not to the array as a whole.
// ============================================================================

groupshared int gs_IntArray[4];

[Test]
[WarpSize(1,1)]
void Atomic_Array_InterlockedAdd_SpecificElement()
{
    gs_IntArray[0] = 10;
    gs_IntArray[1] = 20;
    gs_IntArray[2] = 30;
    gs_IntArray[3] = 40;

    InterlockedAdd(gs_IntArray[1], 5);

    ASSERT(gs_IntArray[0] == 10); // unchanged
    ASSERT(gs_IntArray[1] == 25); // 20 + 5
    ASSERT(gs_IntArray[2] == 30); // unchanged
    ASSERT(gs_IntArray[3] == 40); // unchanged
}

[Test]
[WarpSize(1,1)]
void Atomic_Array_InterlockedAdd_ReturnsOriginal()
{
    gs_IntArray[2] = 100;
    int orig;
    InterlockedAdd(gs_IntArray[2], 7, orig);
    ASSERT(orig == 100);
    ASSERT(gs_IntArray[2] == 107);
}

[Test]
[WarpSize(1,1)]
void Atomic_Array_InterlockedMax_OnElement()
{
    gs_IntArray[0] = 5;
    InterlockedMax(gs_IntArray[0], 12);
    ASSERT(gs_IntArray[0] == 12);
    InterlockedMax(gs_IntArray[0], 3);
    ASSERT(gs_IntArray[0] == 12); // no change, 3 < 12
}

[Test]
[WarpSize(1,1)]
void Atomic_Array_InterlockedExchange_OnElement()
{
    gs_IntArray[3] = 99;
    int prev;
    InterlockedExchange(gs_IntArray[3], 42, prev);
    ASSERT(prev == 99);
    ASSERT(gs_IntArray[3] == 42);
}

// ============================================================================
// INTERLOCKED OPS ON STRUCT MEMBER FIELDS
// The target is a field of a groupshared struct — the lvalue reference must
// point into the struct's field, not to the struct itself.
// ============================================================================

struct AtomicPoint { int x; int y; };
groupshared AtomicPoint gs_Point;

[Test]
[WarpSize(1,1)]
void Atomic_StructMember_InterlockedAdd_X()
{
    gs_Point.x = 10;
    gs_Point.y = 20;

    InterlockedAdd(gs_Point.x, 5);

    ASSERT(gs_Point.x == 15); // 10 + 5
    ASSERT(gs_Point.y == 20); // unchanged
}

[Test]
[WarpSize(1,1)]
void Atomic_StructMember_InterlockedAdd_Y()
{
    gs_Point.x = 3;
    gs_Point.y = 7;

    InterlockedAdd(gs_Point.y, 13);

    ASSERT(gs_Point.x == 3);  // unchanged
    ASSERT(gs_Point.y == 20); // 7 + 13
}

[Test]
[WarpSize(1,1)]
void Atomic_StructMember_InterlockedMin_X()
{
    gs_Point.x = 50;
    InterlockedMin(gs_Point.x, 30);
    ASSERT(gs_Point.x == 30);
    InterlockedMin(gs_Point.x, 99);
    ASSERT(gs_Point.x == 30); // no change
}

[Test]
[WarpSize(1,1)]
void Atomic_StructMember_InterlockedExchange_ReturnsOriginal()
{
    gs_Point.x = 77;
    int old;
    InterlockedExchange(gs_Point.x, 11, old);
    ASSERT(old == 77);
    ASSERT(gs_Point.x == 11);
}

// ============================================================================
// INTERLOCKED OPS ON VECTOR COMPONENT (SWIZZLE TARGET)
// Inspired by DXC atomics tests targeting swizzled buffer access:
// InterlockedAdd(gs_vec.x, val) must write only to component x.
// ============================================================================

groupshared int2 gs_IntPair;

[Test]
[WarpSize(1,1)]
void Atomic_VectorComponent_InterlockedAdd_X()
{
    gs_IntPair.x = 100;
    gs_IntPair.y = 200;

    InterlockedAdd(gs_IntPair.x, 10);

    ASSERT(gs_IntPair.x == 110); // 100 + 10
    ASSERT(gs_IntPair.y == 200); // unchanged
}

[Test]
[WarpSize(1,1)]
void Atomic_VectorComponent_InterlockedAdd_Y()
{
    gs_IntPair.x = 1;
    gs_IntPair.y = 2;

    InterlockedAdd(gs_IntPair.y, 8);

    ASSERT(gs_IntPair.x == 1);  // unchanged
    ASSERT(gs_IntPair.y == 10); // 2 + 8
}

[Test]
[WarpSize(1,1)]
void Atomic_VectorComponent_InterlockedExchange_ReturnsOriginal()
{
    gs_IntPair.x = 55;
    gs_IntPair.y = 66;
    int old;
    InterlockedExchange(gs_IntPair.y, 99, old);
    ASSERT(old == 66);
    ASSERT(gs_IntPair.y == 99);
    ASSERT(gs_IntPair.x == 55); // unchanged
}
