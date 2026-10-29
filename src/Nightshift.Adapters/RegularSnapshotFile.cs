using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nightshift.Adapters;

// Only the supported 64-bit Unix stat ABIs are used. Inspect the pathname before
// opening, then inspect the same nonblocking, no-follow descriptor that is read.
internal static class RegularSnapshotFile
{
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] status);
    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(SafeFileHandle handle, [Out] byte[] status);
    [DllImport("libc", EntryPoint = "lstat$INODE64", SetLastError = true)]
    private static extern int DarwinLStat64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] status);
    [DllImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
    private static extern int DarwinFStat64(SafeFileHandle handle, [Out] byte[] status);

    internal static (byte[] Bytes, bool Executable) Read(string path, int maximumBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool mac = OperatingSystem.IsMacOS(); var architecture = RuntimeInformation.ProcessArchitecture;
        if ((!mac && !OperatingSystem.IsLinux()) || architecture is not Architecture.Arm64 and not Architecture.X64)
            throw new IOException("Snapshot capture supports macOS/Linux ARM64 or x64 only");
        bool darwin64Symbol = mac && architecture == Architecture.X64;
        // stat is 144 bytes on Darwin/x64 Linux, 128 bytes on ARM64 Linux.
        // An oversized buffer avoids marshaling unrelated platform-specific fields.
        byte[] status = new byte[256];
        Check(darwin64Symbol ? DarwinLStat64(path, status) : LStat(path, status), path);
        RequireRegular(Mode(status, mac, architecture), path);
        // O_RDONLY | O_NONBLOCK | O_NOFOLLOW | O_CLOEXEC | O_NOCTTY.
        // O_NONBLOCK prevents a regular-file -> FIFO replacement from hanging open.
        int flags = mac ? 0x4 | 0x100 | 0x1000000 | 0x20000 : 0x800 | 0x20000 | 0x80000 | 0x100;
        int descriptor = Open(path, flags);
        if (descriptor < 0) throw Error(path);
        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        Check(darwin64Symbol ? DarwinFStat64(handle, status) : FStat(handle, status), path);
        uint mode = Mode(status, mac, architecture); RequireRegular(mode, path);
        using var stream = new FileStream(handle, FileAccess.Read);
        if (stream.Length > maximumBytes) throw new IOException("Snapshot exceeds collection limits");
        using var bytes = new MemoryStream(); byte[] buffer = new byte[8192];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = stream.Read(buffer);
            if (count == 0) break;
            if (bytes.Length + count > maximumBytes) throw new IOException("Snapshot exceeds collection limits");
            bytes.Write(buffer, 0, count);
        }
        return (bytes.ToArray(), (mode & 0x40) != 0);
    }
    private static uint Mode(byte[] status, bool mac, Architecture architecture) => mac
        ? BitConverter.ToUInt16(status, 4) : BitConverter.ToUInt32(status, architecture == Architecture.X64 ? 24 : 16);
    private static void RequireRegular(uint mode, string path)
    { if ((mode & 0xF000) != 0x8000) throw new IOException("Snapshot input is not a regular file: " + path); }
    private static void Check(int result, string path) { if (result != 0) throw Error(path); }
    private static IOException Error(string path) => new("Cannot safely open snapshot input: " + path, new Win32Exception(Marshal.GetLastPInvokeError()));
}
