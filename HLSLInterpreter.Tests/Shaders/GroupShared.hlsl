groupshared uint4 GroupSharedUint4;

[Test]
void GroupSharedVector_WhenWritten_NoVectorization()
{
    GroupSharedUint4[WaveGetLaneIndex()] = WaveGetLaneIndex();
    ASSERT(GroupSharedUint4 == uint4(0, 1, 2, 3));
}

groupshared uint GroupSharedUintArray[4];

[Test]
void GroupSharedArray_WhenWritten_NoVectorization()
{
    GroupSharedUintArray[WaveGetLaneIndex()] = WaveGetLaneIndex();
    ASSERT(GroupSharedUintArray[0] == 0);
    ASSERT(GroupSharedUintArray[1] == 1);
    ASSERT(GroupSharedUintArray[2] == 2);
    ASSERT(GroupSharedUintArray[3] == 3);
}

// All threads read the same groupshared value written by a uniform index.
groupshared int GroupSharedScalarArray[4];

[Test]
void GroupSharedArray_UniformWrite_AllThreadsSeeUpdate()
{
    GroupSharedScalarArray[2] = 42;
    ASSERT(GroupSharedScalarArray[2] == 42);
}

// Groupshared written inside divergent control flow: only active threads write.
groupshared int GroupSharedDivergent[4];

[Test]
[WarpSize(4, 1)]
void GroupSharedArray_DivergentWrite_OnlyActiveThreadsWrite()
{
    GroupSharedDivergent[WaveGetLaneIndex()] = 0;

    // Only threads 0 and 1 (lane < 2) write their index.
    if (WaveGetLaneIndex() < 2)
        GroupSharedDivergent[WaveGetLaneIndex()] = WaveGetLaneIndex() + 10;

    ASSERT(GroupSharedDivergent[0] == 10);
    ASSERT(GroupSharedDivergent[1] == 11);
    // Threads 2 and 3 were inactive during the conditional write, so values stay 0.
    ASSERT(GroupSharedDivergent[2] == 0);
    ASSERT(GroupSharedDivergent[3] == 0);
}

// Groupshared vector written by a uniform index is visible to all threads.
groupshared float4 GroupSharedFloat4;

[Test]
void GroupSharedVector_UniformWrite_AllThreadsSeeUpdate()
{
    GroupSharedFloat4 = float4(1, 2, 3, 4);
    ASSERT(GroupSharedFloat4.x == 1);
    ASSERT(GroupSharedFloat4.y == 2);
    ASSERT(GroupSharedFloat4.z == 3);
    ASSERT(GroupSharedFloat4.w == 4);
}

groupshared float4x4 GroupSharedMatrix;

[Test]
[WarpSize(4, 1)]
void GroupSharedMatrix_WhenWritten_NoVectorization()
{
    GroupSharedMatrix[WaveGetLaneIndex()] = WaveGetLaneIndex();
    ASSERT(GroupSharedMatrix[0][0] == 0);
    ASSERT(GroupSharedMatrix[1][0] == 1);
    ASSERT(GroupSharedMatrix[2][0] == 2);
    ASSERT(GroupSharedMatrix[3][0] == 3);
}

[Test]
void GroupSharedVector_CanBeShadowedByLocal()
{
    uint4 GroupSharedUint4 = 0;
    GroupSharedUint4[WaveGetLaneIndex()] = WaveGetLaneIndex();
    ASSERT(WaveReadLaneAt(GroupSharedUint4, 0) == uint4(0, 0, 0, 0));
    ASSERT(WaveReadLaneAt(GroupSharedUint4, 1) == uint4(0, 1, 0, 0));
    ASSERT(WaveReadLaneAt(GroupSharedUint4, 2) == uint4(0, 0, 2, 0));
    ASSERT(WaveReadLaneAt(GroupSharedUint4, 3) == uint4(0, 0, 0, 3));
}

[Test]
void GroupSharedVector_WhenWrittenWithRace_StaysUniform()
{
    // Initialize
    GroupSharedFloat4 = 0;

    // Write varying value
    GroupSharedFloat4 = WaveGetLaneIndex();

    // One thread will win!
    ASSERT(WaveActiveAllEqual(GroupSharedFloat4));
}

