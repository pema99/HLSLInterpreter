using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HLSL
{
    public enum ExecutionScope
    {
        Function,
        Conditional,
        Loop,
        Block,
    }

    public class HLSLExecutionState
    {
        public enum ThreadState : byte
        {
            Active,    // Alive
            Inactive,  // Helper lane or disabled by return, break
            Suspended, // Disabled by continue
        }

        private int warpSizeX, warpSizeY;
        private int warpSizeInThreads;
        private int groupSizeX, groupSizeY, groupSizeZ;
        private int warpsPerGroupX, warpsPerGroupY;
        private int groupSizeInThreads;
        private Stack<(ExecutionScope scope, ThreadState[] mask)> executionMask;

        public HLSLExecutionState(int warpSizeX, int warpSizeY)
            : this(warpSizeX, warpSizeY, warpSizeX, warpSizeY, 1) { }

        public HLSLExecutionState(int warpSizeX, int warpSizeY, int groupSizeX, int groupSizeY, int groupSizeZ)
        {
            this.warpSizeX = warpSizeX;
            this.warpSizeY = warpSizeY;
            this.groupSizeX = groupSizeX;
            this.groupSizeY = groupSizeY;
            this.groupSizeZ = groupSizeZ;
            warpSizeInThreads = warpSizeX * warpSizeY;
            warpsPerGroupX = (groupSizeX + warpSizeX - 1) / warpSizeX;
            warpsPerGroupY = (groupSizeY + warpSizeY - 1) / warpSizeY;
            groupSizeInThreads = warpsPerGroupX * warpsPerGroupY * groupSizeZ * warpSizeInThreads;

            executionMask = new Stack<(ExecutionScope, ThreadState[])>();

            // Padding threads, when warps don't tile the group exactly:
            var initial = new ThreadState[groupSizeInThreads];
            for (int threadIndex = 0; threadIndex < groupSizeInThreads; threadIndex++)
            {
                var (tx, ty, tz) = GetThreadPosition(threadIndex);
                bool inGroup = tx < groupSizeX && ty < groupSizeY && tz < groupSizeZ;
                initial[threadIndex] = inGroup ? ThreadState.Active : ThreadState.Inactive;
            }
            executionMask.Push((ExecutionScope.Function, initial));
        }

        public void PushExecutionMask(ExecutionScope scope)
        {
            executionMask.Push((scope, executionMask.Peek().mask.ToArray()));
        }

        public void PopExecutionMask()
        {
            executionMask.Pop();
        }

        public bool IsThreadActive(int threadIndex)
        {
            return executionMask.Peek().mask[threadIndex] == ThreadState.Active;
        }

        public void DisableThread(int threadIndex)
        {
            executionMask.Peek().mask[threadIndex] = ThreadState.Inactive;
        }

        public void EnableThread(int threadIndex)
        {
            executionMask.Peek().mask[threadIndex] = ThreadState.Active;
        }

        // Kill thread for the entire execution, i.e. 'discard'
        public void KillThreadGlobally(int threadIndex)
        {
            foreach (var level in executionMask)
            {
                level.mask[threadIndex] = ThreadState.Inactive;
            }
        }

        // Kill a thread in all scopes until a specific scope type is reached
        public void KillThreadUntilScope(int threadIndex, ExecutionScope scope)
        {
            foreach (var level in executionMask)
            {
                level.mask[threadIndex] = ThreadState.Inactive;
                if (level.scope == scope)
                    break;
            }
        }

        // Kill thread for the current function, i.e. 'return'
        public void KillThreadInFunction(int threadIndex) => KillThreadUntilScope(threadIndex, ExecutionScope.Function);

        // Kill thread for the current loop, i.e. 'break'
        public void KillThreadInLoop(int threadIndex) => KillThreadUntilScope(threadIndex, ExecutionScope.Loop);

        // Kill thread for the current conditional, used for switch statements
        public void KillThreadInConditional(int threadIndex) => KillThreadUntilScope(threadIndex, ExecutionScope.Conditional);

        // Suspend thread for the current loop, i.e. 'continue'
        public void SuspendThreadInLoop(int threadIndex)
        {
            foreach (var level in executionMask)
            {
                if (level.mask[threadIndex] == ThreadState.Active)
                    level.mask[threadIndex] = ThreadState.Suspended;

                if (level.scope == ExecutionScope.Loop)
                    break;
            }
        }

        // Resume previously suspended threads in loop body
        public void ResumeSuspendedThreadsInLoop()
        {
            foreach (var level in executionMask)
            {
                for (int threadIndex = 0; threadIndex < GetThreadCount(); threadIndex++)
                {
                    if (level.mask[threadIndex] == ThreadState.Suspended)
                        level.mask[threadIndex] = ThreadState.Active;
                }

                if (level.scope == ExecutionScope.Loop)
                    break;
            }
        }

        public bool IsAnyThreadActive() => executionMask.Peek().mask.Any(x => x == ThreadState.Active);
        public bool IsUniformExecution() => executionMask.Peek().mask.All(x => x == ThreadState.Active);
        public bool IsVaryingExecution() => !IsUniformExecution();

        // Warp helpers:
        public int GetWarpSizeX() => warpSizeX;
        public int GetWarpSizeY() => warpSizeY;
        public int GetWarpThreadCount() => warpSizeInThreads;
        public (int lx, int ly) GetThreadPositionInWarp(int threadIndex)
        {
            int lane = threadIndex % warpSizeInThreads;
            return (lane % warpSizeX, lane / warpSizeX);
        }

        // Thread group helpers:
        public int GetSizeX() => groupSizeX;
        public int GetSizeY() => groupSizeY;
        public int GetSizeZ() => groupSizeZ;
        public int GetThreadCount() => groupSizeInThreads;
        public (int tx, int ty, int tz) GetThreadPosition(int threadIndex)
        {
            int warpIdx = threadIndex / warpSizeInThreads;
            (int lx, int ly) = GetThreadPositionInWarp(threadIndex);
            int wxy = warpsPerGroupX * warpsPerGroupY;
            int wz = warpIdx / wxy;
            int wxyIdx = warpIdx % wxy;
            int wy = wxyIdx / warpsPerGroupX;
            int wx = wxyIdx % warpsPerGroupX;
            return (wx * warpSizeX + lx, wy * warpSizeY + ly, wz);
        }

        // Thread group <-> warp helpers:
        public int GetFirstThreadIndexInWarp(int warpIndex) => warpIndex * warpSizeInThreads;
        public int GetWarpIndexOfThread(int threadIndex) => threadIndex / warpSizeInThreads;
        public int GetWarpCount() => warpsPerGroupX * warpsPerGroupY * groupSizeZ;

        // Debug API:
        public ThreadState[] GetThreadStates() => executionMask.Peek().mask.ToArray();
        public ThreadState[][] GetThreadStatesPerFrame()
        {
            var frames = new List<ThreadState[]>();
            var stack = executionMask.ToArray();

            if (stack.Length == 0)
                return Array.Empty<ThreadState[]>();

            frames.Add(stack[0].mask.ToArray());

            var functionScopes = stack
                .Where(e => e.scope == ExecutionScope.Function)
                .ToArray();

            for (int i = 0; i < functionScopes.Length - 2; i++)
                frames.Add(functionScopes[i].mask.ToArray());

            return frames.ToArray();
        }
    }
}
