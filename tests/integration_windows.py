"""Windows + authorized Android integration tests. Mutations stay in one UUID test folder."""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import uuid

parser = argparse.ArgumentParser()
parser.add_argument("--exe", required=True)
parser.add_argument("--drive", default="M:")
args = parser.parse_args()
exe = Path(args.exe).resolve()
drive = args.drive.upper().rstrip("\\/")
assert len(drive) == 2 and drive[0] in "DEFGHIJKLMNOPQRSTUVWXYZ" and drive[1] == ":"
assert not Path(drive + "/").exists(), "Test drive must be free"
other = next(letter + ":" for letter in "NOPQRSTUVWXYZDEFGHIJKL" if letter + ":" != drive and not Path(letter + ":/").exists())
work = Path(tempfile.mkdtemp(prefix="AndroidAdbFs-integration-"))
stop = work / "stop.signal"
folder = Path(drive + "/") / ("adbfs_rw_test_" + uuid.uuid4().hex)
env = os.environ.copy()
env["PATH"] = str(Path(os.environ["SystemRoot"]) / "System32")
env.pop("ANDROID_HOME", None)
env.pop("ANDROID_SDK_ROOT", None)
p = subprocess.Popen([str(exe), "--mount", drive, "--stop-file", str(stop)], env=env,
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
    text=True, encoding="utf-8", creationflags=subprocess.CREATE_NEW_CONSOLE)
q = queue.Queue()
lines = []
results = []

def read_output():
    for line in p.stdout:
        lines.append(line)
        q.put(line)
    q.put(None)

def passed(name):
    results.append(name)
    print("PASS: " + name, flush=True)

def hash_file(path):
    with path.open("rb") as file:
        return hashlib.file_digest(file, "sha256").hexdigest()

def remove_test_folder():
    if folder.exists():
        assert folder.resolve().parent == Path(drive + "/").resolve()
        assert folder.name.startswith("adbfs_rw_test_")
        shutil.rmtree(folder)

threading.Thread(target=read_output, daemon=True).start()
try:
    while True:
        try:
            line = q.get(timeout=40)
        except queue.Empty:
            raise RuntimeError("Mount timed out: " + "".join(lines))
        if line is None:
            raise RuntimeError("Exited before mount: " + "".join(lines))
        if "MOUNTED " in line:
            break
    duplicate = subprocess.run([str(exe), "--mount", other], env=env, capture_output=True,
        text=True, encoding="utf-8", timeout=15)
    assert duplicate.returncode == 0 and "已经挂载" in duplicate.stdout and not Path(other + "/").exists()
    passed("Same device cannot mount a second drive")
    folder.mkdir()
    file = folder / "测试 'quote' 100%.bin"
    payload = bytes(range(256)) * 4096
    file.write_bytes(payload)
    assert file.read_bytes() == payload
    passed("Unicode, spaces, quotes, percent names and all 256 byte values (1 MiB)")
    expected = bytearray(payload)
    with file.open("r+b", buffering=0) as stream:
        stream.seek(512)
        stream.write(b"PATCHED!")
        stream.flush()
        os.fsync(stream.fileno())
    expected[512:520] = b"PATCHED!"
    assert file.read_bytes() == expected
    passed("Random in-place overwrite")
    with file.open("r+b", buffering=0) as stream:
        stream.truncate(12345)
    expected = expected[:12345]
    with file.open("ab", buffering=0) as stream:
        stream.write(b"append")
    expected += b"append"
    assert file.read_bytes() == expected
    passed("Truncate and append")
    with file.open("r+b", buffering=0) as stream:
        stream.seek(1024 * 1024)
        stream.write(b"\x00\x1a\xff")
        stream.truncate(2 * 1024 * 1024)
    expected += bytes(1024 * 1024 - len(expected)) + b"\x00\x1a\xff" + bytes(1024 * 1024 - 3)
    assert file.read_bytes() == expected
    passed("Sparse seek and zero-filled extension")
    file.write_bytes(b"short")
    assert file.read_bytes() == b"short"
    try:
        with file.open("xb"):
            pass
        raise AssertionError("Exclusive create overwrote a file")
    except FileExistsError:
        pass
    assert file.read_bytes() == b"short"
    passed("Overwrite truncation and exclusive-create collision")
    source, target = folder / "source", folder / "target"
    source.write_bytes(b"new")
    target.write_bytes(b"old")
    source.replace(target)
    assert target.read_bytes() == b"new" and not source.exists()
    passed("Replace an existing file")
    stamp = 1700000000
    os.utime(target, (stamp, stamp))
    assert abs(target.stat().st_mtime - stamp) <= 1
    passed("Modification timestamp")
    try:
        folder.rmdir()
        raise AssertionError("Nonempty directory was deleted")
    except OSError:
        pass
    passed("Nonempty directory deletion is rejected")
    local = work / "source.bin"
    local.write_bytes(os.urandom(16 * 1024 * 1024))
    copied = folder / "copied.bin"
    start = time.perf_counter()
    shutil.copy2(local, copied)
    seconds = time.perf_counter() - start
    assert hash_file(local) == hash_file(copied)
    passed(f"16 MiB copy with metadata, SHA-256 verified ({seconds:.2f}s)")
    parent = folder / "parent"
    parent.mkdir()
    child = parent / "child"
    child.write_bytes(b"child")
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.WriteFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.CreateFileW(str(child), 0xC0000000, 7, None, 3, 0, None)
    assert handle not in (None, ctypes.c_void_p(-1).value), ctypes.get_last_error()
    try:
        renamed_child = parent / "renamed-child"
        child.rename(renamed_child)
        count = wintypes.DWORD()
        assert kernel.WriteFile(handle, ctypes.c_char_p(b"CHILD"), 5, ctypes.byref(count), None), ctypes.get_last_error()
        assert count.value == 5
    finally:
        kernel.CloseHandle(handle)
    assert renamed_child.read_bytes() == b"CHILD"
    moved = folder / "renamed-parent"
    parent.rename(moved)
    assert (moved / "renamed-child").read_bytes() == b"CHILD"
    passed("Rename an open file and write through the old handle; rename its directory after close")
    remove_test_folder()
    passed("All remote test files and directories removed")
    helper = "import ctypes,sys; k=ctypes.windll.kernel32; k.FreeConsole(); assert k.AttachConsole(int(sys.argv[1])); h=k.GetConsoleWindow(); assert h; ctypes.windll.user32.PostMessageW(h,0x10,0,0)"
    subprocess.run([sys.executable, "-c", helper, str(p.pid)], timeout=10, capture_output=True)
    p.wait(timeout=10)
    assert not Path(drive + "/").exists() and any("UNMOUNTED" in line for line in lines)
    passed("Closing the real console window gracefully unmounts the drive")
finally:
    if p.poll() is None:
        try:
            remove_test_folder()
        finally:
            stop.write_text("stop")
            try:
                p.wait(timeout=20)
            except subprocess.TimeoutExpired:
                p.kill()
    (work / "output.log").write_text("".join(lines), encoding="utf-8")
    (work / "results.json").write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8")
    print("Test logs: " + str(work), flush=True)
