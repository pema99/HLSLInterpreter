using HLSL;
using UnityShaderParser.HLSL;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Execution;

public static class SoftwareRenderer
{
    #region Execution and input building
    public static HLSLValue RunVertFrag(
        HLSLRunner runner,
        Mesh mesh,
        string vertEntry,
        string fragEntry,
        int warpX,
        int warpY,
        int canvasW,
        int canvasH,
        int groupOffsetX,
        int groupOffsetY)
    {
        int threadsPerWarp = warpX * warpY;
        int tileX0 = groupOffsetX * warpX;
        int tileY0 = groupOffsetY * warpY;

        var vertFunc = runner.GetFunction(vertEntry) ?? throw new InvalidOperationException($"Vertex function '{vertEntry}' not found.");

        // Don't debug vertex stage for fragment-debug runs.
        var savedBefore = runner.DebugHookBeforeStatement;
        var savedAfter = runner.DebugHookAfterStatement;
        runner.DebugHookBeforeStatement = null;
        runner.DebugHookAfterStatement = null;

        // Run vertex function.
        var vertOutputs = RunVertOnly(runner, mesh, vertEntry, warpX, warpY, 0, mesh.VertexCount);

        // Start debugging.
        runner.DebugHookBeforeStatement = savedBefore;
        runner.DebugHookAfterStatement = savedAfter;

        // Get flattened vertex positions.
        int svPosOffset = ShaderReflection.LocateSemanticOffset(runner, vertFunc, "SV_POSITION");
        if (svPosOffset < 0)
            throw new InvalidOperationException("Vertex output must include an SV_Position field or function-level semantic.");
        var flatVerts = new RawValue[mesh.VertexCount][];
        for (int i = 0; i < mesh.VertexCount; i++)
            flatVerts[i] = ShaderReflection.FlattenReturnValue(runner, vertFunc, vertOutputs[i]);

        // Project vertices to screen.
        var screenVerts = new ProjectedVertex[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var flat = flatVerts[i];
            screenVerts[i] = ProjectToScreen(
                (flat[svPosOffset + 0].Float, flat[svPosOffset + 1].Float, flat[svPosOffset + 2].Float, flat[svPosOffset + 3].Float),
                canvasW, canvasH);
        }

        // Rasterize resulting triangles.
        var fragments = new Fragment[threadsPerWarp];
        Array.Fill(fragments, Fragment.InvalidFragment);
        for (int tri = 0; tri < mesh.TriangleCount; tri++)
        {
            int i0 = (int)mesh.Indices[tri * 3 + 0];
            int i1 = (int)mesh.Indices[tri * 3 + 1];
            int i2 = (int)mesh.Indices[tri * 3 + 2];
            RasterizeTriangle(screenVerts[i0], screenVerts[i1], screenVerts[i2], i0, i1, i2, tileX0, tileY0, warpX, warpY, fragments);
        }

        // Mark helper lanes.
        for (int threadIdx = 0; threadIdx < threadsPerWarp; threadIdx++)
        {
            if (!fragments[threadIdx].IsValid)
                runner.GetExecutionState().DisableThread(threadIdx);
        }

        // Run frag function.
        var fragFunc = runner.GetFunction(fragEntry) ?? throw new InvalidOperationException($"Fragment function '{fragEntry}' not found.");
        var fragArgs = BuildFragmentArgs(runner, fragFunc, flatVerts, screenVerts, fragments, threadsPerWarp);
        var color = ((NumericValue)runner.CallFunction(fragEntry, fragArgs)).BroadcastToVector(4);

        // Mask out helper lane values.
        for (int threadIdx = 0; threadIdx < threadsPerWarp; threadIdx++)
        {
            if (!fragments[threadIdx].IsValid)
                color = color.SetThreadValue(threadIdx, [0f, 0f, 0f, 1f]);
        }
        return color;
    }

