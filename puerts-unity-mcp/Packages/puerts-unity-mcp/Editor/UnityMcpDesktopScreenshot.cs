using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PuertsUnityMcp.Editor
{
    internal static class UnityMcpDesktopScreenshot
    {
        #region 整个桌面截图

        // 同步截取所有显示器组成的桌面，不等待游戏下一帧，也不改变窗口焦点。
        internal static byte[] Capture(out ScreenshotInfo info)
        {
#if UNITY_EDITOR_WIN
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("无法获取桌面尺寸，未导出本次报告。");
            var pixels = new byte[checked(width * height * 4)];
            IntPtr screenDc = IntPtr.Zero, memoryDc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            Texture2D texture = null;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) throw CaptureError("获取桌面设备");
                memoryDc = CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero) throw CaptureError("创建设备缓存");
                bitmap = CreateCompatibleBitmap(screenDc, width, height);
                if (bitmap == IntPtr.Zero) throw CaptureError("创建截图位图");
                previous = SelectObject(memoryDc, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                {
                    previous = IntPtr.Zero;
                    throw CaptureError("绑定截图位图");
                }
                string capturedAtUtc = DateTime.UtcNow.ToString("o");
                const uint sourceCopyWithLayeredWindows = 0x00CC0020 | 0x40000000;
                if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, sourceCopyWithLayeredWindows))
                    throw CaptureError("读取桌面像素");
                // GetDIBits 要求先解除位图选择，正高度的像素方向与 Unity 纹理一致。
                IntPtr restored = SelectObject(memoryDc, previous);
                if (restored == IntPtr.Zero || restored == new IntPtr(-1)) throw CaptureError("解除位图绑定");
                previous = IntPtr.Zero;
                var bitmapInfo = new BitmapInfo
                {
                    header = new BitmapInfoHeader
                    {
                        size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                        width = width, height = height, planes = 1, bitCount = 32
                    }
                };
                if (GetDIBits(screenDc, bitmap, 0, (uint)height, pixels, ref bitmapInfo, 0) != height)
                    throw CaptureError("提取截图像素");
                // 桌面像素没有可靠 Alpha，转换为不透明 RGBA，避免导出的 PNG 透明。
                for (int offset = 0; offset < pixels.Length; offset += 4)
                {
                    byte blue = pixels[offset];
                    pixels[offset] = pixels[offset + 2];
                    pixels[offset + 2] = blue;
                    pixels[offset + 3] = 255;
                }
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(pixels);
                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0) throw new InvalidOperationException("桌面截图 PNG 编码失败。");
                info = new ScreenshotInfo { capturedAtUtc = capturedAtUtc, left = left, top = top, width = width, height = height };
                return png;
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
#else
            throw new PlatformNotSupportedException("整个桌面截图目前支持 Windows Unity 编辑器。");
#endif
        }

        [Serializable]
        internal sealed class ScreenshotInfo
        {
            public string file = "screen.png";
            public string scope = "Windows virtual desktop, including all monitors; captured at export time, not at the recorded Profiler frame.";
            public string capturedAtUtc;
            public int left, top, width, height;
        }

        #endregion

#if UNITY_EDITOR_WIN
        #region Windows 桌面像素接口

        private static Exception CaptureError(string step)
            => new Win32Exception(Marshal.GetLastWin32Error(), "桌面截图失败：" + step);

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint size;
            public int width, height;
            public ushort planes, bitCount;
            public uint compression, sizeImage;
            public int xPixelsPerMeter, yPixelsPerMeter;
            public uint colorsUsed, colorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader header;
            public uint colors;
        }

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint firstLine, uint lines, [Out] byte[] pixels, ref BitmapInfo info, uint usage);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);

        #endregion
#endif
    }
}
