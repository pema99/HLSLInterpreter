#include "HLSLTest.hlsl"

// ============================================================================
// Multi-warp threadgroups: virtual warp boundaries on wave intrinsics.
// ============================================================================

[Test]
[WarpSize(2, 2)]
[numthreads(4, 4, 1)]
void Threadgroup_2x2Warps_4x4Group_LaneCountAndIndex(uint flat : SV_GroupIndex)
{
    // 4 warps, each with 4 lanes.
    ASSERT(WaveGetLaneCount() == 4);
    uint lane = WaveGetLaneIndex();
    ASSERT(lane < 4);
    // Each warp's lanes sum to 0+1+2+3 = 6 independently.
    ASSERT(WaveActiveSum(lane) == 6);
}

[Test]
[WarpSize(2, 2)]
[numthreads(4, 4, 1)]
void Threadgroup_WaveActiveSum_VariesPerWarp(uint flat : SV_GroupIndex)
{
    // Sum of SV_GroupIndex within each warp differs by warp.
    // Warp 0 covers lanes 0,1,4,5 (group indices); warp 1 covers 2,3,6,7;
    // warp 2 covers 8,9,12,13; warp 3 covers 10,11,14,15.
    uint s = WaveActiveSum(flat);
    if (flat == 0) ASSERT(s == 0 + 1 + 4 + 5);
    if (flat == 2) ASSERT(s == 2 + 3 + 6 + 7);
    if (flat == 8) ASSERT(s == 8 + 9 + 12 + 13);
    if (flat == 10) ASSERT(s == 10 + 11 + 14 + 15);
}

[Test]
[WarpSize(2, 2)]
[numthreads(4, 4, 1)]
void Threadgroup_WaveActiveBallot_PerWarp()
{
    // Every active lane within each warp sets its own bit.
    uint4 ballot = WaveActiveBallot(true);
    // 4 lanes per warp -> low nibble of word 0 is 0xF, all other bits zero.
    ASSERT(ballot.x == 0xFu);
    ASSERT(ballot.y == 0u);
    ASSERT(ballot.z == 0u);
    ASSERT(ballot.w == 0u);
}

[Test]
[WarpSize(2, 2)]
[numthreads(4, 4, 1)]
void Threadgroup_WaveReadLaneAt_PerWarp(uint flat : SV_GroupIndex)
{
    // Each warp's lane 0 sees its own group index, not warp 0's.
    uint laneZero = WaveReadLaneAt(flat, 0);
    if (flat < 2 || (flat >= 4 && flat < 6))
        ASSERT(laneZero == 0);     // warp 0
    else if (flat == 2 || flat == 3 || flat == 6 || flat == 7)
        ASSERT(laneZero == 2);     // warp 1
    else if (flat == 8 || flat == 9 || flat == 12 || flat == 13)
        ASSERT(laneZero == 8);     // warp 2
    else
        ASSERT(laneZero == 10);    // warp 3
}

[Test]
[WarpSize(4, 4)]
[numthreads(4, 4, 1)]
void Threadgroup_QuadReadLaneAt_LargerThanQuadWarp(uint3 tid : SV_GroupThreadID, uint flat : SV_GroupIndex)
{
    // Single 4x4 warp = four 2x2 quads. QuadReadLaneAt(_, q) must read inside the
    // calling thread's quad (top-left corner at (tid.x & ~1, tid.y & ~1)).
    uint q0 = QuadReadLaneAt(flat, 0);
    uint q1 = QuadReadLaneAt(flat, 1);
    uint q2 = QuadReadLaneAt(flat, 2);
    uint q3 = QuadReadLaneAt(flat, 3);
    uint qx = tid.x & ~1u;
    uint qy = tid.y & ~1u;
    ASSERT(q0 == qy       * 4u + qx);
    ASSERT(q1 == qy       * 4u + (qx + 1u));
    ASSERT(q2 == (qy + 1u) * 4u + qx);
    ASSERT(q3 == (qy + 1u) * 4u + (qx + 1u));
}

[Test]
[WarpSize(2, 2)]
[numthreads(4, 4, 1)]
void Threadgroup_QuadReadAcrossX_PerWarp(uint flat : SV_GroupIndex)
{
    // Quad swap stays inside the warp.
    uint swapped = QuadReadAcrossX(flat);
    // In warp 0, group indices 0,1,4,5: lane (lx=0,ly=0)=0 swaps with (1,0)=1, (0,1)=4 swaps with (1,1)=5.
    if (flat == 0) ASSERT(swapped == 1);
    if (flat == 1) ASSERT(swapped == 0);
    if (flat == 4) ASSERT(swapped == 5);
    if (flat == 5) ASSERT(swapped == 4);
    // Warp 3 group indices 10,11,14,15.
    if (flat == 10) ASSERT(swapped == 11);
    if (flat == 11) ASSERT(swapped == 10);
    if (flat == 14) ASSERT(swapped == 15);
    if (flat == 15) ASSERT(swapped == 14);
}

// Atomics are threadgroup-wide, not per-warp.
groupshared uint TGAtomicCounter;