[Test]
void GroupSharedArray_WhenWrittenWithRace_StaysUniform()
{
    // Initialize
    GroupSharedUintArray[0] = 0;

    // Write varying value
    GroupSharedUintArray[0] = WaveGetLaneIndex();

    // One thread will win!
    ASSERT(WaveActiveAllEqual(GroupSharedUintArray[0]));
}

[Test]
void GroupSharedVectorSwizzle_WhenWrittenWithRace_StaysUniform()
{
    GroupSharedFloat4.xzy = 0;
    GroupSharedFloat4.xzy = WaveGetLaneIndex();
    ASSERT(WaveActiveAllEqual(GroupSharedFloat4.xzy));
}

[Test]
void GroupSharedMatrixSwizzle_WhenWrittenWithRace_StaysUniform()
{
    GroupSharedMatrix._11 = 0;
    GroupSharedMatrix._11 = WaveGetLaneIndex();
    ASSERT(WaveActiveAllEqual(GroupSharedMatrix._11));
}

struct MyStruct { float x; float y; };
groupshared MyStruct GroupSharedStruct;

[Test]
void GroupSharedStructMember_WhenWrittenWithRace_StaysUniform()
{
    GroupSharedStruct.x = 0;
    GroupSharedStruct.x = WaveGetLaneIndex();
    ASSERT(WaveActiveAllEqual(GroupSharedStruct.x));
    ASSERT(WaveReadLaneAt(GroupSharedStruct.x, 0) == WaveReadLaneAt(GroupSharedStruct.x, 3));
}

groupshared float4 ReductionBuffer;

[Test]
void GroupShared_Reduction()
{
    ReductionBuffer[WaveGetLaneIndex()] = WaveGetLaneIndex();

    if (WaveIsFirstLane())
    {
        float sum = 0;
        for (int i = 0; i < 4; i++)
        {
            sum += ReductionBuffer[i];
        }
        ReductionBuffer[0] = sum;
    }

    ASSERT(ReductionBuffer[0] == 0+1+2+3);
}

groupshared int GroupSharedSingleInt;

// Divergent if, scalar groupshared, uniform RHS: only the active thread writes,
// all threads should read the new value afterward.
[Test]
void GroupSharedScalar_DivergentWrite_UniformRHS()
{
    GroupSharedSingleInt = 0;
    if (WaveIsFirstLane())
        GroupSharedSingleInt = 99;
    ASSERT(GroupSharedSingleInt == 99);
}

// Divergent if, scalar groupshared, uniform RHS: read-modify-write on same slot.
[Test]
void GroupSharedScalar_DivergentReadModifyWrite()
{
    GroupSharedSingleInt = 10;
    if (WaveIsFirstLane())
        GroupSharedSingleInt += 5;
    ASSERT(GroupSharedSingleInt == 15);
}

// Divergent if, array with varying LHS and uniform RHS: only active threads write
// their respective slots; inactive threads leave their slots untouched.
[Test]
[WarpSize(4, 1)]
void GroupSharedArray_DivergentWrite_VaryingLHS_UniformRHS()
{
    GroupSharedDivergent[WaveGetLaneIndex()] = 0;
    // Only threads 2 and 3 are active.
    if (WaveGetLaneIndex() >= 2)
        GroupSharedDivergent[WaveGetLaneIndex()] = 99;
    ASSERT(GroupSharedDivergent[0] == 0);
    ASSERT(GroupSharedDivergent[1] == 0);
    ASSERT(GroupSharedDivergent[2] == 99);
    ASSERT(GroupSharedDivergent[3] == 99);
}

// Divergent if, array with uniform LHS and varying RHS: among the active threads
// all writing to the same slot, the last active thread wins.
[Test]
[WarpSize(4, 1)]
void GroupSharedArray_DivergentWrite_UniformLHS_VaryingRHS_LastActiveWins()
{
    GroupSharedDivergent[0] = -1;
    // Threads 0, 1, 2 are active; each writes its lane index to slot 0.
    if (WaveGetLaneIndex() < 3)
        GroupSharedDivergent[0] = WaveGetLaneIndex();
    // Thread 2 is the last active thread, so its value wins.
    ASSERT(GroupSharedDivergent[0] == 2);
}

