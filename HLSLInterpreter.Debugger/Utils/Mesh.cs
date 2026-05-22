using System.Globalization;

namespace HLSLInterpreter.Debugger.Utils;

public sealed class Mesh
{
    public float[] Positions { get; init; } = Array.Empty<float>();
    public float[] Normals { get; init; } = Array.Empty<float>();
    public float[] Uvs { get; init; } = Array.Empty<float>();
    public uint[] Indices { get; init; } = Array.Empty<uint>();

    public int VertexCount => Positions.Length / 3;
    public int IndexCount => Indices.Length;
    public int TriangleCount => IndexCount / 3;

    public const int VertexStrideFloats = 8;

    public static readonly IReadOnlyDictionary<(string Base, int Index), int> AttributeDimensions
        = new Dictionary<(string, int), int>
        {
            { ("POSITION", 0), 3 },
            { ("NORMAL",   0), 3 },
            { ("TEXCOORD", 0), 2 },
        };

    public void ReadAttribute(int vertexIndex, (string Base, int Index) semantic, Span<float> outBuffer)
    {
        switch (semantic.Base)
        {
            case "POSITION":
                outBuffer[0] = Positions[vertexIndex * 3 + 0];
                outBuffer[1] = Positions[vertexIndex * 3 + 1];
                outBuffer[2] = Positions[vertexIndex * 3 + 2];
                break;
            case "NORMAL":
                outBuffer[0] = Normals[vertexIndex * 3 + 0];
                outBuffer[1] = Normals[vertexIndex * 3 + 1];
                outBuffer[2] = Normals[vertexIndex * 3 + 2];
                break;
            case "TEXCOORD":
                outBuffer[0] = Uvs[vertexIndex * 2 + 0];
                outBuffer[1] = Uvs[vertexIndex * 2 + 1];
                break;
            default:
                throw new ArgumentException($"Semantic '{semantic.Base}' not supported.");
        }
    }

    public float[] GetInterleavedVertices()
    {
        var result = new float[VertexCount * VertexStrideFloats];
        int o = 0;
        for (int v = 0; v < VertexCount; v++)
        {
            result[o++] = Positions[v * 3 + 0];
            result[o++] = Positions[v * 3 + 1];
            result[o++] = Positions[v * 3 + 2];
            result[o++] = Normals[v * 3 + 0];
            result[o++] = Normals[v * 3 + 1];
            result[o++] = Normals[v * 3 + 2];
            result[o++] = Uvs[v * 2 + 0];
            result[o++] = Uvs[v * 2 + 1];
        }
        return result;
    }

    public void FitToUnitCube()
    {
        int count = VertexCount;
        if (count == 0) return;
        float maxAbs = 0f;
        for (int v = 0; v < count; v++)
        {
            float x = MathF.Abs(Positions[v * 3 + 0]);
            float y = MathF.Abs(Positions[v * 3 + 1]);
            float z = MathF.Abs(Positions[v * 3 + 2]);
            if (x > maxAbs) maxAbs = x;
            if (y > maxAbs) maxAbs = y;
            if (z > maxAbs) maxAbs = z;
        }
        if (maxAbs == 0f) return;
        float scale = 1f / maxAbs;
        for (int v = 0; v < count; v++)
        {
            Positions[v * 3 + 0] *= scale;
            Positions[v * 3 + 1] *= scale;
            Positions[v * 3 + 2] *= scale;
        }
    }

    public void CenterOnCentroid()
    {
        int count = VertexCount;
        if (count == 0) return;
        float sumX = 0, sumY = 0, sumZ = 0;
        for (int v = 0; v < count; v++)
        {
            sumX += Positions[v * 3 + 0];
            sumY += Positions[v * 3 + 1];
            sumZ += Positions[v * 3 + 2];
        }
        float cx = sumX / count, cy = sumY / count, cz = sumZ / count;
        for (int v = 0; v < count; v++)
        {
            Positions[v * 3 + 0] -= cx;
            Positions[v * 3 + 1] -= cy;
            Positions[v * 3 + 2] -= cz;
        }
    }

