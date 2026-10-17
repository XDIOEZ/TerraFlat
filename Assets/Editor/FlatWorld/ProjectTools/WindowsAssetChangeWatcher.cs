#if UNITY_EDITOR_WIN
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/// <summary>通过 Windows 目录通知等待外部改动，避免 Mono FileSystemWatcher 递归扫描资源库。</summary>
internal sealed class WindowsAssetChangeWatcher : IDisposable
{
    #region 目录通知与生命周期

    private const int BufferSize = 64 * 1024;
    private const int ErrorIoPending = 997;
    private const int ErrorOperationAborted = 995;
    private const int ErrorNotifyEnumDir = 1022;
    private readonly string rootPath;
    private readonly Action<string> onChanged;
    private readonly Action<string> onError;
    private readonly ManualResetEvent stopSignal = new(false);
    private readonly Thread worker;
    private int disposed;

    internal WindowsAssetChangeWatcher(string rootPath, Action<string> onChanged, Action<string> onError)
    {
        this.rootPath = rootPath;
        this.onChanged = onChanged;
        this.onError = onError;
        worker = new Thread(Watch)
        {
            IsBackground = true,
            Name = "FlatWorld Asset Change Notifications"
        };
        worker.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        stopSignal.Set();
        // 停止事件唤醒原生等待，再由工作线程取消并收回尚未完成的 I/O。
        if (worker.Join(2000))
            stopSignal.Dispose();
        else
            onError("目录通知线程未及时结束，已发出停止请求；不会强制中止仍在使用原生缓冲的线程。");
    }

    private void Watch()
    {
        bool errorReported = false;
        try
        {
            while (!stopSignal.WaitOne(0))
            {
                try
                {
                    using SafeFileHandle directory = CreateFile(rootPath, 0x0001, 0x0007,
                        IntPtr.Zero, 3, 0x42000000, IntPtr.Zero);
                    if (directory.IsInvalid)
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    WatchDirectory(directory, errorReported);
                }
                catch (ThreadAbortException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (stopSignal.WaitOne(0))
                        break;

                    // 根目录删除或权限变化时只提示一次，按两秒间隔等待恢复，禁止忙重试。
                    if (!errorReported)
                    {
                        errorReported = true;
                        onError("Windows 资源目录通知暂时不可用，将间隔两秒重试：" + exception.Message);
                        onChanged(null);
                    }
                    if (stopSignal.WaitOne(2000))
                        break;
                }
            }
        }
        catch (ThreadAbortException)
        {
            throw;
        }
    }

    private void WatchDirectory(SafeFileHandle directory, bool refreshOnStart)
    {
        using ManualResetEvent changedSignal = new(false);
        WaitHandle[] signals = { stopSignal, changedSignal };
        IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
        IntPtr overlapped = IntPtr.Zero;
        bool pending = false;
        try
        {
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<DirectoryOverlapped>());
            DirectoryOverlapped operation = new() { Event = changedSignal.SafeWaitHandle.DangerousGetHandle() };
            while (!stopSignal.WaitOne(0))
            {
                changedSignal.Reset();
                Marshal.StructureToPtr(operation, overlapped, false);
                bool queued = ReadDirectoryChangesW(directory, buffer, BufferSize, true, 0x0013,
                    IntPtr.Zero, overlapped, IntPtr.Zero);
                int error = queued ? 0 : Marshal.GetLastWin32Error();
                if (!queued && error != ErrorIoPending)
                    throw new Win32Exception(error);

                pending = true;
                if (refreshOnStart)
                {
                    onChanged(null);
                    refreshOnStart = false;
                }
                if (WaitHandle.WaitAny(signals) == 0)
                    return;

                bool completed = GetOverlappedResult(directory, overlapped, out uint bytes, false);
                error = completed ? 0 : Marshal.GetLastWin32Error();
                pending = false;
                if (!completed)
                {
                    if (error == ErrorOperationAborted && stopSignal.WaitOne(0))
                        return;
                    if (error != ErrorNotifyEnumDir)
                        throw new Win32Exception(error);
                    bytes = 0;
                }

                // 通知溢出时只排入一次全量刷新，由主线程沿用静默合并和 PlayMode 延迟规则。
                if (bytes == 0)
                    onChanged(null);
                else
                    DispatchChanges(buffer, (int)bytes);
            }
        }
        finally
        {
            if (pending)
            {
                CancelIoEx(directory, overlapped);
                // 取消后仍须等 I/O 完成，才能释放 Windows 可能正在写入的内存和事件。
                GetOverlappedResult(directory, overlapped, out _, true);
            }
            if (overlapped != IntPtr.Zero)
                Marshal.FreeHGlobal(overlapped);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void DispatchChanges(IntPtr buffer, int bytes)
    {
        int offset = 0;
        while (offset <= bytes - 12)
        {
            int next = Marshal.ReadInt32(buffer, offset);
            int nameBytes = Marshal.ReadInt32(buffer, offset + 8);
            if (nameBytes < 0 || (nameBytes & 1) != 0 || nameBytes > bytes - offset - 12)
            {
                onChanged(null);
                return;
            }

            onChanged(Marshal.PtrToStringUni(IntPtr.Add(buffer, offset + 12), nameBytes / 2));
            if (next == 0)
                return;
            if (next < 12 || next > bytes - offset)
            {
                onChanged(null);
                return;
            }
            offset += next;
        }
    }

    #endregion

    #region Windows 原生接口

    [StructLayout(LayoutKind.Sequential)]
    private struct DirectoryOverlapped
    {
        public UIntPtr Internal;
        public UIntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadDirectoryChangesW(SafeFileHandle directory, IntPtr buffer,
        int length, [MarshalAs(UnmanagedType.Bool)] bool subtree, uint filter,
        IntPtr bytesReturned, IntPtr overlapped, IntPtr completionRoutine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafeFileHandle directory, IntPtr overlapped,
        out uint bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle directory, IntPtr overlapped);

    #endregion
}
#endif