    public static HLSLValue[] RunVertOnly(
        HLSLRunner runner,
        Mesh mesh,
        string vertEntry,
        int warpX,
        int warpY,
        int vertexOffset,
        int vertexCount)
    {
        int threadsPerWarp = warpX * warpY;
        var vertFunc = runner.GetFunction(vertEntry) ?? throw new InvalidOperationException($"Vertex function '{vertEntry}' not found.");
        var outs = new HLSLValue[vertexCount];
        for (int batchStart = 0; batchStart < vertexCount; batchStart += threadsPerWarp)
        {
            int batchSize = Math.Min(threadsPerWarp, vertexCount - batchStart);
            var args = BuildVertexArgs(runner, vertFunc, mesh, vertexOffset + batchStart, threadsPerWarp);
            for (int t = batchSize; t < threadsPerWarp; t++)
                runner.GetExecutionState().DisableThread(t);
            var batchOutput = runner.CallFunction(vertEntry, args);
            for (int t = batchSize; t < threadsPerWarp; t++)
                runner.GetExecutionState().EnableThread(t);
            for (int i = 0; i < batchSize; i++)
                outs[batchStart + i] = HLSLValueUtils.Scalarize(batchOutput, i);
        }
        return outs;
    }

    // Build inputs to vertex stage.
    private static HLSLValue[] BuildVertexArgs(
        HLSLRunner runner,
        FunctionDefinitionNode vertFunc,
        Mesh mesh,
        int batchStart,
        int threadsPerWarp)
    {
        return ShaderReflection.BuildArgs(runner, vertFunc, (type, semantic, dim, interp) =>
        {
            if (semantic.Base == "SV_VERTEXID")
            {
                var ids = new RawValue[threadsPerWarp];
                for (int threadIdx = 0; threadIdx < threadsPerWarp; threadIdx++)
                {
                    ids[threadIdx] = (uint)Math.Min(batchStart + threadIdx, mesh.VertexCount - 1);
                }
                return new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarVGPR(ids));
            }
            if (semantic.Base == "SV_INSTANCEID")
            {
                var ids = new RawValue[threadsPerWarp];
                for (int threadIdx = 0; threadIdx < threadsPerWarp; threadIdx++)
                {
                    ids[threadIdx] = (uint)0;
                }
                return new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarVGPR(ids));
            }

            if (semantic.Base == null || !Mesh.AttributeDimensions.ContainsKey(semantic))
                throw new InvalidOperationException(
                    $"Vertex input '{semantic.Base}{semantic.Index}' is not provided by the mesh. " +
                    "Available: POSITION, NORMAL, TEXCOORD0.");

            var perThread = new RawValue[threadsPerWarp][];
            var buf = new float[4];
            for (int threadIdx = 0; threadIdx < threadsPerWarp; threadIdx++)
            {
                int v = Math.Min(batchStart + threadIdx, mesh.VertexCount - 1);
                mesh.ReadAttribute(v, semantic, buf);
                perThread[threadIdx] = new RawValue[dim];
                for (int i = 0; i < dim; i++)
                {
                    perThread[threadIdx][i] = buf[i];
                }
            }
            return new VectorValue(ScalarType.Float, HLSLValueUtils.MakeVectorVGPR(perThread));
        });
    }

    // Build inputs to fragment stage.
    private static HLSLValue[] BuildFragmentArgs(
        HLSLRunner runner,
        FunctionDefinitionNode fragFunc,
        RawValue[][] flatVerts,
        ProjectedVertex[] screenVerts,
        Fragment[] fragments,
        int threadsPerWarp)
    {
        // Perspective interpolation for passing attributes from vert to frag stage.
        static (float pcw0, float pcw1, float pcw2) PerspectiveWeights(Fragment frag, ProjectedVertex[] screenVerts)
        {
            float invW0 = 1f / screenVerts[frag.Index0].ClipW;
            float invW1 = 1f / screenVerts[frag.Index1].ClipW;
            float invW2 = 1f / screenVerts[frag.Index2].ClipW;
            float a = frag.Bary0 * invW0;
            float b = frag.Bary1 * invW1;
            float c = frag.Bary2 * invW2;
            float invSum = 1f / (a + b + c);
            return (a * invSum, b * invSum, c * invSum);
        }

        int cursor = 0;
        return ShaderReflection.BuildArgs(runner, fragFunc, (type, semantic, dim, modifiers) =>
        {
            int slot = cursor;
            cursor += dim;
            bool isPosition = semantic.Base == "SV_POSITION";
            ScalarType scalarType = ShaderReflection.GetScalarType(type);

            bool noInterpolation = !HLSLTypeUtils.IsFloat(scalarType)
                || modifiers.Contains(BindingModifier.Nointerpolation);
            bool noPerspective = modifiers.Contains(BindingModifier.Noperspective);

            var perThread = new RawValue[threadsPerWarp][];
            for (int threadIdx = 0; threadIdx < threadsPerWarp; threadIdx++)
            {
                var row = new RawValue[dim];
                var frag = fragments[threadIdx];
                if (!frag.IsValid)
                {
                    // Helper lane
                }
                else if (isPosition)
                {
                    if (dim > 0) row[0] = frag.X + 0.5f;
                    if (dim > 1) row[1] = frag.Y + 0.5f;
                    if (dim > 2) row[2] = frag.Depth;
                    if (dim > 3) row[3] = 1f;
                }
                else if (!noInterpolation)
                {
                    float w0, w1, w2;
                    if (noPerspective)
                    {
                        w0 = frag.Bary0; w1 = frag.Bary1; w2 = frag.Bary2;
                    }
                    else
                    {
                        (w0, w1, w2) = PerspectiveWeights(frag, screenVerts);
                    }
                    var f0 = flatVerts[frag.Index0];
                    var f1 = flatVerts[frag.Index1];
                    var f2 = flatVerts[frag.Index2];
                    for (int i = 0; i < dim; i++)
                    {
                        row[i] = w0 * f0[slot + i].Float + w1 * f1[slot + i].Float + w2 * f2[slot + i].Float;
                    }
                }
                else
                {
                    // Pass-through for int/uint/etc - take the provoking vertex's bits.
                    var f0 = flatVerts[frag.Index0];
                    for (int i = 0; i < dim; i++)
                    {
                        row[i] = f0[slot + i];
                    }
                }
                perThread[threadIdx] = row;
            }

            if (dim == 1)
            {
                var scalars = new RawValue[threadsPerWarp];
                for (int threadIdx = 0; threadIdx < threadsPerWarp; threadIdx++)
                {
                    scalars[threadIdx] = perThread[threadIdx][0];
                }
                return new ScalarValue(scalarType, HLSLValueUtils.MakeScalarVGPR(scalars));
            }
            return new VectorValue(scalarType, HLSLValueUtils.MakeVectorVGPR(perThread));
        });
    }
    #endregion

    #region Rasterization
    private readonly struct ProjectedVertex
    {
        public readonly float ScreenX, ScreenY, NdcZ, ClipW;
        public readonly bool BehindCamera => ClipW <= 0;
        public ProjectedVertex(float sx, float sy, float ndcZ, float clipW)
        {
            ScreenX = sx;
            ScreenY = sy;
            NdcZ = ndcZ;
            ClipW = clipW;
        }
    }

    private readonly struct Fragment
    {
        public readonly int X, Y;
        public readonly float Depth;
        public readonly float Bary0, Bary1, Bary2; // screen-space barycentrics
        public readonly int Index0, Index1, Index2; // triangle vertex indices

        public static Fragment InvalidFragment => new Fragment(0, 0, float.PositiveInfinity, 0, 0, 0, 0, 0, 0);
        public bool IsValid => !float.IsPositiveInfinity(Depth);

        public Fragment(int x, int y, float depth, float w0, float w1, float w2, int i0, int i1, int i2)
        {
            X = x;
            Y = y;
            Depth = depth;
            Bary0 = w0;
            Bary1 = w1;
            Bary2 = w2;
            Index0 = i0;
            Index1 = i1;
            Index2 = i2;
        }
    }

    private static ProjectedVertex ProjectToScreen((float x, float y, float z, float w) fragPos, int width, int height)
    {
        // Behind camera, bail
        bool behind = fragPos.w <= 0;
        if (behind)
            return new ProjectedVertex(0, 0, 0, fragPos.w);

        float ndcX = fragPos.x / fragPos.w;
        float ndcY = fragPos.y / fragPos.w;
        float ndcZ = fragPos.z / fragPos.w;
        float screenX = (ndcX * 0.5f + 0.5f) * width;
        float screenY = (1.0f - (ndcY * 0.5f + 0.5f)) * height;
        return new ProjectedVertex(screenX, screenY, ndcZ, fragPos.w);
    }

    // https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-rules
    private static void RasterizeTriangle(
        ProjectedVertex vertA,
        ProjectedVertex vertB,
        ProjectedVertex vertC,
        int indexA,
        int indexB,
        int indexC,
        int tileOffsetX,
        int tileOffsetY,
        int tileWidth,
        int tileHeight,
        Fragment[] fragments)
    {
        static float EdgeFunction(float ax, float ay, float bx, float by, float cx, float cy)
        {
            return (cx - ax) * (by - ay) - (cy - ay) * (bx - ax);
        }
        static bool IsTopLeftEdge(float p1x, float p1y, float p2x, float p2y)
        {
            if (p1y == p2y)
                return p2x < p1x;
            return p2y > p1y;
        }

        // Discard triangles behind camera.
        if (vertA.BehindCamera || vertB.BehindCamera || vertC.BehindCamera)
            return;

        // Discard zero area triangles.
        float area = EdgeFunction(vertA.ScreenX, vertA.ScreenY, vertB.ScreenX, vertB.ScreenY, vertC.ScreenX, vertC.ScreenY);
        if (area <= 0)
            return;

        float minX = Math.Min(vertA.ScreenX, Math.Min(vertB.ScreenX, vertC.ScreenX));
        float maxX = Math.Max(vertA.ScreenX, Math.Max(vertB.ScreenX, vertC.ScreenX));
        float minY = Math.Min(vertA.ScreenY, Math.Min(vertB.ScreenY, vertC.ScreenY));
        float maxY = Math.Max(vertA.ScreenY, Math.Max(vertB.ScreenY, vertC.ScreenY));

        // Triangle bbox clipped to the warp tile in canvas-pixel coords.
        int tileX1 = tileOffsetX + tileWidth - 1;
        int tileY1 = tileOffsetY + tileHeight - 1;
        int x0 = Math.Max(tileOffsetX, (int)Math.Floor(minX));
        int x1 = Math.Min(tileX1, (int)Math.Ceiling(maxX));
        int y0 = Math.Max(tileOffsetY, (int)Math.Floor(minY));
        int y1 = Math.Min(tileY1, (int)Math.Ceiling(maxY));

        // Top-left rule.
        bool tl0 = IsTopLeftEdge(vertB.ScreenX, vertB.ScreenY, vertC.ScreenX, vertC.ScreenY);
        bool tl1 = IsTopLeftEdge(vertC.ScreenX, vertC.ScreenY, vertA.ScreenX, vertA.ScreenY);
        bool tl2 = IsTopLeftEdge(vertA.ScreenX, vertA.ScreenY, vertB.ScreenX, vertB.ScreenY);

        float invArea = 1.0f / area;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = EdgeFunction(vertB.ScreenX, vertB.ScreenY, vertC.ScreenX, vertC.ScreenY, px, py);
                float w1 = EdgeFunction(vertC.ScreenX, vertC.ScreenY, vertA.ScreenX, vertA.ScreenY, px, py);
                float w2 = EdgeFunction(vertA.ScreenX, vertA.ScreenY, vertB.ScreenX, vertB.ScreenY, px, py);

                // Ensure neighboring triangles don't write to the same pixel.
                // Disambiguated by top-left rule.
                if ((tl0 ? w0 < 0 : w0 <= 0) ||
                    (tl1 ? w1 < 0 : w1 <= 0) ||
                    (tl2 ? w2 < 0 : w2 <= 0))
                    continue;

                w0 *= invArea;
                w1 *= invArea;
                w2 *= invArea;

                // Outside of frustum, discard.
                float depth = w0 * vertA.NdcZ + w1 * vertB.NdcZ + w2 * vertC.NdcZ;
                if (depth < 0 || depth > 1)
                    continue;

                // Get screen position.
                int localX = x - tileOffsetX;
                int localY = y - tileOffsetY;
                int idx = localY * tileWidth + localX;

                // Depth comparison.
                if (fragments[idx].Depth <= depth)
                    continue;

                fragments[idx] = new Fragment(x, y, depth, w0, w1, w2, indexA, indexB, indexC);
            }
        }
    }
    #endregion

}
