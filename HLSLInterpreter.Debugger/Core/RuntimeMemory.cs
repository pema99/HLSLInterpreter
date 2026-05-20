using System.Runtime;

namespace HLSLInterpreter.Debugger.Core;

public static class RuntimeMemory
{
    // Aggressively reclaims memory between runs. Interpreting a full frame
    // allocates heavily, so this keeps the working set down.
    public static void Reclaim()
    {
        if (OperatingSystem.IsBrowser())
        {
            GC.Collect();
            return;
        }
        var previous = GCSettings.LargeObjectHeapCompactionMode;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GCSettings.LargeObjectHeapCompactionMode = previous;
    }
}
