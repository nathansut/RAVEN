using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace RAVEN
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length >= 1 && args[0].Equals("testfeature", StringComparison.OrdinalIgnoreCase))
            {
                TestFeature(args);
                return;
            }

            if (args.Length >= 3 && args[0].Equals("rdynamic", StringComparison.OrdinalIgnoreCase))
            {
                RunCLI(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        // Usage: RAVEN.exe testfeature <input> [w] [h] [contrast] [brightness]
        // Replicates the full GUI pipeline: load → grayscale → threshold → write-back → save
        // Output and log written next to input file as <input>_rdynamic.png / .log
        static void TestFeature(string[] args)
        {
            string input   = args.Length > 1 ? args[1] : @"c:\DEV\RAVEN\BlankJPG.jpg";
            int w          = args.Length > 2 ? int.Parse(args[2]) : 7;
            int h          = args.Length > 3 ? int.Parse(args[3]) : 7;
            int contrast   = args.Length > 4 ? int.Parse(args[4]) : 248;
            int brightness = args.Length > 5 ? int.Parse(args[5]) : 220;

            string output  = Path.ChangeExtension(input, null) + "_rdynamic.png";
            string logPath = output + ".log";

            using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            void Log(string msg) { log.WriteLine(msg); }

            try
            {
                Log($"=== testfeature pipeline ===");
                Log($"Input:     {input}");
                Log($"Output:    {output}");
                Log($"Params:    w={w} h={h} contrast={contrast} brightness={brightness}");
                Log("");

                // --- Step 1+2+3: Full pipeline via the real production method ---
                var sw = Stopwatch.StartNew();
                OpenThresholdBridge.ApplyThresholdToFile(input, output, w, h, contrast, brightness);
                sw.Stop();
                Log($"Step 2 - ApplyThresholdToFile: {sw.ElapsedMilliseconds}ms");

                if (!File.Exists(output)) { Log("ERROR: output file was not created"); return; }

                // Verify output pixels are strictly binary
                using var resultMat = CvInvoke.Imread(output, ImreadModes.Grayscale);
                int black = 0, white = 0, bad = 0;
                byte[] resultPixels = new byte[resultMat.Width * resultMat.Height];
                for (int y = 0; y < resultMat.Height; y++)
                    Marshal.Copy(resultMat.DataPointer + y * resultMat.Step, resultPixels, y * resultMat.Width, resultMat.Width);
                foreach (byte p in resultPixels)
                {
                    if      (p == 0)   black++;
                    else if (p == 255) white++;
                    else               bad++;
                }
                Log($"           Black: {black:N0}  White: {white:N0}  Non-binary: {bad}");
                Log($"           White ratio: {white * 100.0 / resultPixels.Length:F1}%");
                if (bad > 0) Log($"           WARNING: {bad} non-binary pixels!");
                else         Log($"           OK: all pixels strictly 0 or 255");

                Log("");
                Log("PASS - pipeline completed successfully");
            }
            catch (Exception ex)
            {
                Log($"FAIL - {ex}");
            }
        }

        // Usage: RAVEN.exe rdynamic <input> <output> [w] [h] [contrast] [brightness]
        // Results are written to <output>.log
        static void RunCLI(string[] args)
        {
            string logPath = args.Length > 2 ? args[2] + ".log" : "rdynamic.log";
            using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            void Log(string msg) { log.WriteLine(msg); }

            try
            {
                string input   = args[1];
                string output  = args[2];
                int w          = args.Length > 3 ? int.Parse(args[3]) : 7;
                int h          = args.Length > 4 ? int.Parse(args[4]) : 7;
                int contrast   = args.Length > 5 ? int.Parse(args[5]) : 248;
                int brightness = args.Length > 6 ? int.Parse(args[6]) : 220;

                Log($"Input:  {input}");
                Log($"Params: w={w} h={h} contrast={contrast} brightness={brightness}");

                using var mat = CvInvoke.Imread(input, ImreadModes.Grayscale);
                if (mat.IsEmpty) { Log("ERROR: Could not load image."); return; }

                int width = mat.Width, height = mat.Height;
                Log($"Image:  {width}x{height} ({width * height / 1_000_000.0:F1}MP)");

                byte[] gray = new byte[width * height];
                for (int y = 0; y < height; y++)
                    Marshal.Copy(mat.DataPointer + y * mat.Step, gray, y * width, width);

                var sw = Stopwatch.StartNew();
                byte[] binary = DynamicThreshold.Apply(gray, width, height, w, h, contrast, brightness);
                sw.Stop();
                Log($"Threshold: {sw.ElapsedMilliseconds}ms");

                using var outMat = new Mat(height, width, DepthType.Cv8U, 1);
                Marshal.Copy(binary, 0, outMat.DataPointer, binary.Length);
                CvInvoke.Imwrite(output, outMat);
                Log($"Saved:  {output}");
                Log("OK");
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex}");
            }
        }
    }
}
