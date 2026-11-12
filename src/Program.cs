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

record RemoteInfo(string Name, uint Mode, ulong Size, long Modified)
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
            BinaryPrimitives.ReadUInt64LittleEndian(data[36..]), BinaryPrimitives.ReadInt64LittleEndian(data[52..]));
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
    int idleCount, disposed;
    long created, reused;
    public AdbSync Rent()
    {
        while (idle.TryTake(out var item))
        {
            Interlocked.Decrement(ref idleCount);
            if (Environment.TickCount64 - item.IdleSince < 30000)
            { Interlocked.Increment(ref reused); return item.Sync; }
            item.Sync.Dispose();
        }
        var sync = new AdbSync(serial); Interlocked.Increment(ref created); return sync;
    }
    public void Return(AdbSync sync)
    {
        if (sync == null) return;
        if (!sync.AtBoundary || Volatile.Read(ref disposed) != 0) { sync.Dispose(); return; }
        if (Interlocked.Increment(ref idleCount) > 16)
        { Interlocked.Decrement(ref idleCount); sync.Dispose(); return; }
        idle.Add((sync, Environment.TickCount64));
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
        while (idle.TryTake(out var item)) { Interlocked.Decrement(ref idleCount); item.Sync.Dispose(); }
    }
    public string Counters => $"connections_created={created} connections_reused={reused}";
}

sealed record Node(string WindowsPath, string RemotePath, string Name, RemoteInfo Info)
{
    public FsInfo NativeInfo;
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
    public void Dispose() { lock (Gate) Reset(); }
}

