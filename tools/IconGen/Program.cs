using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Svg;

class Program
{
    static int Main(string[] args)
    {
        string svgPath = args.Length > 0 ? args[0] : @"C:\aryan\SW2GZ\assets\gazebo_logo.svg";
        string outDir  = args.Length > 1 ? args[1] : @"C:\aryan\SW2GZ\SW2GZ\UI\Resources\Icons";
        int[] sizes = { 20, 32, 40, 64, 96, 128 };

        var doc = SvgDocument.Open(svgPath);
        Directory.CreateDirectory(outDir);

        foreach (int s in sizes)
        {
            // Render onto a transparent square canvas, preserving aspect (logo is ~square).
            using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                using var rendered = doc.Draw(s, s); // scales SVG to s x s
                g.DrawImage(rendered, 0, 0, s, s);
            }
            string outPath = Path.Combine(outDir, $"sw2gz_{s}.png");
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length} bytes)");
        }
        Console.WriteLine("done");
        return 0;
    }
}
