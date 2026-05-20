namespace HLSLInterpreter.Debugger.Core;

// Pure cursor math over a recorded ExecutionTrace. Each method takes the current
// step index and returns the new one.
public static class TraceNavigator
{
    public static int Clamp(ExecutionTrace trace, int index) =>
        trace.Steps.Count == 0 ? -1 : Math.Clamp(index, 0, trace.Steps.Count - 1);

    public static int End(ExecutionTrace trace) => trace.Steps.Count - 1;

    public static int Forward(ExecutionTrace trace, int index) =>
        index < trace.Steps.Count - 1 ? index + 1 : index;

    public static int Back(ExecutionTrace trace, int index) =>
        index > 0 ? index - 1 : index;

    public static int Over(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index < 0 || index >= steps.Count - 1) return index;
        int depth = steps[index].CallDepth;
        int next = index + 1;
        if (depth > 0)
            while (next < steps.Count - 1 && steps[next].CallDepth > depth)
                next++;
        return next;
    }

    public static int Out(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index < 0 || index >= steps.Count - 1) return index;
        int depth = steps[index].CallDepth;
        int next = index + 1;
        while (next < steps.Count - 1 && steps[next].CallDepth >= depth)
            next++;
        return next;
    }

    public static int OverBack(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index <= 0) return index;
        int depth = steps[index].CallDepth;
        int prev = index - 1;
        while (prev > 0 && steps[prev].CallDepth > depth)
            prev--;
        return prev;
    }

    public static int OutBack(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index <= 0) return index;
        int depth = steps[index].CallDepth;
        int prev = index - 1;
        while (prev > 0 && steps[prev].CallDepth >= depth)
            prev--;
        return prev;
    }

    public static int ToBreakpoint(ExecutionTrace trace, int index, IReadOnlySet<int> breakpoints)
    {
        var steps = trace.Steps;
        int next = index + 1;
        while (next < steps.Count && !breakpoints.Contains(steps[next].Line))
            next++;
        return Math.Min(next, steps.Count - 1);
    }

    public static int ToBreakpointBack(ExecutionTrace trace, int index, IReadOnlySet<int> breakpoints)
    {
        var steps = trace.Steps;
        int prev = index - 1;
        while (prev > 0 && !breakpoints.Contains(steps[prev].Line))
            prev--;
        return Math.Max(prev, 0);
    }
}
