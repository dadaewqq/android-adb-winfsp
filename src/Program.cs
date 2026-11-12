// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using Fsp;
using FsInfo = Fsp.Interop.FileInfo;
using VolumeInfo = Fsp.Interop.VolumeInfo;

record RemoteInfo(string Name, uint Mode, ulong Size, long Modified, long Accessed = 0, long Changed = 0)
{
    public bool Directory => (Mode & 0xF000) == 0x4000;
    public bool Regular => (Mode & 0xF000) == 0x8000;
}

sealed class AdbSync : IDisposable
{
    readonly TcpClient client = new();
    readonly NetworkStream stream;
    public bool AtBoundary { get; private set; } = true;
    public AdbSync(string serial)
    {
        client.ReceiveTimeout = client.SendTimeout = 15000;
        client.NoDelay = true;
        client.Connect("127.0.0.1", 5037);
        stream = client.GetStream();
        try { Host("host:transport:" + serial); Host("sync:"); }
        catch { Dispose(); throw; }
    }
    public void Dispose() { client.Dispose(); }
    public byte[] ReadExactly(int count)
    {
        var bytes = new byte[count];
        stream.ReadExactly(bytes);
        return bytes;
    }
    void Host(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(Encoding.ASCII.GetBytes(bytes.Length.ToString("x4")).Concat(bytes).ToArray());
        string status = Encoding.ASCII.GetString(ReadExactly(4));
        if (status == "FAIL")
            throw new IOException(Encoding.UTF8.GetString(ReadExactly(Convert.ToInt32(Encoding.ASCII.GetString(ReadExactly(4)), 16))));
        if (status != "OKAY") throw new IOException("Unexpected ADB status " + status);
    }
    public void Request(string operation, string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        if (bytes.Length > 1024) throw new IOException("Android path too long");
        var packet = new byte[8 + bytes.Length];
        Encoding.ASCII.GetBytes(operation, packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), bytes.Length);
        bytes.CopyTo(packet, 8);
        AtBoundary = false;
        stream.Write(packet);
    }
    string Response()
    {
        var kind = Encoding.ASCII.GetString(ReadExactly(4));
        if (kind == "FAIL")
            throw new IOException(Encoding.UTF8.GetString(ReadExactly((int)BinaryPrimitives.ReadUInt32LittleEndian(ReadExactly(4)))));
        return kind;
    }
    static RemoteInfo Parse(string name, ReadOnlySpan<byte> data)
    {
        int error = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (error != 0) throw new IOException($"Android stat failed ({error}): {name}");
        return new(name, BinaryPrimitives.ReadUInt32LittleEndian(data[20..]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[36..]), BinaryPrimitives.ReadInt64LittleEndian(data[52..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[44..]), BinaryPrimitives.ReadInt64LittleEndian(data[60..]));
    }
    public RemoteInfo Stat(string path)
    {
        Request("LST2", path);
        if (Response() != "LST2") throw new IOException("ADB stat_v2 is required");
        var result = Parse(path.TrimEnd('/').Split('/').Last(), ReadExactly(68));
        AtBoundary = true;
        return result;
    }
    public List<RemoteInfo> List(string path)
    {
        Request("LIS2", path);
        List<RemoteInfo> result = [];
        while (true)
        {
            string kind = Response();
            if (kind != "DNT2" && kind != "DONE") throw new IOException("Unexpected ADB directory response");
            var data = ReadExactly(72);
            if (kind == "DONE") { AtBoundary = true; return result; }
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(68));
            if (length < 0 || length > 1024 * 1024) throw new IOException("Invalid ADB filename length");
            string name = new UTF8Encoding(false, true).GetString(ReadExactly(length));
            if (name is "." or ".." || name.Contains('/') || name.Contains('\0')) continue;
            try { var info = Parse(name, data); if (info.Directory || info.Regular) result.Add(info); }
            catch (IOException) { /* inaccessible entry */ }
        }
    }
    public int ReadBlock(ref byte[] buffer)
    {
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (header[..4].SequenceEqual("DONE"u8)) { AtBoundary = true; return 0; }
        if (length < 0 || length > 1024 * 1024) throw new IOException("Invalid ADB data block length");
        if (header[..4].SequenceEqual("FAIL"u8)) throw new IOException(Encoding.UTF8.GetString(ReadExactly(length)));
        if (!header[..4].SequenceEqual("DATA"u8)) throw new IOException("Invalid ADB data block");
        if (buffer.Length < length) buffer = new byte[length];
        stream.ReadExactly(buffer.AsSpan(0, length));
        return length;
    }
}