[Test]
[WarpSize(2, 2)]
[numthreads(4, 4, 1)]
void Threadgroup_AtomicAdd_AcrossAllWarps()
{
    TGAtomicCounter = 0;
    InterlockedAdd(TGAtomicCounter, 1);
    // 16 logical threads contribute, atomics span the whole threadgroup.
    ASSERT(TGAtomicCounter == 16);
}

// ============================================================================
// 1D threadgroup with multiple warps.
// ============================================================================

[Test]
[WarpSize(4, 1)]
[numthreads(8, 1, 1)]
void Threadgroup_1D_TwoWarps_WaveSum(uint flat : SV_GroupIndex)
{
    uint s = WaveActiveSum(flat);
    if (flat < 4) ASSERT(s == 0 + 1 + 2 + 3);
    else          ASSERT(s == 4 + 5 + 6 + 7);
}

// ============================================================================
// Non-divisible threadgroup: edge warp has padding lanes.
// ============================================================================

[Test]
[WarpSize(4, 1)]
[numthreads(6, 1, 1)]
void Threadgroup_NonDivisible_PartialEdgeWarp(uint flat : SV_GroupIndex)
{
    // Warp 0 has 4 active lanes; warp 1 has 2 active + 2 padding.
    uint count = WaveActiveCountBits(true);
    if (flat < 4) ASSERT(count == 4);
    else          ASSERT(count == 2);
}

groupshared uint TGAtomicNonDivisible;

[Test]
[WarpSize(4, 1)]
[numthreads(6, 1, 1)]
void Threadgroup_NonDivisible_AtomicCountsLogicalThreadsOnly()
{
    TGAtomicNonDivisible = 0;
    InterlockedAdd(TGAtomicNonDivisible, 1);
    // 6 logical threads, padding lanes don't run.
    ASSERT(TGAtomicNonDivisible == 6);
}

// ============================================================================
// Divergence within one warp doesn't affect other warps.
// ============================================================================

[Test]
[WarpSize(2, 1)]
[numthreads(4, 1, 1)]
void Threadgroup_DivergenceIsolatedToOwnWarp(uint flat : SV_GroupIndex)
{
    if (flat == 0)
    {
        // Only lane 0 of warp 0 is active here.
        uint s = WaveActiveSum(uint(7));
        ASSERT(s == 7);
    }
    else
    {
        // Warp 0 lane 1 + all of warp 1 see their normal sums.
        uint s = WaveActiveSum(uint(7));
        if (flat == 1) ASSERT(s == 7);          // warp 0 lane 1 alone
        else            ASSERT(s == 7 + 7);     // warp 1 both lanes
    }
}

// ============================================================================
// SV system value auto-fill on test parameters.
// ============================================================================

[Test]
[numthreads(4, 4, 1)]
void Threadgroup_SVGroupThreadID_Matches2DLayout(uint3 tid : SV_GroupThreadID, uint flat : SV_GroupIndex)
{
    // Default warp size is 2x2; group is folded to 4x4.
    ASSERT(tid.x == flat % 4);
    ASSERT(tid.y == flat / 4);
    ASSERT(tid.z == 0);
}

[Test]
[numthreads(4, 1, 1)]
[WarpSize(4, 1)]
void Threadgroup_SVGroupID_AlwaysZero(uint3 gid : SV_GroupID, uint flat : SV_GroupIndex)
{
    ASSERT(gid.x == 0);
    ASSERT(gid.y == 0);
    ASSERT(gid.z == 0);
}

[Test]
[numthreads(4, 1, 1)]
[WarpSize(4, 1)]
void Threadgroup_SVDispatchThreadID_EqualsGroupThreadID_ForSingleGroup(uint3 dtid : SV_DispatchThreadID, uint3 tid : SV_GroupThreadID)
{
    ASSERT(dtid.x == tid.x);
    ASSERT(dtid.y == tid.y);
    ASSERT(dtid.z == tid.z);
}

[Test]
[numthreads(4, 1, 1)]
[WarpSize(4, 1)]
[TestCase(10)]
[TestCase(20)]
void Threadgroup_SVAutoFill_MixesWithTestCase(int multiplier, uint flat : SV_GroupIndex)
{
    // [TestCase] supplies only `multiplier`; `flat` is auto-populated.
    ASSERT(flat * multiplier <= 60);
}

// ============================================================================
// 2D threadgroup not divisible by warp dims along both axes.
// ============================================================================

[Test]
[WarpSize(2, 2)]
[numthreads(3, 3, 1)]
void Threadgroup_2D_NonDivisible_PartialWarpsOnEdges(uint flat : SV_GroupIndex)
{
    // 9 logical threads tiled into a 2x2 grid of 2x2 warps.
    //   warp 0: (0,0)(1,0)(0,1)(1,1)            -> 4 active lanes
    //   warp 1: (2,0)____(2,1)____              -> 2 active lanes
    //   warp 2: (0,2)(1,2)____ ____             -> 2 active lanes
    //   warp 3: (2,2)____ ____ ____             -> 1 active lane
    uint count = WaveActiveCountBits(true);
    if (flat == 0 || flat == 1 || flat == 3 || flat == 4) ASSERT(count == 4);
    else if (flat == 2 || flat == 5)                      ASSERT(count == 2);
    else if (flat == 6 || flat == 7)                      ASSERT(count == 2);
    else /* flat == 8 */                                  ASSERT(count == 1);
}

