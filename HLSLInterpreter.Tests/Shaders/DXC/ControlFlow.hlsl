#include "../HLSLTest.hlsl"

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/control_flow/

// ============================================================================
// MULTIPLE RETURNS ACROSS DIFFERENT CONTROL STRUCTURES
// Inspired by DXC control_flow/return/multi_ret.hlsl
// ============================================================================

// All branches return - if/else where both sides return.
float IfElseAllReturn(int x)
{
    if (x < 0)
        return -1.0;
    else
        return 1.0;
}

[Test]
void ControlFlow_IfElseAllReturn_NegativeInput_ReturnsNegOne()
{
    ASSERT(IfElseAllReturn(-5) == -1.0);
}

[Test]
void ControlFlow_IfElseAllReturn_PositiveInput_ReturnsPosOne()
{
    ASSERT(IfElseAllReturn(5) == 1.0);
}

// Nested if-else where every leaf returns.
float NestedAllReturn(int x, int y)
{
    if (x > 0)
    {
        if (y > 0) return 1.0;
        else       return 2.0;
    }
    else
    {
        if (y > 0) return 3.0;
        else       return 4.0;
    }
}

[Test]
void ControlFlow_NestedIfElseAllReturn_QuadrantI()
{
    ASSERT(NestedAllReturn(1, 1) == 1.0);
}

[Test]
void ControlFlow_NestedIfElseAllReturn_QuadrantII()
{
    ASSERT(NestedAllReturn(1, -1) == 2.0);
}

[Test]
void ControlFlow_NestedIfElseAllReturn_QuadrantIII()
{
    ASSERT(NestedAllReturn(-1, 1) == 3.0);
}

[Test]
void ControlFlow_NestedIfElseAllReturn_QuadrantIV()
{
    ASSERT(NestedAllReturn(-1, -1) == 4.0);
}

// Multi-return: early returns at different points in if/loop/switch.
// Inspired by multi_ret.hlsl
float MultiReturn(int x, int loopBound)
{
    float c = 0.0;

    if (x < 0)
    {
        c += 1.0;
        return c; // return 1.0
    }

    for (int j = 0; j < loopBound; j++)
    {
        c += 2.0;
        if (c > 10.0)
            return c; // early loop return
    }

    switch (x)
    {
        case 1:
            c += 100.0;
            return c;
        case 2:
            c += 200.0;
            return c;
    }

    return c;
}

[Test]
void ControlFlow_MultiReturn_NegativeX_ReturnsEarlyFromIf()
{
    ASSERT(MultiReturn(-1, 5) == 1.0);
}

[Test]
void ControlFlow_MultiReturn_Loop_EarlyExitWhenThresholdReached()
{
    // loopBound=6: c accumulates 2,4,6,8,10,12 - exits when c>10 (c==12)
    ASSERT(MultiReturn(0, 6) == 12.0);
}

[Test]
void ControlFlow_MultiReturn_SwitchCase1_ReturnsCorrect()
{
    // x=1, loopBound=2: c = 4.0, then switch case 1: c += 100 = 104
    ASSERT(MultiReturn(1, 2) == 104.0);
}

[Test]
void ControlFlow_MultiReturn_SwitchCase2_ReturnsCorrect()
{
    // x=2, loopBound=1: c = 2.0, then switch case 2: c += 200 = 202
    ASSERT(MultiReturn(2, 1) == 202.0);
}

[Test]
void ControlFlow_MultiReturn_NoEarlyExit_ReturnsAccumulated()
{
    // x=3, loopBound=2: c = 4.0, no switch match, returns 4.0
    ASSERT(MultiReturn(3, 2) == 4.0);
}

// ============================================================================
// UNREACHABLE CODE AFTER RETURN/BREAK
// Inspired by DXC control_flow/loops/midbreak.hlsl
// Verifies that code after 'break' or 'return' is never executed.
// ============================================================================

[Test]
void ControlFlow_UnreachableAfterBreak_NotExecuted()
{
    int executed = 0;
    int i = 0;
    while (i < 5)
    {
        executed++;
        break;
        executed = 999; // unreachable
    }
    ASSERT(executed == 1);
}

[Test]
void ControlFlow_UnreachableAfterReturnInLoop_NotExecuted()
{
    int counter = 0;
    for (int i = 0; i < 10; i++)
    {
        counter++;
        if (i == 2)
        {
            counter += 1000;
            break;
            counter = 999999; // unreachable
        }
    }
    ASSERT(counter == 1003); // 1 + 2 + 1000 (incremented for i=0,1,2 then +1000 then break)
}

// ============================================================================
// RETURN IN SWITCH EXITS FUNCTION (not just switch)
// Verifies that 'return' inside a switch case exits the function entirely.
// ============================================================================

int ReturnFromSwitch(int x)
{
    int result = 0;
    switch (x)
    {
        case 1:
            return 100;
        case 2:
            return 200;
    }
    result = 999; // should only run if no case matched
    return result;
}

[Test]
void ControlFlow_ReturnInSwitch_ExitsFunction_Case1()
{
    ASSERT(ReturnFromSwitch(1) == 100);
}

[Test]
void ControlFlow_ReturnInSwitch_ExitsFunction_Case2()
{
    ASSERT(ReturnFromSwitch(2) == 200);
}

[Test]
void ControlFlow_ReturnInSwitch_NoMatch_FallThrough()
{
    ASSERT(ReturnFromSwitch(3) == 999);
}