    public void RecalculateNormals()
    {
        Array.Clear(Normals, 0, Normals.Length);

        for (int t = 0; t < Indices.Length; t += 3)
        {
            int i0 = (int)Indices[t + 0];
            int i1 = (int)Indices[t + 1];
            int i2 = (int)Indices[t + 2];

            float ax = Positions[i0 * 3], ay = Positions[i0 * 3 + 1], az = Positions[i0 * 3 + 2];
            float bx = Positions[i1 * 3], by = Positions[i1 * 3 + 1], bz = Positions[i1 * 3 + 2];
            float cx = Positions[i2 * 3], cy = Positions[i2 * 3 + 1], cz = Positions[i2 * 3 + 2];

            float ex = bx - ax, ey = by - ay, ez = bz - az;
            float fx = cx - ax, fy = cy - ay, fz = cz - az;
            float nx = ey * fz - ez * fy;
            float ny = ez * fx - ex * fz;
            float nz = ex * fy - ey * fx;

            Normals[i0 * 3 + 0] += nx; Normals[i0 * 3 + 1] += ny; Normals[i0 * 3 + 2] += nz;
            Normals[i1 * 3 + 0] += nx; Normals[i1 * 3 + 1] += ny; Normals[i1 * 3 + 2] += nz;
            Normals[i2 * 3 + 0] += nx; Normals[i2 * 3 + 1] += ny; Normals[i2 * 3 + 2] += nz;
        }

        for (int v = 0; v < VertexCount; v++)
        {
            float nx = Normals[v * 3], ny = Normals[v * 3 + 1], nz = Normals[v * 3 + 2];
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0)
            {
                Normals[v * 3 + 0] = nx / len;
                Normals[v * 3 + 1] = ny / len;
                Normals[v * 3 + 2] = nz / len;
            }
        }
    }

    public static Mesh ParseObj(string text)
    {
        static float ParseFloat(string s)
        {
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        var positions = new List<(float X, float Y, float Z)>();
        var normals = new List<(float X, float Y, float Z)>();
        var uvs = new List<(float U, float V)>();

        var faceCorners = new List<(int Pos, int Uv, int Normal)>();

        using var reader = new StringReader(text);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            int hash = line.IndexOf('#');
            if (hash >= 0)
                line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            switch (parts[0])
            {
                case "v":
                    if (parts.Length >= 4)
                        positions.Add((ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;
                case "vn":
                    if (parts.Length >= 4)
                        normals.Add((ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;
                case "vt":
                    if (parts.Length >= 3)
                        uvs.Add((ParseFloat(parts[1]), ParseFloat(parts[2])));
                    else if (parts.Length == 2)
                        uvs.Add((ParseFloat(parts[1]), 0f));
                    break;
                case "f":
                    if (parts.Length < 4)
                        break;
                    var corners = new (int Pos, int Uv, int Normal)[parts.Length - 1];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var s = parts[i].Split('/');
                        corners[i - 1] = (
                            int.Parse(s[0]) - 1,
                            s.Length > 1 && s[1].Length > 0 ? int.Parse(s[1]) - 1 : -1,
                            s.Length > 2 && s[2].Length > 0 ? int.Parse(s[2]) - 1 : -1);
                    }

                    for (int i = 1; i < corners.Length - 1; i++)
                    {
                        faceCorners.Add(corners[0]);
                        faceCorners.Add(corners[i]);
                        faceCorners.Add(corners[i + 1]);
                    }
                    break;
            }
        }

        bool hasNormals = normals.Count > 0;
        bool hasUvs = uvs.Count > 0;

        var indexMap = new Dictionary<(int, int, int), uint>();
        var outPos = new List<float>();
        var outNrm = new List<float>();
        var outUv = new List<float>();
        var outIdx = new List<uint>();

        foreach (var key in faceCorners)
        {
            if (!indexMap.TryGetValue(key, out var idx))
            {
                if (key.Pos < 0 || key.Pos >= positions.Count)
                    throw new InvalidDataException($"OBJ references position index {key.Pos + 1} which doesn't exist.");

                idx = (uint)(outPos.Count / 3);
                indexMap[key] = idx;

                var (px, py, pz) = positions[key.Pos];
                outPos.Add(px); outPos.Add(py); outPos.Add(pz);

                if (hasNormals && key.Normal >= 0 && key.Normal < normals.Count)
                {
                    var (nx, ny, nz) = normals[key.Normal];
                    outNrm.Add(nx); outNrm.Add(ny); outNrm.Add(nz);
                }
                else
                {
                    outNrm.Add(0); outNrm.Add(0); outNrm.Add(0);
                }

                if (hasUvs && key.Uv >= 0 && key.Uv < uvs.Count)
                {
                    var (u, v) = uvs[key.Uv];
                    outUv.Add(u); outUv.Add(v);
                }
                else
                {
                    outUv.Add(0); outUv.Add(0);
                }
            }
            outIdx.Add(idx);
        }

        var mesh = new Mesh
        {
            Positions = outPos.ToArray(),
            Normals = outNrm.ToArray(),
            Uvs = outUv.ToArray(),
            Indices = outIdx.ToArray(),
        };

        if (!hasNormals && mesh.VertexCount > 0)
            mesh.RecalculateNormals();

        // Make it easier to see the mesh
        mesh.CenterOnCentroid();
        mesh.FitToUnitCube();

        return mesh;
    }

    // Default cube mesh
    public static Mesh CreateCube()
    {
        var c = new (float X, float Y, float Z)[]
        {
            (-1,-1,-1), ( 1,-1,-1), ( 1, 1,-1), (-1, 1,-1),
            (-1,-1, 1), ( 1,-1, 1), ( 1, 1, 1), (-1, 1, 1),
        };
        var faces = new (int BL, int BR, int TR, int TL, float Nx, float Ny, float Nz)[]
        {
            (4,5,6,7,  0, 0, 1),
            (1,0,3,2,  0, 0,-1),
            (5,1,2,6,  1, 0, 0),
            (0,4,7,3, -1, 0, 0),
            (3,7,6,2,  0, 1, 0),
            (0,1,5,4,  0,-1, 0),
        };
        var uvs = new (float U, float V)[] { (0, 0), (1, 0), (1, 1), (0, 1) };

        var positions = new float[24 * 3];
        var normals = new float[24 * 3];
        var uvBuf = new float[24 * 2];
        int op = 0, on = 0, ou = 0;
        foreach (var f in faces)
        {
            int[] cornerIdx = [f.BL, f.BR, f.TR, f.TL];
            for (int i = 0; i < 4; i++)
            {
                positions[op++] = c[cornerIdx[i]].X;
                positions[op++] = c[cornerIdx[i]].Y;
                positions[op++] = c[cornerIdx[i]].Z;
                normals[on++] = f.Nx;
                normals[on++] = f.Ny;
                normals[on++] = f.Nz;
                uvBuf[ou++] = uvs[i].U;
                uvBuf[ou++] = uvs[i].V;
            }
        }

        var indices = new uint[36];
        for (int f = 0; f < 6; f++)
        {
            int b = f * 4, oi = f * 6;
            indices[oi + 0] = (uint)(b + 0);
            indices[oi + 1] = (uint)(b + 1);
            indices[oi + 2] = (uint)(b + 2);
            indices[oi + 3] = (uint)(b + 0);
            indices[oi + 4] = (uint)(b + 2);
            indices[oi + 5] = (uint)(b + 3);
        }

        return new Mesh { Positions = positions, Normals = normals, Uvs = uvBuf, Indices = indices };
    }

}
