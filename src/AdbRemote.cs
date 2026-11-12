// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

// shell-v2 raw packets avoid Windows adb.exe stdin text-mode/Ctrl-Z conversion.
sealed class AdbRemote(string serial) : IDisposable
{
    readonly ConcurrentDictionary<TcpClient, byte> active = new();
    int canceled;
    public static string Quote(string text) => "'" + text.Replace("'", "'\\''") + "'";
    static byte[] Exact(NetworkStream stream, int count)
    {
        var bytes = new byte[count]; stream.ReadExactly(bytes); return bytes;
    }
    static void Service(NetworkStream stream, string service)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(service);
        if (bytes.Length > 65535) throw new IOException("ADB service command too long");
        stream.Write(Encoding.ASCII.GetBytes(bytes.Length.ToString("x4"))); stream.Write(bytes);
        string status = Encoding.ASCII.GetString(Exact(stream, 4));
        if (status == "FAIL") throw new IOException(Encoding.UTF8.GetString(Exact(stream, Convert.ToInt32(Encoding.ASCII.GetString(Exact(stream, 4)), 16))));
        if (status != "OKAY") throw new IOException("Unexpected ADB status: " + status);
    }
    static void Packet(NetworkStream stream, byte id, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[5]; header[0] = id;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..], data.Length);
        stream.Write(header); stream.Write(data);
    }
    public string Run(string command, byte[] input = null)
    {
        if (Volatile.Read(ref canceled) != 0) throw new IOException("文件盘正在卸载。");
        using var client = new TcpClient { ReceiveTimeout = 30000, SendTimeout = 30000, NoDelay = true };
        active[client] = 0;
        try
        {
            client.Connect("127.0.0.1", 5037);
            if (Volatile.Read(ref canceled) != 0) throw new IOException("文件盘正在卸载。");
            using var stream = client.GetStream();
            Service(stream, "host:transport:" + serial);
            Service(stream, "shell,v2,raw:" + command);
            var receive = Task.Run(() =>
            {
                using var output = new MemoryStream(); using var error = new MemoryStream();
                while (true)
                {
                    byte[] header = Exact(stream, 5);
                    int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
                    if (length < 0 || length > 1024 * 1024) throw new IOException("Invalid ADB shell packet");
                    byte[] data = Exact(stream, length);
                    if (header[0] == 1) output.Write(data);
                    else if (header[0] == 2) error.Write(data);
                    else if (header[0] == 3)
                    {
                        if (length != 1 && length != 4) throw new IOException("Invalid ADB shell exit status");
                        int code = length == 1 ? data[0] : BinaryPrimitives.ReadInt32LittleEndian(data);
                        return (Code: code, Output: Encoding.UTF8.GetString(output.ToArray()), Error: Encoding.UTF8.GetString(error.ToArray()));
                    }
                    if (output.Length + error.Length > 4 * 1024 * 1024) throw new IOException("ADB command output exceeds limit");
                }
            });
            try
            {
                if (input != null)
                    for (int offset = 0; offset < input.Length; offset += 65536)
                        Packet(stream, 0, input.AsSpan(offset, Math.Min(65536, input.Length - offset)));
                Packet(stream, 4, []); // Close stdin, preserving stdout/stderr and exit status.
                var result = receive.GetAwaiter().GetResult();
                if (result.Code != 0)
                {
                    if (result.Error.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("Read-only file system", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException(result.Error.Trim());
                    if (result.Error.Contains("No such file", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException(result.Error.Trim());
                    throw new IOException($"Android 操作失败 ({result.Code}): {result.Error.Trim()} {result.Output.Trim()}");
                }
                return result.Output.Trim();
            }
            finally
            {
                client.Dispose();
                try { receive.GetAwaiter().GetResult(); } catch { }
            }
        }
        finally { active.TryRemove(client, out _); }
    }
    public void Write(string path, ulong offset, byte[] data) => Run($"dd of={Quote(path)} bs=65536 seek={offset} oflag=seek_bytes conv=notrunc,nocreat,fsync status=none", data);
    public void Truncate(string path, ulong size) => Run($"truncate -c -s {size} {Quote(path)} && dd of={Quote(path)} count=0 conv=notrunc,nocreat,fsync status=none");
    public void Flush(string path) => Run($"dd of={Quote(path)} count=0 conv=notrunc,nocreat,fsync status=none");
    public void Cancel()
    {
        Interlocked.Exchange(ref canceled, 1);
        foreach (var client in active.Keys) client.Dispose();
    }
    public void Dispose() => Cancel();
}
