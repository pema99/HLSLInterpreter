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