sealed class AndroidFs : FileSystemBase
{
    readonly string serial, root;
    readonly Node rootNode;
    readonly byte[] security;
    readonly ulong total, free;
    readonly SyncPool pool;
    readonly ConcurrentDictionary<string, (long Expires, DirectorySnapshot Snapshot)> directories = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, object> directoryGates = new(StringComparer.Ordinal);
    long listRequests, fileOpens, readBytes;
    public AndroidFs(string serial, string root, ulong total, ulong free)
    {
        this.serial = serial; this.root = root.TrimEnd('/'); this.total = total; this.free = free;
        pool = new SyncPool(serial);
        using var sync = new AdbSync(serial);
        var info = sync.Stat(this.root + "/");
        if (!info.Directory) throw new IOException("Export root is not a directory");
        rootNode = MakeNode("\\", this.root, "", info);
        // Specific read/execute rights. There is deliberately no write or delete right.
        var sd = new RawSecurityDescriptor("O:BAG:BAD:P(A;;FRFX;;;WD)");
        security = new byte[sd.BinaryLength]; sd.GetBinaryForm(security, 0);
    }
    public override int Init(object hostObject)
    {
        var host = (FileSystemHost)hostObject;
        host.SectorSize = 512; host.SectorsPerAllocationUnit = 8; host.MaxComponentLength = 255;
        host.VolumeCreationTime = ToTime(rootNode.Info.Modified);
        host.VolumeSerialNumber = BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(serial + root)));
        host.FileSystemName = "AndroidADB";
        host.CaseSensitiveSearch = true; host.CasePreservedNames = true; host.UnicodeOnDisk = true;
        host.PersistentAcls = true; host.NamedStreams = false; host.ReparsePoints = false;
        host.FileInfoTimeout = 2000; host.DirInfoTimeout = 2000; host.VolumeInfoTimeout = 10000;
        host.PostCleanupWhenModifiedOnly = true;
        return STATUS_SUCCESS;
    }
    public override int ExceptionHandler(Exception ex)
    {
        if (ex is not FileNotFoundException && ex is not DirectoryNotFoundException)
            Console.Error.WriteLine($"{DateTime.Now:O} {ex.GetType().Name}: {ex.Message}");
        return ex switch {
            FileNotFoundException => STATUS_OBJECT_NAME_NOT_FOUND,
            DirectoryNotFoundException => STATUS_OBJECT_PATH_NOT_FOUND,
            UnauthorizedAccessException => STATUS_ACCESS_DENIED,
            SocketException => STATUS_DEVICE_NOT_CONNECTED,
            _ => STATUS_IO_DEVICE_ERROR
        };
    }
    static ulong ToTime(long unix)
    {
        try { return (ulong)DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, unix)).UtcDateTime.ToFileTimeUtc(); }
        catch { return (ulong)DateTime.UnixEpoch.ToFileTimeUtc(); }
    }
    public static string EncodeName(string name)
    {
        var result = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c < 32 || "<>:\"/\\|?*%".Contains(c) || (i == name.Length-1 && (c == '.' || c == ' ')))
                result.Append('%').Append(((int)c).ToString("X2"));
            else result.Append(c);
        }
        string encoded = result.ToString();
        string stem = encoded.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9'))
            encoded = "%" + ((int)encoded[0]).ToString("X2") + encoded[1..];
        return encoded;
    }
    static string Join(string parent, string name) => parent == "\\" ? "\\" + name : parent + "\\" + name;
    DirectorySnapshot Children(Node node)
    {
        if (!node.Info.Directory) throw new DirectoryNotFoundException(node.WindowsPath);
        if (directories.TryGetValue(node.WindowsPath, out var cached) && cached.Expires > Environment.TickCount64)
            return cached.Snapshot;
        lock (directoryGates.GetOrAdd(node.WindowsPath, _ => new object()))
        {
            if (directories.TryGetValue(node.WindowsPath, out cached) && cached.Expires > Environment.TickCount64)
                return cached.Snapshot;
            var sync = pool.Rent();
            List<RemoteInfo> remote;
            try { remote = sync.List(node.RemotePath + "/"); }
            finally { pool.Return(sync); }
            var nodes = remote.Select(info => {
                var name = EncodeName(info.Name);
                return MakeNode(Join(node.WindowsPath, name), node.RemotePath + "/" + info.Name, name, info);
            }).OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
            var snapshot = new DirectorySnapshot(nodes, nodes.ToDictionary(n => n.Name, StringComparer.Ordinal));
            Interlocked.Increment(ref listRequests);
            directories[node.WindowsPath] = (Environment.TickCount64 + 3000, snapshot);
            // Bound the metadata cache during long full-phone scans.
            if (directories.Count > 4096)
                foreach (var entry in directories.Where(e => e.Value.Expires < Environment.TickCount64).Take(1024))
                    directories.TryRemove(entry.Key, out _);
            return snapshot;
        }
    }
    Node Lookup(string path)
    {
        if (path == "\\" || path.Length == 0) return rootNode;
        var node = rootNode;
        foreach (var part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "." or "..") throw new UnauthorizedAccessException("Invalid relative path");
            node = Children(node).ByName.TryGetValue(part, out var child) ? child : throw new FileNotFoundException(path);
        }
        return node;
    }
    static Node MakeNode(string windows, string remote, string name, RemoteInfo info)
    {
        var node = new Node(windows, remote, name, info);
        node.NativeInfo = BuildFileInfo(node);
        return node;
    }
    static FsInfo FileInfo(Node node) => node.NativeInfo;
    static FsInfo BuildFileInfo(Node node)
    {
        var info = node.Info;
        ulong time = ToTime(info.Modified);
        return new FsInfo {
            FileAttributes = (uint)(System.IO.FileAttributes.ReadOnly |
                (info.Directory ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Archive) |
                (node.Name.StartsWith('.') ? System.IO.FileAttributes.Hidden : 0)),
            FileSize = info.Directory ? 0 : info.Size,
            AllocationSize = info.Directory ? 0 : info.Size,
            CreationTime = time, LastAccessTime = time, LastWriteTime = time, ChangeTime = time,
            IndexNumber = BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(node.RemotePath))),
            HardLinks = 1
        };
    }
    public override int GetVolumeInfo(out VolumeInfo info)
    {
        info = new VolumeInfo { TotalSize = total, FreeSize = free };
        info.SetVolumeLabel("Android ADB (RO)"); return STATUS_SUCCESS;
    }
    public override int GetSecurityByName(string path, out uint attributes, ref byte[] descriptor)
    {
        attributes = FileInfo(Lookup(path)).FileAttributes; descriptor = security; return STATUS_SUCCESS;
    }
    public override int GetSecurity(object node, object handle, ref byte[] descriptor)
    { descriptor = security; return STATUS_SUCCESS; }
    public override int Open(string path, uint options, uint access, out object node, out object descriptor, out FsInfo info, out string normalized)
    {
        node = descriptor = null; info = default; normalized = null;
        if ((access & (0x2u | 0x4u | 0x10u | 0x100u | 0x10000u | 0x40000u | 0x80000u | 0x40000000u)) != 0)
            return STATUS_MEDIA_WRITE_PROTECTED;
        var found = Lookup(path);
        if ((options & 1) != 0 && !found.Info.Directory) return STATUS_NOT_A_DIRECTORY;
        if ((options & 0x40) != 0 && found.Info.Directory) return STATUS_FILE_IS_A_DIRECTORY;
        node = found; descriptor = new OpenHandle { Node = found, Pool = pool }; info = FileInfo(found); normalized = path;
        return STATUS_SUCCESS;
    }
    public override void Close(object node, object descriptor) => (descriptor as OpenHandle)?.Dispose();
    public override int GetFileInfo(object node, object descriptor, out FsInfo info)
    { info = FileInfo((Node)node); return STATUS_SUCCESS; }
    public override int GetDirInfoByName(object node, object descriptor, string name, out string normalized, out FsInfo info)
    {
        normalized = null; info = default;
        if (!Children((Node)node).ByName.TryGetValue(name, out var child)) return STATUS_OBJECT_NAME_NOT_FOUND;
        normalized = child.Name; info = FileInfo(child); return STATUS_SUCCESS;
    }
    public override bool ReadDirectoryEntry(object nodeObject, object descriptor, string pattern, string marker, ref object context, out string name, out FsInfo info)
    {
        var node = (Node)nodeObject;
        var iterator = context as IEnumerator<(string, Node)>;
        if (iterator == null)
        {
            List<(string, Node)> entries = [];
            if (node.WindowsPath != "\\")
            {
                entries.Add((".", node));
                int split = node.WindowsPath.LastIndexOf('\\');
                entries.Add(("..", Lookup(split == 0 ? "\\" : node.WindowsPath[..split])));
            }
            entries.AddRange(Children(node).Nodes.Select(n => (n.Name, n)));
            iterator = entries.Where(e => marker == null || StringComparer.Ordinal.Compare(e.Item1, marker) > 0).GetEnumerator();
            context = iterator;
        }
        if (iterator.MoveNext()) { (name, var child) = iterator.Current; info = FileInfo(child); return true; }
        iterator.Dispose(); name = null; info = default; return false;
    }
    public override int Read(object nodeObject, object descriptor, IntPtr buffer, ulong offset, uint length, out uint transferred)
    {
        transferred = 0;
        var handle = (OpenHandle)descriptor;
        var node = handle.Node;
        if (node.Info.Directory) return STATUS_FILE_IS_A_DIRECTORY;
        if (offset >= node.Info.Size) return STATUS_END_OF_FILE;
        lock (handle.Gate)
        {
            if (offset < handle.Position) handle.Reset();
            if (handle.Reader == null)
            {
                handle.Reader = pool.Rent();
                handle.Reader.Request("RECV", node.RemotePath);
                Interlocked.Increment(ref fileOpens);
            }
            int wanted = (int)Math.Min((ulong)length, node.Info.Size - offset);
            while (handle.Position < offset + (ulong)wanted)
            {
                if (handle.BlockOffset >= handle.BlockCount)
                {
                    handle.BlockCount = handle.Reader.ReadBlock(ref handle.Block); handle.BlockOffset = 0;
                    Interlocked.Add(ref readBytes, handle.BlockCount);
                    if (handle.BlockCount == 0) break;
                }
                int available = handle.BlockCount - handle.BlockOffset;
                if (handle.Position < offset)
                {
                    int skip = (int)Math.Min((ulong)available, offset - handle.Position);
                    handle.BlockOffset += skip; handle.Position += (ulong)skip; continue;
                }
                int count = Math.Min(available, wanted - (int)transferred);
                Marshal.Copy(handle.Block, handle.BlockOffset, IntPtr.Add(buffer, (int)transferred), count);
                transferred += (uint)count; handle.BlockOffset += count; handle.Position += (ulong)count;
            }
        }
        return transferred > 0 ? STATUS_SUCCESS : STATUS_END_OF_FILE;
    }
    public override int Flush(object node, object descriptor, out FsInfo info)
    { info = node is Node n ? FileInfo(n) : default; return STATUS_SUCCESS; }
    public override int Create(string p, uint o, uint a, uint f, byte[] s, ulong z, out object n, out object d, out FsInfo i, out string normalized)
    { n = d = null; i = default; normalized = null; return STATUS_MEDIA_WRITE_PROTECTED; }
    public override int Overwrite(object n, object d, uint f, bool r, ulong z, out FsInfo i)
    { i = default; return STATUS_MEDIA_WRITE_PROTECTED; }
    public override int Write(object n, object d, IntPtr b, ulong o, uint l, bool e, bool c, out uint t, out FsInfo i)
    { t = 0; i = default; return STATUS_MEDIA_WRITE_PROTECTED; }
    public override int SetBasicInfo(object n, object d, uint a, ulong c, ulong x, ulong w, ulong h, out FsInfo i)
    { i = default; return STATUS_MEDIA_WRITE_PROTECTED; }
    public override int SetFileSize(object n, object d, ulong s, bool a, out FsInfo i)
    { i = default; return STATUS_MEDIA_WRITE_PROTECTED; }
    public override int CanDelete(object n, object d, string p) => STATUS_MEDIA_WRITE_PROTECTED;
    public override int SetDelete(object n, object d, string p, bool delete) => delete ? STATUS_MEDIA_WRITE_PROTECTED : STATUS_SUCCESS;
    public override int Rename(object n, object d, string p, string q, bool r) => STATUS_MEDIA_WRITE_PROTECTED;
    public override int SetSecurity(object n, object d, AccessControlSections s, byte[] b) => STATUS_MEDIA_WRITE_PROTECTED;
    public override void Unmounted(object host) => pool.Dispose();
    public void Stats() => Console.WriteLine($"STATS lists={listRequests} file_reads={fileOpens} adb_bytes={readBytes} {pool.Counters}");
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
    static int MountDevice(string serial, string root, string mount, string stopFile, ulong total, ulong free, bool interactive)
    {
        using var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        var fs = new AndroidFs(serial, root, total, free);
        using var host = new FileSystemHost(fs);
        int status = host.MountEx(mount, 8, null, false, 0);
        if (status < 0) throw new IOException($"WinFsp mount failed: 0x{status:X8}");
        Console.WriteLine($"MOUNTED {host.MountPoint()} <- {serial}:{root} (read-only)"); Console.Out.Flush();
        if (interactive)
        {
            Console.WriteLine($"\n已挂载到 {mount}，可浏览或复制文件到电脑。\n请保留此窗口；在这里按回车，或按 Ctrl+C，正常卸载并退出。\n同时挂载其他手机时，再双击打开一个 EXE 窗口即可。");
            _ = Task.Run(() => { Console.ReadLine(); stop.Set(); });
        }
        try {
            int tick=0;
            while (!stop.Wait(500)) {
                if (stopFile != null && File.Exists(stopFile)) break;
                if (!interactive && ++tick % 20 == 0) { fs.Stats(); Console.Out.Flush(); }
            }
        } finally { host.Unmount(); fs.Stats(); Console.WriteLine("UNMOUNTED"); }
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
                Console.WriteLine("\nAndroid 文件盘（只读）\nWinFsp 已就绪；手机无需 APK。\n");
                var selected = ChooseDevice(adb);
                serial = selected.Serial;
                mount = ChooseDrive(selected.SingleDevice);
            }
            var devices = RunAdb(adb, "devices").Split('\n').Skip(1).Select(l => l.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 2 && p[1] == "device").Select(p => p[0]).ToList();
            serial ??= devices.Count == 1 ? devices[0] : throw new IOException("Connect exactly one authorized Android device or specify --serial");
            if (!devices.Contains(serial)) throw new IOException("Selected Android device is not connected");
            var features = RunAdb(adb, "-s", serial, "features").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (!features.Contains("stat_v2") || !features.Contains("ls_v2")) throw new IOException("ADB stat_v2 and ls_v2 are required");
            root = RunAdb(adb, "-s", serial, "shell", "readlink -f " + Quote(root));
            if (!root.StartsWith('/') || root.Contains('\n')) throw new IOException("Unable to resolve Android export root");
            var df = RunAdb(adb, "-s", serial, "shell", "df -k " + Quote(root)).Split('\n').Last().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            ulong total = ulong.Parse(df[1]) * 1024, free = ulong.Parse(df[3]) * 1024;
            return MountDevice(serial, root, mount, stopFile, total, free, interactive);
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