// Divergent if, array with uniform LHS and uniform RHS.
[Test]
void GroupSharedArray_DivergentWrite_UniformLHS_UniformRHS()
{
    GroupSharedScalarArray[0] = 0;
    if (WaveIsFirstLane())
        GroupSharedScalarArray[0] = 42;
    ASSERT(GroupSharedScalarArray[0] == 42);
}

// Divergent if, array read-modify-write on the same slot.
[Test]
void GroupSharedArray_DivergentReadModifyWrite()
{
    GroupSharedScalarArray[1] = 100;
    if (WaveIsFirstLane())
        GroupSharedScalarArray[1] += 7;
    ASSERT(GroupSharedScalarArray[1] == 107);
}

// Divergent if, struct members with uniform LHS and uniform RHS.
[Test]
void GroupSharedStruct_DivergentWrite()
{
    GroupSharedStruct.x = 0;
    GroupSharedStruct.y = 0;
    if (WaveIsFirstLane())
    {
        GroupSharedStruct.x = 1;
        GroupSharedStruct.y = 2;
    }
    ASSERT(GroupSharedStruct.x == 1);
    ASSERT(GroupSharedStruct.y == 2);
}

// Divergent if, vector swizzle with uniform LHS and uniform RHS.
[Test]
void GroupSharedVector_DivergentSwizzleWrite()
{
    GroupSharedFloat4 = 0;
    if (WaveIsFirstLane())
        GroupSharedFloat4.xy = float2(3, 7);
    ASSERT(GroupSharedFloat4.x == 3);
    ASSERT(GroupSharedFloat4.y == 7);
    ASSERT(GroupSharedFloat4.z == 0);
    ASSERT(GroupSharedFloat4.w == 0);
}

// Divergent if, matrix row with uniform LHS and uniform RHS.
[Test]
[WarpSize(4, 1)]
void GroupSharedMatrix_DivergentRowWrite()
{
    GroupSharedMatrix = 0;
    if (WaveIsFirstLane())
        GroupSharedMatrix[1] = float4(10, 20, 30, 40);
    ASSERT(GroupSharedMatrix[0][0] == 0);
    ASSERT(GroupSharedMatrix[1][0] == 10);
    ASSERT(GroupSharedMatrix[1][1] == 20);
    ASSERT(GroupSharedMatrix[1][2] == 30);
    ASSERT(GroupSharedMatrix[1][3] == 40);
    ASSERT(GroupSharedMatrix[2][0] == 0);
}

groupshared float ReductionBuffer2[4];

[Test]
void GroupShared_Reduction_Array()
{
    ReductionBuffer2[WaveGetLaneIndex()] = WaveGetLaneIndex();

    if (WaveIsFirstLane())
    {
        float sum = 0;
        for (int i = 0; i < 4; i++)
        {
            sum += ReductionBuffer2[i];
        }
        ReductionBuffer2[0] = sum;
    }

    ASSERT(ReductionBuffer2[0] == 0+1+2+3);
}

// ============================================================
// Interlocked intrinsics on groupshared variables
// ============================================================

groupshared int  gs_IntVal;
groupshared uint gs_UintVal;

[Test]
[WarpSize(1,1)]
void Interlocked_Add()
{
    gs_IntVal = 10;
    InterlockedAdd(gs_IntVal, 5);
    ASSERT(gs_IntVal == 15);
}

[Test]
[WarpSize(1,1)]
void Interlocked_Add_ReturnsOriginal()
{
    gs_IntVal = 10;
    int orig;
    InterlockedAdd(gs_IntVal, 3, orig);
    ASSERT(orig == 10);
    ASSERT(gs_IntVal == 13);
}

[Test]
[WarpSize(1,1)]
void Interlocked_And()
{
    gs_UintVal = 0xF0u;
    InterlockedAnd(gs_UintVal, 0x3Cu);
    ASSERT(gs_UintVal == 0x30u);
}

[Test]
[WarpSize(1,1)]
void Interlocked_Or()
{
    gs_UintVal = 0x0Fu;
    InterlockedOr(gs_UintVal, 0xF0u);
    ASSERT(gs_UintVal == 0xFFu);
}

