using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage;
using WinRT;

#region 窗口图像采集

try
{
    var pid = int.Parse(args[0]);
    var outputPath = Path.GetFullPath(args[1]);
    var projectName = args[2];
    var hwnd = FindUnityMainWindow(pid, projectName);
    if (IsIconic(hwnd)) throw new InvalidOperationException("Unity 主窗口已最小化，无法采集");
    if (!GraphicsCaptureSession.IsSupported()) throw new InvalidOperationException("系统不支持 Windows Graphics Capture");
    var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
    var itemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    var itemPtr = factory.CreateForWindow(hwnd, in itemGuid);
    var item = GraphicsCaptureItem.FromAbi(itemPtr);
    Marshal.Release(itemPtr);
    using var device = CreateGraphicsDevice();
    using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
    using var session = pool.CreateCaptureSession(item);
    session.IsCursorCaptureEnabled = false;
    session.StartCapture();
    Direct3D11CaptureFrame? frame = null;
    var timeout = Stopwatch.StartNew();
    while (frame == null && timeout.Elapsed < TimeSpan.FromSeconds(8))
    {
        frame = pool.TryGetNextFrame();
        if (frame == null) await Task.Delay(20);
    }
    using (frame ?? throw new TimeoutException("等待 Unity 窗口画面超时"))
    {
        if (frame.ContentSize.Width != item.Size.Width || frame.ContentSize.Height != item.Size.Height)
            throw new InvalidOperationException("采集期间窗口尺寸改变");
        var capturedAtUtc = DateTime.UtcNow.ToString("o");
        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        Console.WriteLine($"{frame.ContentSize.Width} {frame.ContentSize.Height} {capturedAtUtc}");
    }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    return 1;
}

#endregion

#region 窗口定位与 Direct3D 设备

// 按当前编辑器 PID 和项目标题定位主窗口，不读取或切换前台窗口。
static IntPtr FindUnityMainWindow(int processId, string projectName)
{
    IntPtr found = IntPtr.Zero;
    bool ambiguous = false;
    EnumWindows((window, _) =>
    {
        GetWindowThreadProcessId(window, out var owner);
        if (owner != processId || !IsWindowVisible(window)) return true;
        var title = new StringBuilder(1024);
        GetWindowText(window, title, title.Capacity);
        string text = title.ToString();
        if (!text.StartsWith(projectName + " - ", StringComparison.OrdinalIgnoreCase) ||
            !text.Contains(" - Unity ", StringComparison.Ordinal)) return true;
        if (found != IntPtr.Zero) ambiguous = true;
        found = window;
        return true;
    }, IntPtr.Zero);
    if (found == IntPtr.Zero || ambiguous) throw new InvalidOperationException("无法唯一定位本项目 Unity 主窗口");
    return found;
}

static IDirect3DDevice CreateGraphicsDevice()
{
    Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out var d3d, IntPtr.Zero, IntPtr.Zero));
    var dxgiGuid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    try
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, ref dxgiGuid, out var dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var graphicsDevice));
            try { return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice); }
            finally { Marshal.Release(graphicsDevice); }
        }
        finally { Marshal.Release(dxgi); }
    }
    finally { Marshal.Release(d3d); }
}

[DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder title, int length);
[DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelsCount, uint sdkVersion, out IntPtr device, IntPtr featureLevel, IntPtr immediateContext);
[DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);

[ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow(IntPtr hwnd, in Guid iid);
    IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
}

delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr data);

#endregion