sealed class SyncPool(string serial) : IDisposable
{
    readonly ConcurrentBag<(AdbSync Sync, long IdleSince)> idle = new();
    readonly ConcurrentDictionary<AdbSync, byte> live = new();
    int idleCount, disposed;
    long created, reused;
    public AdbSync Rent()
    {
        if (Volatile.Read(ref disposed) != 0) throw new IOException("文件盘正在卸载。");
        while (idle.TryTake(out var item))
        {
            Interlocked.Decrement(ref idleCount);
            if (Environment.TickCount64 - item.IdleSince < 30000)
            { Interlocked.Increment(ref reused); return item.Sync; }
            item.Sync.Dispose(); live.TryRemove(item.Sync, out _);
        }
        var sync = new AdbSync(serial); live[sync] = 0;
        if (Volatile.Read(ref disposed) != 0) { sync.Dispose(); live.TryRemove(sync, out _); throw new IOException("文件盘正在卸载。"); }
        Interlocked.Increment(ref created); return sync;
    }
    public void Return(AdbSync sync)
    {
        if (sync == null) return;
        if (!sync.AtBoundary || Volatile.Read(ref disposed) != 0) { sync.Dispose(); live.TryRemove(sync, out _); return; }
        if (Interlocked.Increment(ref idleCount) > 16)
        { Interlocked.Decrement(ref idleCount); sync.Dispose(); live.TryRemove(sync, out _); return; }
        idle.Add((sync, Environment.TickCount64));
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
        foreach (var sync in live.Keys) { sync.Dispose(); live.TryRemove(sync, out _); }
        while (idle.TryTake(out var item)) { Interlocked.Decrement(ref idleCount); item.Sync.Dispose(); }
    }
    public string Counters => $"connections_created={created} connections_reused={reused}";
}

sealed class Node(string windowsPath, string remotePath, string name, RemoteInfo info)
{
    public string WindowsPath = windowsPath, RemotePath = remotePath, Name = name;
    public RemoteInfo Info = info;
    public readonly ulong Index = BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(remotePath)));
    public bool Deleted;
    public readonly ConcurrentDictionary<OpenHandle, byte> Handles = new();
}
sealed record DirectorySnapshot(List<Node> Nodes, Dictionary<string, Node> ByName);

sealed class OpenHandle : IDisposable
{
    public Node Node;
    public AdbSync Reader;
    public SyncPool Pool;
    public ulong Position;
    public byte[] Block = [];
    public int BlockOffset;
    public int BlockCount;
    public bool CleanedUp;
    public readonly object Gate = new();
    public void Reset()
    {
        if (Reader != null)
        {
            // A complete RECV still has its DONE trailer. Consume it before reusing
            // this connection; partial reads must be closed, never pooled mid-stream.
            if (!Reader.AtBoundary && Position == Node.Info.Size && BlockOffset == BlockCount)
            {
                try { Reader.ReadBlock(ref Block); }
                catch { Reader.Dispose(); Reader = null; }
            }
            Pool.Return(Reader);
        }
        Reader = null; Position = 0; BlockOffset = BlockCount = 0;
    }
    public void Dispose() { lock (Gate) Reset(); Node.Handles.TryRemove(this, out _); }
}