// ============================================================================
// LOOP WITH STRUCT-BASED EXIT VALUE
// Inspired by DXC control_flow/loops/struct_exit_exit_value.hlsl
// Tests that loop variables updated in multiple branches have correct values
// when the loop exits.
// ============================================================================

[Test]
void ControlFlow_LoopWithConditionalAccumulate_CorrectExitValue()
{
    float ret = 0.0;
    float arr[3] = { 1.0, 2.0, 3.0 };

    for (int i = 0; i < 3; i++)
    {
        int offset = i;
        if (i % 2 == 0)
            offset = 0;

        ret += arr[offset];
    }

    // i=0: offset=0 → arr[0]=1
    // i=1: offset=1 → arr[1]=2
    // i=2: offset=0 → arr[0]=1
    ASSERT(ret == 4.0);
}

// ============================================================================
// IF-RETURN WITH ELSE-RETURN PATTERN
// Inspired by DXC control_flow/return/if_ret_with_else_ret.hlsl
// Every branch returns, no fall-through to the function end.
// ============================================================================

float ClassifySign(float x)
{
    if (x > 0.0)
        return 1.0;
    if (x < 0.0)
        return -1.0;
    return 0.0;
}

[Test]
void ControlFlow_IfRetWithElseRet_Positive_ReturnsOne()
{
    ASSERT(ClassifySign(5.0) == 1.0);
}

[Test]
void ControlFlow_IfRetWithElseRet_Negative_ReturnsNegOne()
{
    ASSERT(ClassifySign(-3.0) == -1.0);
}

[Test]
void ControlFlow_IfRetWithElseRet_Zero_ReturnsZero()
{
    ASSERT(ClassifySign(0.0) == 0.0);
}

// ============================================================================
// SINGLE RETURN IN LOOP
// Inspired by DXC control_flow/return/single_ret_in_loop.hlsl
// ============================================================================

float AccumulateUntilLimit(int count, float limit)
{
    float c = 0.0;
    for (int j = 0; j < count; j++)
    {
        c += 1.0;
        if (c >= limit)
            return c;
    }
    return c;
}

[Test]
void ControlFlow_SingleReturnInLoop_ExitsWhenLimitReached()
{
    ASSERT(AccumulateUntilLimit(100, 3.0) == 3.0);
}

[Test]
void ControlFlow_SingleReturnInLoop_LoopFinishesNormally()
{
    ASSERT(AccumulateUntilLimit(5, 10.0) == 5.0);
}

// ============================================================================
// WHOLE SCOPE RETURNED VIA IF
// Inspired by DXC control_flow/return/whole_scope_returned_if.hlsl
// If an if-else guarantees a return, code after is dead.
// ============================================================================

int WholeIfReturn(bool cond)
{
    int x = 0;
    if (cond)
    {
        x = 10;
        return x;
    }
    else
    {
        x = 20;
        return x;
    }
}

[Test]
void ControlFlow_WholeIfReturn_TrueBranch()
{
    ASSERT(WholeIfReturn(true) == 10);
}

[Test]
void ControlFlow_WholeIfReturn_FalseBranch()
{
    ASSERT(WholeIfReturn(false) == 20);
}

// ============================================================================
// WHILE(B) WITH IMMEDIATE BREAK — MIDBREAK PATTERN
// Inspired by DXC control_flow/loops/midbreak.hlsl:
//   while(b) { b--; a += f(a); break; a += g(a); }
// The second body statement after break is dead.
// ============================================================================

[Test]
void ControlFlow_MidbreakPattern_OnlyPreBreakCodeRuns()
{
    float a = 1.0;
    int b = 5;
    while (b)
    {
        b--;
        a += cos(a);
        break;
        a += 99999.0; // unreachable
    }
    // Cos(1) ≈ 0.5403; a ≈ 1.5403 and b == 4
    ASSERT(b == 4);
    ASSERT(abs(a - (1.0 + cos(1.0))) < 0.001);
}

// ============================================================================
// LOOP WITH MULTIPLE EXITS AND COMPLEX CONDITIONS
// Inspired by DXC control_flow/loops/loop*.hlsl
// ============================================================================

[Test]
void ControlFlow_LoopAttribute_AccumulatesXTimesB()
{
    // Mimics loop1.hlsl logic: accumulate a.x across b.x iterations
    float s = 0.0;
    float ax = 2.5;
    int bx = 4;
    for (int i = 0; i < bx; i++)
        s += ax;
    ASSERT(abs(s - 10.0) < 0.001); // 2.5 * 4
}

[Test]
void ControlFlow_LoopWithNestedConditional_EarlyReturn()
{
    // Mimics struct_exit_exit_value.hlsl nested conditionals with complex
    // offset and early exit
    float ret = 0.0;
    float arr[3] = { 1.0, 2.0, 3.0 };
    int a = 3, b = 2, c = 1;

    for (int i = 1; i <= 3; i++)
    {
        if ((a * i) & c)
        {
            ret += arr[i - 1]; // i=1 → arr[0]=1, i=3 → arr[2]=3

            if ((a * i) & b)
            {
                int offset = i;
                if (i % 2 == 0)
                    offset = 1;

                if (i == 3)
                {
                    ret += arr[offset - 1];
                    break;
                }
            }
        }
    }

    // i=1: (3*1)&1 = 3&1 = 1 → ret+=arr[0]=1; (3*1)&2 = 3&2=2 → offset=1; not i==3
    // i=2: (3*2)&1 = 6&1=0 → skip
    // i=3: (3*3)&1 = 9&1=1 → ret+=arr[2]=3, total=4; (9&2)=0 → skip inner
    // Result: ret = 4.0
    ASSERT(ret == 4.0);
}