[Test]
[WarpSize(1,1)]
void Interlocked_Xor()
{
    gs_UintVal = 0xFFu;
    InterlockedXor(gs_UintVal, 0x0Fu);
    ASSERT(gs_UintVal == 0xF0u);
}

[Test]
[WarpSize(1,1)]
void Interlocked_Min()
{
    gs_IntVal = 10;
    InterlockedMin(gs_IntVal, 3);
    ASSERT(gs_IntVal == 3);
    InterlockedMin(gs_IntVal, 99);
    ASSERT(gs_IntVal == 3);
}

[Test]
[WarpSize(1,1)]
void Interlocked_Max()
{
    gs_IntVal = 10;
    InterlockedMax(gs_IntVal, 20);
    ASSERT(gs_IntVal == 20);
    InterlockedMax(gs_IntVal, 5);
    ASSERT(gs_IntVal == 20);
}

[Test]
[WarpSize(1,1)]
void Interlocked_Exchange()
{
    gs_IntVal = 42;
    int orig;
    InterlockedExchange(gs_IntVal, 7, orig);
    ASSERT(orig == 42);
    ASSERT(gs_IntVal == 7);
}

[Test]
[WarpSize(1,1)]
void Interlocked_CompareStore_Matches()
{
    gs_UintVal = 5u;
    InterlockedCompareStore(gs_UintVal, 5u, 99u);
    ASSERT(gs_UintVal == 99u);
}

[Test]
[WarpSize(1,1)]
void Interlocked_CompareStore_NoMatch()
{
    gs_UintVal = 5u;
    InterlockedCompareStore(gs_UintVal, 9u, 99u);
    ASSERT(gs_UintVal == 5u);
}

[Test]
[WarpSize(1,1)]
void Interlocked_CompareExchange()
{
    gs_UintVal = 5u;
    uint orig;
    InterlockedCompareExchange(gs_UintVal, 5u, 99u, orig);
    ASSERT(orig == 5u);
    ASSERT(gs_UintVal == 99u);
}

// Only the first lane performs the add; final value should be 10 + 3 = 13.
[Test]
void Interlocked_Add_DivergentWrite_OnlyActiveThreadWrites()
{
    gs_IntVal = 10;
    if (WaveIsFirstLane())
        InterlockedAdd(gs_IntVal, 3);
    ASSERT(gs_IntVal == 13);
}

// Each lane adds its own lane index; with 4 lanes: 0+1+2+3 = 6, so 10+6 = 16.
[Test]
[WarpSize(4, 1)]
void Interlocked_Add_VaryingOperand()
{
    gs_IntVal = 10;
    InterlockedAdd(gs_IntVal, (int)WaveGetLaneIndex());
    ASSERT(gs_IntVal == 16);
}

// Capture originals: each lane reads the value before its own add.
// Lane 0 reads 10, writes 10+0=10. Lane 1 reads 10, writes 11. Lane 2 reads 11, writes 13. Lane 3 reads 13, writes 16.
[Test]
[WarpSize(4, 1)]
void Interlocked_Add_VaryingOperand_OriginalPerThread()
{
    gs_IntVal = 10;
    int orig;
    InterlockedAdd(gs_IntVal, (int)WaveGetLaneIndex(), orig);
    ASSERT(WaveReadLaneAt(orig, 0) == 10);
    ASSERT(WaveReadLaneAt(orig, 1) == 10);
    ASSERT(WaveReadLaneAt(orig, 2) == 11);
    ASSERT(WaveReadLaneAt(orig, 3) == 13);
    ASSERT(gs_IntVal == 16);
}

// Only the first lane runs; the exchange should only happen once.
[Test]
void Interlocked_Exchange_DivergentWrite_OnlyActiveThreadWrites()
{
    gs_IntVal = 42;
    int orig;
    if (WaveIsFirstLane())
        InterlockedExchange(gs_IntVal, 7, orig);
    ASSERT(gs_IntVal == 7);
}

[Test]
void Interlocked_BarrierSyncVariants_AreNoOps()
{
    gs_IntVal = 1;
    AllMemoryBarrierWithGroupSync();
    DeviceMemoryBarrierWithGroupSync();
    GroupMemoryBarrierWithGroupSync();
    ASSERT(gs_IntVal == 1);
}