groupshared uint TG2DNonDivisibleAtomic;

[Test]
[WarpSize(2, 2)]
[numthreads(3, 3, 1)]
void Threadgroup_2D_NonDivisible_AtomicCountsLogicalThreadsOnly()
{
    TG2DNonDivisibleAtomic = 0;
    InterlockedAdd(TG2DNonDivisibleAtomic, 1);
    // 9 logical threads contribute even though the simulated tile is 4x(2x2)=16 lanes.
    ASSERT(TG2DNonDivisibleAtomic == 9);
}

[Test]
[WarpSize(2, 2)]
[numthreads(3, 3, 1)]
void Threadgroup_2D_NonDivisible_GroupThreadID(uint3 tid : SV_GroupThreadID, uint flat : SV_GroupIndex)
{
    // Active lanes see their real (x, y) position; padding lanes are masked out so the assert never fires for them.
    ASSERT(tid.x < 3);
    ASSERT(tid.y < 3);
    ASSERT(flat == tid.y * 3 + tid.x);
}

// ============================================================================
// 3D threadgroup: groupZ > 1 produces one stack of warps per z-slice.
// ============================================================================

[Test]
[WarpSize(2, 2)]
[numthreads(2, 2, 2)]
void Threadgroup_3D_TwoZSlices_LaneCount(uint flat : SV_GroupIndex)
{
    // Two z-slices, each fitting in a single 2x2 warp -> 2 warps total.
    ASSERT(WaveGetLaneCount() == 4);
    // Each warp's lanes sum to 0+1+2+3 = 6 independently.
    uint lane = WaveGetLaneIndex();
    ASSERT(WaveActiveSum(lane) == 6);
}

[Test]
[WarpSize(2, 2)]
[numthreads(2, 2, 2)]
void Threadgroup_3D_GroupThreadID_CoversAllSlices(uint3 tid : SV_GroupThreadID, uint flat : SV_GroupIndex)
{
    // SV_GroupIndex is row/column/slice major: flat = z*4 + y*2 + x.
    ASSERT(tid.x == flat % 2);
    ASSERT(tid.y == (flat / 2) % 2);
    ASSERT(tid.z == flat / 4);
}

[Test]
[WarpSize(2, 2)]
[numthreads(2, 2, 2)]
void Threadgroup_3D_WaveSum_PerZSlice(uint flat : SV_GroupIndex)
{
    // Each z-slice is its own warp, so sums differ across slices.
    uint s = WaveActiveSum(flat);
    if (flat < 4) ASSERT(s == 0 + 1 + 2 + 3);   // slice z=0
    else          ASSERT(s == 4 + 5 + 6 + 7);   // slice z=1
}

groupshared uint TG3DAtomic;

[Test]
[WarpSize(2, 2)]
[numthreads(2, 2, 2)]
void Threadgroup_3D_AtomicAcrossSlices()
{
    TG3DAtomic = 0;
    InterlockedAdd(TG3DAtomic, 1);
    // 2*2*2 = 8 logical threads, atomics span the whole threadgroup including across z.
    ASSERT(TG3DAtomic == 8);
}

[Test]
[WarpSize(4, 1)]
[numthreads(3, 1, 2)]
void Threadgroup_3D_NonDivisibleX_TwoSlices(uint flat : SV_GroupIndex)
{
    // 3-wide group with 4-wide warp gives one padded warp per z-slice.
    // Slice 0 -> warp 0 covers flat 0,1,2 + 1 padding.
    // Slice 1 -> warp 1 covers flat 3,4,5 + 1 padding.
    uint count = WaveActiveCountBits(true);
    ASSERT(count == 3);
    uint s = WaveActiveSum(flat);
    if (flat < 3) ASSERT(s == 0 + 1 + 2);
    else          ASSERT(s == 3 + 4 + 5);
}

// ============================================================================
// Two-stage reduction: warp-level wave sum -> threadgroup-level scalar sum.
// ============================================================================

groupshared uint TwoStagePartials[2];

[Test]
[WarpSize(4, 1)]
[numthreads(8, 1, 1)]
void Threadgroup_TwoStageReduction(uint flat : SV_GroupIndex)
{
    // Stage 1: each warp reduces its own lanes' contributions.
    uint warpSum = WaveActiveSum(flat);

    // Stage 2: first lane of each warp publishes its partial.
    uint warpIndex = flat / WaveGetLaneCount();
    if (WaveIsFirstLane())
        TwoStagePartials[warpIndex] = warpSum;

    GroupMemoryBarrierWithGroupSync();

    // Stage 3: warp 0 alone sums the partials and asserts.
    if (warpIndex == 0)
    {
        uint total = 0;
        for (uint i = 0; i < 2; i++)
            total += TwoStagePartials[i];
        ASSERT(total == 0 + 1 + 2 + 3 + 4 + 5 + 6 + 7);
    }
}
