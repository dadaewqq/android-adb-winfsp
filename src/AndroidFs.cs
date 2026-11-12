// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Fsp;
using FsInfo = Fsp.Interop.FileInfo;
using VolumeInfo = Fsp.Interop.VolumeInfo;

sealed class AndroidFs : FileSystemBase
{
    readonly string serial, root;
    readonly Node rootNode;
    readonly byte[] security;
    ulong total, free;
    long volumeUpdated;
    readonly SyncPool pool;
    readonly AdbRemote remote;
    readonly ReaderWriterLockSlim access = new(LockRecursionPolicy.SupportsRecursion);
    readonly ConcurrentDictionary<string, Node> nodes = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, (long Expires, DirectorySnapshot Snapshot)> directories = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, object> directoryGates = new(StringComparer.Ordinal);
    long listRequests, fileOpens, readBytes, writtenBytes;

    sealed class Scope : IDisposable
    {
        readonly ReaderWriterLockSlim gate;
        readonly bool write;
        public Scope(ReaderWriterLockSlim gate, bool write) { this.gate = gate; this.write = write; if (write) gate.EnterWriteLock(); else gate.EnterReadLock(); }
        public void Dispose() { if (write) gate.ExitWriteLock(); else gate.ExitReadLock(); }
    }
    Scope ReadScope() => new(access, false);
    Scope WriteScope() => new(access, true);
    public AndroidFs(string serial, string root, ulong total, ulong free)
    {
        this.serial = serial; this.root = root.TrimEnd('/'); this.total = total; this.free = free;
        pool = new SyncPool(serial);
        remote = new AdbRemote(serial);
        using var sync = new AdbSync(serial);
        var info = sync.Stat(this.root + "/");
        if (!info.Directory) throw new IOException("Export root is not a directory");
        rootNode = MakeNode("\\", this.root, "", info);
        string sid = WindowsIdentity.GetCurrent().User.Value;
        var sd = new RawSecurityDescriptor($"O:{sid}G:{sid}D:P(A;;FA;;;{sid})(A;;FA;;;SY)");
        security = new byte[sd.BinaryLength]; sd.GetBinaryForm(security, 0);
    }
    public override int Init(object hostObject)
    {
        var host = (FileSystemHost)hostObject;
        host.SectorSize = 512; host.SectorsPerAllocationUnit = 8; host.MaxComponentLength = 255;
        host.VolumeCreationTime = ToTime(rootNode.Info.Modified);
        host.VolumeSerialNumber = BinaryPrimitives.ReadUInt32LittleEndian(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(serial + root)));
        host.FileSystemName = "AndroidADB";
        host.CaseSensitiveSearch = true; host.CasePreservedNames = true; host.UnicodeOnDisk = true;
        host.PersistentAcls = false; host.NamedStreams = false; host.ReparsePoints = false;
        host.FileInfoTimeout = 0; host.DirInfoTimeout = 0; host.VolumeInfoTimeout = 10000;
        host.PostCleanupWhenModifiedOnly = false;
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
            IOException when ex.Message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase) => STATUS_DISK_FULL,
            ArgumentException => STATUS_INVALID_PARAMETER,
            _ => STATUS_IO_DEVICE_ERROR
        };
    }
    static ulong ToTime(long unix)
    {
        try { return (ulong)DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, unix)).UtcDateTime.ToFileTimeUtc(); }
        catch { return (ulong)DateTime.UnixEpoch.ToFileTimeUtc(); }
    }
    public static string EncodeName(string name) => FileNames.Encode(name);
    static string DecodeName(string name) => FileNames.Decode(name);
    static string Join(string parent, string name) => parent == "\\" ? "\\" + name : parent + "\\" + name;
    Node MakeNode(string windows, string path, string name, RemoteInfo info)
    {
        var node = nodes.GetOrAdd(windows, _ => new Node(windows, path, name, info));
        node.Info = info;
        return node;
    }
    DirectorySnapshot Children(Node node)
    {
        if (!node.Info.Directory) throw new DirectoryNotFoundException(node.WindowsPath);
        if (directories.TryGetValue(node.WindowsPath, out var cached) && cached.Expires > Environment.TickCount64) return cached.Snapshot;
        lock (directoryGates.GetOrAdd(node.WindowsPath, _ => new object()))
        {
            if (directories.TryGetValue(node.WindowsPath, out cached) && cached.Expires > Environment.TickCount64) return cached.Snapshot;
            var sync = pool.Rent();
            List<RemoteInfo> entries;
            try { entries = sync.List(node.RemotePath + "/"); } finally { pool.Return(sync); }
            var list = entries.Select(info => MakeNode(Join(node.WindowsPath, EncodeName(info.Name)), node.RemotePath + "/" + info.Name, EncodeName(info.Name), info))
                .OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
            var snapshot = new DirectorySnapshot(list, list.ToDictionary(n => n.Name, StringComparer.Ordinal));
            directories[node.WindowsPath] = (Environment.TickCount64 + 3000, snapshot);
            Interlocked.Increment(ref listRequests);
            if (directories.Count > 4096)
                foreach (var entry in directories.Where(e => e.Value.Expires < Environment.TickCount64).Take(1024)) directories.TryRemove(entry.Key, out _);
            return snapshot;
        }
    }
    Node Lookup(string path)
    {
        if (path == "\\" || path.Length == 0) return rootNode;
        var node = rootNode;
        foreach (string part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "." or "..") throw new UnauthorizedAccessException("Invalid relative path");
            node = Children(node).ByName.TryGetValue(part, out var child) ? child : throw new FileNotFoundException(path);
        }
        if (node.Deleted) throw new FileNotFoundException(path);
        return node;
    }
    (Node Parent, string Name, string Path) Destination(string path)
    {
        int split = path.LastIndexOf('\\');
        if (split < 0 || split == path.Length - 1) throw new ArgumentException("Invalid target path");
        Node parent = Lookup(split == 0 ? "\\" : path[..split]);
        if (!parent.Info.Directory) throw new DirectoryNotFoundException(path);
        string name = path[(split + 1)..];
        return (parent, name, parent.RemotePath + "/" + DecodeName(name));
    }
    static FsInfo FileInfo(Node node)
    {
        RemoteInfo info = node.Info;
        ulong time = ToTime(info.Modified);
        return new FsInfo {
            FileAttributes = (uint)((info.Directory ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Archive) | (node.Name.StartsWith('.') ? System.IO.FileAttributes.Hidden : 0)),
            FileSize = info.Directory ? 0 : info.Size, AllocationSize = info.Directory ? 0 : (info.Size + 4095) & ~4095UL,
            CreationTime = time, LastAccessTime = ToTime(info.Accessed > 0 ? info.Accessed : info.Modified), LastWriteTime = time, ChangeTime = ToTime(info.Changed > 0 ? info.Changed : info.Modified),
            IndexNumber = node.Index, HardLinks = 1
        };
    }
    void Refresh(Node node)
    {
        var sync = pool.Rent();
        try { node.Info = sync.Stat(node.RemotePath); } finally { pool.Return(sync); }
        directories.Clear();
    }
    static void ResetReaders(Node node) { foreach (var handle in node.Handles.Keys) lock (handle.Gate) handle.Reset(); }
    OpenHandle Handle(Node node)
    {
        var handle = new OpenHandle { Node = node, Pool = pool };
        node.Handles[handle] = 0;
        return handle;
    }
    public override int GetVolumeInfo(out VolumeInfo info)
    {
        using var scope = ReadScope();
        if (Environment.TickCount64 - volumeUpdated >= 10000)
        {
            lock (directoryGates.GetOrAdd("<volume>", _ => new object()))
            {
                if (Environment.TickCount64 - volumeUpdated >= 10000)
                {
                    var fields = remote.Run("df -k " + AdbRemote.Quote(root)).Split('\n').Last().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    total = checked(ulong.Parse(fields[1]) * 1024); free = checked(ulong.Parse(fields[3]) * 1024);
                    volumeUpdated = Environment.TickCount64;
                }
            }
        }
        info = new VolumeInfo { TotalSize = total, FreeSize = free };
        info.SetVolumeLabel("Android ADB"); return STATUS_SUCCESS;
    }
    public override int GetSecurityByName(string path, out uint attributes, ref byte[] descriptor)
    { using var scope = ReadScope(); attributes = FileInfo(Lookup(path)).FileAttributes; descriptor = security; return STATUS_SUCCESS; }
    public override int GetSecurity(object node, object handle, ref byte[] descriptor) { descriptor = security; return STATUS_SUCCESS; }
    public override int Open(string path, uint options, uint grantedAccess, out object node, out object descriptor, out FsInfo info, out string normalized)
    {
        using var scope = ReadScope();
        node = descriptor = null; info = default; normalized = null;
        var found = Lookup(path);
        if ((options & 1) != 0 && !found.Info.Directory) return STATUS_NOT_A_DIRECTORY;
        if ((options & 0x40) != 0 && found.Info.Directory) return STATUS_FILE_IS_A_DIRECTORY;
        node = found; descriptor = Handle(found); info = FileInfo(found); normalized = path;
        return STATUS_SUCCESS;
    }
    public override void Close(object node, object descriptor) { using var scope = ReadScope(); (descriptor as OpenHandle)?.Dispose(); }
    public override int GetFileInfo(object node, object descriptor, out FsInfo info)
    { using var scope = ReadScope(); info = FileInfo((Node)node); return STATUS_SUCCESS; }
    public override int GetDirInfoByName(object node, object descriptor, string name, out string normalized, out FsInfo info)
    {
        using var scope = ReadScope(); normalized = null; info = default;
        if (!Children((Node)node).ByName.TryGetValue(name, out var child)) return STATUS_OBJECT_NAME_NOT_FOUND;
        normalized = child.Name; info = FileInfo(child); return STATUS_SUCCESS;
    }
    public override bool ReadDirectoryEntry(object nodeObject, object descriptor, string pattern, string marker, ref object context, out string name, out FsInfo info)
    {
        using var scope = ReadScope();
        var node = (Node)nodeObject; var iterator = context as IEnumerator<(string, Node)>;
        if (iterator == null)
        {
            List<(string, Node)> entries = [];
            if (node.WindowsPath != "\\") { entries.Add((".", node)); int split = node.WindowsPath.LastIndexOf('\\'); entries.Add(("..", Lookup(split == 0 ? "\\" : node.WindowsPath[..split]))); }
            entries.AddRange(Children(node).Nodes.Select(n => (n.Name, n)));
            iterator = entries.Where(e => marker == null || StringComparer.Ordinal.Compare(e.Item1, marker) > 0).GetEnumerator(); context = iterator;
        }
        if (iterator.MoveNext()) { (name, var child) = iterator.Current; info = FileInfo(child); return true; }
        iterator.Dispose(); name = null; info = default; return false;
    }
    public override int Read(object nodeObject, object descriptor, IntPtr buffer, ulong offset, uint length, out uint transferred)
    {
        using var scope = ReadScope(); transferred = 0;
        var handle = (OpenHandle)descriptor; var node = handle.Node;
        if (node.Info.Directory) return STATUS_FILE_IS_A_DIRECTORY;
        if (offset >= node.Info.Size) return STATUS_END_OF_FILE;
        lock (handle.Gate)
        {
            if (offset < handle.Position) handle.Reset();
            if (handle.Reader == null) { handle.Reader = pool.Rent(); handle.Reader.Request("RECV", node.RemotePath); Interlocked.Increment(ref fileOpens); }
            int wanted = (int)Math.Min((ulong)length, node.Info.Size - offset);
            while (handle.Position < offset + (ulong)wanted)
            {
                if (handle.BlockOffset >= handle.BlockCount)
                {
                    handle.BlockCount = handle.Reader.ReadBlock(ref handle.Block); handle.BlockOffset = 0;
                    Interlocked.Add(ref readBytes, handle.BlockCount); if (handle.BlockCount == 0) break;
                }
                int available = handle.BlockCount - handle.BlockOffset;
                if (handle.Position < offset) { int skip = (int)Math.Min((ulong)available, offset - handle.Position); handle.BlockOffset += skip; handle.Position += (ulong)skip; continue; }
                int count = Math.Min(available, wanted - (int)transferred);
                Marshal.Copy(handle.Block, handle.BlockOffset, IntPtr.Add(buffer, (int)transferred), count);
                transferred += (uint)count; handle.BlockOffset += count; handle.Position += (ulong)count;
            }
        }
        return transferred > 0 ? STATUS_SUCCESS : STATUS_END_OF_FILE;
    }
    public override int Flush(object node, object descriptor, out FsInfo info)
    { using var scope = ReadScope(); info = node is Node n ? FileInfo(n) : default; return STATUS_SUCCESS; }
    public override int Create(string path, uint options, uint grantedAccess, uint attributes, byte[] sd, ulong allocationSize, out object node, out object descriptor, out FsInfo info, out string normalized)
    {
        using var scope = WriteScope(); node = descriptor = null; info = default; normalized = null;
        var target = Destination(path);
        directories.TryRemove(target.Parent.WindowsPath, out _);
        if (Children(target.Parent).ByName.ContainsKey(target.Name)) return STATUS_OBJECT_NAME_COLLISION;
        bool directory = (options & 1) != 0;
        remote.Run(directory ? $"mkdir -- {AdbRemote.Quote(target.Path)}" : $"set -C; : > {AdbRemote.Quote(target.Path)}");
        directories.Clear(); nodes.TryRemove(path, out _);
        var sync = pool.Rent(); RemoteInfo stat;
        try { stat = sync.Stat(target.Path); } finally { pool.Return(sync); }
        var created = MakeNode(path, target.Path, target.Name, stat);
        node = created; descriptor = Handle(created); info = FileInfo(created); normalized = path;
        return STATUS_SUCCESS;
    }
    public override int Overwrite(object node, object descriptor, uint attributes, bool replaceAttributes, ulong allocationSize, out FsInfo info)
    {
        using var scope = WriteScope(); var item = (Node)node; info = default;
        if (item.Info.Directory) return STATUS_FILE_IS_A_DIRECTORY;
        ResetReaders(item); remote.Truncate(item.RemotePath, 0); Refresh(item); info = FileInfo(item); return STATUS_SUCCESS;
    }
    public override int Write(object node, object descriptor, IntPtr buffer, ulong offset, uint length, bool append, bool constrained, out uint transferred, out FsInfo info)
    {
        using var scope = WriteScope(); var item = (Node)node; transferred = 0; info = FileInfo(item);
        if (item.Info.Directory) return STATUS_FILE_IS_A_DIRECTORY;
        if (length > int.MaxValue) return STATUS_INVALID_PARAMETER;
        if (append) offset = item.Info.Size;
        if (constrained) { if (offset >= item.Info.Size) return STATUS_SUCCESS; length = (uint)Math.Min((ulong)length, item.Info.Size - offset); }
        if (offset > long.MaxValue || (ulong)length > (ulong)long.MaxValue - offset) return STATUS_INVALID_PARAMETER;
        if (length == 0) return STATUS_SUCCESS;
        var data = new byte[(int)length]; Marshal.Copy(buffer, data, 0, data.Length);
        ResetReaders(item);
        try { remote.Write(item.RemotePath, offset, data); }
        finally { directories.Clear(); }
        Refresh(item); transferred = length; info = FileInfo(item); Interlocked.Add(ref writtenBytes, length);
        return STATUS_SUCCESS;
    }
    public override int SetFileSize(object node, object descriptor, ulong size, bool allocation, out FsInfo info)
    {
        using var scope = WriteScope(); var item = (Node)node; info = FileInfo(item);
        if (item.Info.Directory) return STATUS_FILE_IS_A_DIRECTORY;
        if (size > long.MaxValue) return STATUS_INVALID_PARAMETER;
        if (!allocation || size < item.Info.Size) { ResetReaders(item); remote.Truncate(item.RemotePath, size); Refresh(item); }
        info = FileInfo(item); return STATUS_SUCCESS;
    }
    public override int SetBasicInfo(object node, object descriptor, uint attributes, ulong creation, ulong lastAccess, ulong lastWrite, ulong change, out FsInfo info)
    {
        using var scope = WriteScope(); var item = (Node)node; info = FileInfo(item);
        if (attributes != uint.MaxValue && (attributes & (uint)System.IO.FileAttributes.ReadOnly) != 0) return STATUS_NOT_SUPPORTED;
        string Date(ulong time) => DateTime.FromFileTimeUtc(checked((long)time)).ToString("yyyy-MM-ddTHH:mm:ss.fffffff+0000", System.Globalization.CultureInfo.InvariantCulture);
        if (lastAccess != 0 && lastAccess != ulong.MaxValue) remote.Run($"touch -a -c -d {AdbRemote.Quote(Date(lastAccess))} -- {AdbRemote.Quote(item.RemotePath)}");
        if (lastWrite != 0 && lastWrite != ulong.MaxValue) remote.Run($"touch -m -c -d {AdbRemote.Quote(Date(lastWrite))} -- {AdbRemote.Quote(item.RemotePath)}");
        Refresh(item); info = FileInfo(item); return STATUS_SUCCESS;
    }
    public override int CanDelete(object node, object descriptor, string path)
    {
        using var scope = WriteScope(); var item = (Node)node;
        if (item == rootNode) return STATUS_ACCESS_DENIED;
        if (item.Info.Directory) { directories.TryRemove(item.WindowsPath, out _); if (Children(item).Nodes.Count != 0) return STATUS_DIRECTORY_NOT_EMPTY; }
        var parent = Destination(item.WindowsPath).Parent;
        remote.Run($"test -w {AdbRemote.Quote(parent.RemotePath)} && test -x {AdbRemote.Quote(parent.RemotePath)}");
        return STATUS_SUCCESS;
    }
    public override int SetDelete(object node, object descriptor, string path, bool delete) => delete ? CanDelete(node, descriptor, path) : STATUS_SUCCESS;
    public override void Cleanup(object node, object descriptor, string path, uint flags)
    {
        using var scope = WriteScope(); var item = (Node)node;
        if (descriptor is OpenHandle handle) handle.CleanedUp = true;
        if ((flags & CleanupDelete) == 0) return;
        if (item == rootNode || item.Deleted) return;
        try
        {
            ResetReaders(item);
            remote.Run((item.Info.Directory ? "rmdir -- " : "rm -- ") + AdbRemote.Quote(item.RemotePath));
            item.Deleted = true; nodes.TryRemove(item.WindowsPath, out _); directories.Clear();
        }
        catch (Exception ex) { Console.Error.WriteLine("删除未完成：" + ex.Message); }
    }
    public override int Rename(object node, object descriptor, string oldPath, string newPath, bool replace)
    {
        using var scope = WriteScope(); var item = (Node)node;
        if (item == rootNode) return STATUS_ACCESS_DENIED;
        if (oldPath == newPath) return STATUS_SUCCESS;
        if (newPath.StartsWith(oldPath + "\\", StringComparison.Ordinal)) return STATUS_INVALID_PARAMETER;
        var target = Destination(newPath); directories.TryRemove(target.Parent.WindowsPath, out _);
        if (Children(target.Parent).ByName.TryGetValue(target.Name, out var existing))
        {
            if (!replace) return STATUS_OBJECT_NAME_COLLISION;
            if (existing.Info.Directory || item.Info.Directory) return STATUS_ACCESS_DENIED;
            if (existing.Handles.Keys.Any(handle => !handle.CleanedUp)) return STATUS_SHARING_VIOLATION;
        }
        var moving = nodes.Values.Where(n => n.WindowsPath == oldPath || n.WindowsPath.StartsWith(oldPath + "\\", StringComparison.Ordinal)).ToList();
        foreach (var entry in moving) ResetReaders(entry);
        string oldRemote = item.RemotePath;
        remote.Run($"mv -T {(replace ? "-f" : "-n")} -- {AdbRemote.Quote(oldRemote)} {AdbRemote.Quote(target.Path)} && test ! -e {AdbRemote.Quote(oldRemote)} && test ! -L {AdbRemote.Quote(oldRemote)}");
        if (existing != null) { existing.Deleted = true; nodes.TryRemove(newPath, out _); }
        foreach (var entry in moving) nodes.TryRemove(entry.WindowsPath, out _);
        foreach (var entry in moving)
        {
            entry.WindowsPath = newPath + entry.WindowsPath[oldPath.Length..];
            entry.RemotePath = target.Path + entry.RemotePath[oldRemote.Length..];
            if (entry == item) entry.Name = target.Name;
            nodes[entry.WindowsPath] = entry;
        }
        directories.Clear(); Refresh(item); return STATUS_SUCCESS;
    }
    public override int SetSecurity(object node, object descriptor, AccessControlSections sections, byte[] sd) => STATUS_NOT_SUPPORTED;
    public void CancelPendingOperations() { remote.Cancel(); pool.Dispose(); }
    public override void Unmounted(object host) => CancelPendingOperations();
    public void Stats() => Console.WriteLine($"STATS lists={listRequests} file_reads={fileOpens} adb_bytes={readBytes} written_bytes={writtenBytes} {pool.Counters}");
}
