using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;
using UnityShaderParser.HLSL.PreProcessor;
using HLSL;

public class Program
{
    public struct ColorRGBA
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public ColorRGBA(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    public static class BitmapWriter
    {
        public static void WriteBmp(string filePath, ColorRGBA[,] pixels)
        {
            int width = pixels.GetLength(0);
            int height = pixels.GetLength(1);

            // BMP rows are padded to multiples of 4 bytes
            int bytesPerPixel = 3; // 24-bit RGB
            int rowSize = (width * bytesPerPixel + 3) & ~3;
            int pixelDataSize = rowSize * height;
            int fileSize = 54 + pixelDataSize; // 54 = BMP header size

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // === BITMAP FILE HEADER (14 bytes) ===
                bw.Write((byte)'B');
                bw.Write((byte)'M');
                bw.Write(fileSize);
                bw.Write(0); // Reserved
                bw.Write(54); // Offset to pixel data

                // === DIB HEADER (40 bytes) ===
                bw.Write(40);        // DIB header size
                bw.Write(width);
                bw.Write(height);
                bw.Write((short)1); // Color planes
                bw.Write((short)24); // Bits per pixel
                bw.Write(0);        // Compression (0 = none)
                bw.Write(pixelDataSize);
                bw.Write(0);        // Horizontal resolution (pixels/meter)
                bw.Write(0);        // Vertical resolution
                bw.Write(0);        // Colors in palette
                bw.Write(0);        // Important colors

                // === PIXEL DATA ===
                // BMP stores pixels bottom-to-top
                byte[] padding = new byte[rowSize - width * bytesPerPixel];

                for (int y = height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var c = pixels[x, y];

                        // BMP uses BGR order
                        bw.Write(c.B);
                        bw.Write(c.G);
                        bw.Write(c.R);
                    }

                    // Row padding
                    bw.Write(padding);
                }
            }
        }
    }

    private static void RunShader(string shaderPath, ColorRGBA[,] outputColors)
    {
        string shaderSource = File.ReadAllText(shaderPath);

        var config = new HLSLParserConfig()
        {
            PreProcessorMode = PreProcessorMode.ExpandAll,
            Defines = new Dictionary<string, string>() { { "HLSL_TEST", "1" } }
        };

        var toks = ShaderParser.ParseTopLevelDeclarations(shaderSource, config, out var diags, out var prags);

        int resolutionX = outputColors?.GetLength(0) ?? 92;
        int resolutionY = outputColors?.GetLength(1) ?? 92;
        int warpSize = 4;
        int progress = 0;

#if DEBUG
        for (int y = 0; y < resolutionY / warpSize; y++)
#else
        Parallel.For(0, resolutionY / warpSize, y =>
#endif
        {
            HLSLRunner runner = new HLSLRunner();
            runner.ProcessCode(toks);
            runner.SetVariable("_Time", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>(new RawValue[] { 0f, 0f })));
            runner.SetVariable("_Resolution", new ScalarValue(ScalarType.Float, new HLSLRegister<RawValue>(1f)));

            var uvs = new RawValue[warpSize * warpSize][];
            for (int i = 0; i < uvs.Length; i++)
                uvs[i] = new RawValue[2];

            for (int x = 0; x < resolutionX / warpSize; x++)
            {
                var v2fdict = new Dictionary<string, HLSLValue>();
                for (int warpY = 0; warpY < warpSize; warpY++)
                {
                    for (int warpX = 0; warpX < warpSize; warpX++)
                    {
                        uvs[warpY * warpSize + warpX][0] = ((float)x * warpSize + warpX) / resolutionX;
                        uvs[warpY * warpSize + warpX][1] = 1.0f - ((float)y * warpSize + warpY) / resolutionY;
                    }
                }
                v2fdict["uv"] = new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>(uvs).Converge());
                var v2f = new StructValue("v2f", v2fdict);

                runner.SetWarpSize(warpSize, warpSize);
                var color = runner.CallFunction("frag", v2f);
                if (outputColors != null)
                {
                    for (int warpY = 0; warpY < warpSize; warpY++)
                    {
                        for (int warpX = 0; warpX < warpSize; warpX++)
                        {
                            var colorVec = ((VectorValue)color).Values.Get(warpY * warpSize + warpX);
                            outputColors[x * warpSize + warpX, y * warpSize + warpY] = new ColorRGBA(
                                (byte)(Math.Clamp(colorVec[0].Float, 0, 1) * 255),
                                (byte)(Math.Clamp(colorVec[1].Float, 0, 1) * 255),
                                (byte)(Math.Clamp(colorVec[2].Float, 0, 1) * 255),
                                (byte)(Math.Clamp(colorVec[3].Float, 0, 1) * 255)
                            );
                        }
                    }
                }
            }
            if (outputColors != null)
                Console.WriteLine($"{Interlocked.Add(ref progress, 1) / (float)(resolutionY / warpSize) * 100f}%");
        }
#if !DEBUG
        );
#endif
    }

    public static void RunShaderToy()
    {
        string shaderFolderPath = @"Shaders/ShaderToy";
        Console.WriteLine("Pick example to run:");
        int shaderIndex = 0;
        var shaders = Directory.GetFiles(shaderFolderPath);
        foreach (var file in shaders)
        {
            Console.WriteLine($"({shaderIndex++}) {Path.GetFileName(file)}");
        }
        string shaderPath = shaders[int.Parse(Console.ReadKey().KeyChar.ToString())];
        Console.WriteLine();

        int resolutionX = 92;
        int resolutionY = 92;
        ColorRGBA[,] colors = new ColorRGBA[resolutionX, resolutionY];

        var sw = Stopwatch.StartNew();
        RunShader(shaderPath, colors);
        sw.Stop();
        Console.WriteLine("Took " + sw.ElapsedMilliseconds / 1000.0f + " seconds.");

        BitmapWriter.WriteBmp("output.bmp", colors);
        Console.WriteLine($"Wrote output to {Path.GetFullPath("output.bmp")}");

        // Open output
        var p = new Process();
        p.StartInfo = new ProcessStartInfo(Path.GetFullPath("output.bmp"))
        {
            UseShellExecute = true
        };
        p.Start();
    }

    public static void BenchmarkAll()
    {
        string shaderFolderPath = @"Shaders/ShaderToy";
        var shaders = Directory.GetFiles(shaderFolderPath).OrderBy(p => p).ToArray();

        Console.WriteLine("Warming up...");
        for (int i = 0; i < 2; i++)
            foreach (var shader in shaders)
                RunShader(shader, null);

        Console.WriteLine();
        Console.WriteLine("Timed runs:");
        var perShaderMs = new Dictionary<string, long>();
        var total = Stopwatch.StartNew();
        foreach (var shader in shaders)
        {
            var sw = Stopwatch.StartNew();
            RunShader(shader, null);
            sw.Stop();
            perShaderMs[Path.GetFileName(shader)] = sw.ElapsedMilliseconds;
        }
        total.Stop();

        Console.WriteLine();
        foreach (var kv in perShaderMs)
            Console.WriteLine($"  {kv.Key,-30} {kv.Value,8} ms");
        Console.WriteLine();
        Console.WriteLine($"  {"TOTAL",-30} {total.ElapsedMilliseconds,8} ms");
    }

    public static void Main()
    {
        while (true)
            RunShaderToy();
    }
}
