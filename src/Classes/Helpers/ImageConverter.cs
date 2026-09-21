using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PublishContent.Classes.Helpers
{
    class ImageConverter
    {
        static readonly int[] DefaultIconSizes = { 16, 24, 32, 48, 64, 128, 256 };

        /// <summary>
        /// Builds a classic 32-bit BMP ICO (multiple sizes, alpha preserved).
        /// PNG-in-ICO and System.Drawing.Icon.Save() both get reduced to 16 colors by Citrix/GDI.
        /// </summary>
        public static byte[] ConvertImageToIcoBytes(string inputImage, int[] sizes = null)
        {
            if (string.Equals(Path.GetExtension(inputImage), ".ico", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllBytes(inputImage);

            sizes = sizes ?? DefaultIconSizes;
            using (var source = Image.FromFile(inputImage))
            {
                return ConvertImageToIcoBytes(source, sizes);
            }
        }

        public static byte[] ConvertImageToIcoBytes(Image source, int[] sizes)
        {
            var frames = new List<byte[]>(sizes.Length);
            foreach (var size in sizes)
            {
                using (var squared = ResizeToSquare(source, size))
                    frames.Add(BuildBmpIconFrame(squared));
            }

            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)frames.Count);

                var offset = 6 + (16 * frames.Count);
                for (var i = 0; i < frames.Count; i++)
                {
                    var size = sizes[i];
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(frames[i].Length);
                    writer.Write(offset);
                    offset += frames[i].Length;
                }

                foreach (var frame in frames)
                    writer.Write(frame);

                writer.Flush();
                return output.ToArray();
            }
        }

        public static bool ConvertToIco(Stream inputStream, Stream outputStream, int size, bool keepAspectRatio = false)
        {
            using (var input = Image.FromStream(inputStream))
            {
                var bytes = ConvertImageToIcoBytes(input, new[] { size });
                outputStream.Write(bytes, 0, bytes.Length);
                return true;
            }
        }

        public static bool ConvertToIco(string inputImage, string outputIcon, int size, bool keepAspectRatio = false)
        {
            var bytes = ConvertImageToIcoBytes(inputImage, new[] { size });
            File.WriteAllBytes(outputIcon, bytes);
            return true;
        }

        static Bitmap ResizeToSquare(Image source, int size)
        {
            var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            dest.SetResolution(96, 96);
            using (var g = Graphics.FromImage(dest))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var scale = Math.Min((float)size / source.Width, (float)size / source.Height);
                var width = Math.Max(1, (int)Math.Round(source.Width * scale));
                var height = Math.Max(1, (int)Math.Round(source.Height * scale));
                var x = (size - width) / 2;
                var y = (size - height) / 2;
                g.DrawImage(source, new Rectangle(x, y, width, height));
            }
            return dest;
        }

        static byte[] BuildBmpIconFrame(Bitmap bitmap)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var xorStride = width * 4;
            var maskStride = ((width + 31) / 32) * 4;
            var xorSize = xorStride * height;
            var maskSize = maskStride * height;

            using (var stream = new MemoryStream(40 + xorSize + maskSize))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(40);
                writer.Write(width);
                writer.Write(height * 2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0);
                writer.Write(xorSize + maskSize);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);

                var data = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[xorStride];
                    for (var y = height - 1; y >= 0; y--)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, xorStride);
                        writer.Write(row);
                    }

                    var maskRow = new byte[maskStride];
                    for (var y = height - 1; y >= 0; y--)
                    {
                        Array.Clear(maskRow, 0, maskStride);
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, xorStride);
                        for (var x = 0; x < width; x++)
                        {
                            if (row[(x * 4) + 3] == 0)
                                maskRow[x / 8] |= (byte)(0x80 >> (x % 8));
                        }
                        writer.Write(maskRow);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return stream.ToArray();
            }
        }
    }
}