static class Program
{
    static string RunAdb(string adb, params string[] arguments)
    {
        var start = new ProcessStartInfo(adb) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(); throw new IOException("ADB command timed out"); }
        string output = outputTask.GetAwaiter().GetResult(), error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException(error);
        return output.Trim();
    }
    static string Quote(string text) => "'" + text.Replace("'", "'\\''") + "'";
    static string FindAdb(string explicitPath)
    {
        if (explicitPath != null) return explicitPath;
        string adb = AdbTools.EnsureAvailable();
        Console.WriteLine($"使用内置 ADB {AdbTools.Version}");
        return adb;
    }
    static (string Serial, bool SingleDevice) ChooseDevice(string adb)
    {
        while (true)
        {
            string listing = RunAdb(adb, "devices", "-l");
            var devices = listing.Split('\n').Skip(1)
                .Select(l => l.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 2 && p[1] == "device").ToList();
            Console.WriteLine("\n连接状态：\n" + listing);
            if (devices.Count == 0)
            {
                Console.WriteLine("未找到已授权手机。请连接手机并允许 USB 调试。回车刷新，输入 Q 退出：");
                string retry = Console.ReadLine();
                if (retry == null || retry.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException();
                continue;
            }
            if (devices.Count == 1)
            {
                Console.WriteLine($"\n已自动选择唯一一台已授权手机：{devices[0][0]}");
                return (devices[0][0], true);
            }
            Console.WriteLine("\n请选择要挂载的手机：");
            for (int i = 0; i < devices.Count; i++) Console.WriteLine($"  {i + 1}. {string.Join(' ', devices[i])}");
            Console.Write("输入编号，或输入 Q 退出：");
            string input = Console.ReadLine()?.Trim();
            if (input == null || input.Equals("q", StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException();
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= devices.Count) return (devices[choice - 1][0], false);
            Console.WriteLine("编号无效，请重新选择。");
        }
    }
    static bool DriveOccupied(string mount) => DriveInfo.GetDrives().Any(d => d.Name.StartsWith(mount, StringComparison.OrdinalIgnoreCase));
    static string ChooseDrive(bool automatic)
    {
        string suggested = Enumerable.Range('K', 16).Concat(Enumerable.Range('D', 7))
            .Select(c => ((char)c) + ":").FirstOrDefault(d => !DriveOccupied(d));
        if (suggested == null) throw new IOException("没有可用盘符。");
        if (automatic)
        {
            Console.WriteLine($"已自动选择空闲盘符：{suggested}");
            return suggested;
        }
        while (true)
        {
            Console.Write($"\n输入盘符（回车使用 {suggested}），或输入 Q 退出：");
            string input = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (input == null || input == "Q") throw new OperationCanceledException();
            if (input.Length == 0) return suggested;
            if (input.Length == 1) input += ":";
            if (input.Length != 2 || input[0] < 'D' || input[0] > 'Z' || input[1] != ':') { Console.WriteLine("请输入 D 到 Z 之间的盘符。"); continue; }
            if (DriveOccupied(input)) { Console.WriteLine("该盘符已占用，请换一个。"); continue; }
            return input;
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int MountDevice(string serial, string root, string mount, string stopFile, ulong total, ulong free, bool interactive, DeviceMountLease lease)
    {
        using var stop = new ManualResetEventSlim();
        var fs = new AndroidFs(serial, root, total, free);
        using var host = new FileSystemHost(fs);
        using var lifetime = new ConsoleLifetime(stop, fs.CancelPendingOperations);
        int status = host.MountEx(mount, 8, null, false, 0);
        if (status < 0) throw new IOException($"WinFsp mount failed: 0x{status:X8}");
        try {
            lease.Record(serial, mount);
            Console.WriteLine($"MOUNTED {host.MountPoint()} <- {serial}:{root} (read-write)"); Console.Out.Flush();
            if (interactive)
            {
                Console.WriteLine($"\n已挂载到 {mount}（读写），修改和删除会直接作用于手机。\n按回车、Ctrl+C 或关闭此窗口均会卸载。请先等待正在进行的文件操作结束。\n同一台手机只能挂载一个盘；不同手机可分别打开程序挂载。");
                _ = Task.Run(() => { Console.ReadLine(); stop.Set(); });
            }
            int tick=0;
            while (!stop.Wait(500)) {
                if (stopFile != null && File.Exists(stopFile)) break;
                if (!interactive && ++tick % 20 == 0) { fs.Stats(); Console.Out.Flush(); }
            }
        } finally {
            try { host.Unmount(); fs.CancelPendingOperations(); fs.Stats(); Console.WriteLine("UNMOUNTED"); }
            finally { lifetime.Complete(); }
        }
        return 0;
    }
    static int Main(string[] args)
    {
        bool interactive = args.Length == 0;
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            Console.WriteLine("Android ADB WinFsp - GPL-3.0-or-later");
            Console.WriteLine("WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos");
            Console.WriteLine("https://github.com/winfsp/winfsp");
            string mount = "K:", root = "/sdcard", adb = null, serial = null;
            string stopFile = null;
            for (int i=0; i<args.Length; i++)
                switch (args[i]) {
                    case "--mount": mount=args[++i]; break;
                    case "--root": root=args[++i]; break;
                    case "--adb": adb=args[++i]; break;
                    case "--serial": serial=args[++i]; break;
                    case "--stop-file": stopFile=args[++i]; break;
                    case "--adb-version": Console.WriteLine(RunAdb(FindAdb(adb), "version")); return 0;
                    case "--install-winfsp": return WinFspSetup.EnsureInstalled(false, true) ? 0 : 3010;
                    case "--check-winfsp":
                        Console.WriteLine("Bundled installer: " + WinFspSetup.InstallerName + " SHA256=" + WinFspSetup.InstallerHash);
                        string installed = WinFspSetup.InstalledDirectory();
                        Console.WriteLine(installed == null ? "WinFsp: not installed" : "WinFsp: " + installed);
                        return installed == null ? 1 : 0;
                    case "--extract-winfsp": WinFspSetup.ExtractInstaller(args[++i]); Console.WriteLine("Official WinFsp MSI extracted and SHA-256 verified."); return 0;
                    case "--help": Console.WriteLine("Usage: AndroidAdbFs [--mount K:] [--root /sdcard] [--serial ID] [--adb PATH] [--stop-file PATH]\nDouble-click for device selection and optional WinFsp setup.\n--check-winfsp checks installation; --install-winfsp installs only if missing.\n--extract-winfsp PATH extracts the official MSI without running it.\n--adb-version shows bundled ADB version; --adb PATH overrides bundled ADB.\n--license shows embedded project, WinFsp and ADB licenses."); return 0;
                    case "--license":
                        foreach (string resource in new[] { "AndroidAdbFs.LICENSE", "AndroidAdbFs.WinFspLicense", "AndroidAdbFs.Adb.NOTICE.txt" })
                        {
                            using var reader = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream(resource));
                            Console.WriteLine(reader.ReadToEnd());
                        }
                        return 0;
                    default: throw new ArgumentException("Usage: AndroidAdbFs [--mount K:] [--root /sdcard] [--serial ID] [--adb PATH]");
                }
            if (!WinFspSetup.EnsureInstalled(interactive))
            {
                if (interactive && !Console.IsInputRedirected) { Console.WriteLine("按回车退出。"); Console.ReadLine(); }
                return 3010;
            }
            adb = FindAdb(adb);
            if (interactive)
            {
                Console.WriteLine("\nAndroid 文件盘（读写）\nWinFsp 已就绪；手机无需 APK。\n");
                var selected = ChooseDevice(adb);
                serial = selected.Serial;
                mount = ChooseDrive(selected.SingleDevice);
            }
            var devices = RunAdb(adb, "devices").Split('\n').Skip(1).Select(l => l.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 2 && p[1] == "device").Select(p => p[0]).ToList();
            serial ??= devices.Count == 1 ? devices[0] : throw new IOException("Connect exactly one authorized Android device or specify --serial");
            if (!devices.Contains(serial)) throw new IOException("Selected Android device is not connected");
            string deviceId = RunAdb(adb, "-s", serial, "shell", "getprop ro.serialno").Trim();
            if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Equals("unknown", StringComparison.OrdinalIgnoreCase)) deviceId = serial;
            using var lease = new DeviceMountLease(deviceId);
            var features = RunAdb(adb, "-s", serial, "features").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (!features.Contains("stat_v2") || !features.Contains("ls_v2")) throw new IOException("ADB stat_v2 and ls_v2 are required");
            if (!features.Contains("shell_v2")) throw new IOException("读写版本需要手机支持 ADB shell_v2。");
            root = RunAdb(adb, "-s", serial, "shell", "readlink -f " + Quote(root));
            if (!root.StartsWith('/') || root.Contains('\n')) throw new IOException("Unable to resolve Android export root");
            var df = RunAdb(adb, "-s", serial, "shell", "df -k " + Quote(root)).Split('\n').Last().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            ulong total = ulong.Parse(df[1]) * 1024, free = ulong.Parse(df[3]) * 1024;
            return MountDevice(serial, root, mount, stopFile, total, free, interactive, lease);
        }
        catch (AlreadyMountedException ex)
        {
            Console.WriteLine(ex.Message);
            if (interactive && ex.Drive is { Length: 2 } && ex.Drive[1] == ':' && char.IsAsciiLetter(ex.Drive[0]))
                Process.Start(new ProcessStartInfo("explorer.exe", ex.Drive + "\\") { UseShellExecute = true });
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (interactive && !Console.IsInputRedirected) { Console.WriteLine("\n启动失败。检查 ADB 连接和 WinFsp 安装后重试。按回车退出。"); Console.ReadLine(); }
            return 1;
        }
    }
}
