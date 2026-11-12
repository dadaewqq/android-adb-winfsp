# Android ADB WinFsp 文件盘

将 Android 手机的共享存储挂载为 Windows **读写磁盘**。可以在资源管理器中浏览、复制、创建、修改、删除和重命名文件，也可以用支持普通盘符的工具统计目录大小。

**下载一个 EXE，双击即可使用。只有一台已授权手机时，自动选择手机和空闲盘符并挂载，无需按键确认。** 已内置官方稳定版 ADB 37.0.1 和 WinFsp 安装包，不用单独准备 ADB、不用脚本或配置文件。缺少 WinFsp 时自动进入 UAC 安装；手机不用 APK，不要求 root。

**v0.5.0：同一手机只挂载一个盘，关闭程序窗口自动卸载，并支持读写。修改和删除会直接作用于手机文件。** 升级前请先退出旧版本，再运行新版。

## 1. 选择下载版本

前往 **[最新版本下载页](https://github.com/dadaewqq/android-adb-winfsp/releases/latest)**，在 **Assets** 中下载以下任意一个 EXE。

| 电脑环境 | 下载文件 | 文件大小 |
| --- | --- | ---: |
| 已安装 .NET 10 x64 Runtime | `AndroidAdbFs-windows-x64-framework-dependent.exe` | 约 11.8 MiB |
| 没装 .NET，或不想额外安装 | `AndroidAdbFs-windows-x64-self-contained.exe` | 约 41.4 MiB |

- **依赖版**：不带 .NET，使用电脑上安装的 .NET 10 x64 Runtime。
- **自包含版**：自带 .NET 运行时，不需要另外安装 .NET。
- 两版功能相同，都内嵌 **WinFsp 2.1 官方安装包**；它们均不是 Native AOT。
- 两版都内置 **ADB 37.0.1** 及必需 DLL，默认使用内置版本，无需配置 PATH。

只需下载一个 EXE。页面上的 `Source code` 是源码，`SHA256SUMS.txt` 用于校验，普通使用无需下载它们。

## 2. 准备环境

| 位置 | 需要准备什么 |
| --- | --- |
| 电脑系统 | Windows x64。 |
| WinFsp | 已安装则直接使用；未安装时自动启动内嵌的官方安装器，只需在 UAC 中允许管理员权限。 |
| ADB | 已内置，自动释放并调用，无需安装或设置路径。 |
| .NET | **只有依赖版需要** [.NET 10 x64 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。 |
| 手机 | 开启 USB 调试，通过数据线连接，并允许此电脑调试。 |

使用自包含版时，无需另装 .NET 或下载 ADB。WinFsp 包含系统驱动，首次仍需要 UAC 授权安装到 Windows。部分手机需要厂商 USB 调试驱动；可访问哪些目录，取决于手机的 ADB 权限。

## 3. 双击、选择、使用

1. **连接手机**，在手机上允许 USB 调试。
2. **双击下载的 EXE**。已有 WinFsp 时直接继续；没有时自动进入安装流程，在 UAC 中允许即可，不需要输入 `Y`。ADB 自动就绪，无需查找路径。
3. **只有一台已授权手机时，等待自动挂载即可**：优先使用 K 盘，已占用则依次选择下一个空闲盘符，不询问手机和盘符。多台手机时才需要选择设备及盘符。
4. 看到 **“已挂载到……”** 后，在资源管理器的“此电脑”中打开对应磁盘。
5. **文件操作完成后，按回车、按 Ctrl+C 或直接关闭程序窗口**，程序会卸载磁盘并退出，再拔掉数据线。

**使用过程中请保留程序窗口。** 关闭资源管理器不会卸载磁盘；关闭本程序的窗口会卸载。正在复制或写入时关闭窗口会中断操作，请等操作完成后再关闭。

程序默认展示手机 `/sdcard`，即共享存储。无需编辑配置文件，不会在 EXE 旁创建脚本、日志或配置目录。

### 第一次安装 WinFsp

- 内嵌的是未修改的官方 `winfsp-2.1.25156.msi`，构建时验证 SHA-256 和数字签名，释放时再次验证 SHA-256。
- 检测到缺少 WinFsp 后直接进入安装流程，仅 Windows Installer 通过 UAC 提权；文件盘主程序保持原权限。没有管理员授权时无法安装驱动。
- 只安装运行组件，不安装 Developer 组件；不自动升级已有 WinFsp，也不自动重启。
- Windows Installer 要求重启时，程序提示手动重启并停止挂载；重启后再次运行即可。
- 安装日志保存在 `%LOCALAPPDATA%/AndroidAdbFs/Setup/` 下；安装结束后删除临时 MSI，保留日志以便排查。Windows Installer 自身可能保留系统维护所需的安装缓存。
- 前版完整流程已由使用者在其他电脑验证。本版已测试自动进入安装器的流程、取消 UAC、已有安装时跳过，以及单手机无输入挂载；安装分支测试使用隔离注册表及模拟安装器，没有卸载本机已有驱动。

### 单文件是否会在运行时释放文件？

会。.NET 单文件程序运行时自动使用临时缓存目录，通常位于 `%TEMP%/.net/AndroidAdbFs/`，用于加载打包的组件。分发给同事时只需一个 EXE，无需手动维护缓存内容。首次启动可能稍慢。

内置 ADB 及其 DLL、许可文件释放到 `%LOCALAPPDATA%/AndroidAdbFs/Tools/adb-37.0.1/`，下次运行校验后复用，损坏时从内嵌资源恢复。不修改系统 PATH；默认复用本机已有的兼容 ADB 服务，不主动执行 `kill-server`，卸载文件盘时也不关闭共享服务。启动 ADB 服务后，即使关闭本程序，ADB 服务也可能继续在后台运行。

## 4. 连接多台手机

**只挂载一台**：仅连接一台已授权手机时自动挂载；连接多台时，在设备列表中选择对应编号及盘符，不需要填写序列号配置。

**同时挂载不同手机**：再次双击同一个 EXE，选择另一台手机和另一个空闲盘符。每个窗口管理自己挂载的磁盘。

**同一台手机只能挂载一个盘**。再次运行并选择已挂载的手机，会提示它已经挂载，直接打开已有磁盘，不再创建第二个盘。退出原窗口、完成卸载后，才能重新挂载。互斥机制适用于本版及后续版本；升级前请先退出不具备该机制的旧版本。

例如：

| 程序窗口 | 选择的手机 | 选择的盘符 |
| --- | --- | --- |
| 第一个窗口 | 手机 A | `K:` |
| 第二个窗口 | 手机 B | `M:` |

使用结束后，分别关闭对应窗口或按回车卸载。无需复制 EXE，也无需复制目录。

## 5. 使用范围与限制

| 操作 | 是否支持 |
| --- | --- |
| 浏览目录、查看属性、手机与电脑之间复制文件 | 支持。 |
| 用支持普通盘符的工具统计文件大小 | 支持，统计范围受 ADB 权限限制。 |
| 创建文件与目录、修改、追加、截断、删除、移动、重命名 | 支持，实际权限取决于手机 adbd。 |
| 一个手机同时挂载多个盘符 | 不支持；会复用已有挂载。 |
| 关闭程序窗口自动卸载 | 支持；先等正在进行的文件操作完成。 |
| 访问应用私有目录等受限位置 | 取决于手机的 ADB 权限，不保证可访问。 |
| 断线后自动恢复挂载 | 不支持。请正常退出，重新连接后再次运行。 |

其他限制：

- 共享存储不等于手机完整系统分区。
- 目录元数据缓存约 3 秒，手机上刚发生的修改可能稍后显示。
- 适合顺序读取和文件复制；大文件的随机跳转可能较慢，尤其是向后跳转。
- 写入走 ADB shell-v2 原始通道及手机现有的 `dd`、`truncate`、`mv`、`rm` 等工具，不安装手机端 APK。要求支持 `oflag=seek_bytes`、`conv=notrunc,nocreat,fsync` 等选项；缺少这些功能的旧系统可能无法写入。
- 写入会同步到手机后才返回成功；频繁的小块写入及元数据修改有额外往返开销。
- 不提供 Windows ACL、只读属性、压缩、加密、备用数据流等 NTFS 功能；创建时间没有独立的持久化存储，修改/访问时间按 Android 文件系统精度处理。
- 手机文件名中不符合 Windows 规则的字符会用 `%HH` 显示，普通 `100%.txt` 等文件名可以正常使用。特殊转义名称按映射处理。
- 通过文件盘写入照片不会主动触发 Android 媒体库扫描，相册显示可能需要系统后续扫描。
- 仅支持本机默认 ADB 服务 `127.0.0.1:5037`，不支持远程服务或自定义服务端口。
- 手机的 ADB 实现必须支持 `stat_v2`、`ls_v2` 和 `shell_v2`，目前没有旧协议回退。

## 6. 常见问题

| 现象 | 处理方法 |
| --- | --- |
| 内置 ADB 释放或校验失败 | 检查用户缓存目录的写入权限、剩余空间及文件是否被其他程序占用；也可用 `--adb PATH` 指定已有 ADB。 |
| 未找到已授权手机 | 检查数据线、USB 调试、厂商 ADB 驱动和手机上的授权提示，然后回车刷新。 |
| 状态是 `unauthorized` | 在手机上确认允许此电脑调试。 |
| 状态是 `offline` | 检查连接，重新插拔后重试。 |
| 连接了多台手机 | 在设备列表中输入目标手机的编号。 |
| 盘符已占用 | 输入其他盘符；直接回车会使用建议的空闲盘符。 |
| 提示缺少 .NET | 安装 .NET 10 x64 Runtime，或改用自包含 EXE。 |
| 提示缺少 WinFsp、驱动或 DLL | 双击程序并同意安装内嵌 WinFsp；无法授权时联系管理员。已安装但文件损坏时可手动修复 WinFsp。 |
| WinFsp 安装失败 | 根据窗口显示的 MSI 退出码及安装日志排查；取消 UAC 或安装时程序不会继续挂载。 |
| WinFsp 提示需要重启 | 保存工作，手动重启电脑后再次运行。 |
| 提示不支持 `stat_v2` / `ls_v2` | 已内置新版电脑端 ADB；手机上的旧版或精简 adbd 仍可能不支持所需协议。 |
| 某些目录无法打开 | 检查该目录是否允许 ADB 访问。 |
| 断线后磁盘不可用 | 在程序窗口正常退出，重新连接手机后再次运行。 |

错误会直接显示在程序窗口中。反馈前请按需要隐藏设备序列号和文件路径。

## 性能参考

下面是旧只读实现、同一台 Android 手机和 USB 连接下的两轮平均值，**不等同于 v0.5.0 读写版的性能结果，也不代表所有手机**。AFS 是另一个需要手机端 APK 的文件盘方案。

| 测试 | 本项目 | AFS | 直接 `adb pull` |
| --- | ---: | ---: | ---: |
| 复制一个 107.7 MB 文件 | 2.76 秒 | 4.60 秒 | 2.80 秒 |
| 复制 256 个小文件，共约 9 MB | 1.66 秒 | 5.54 秒 | 1.59 秒 |
| 遍历 Pictures，5994 个文件，并获取大小 | 1.93 秒 | 2.06 秒 | — |
| 在同一大文件中随机读取 8 次，每次 64 KiB | 7.09 秒 | 0.137 秒 | — |

文件复制适合本项目；需要频繁跳转读取大文件时，带手机端服务、支持按偏移读取的方案更有优势。

测试未清空电脑及手机缓存，两轮执行顺序相反。复制文件的 SHA-256 和随机读取内容均验证通过；校验、清理不计入耗时。v0.5.0 已验证读写完整性，包括 16 MiB 文件复制、偏移覆盖、稀疏扩展、截断、追加、替换和时间戳，不以此表作为新版写入性能承诺。

## 源码编译与命令行

普通用户只需下载 EXE。自行编译需要 **.NET 10 SDK** 和带 **Developer 组件的 WinFsp**。

在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

WinFsp 安装在其他目录时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -WinFspDir 'D:/Tools/WinFsp'
```

输出位于 `dist`：

```text
AndroidAdbFs-windows-x64-framework-dependent.exe
AndroidAdbFs-windows-x64-self-contained.exe
SHA256SUMS.txt
```

自包含版启用单文件压缩；两版均未启用程序集裁剪，以保留 WinFsp 托管绑定所需功能。WinFsp 托管绑定自动释放到运行时缓存目录；系统驱动通过内嵌 MSI 安装，不是便携加载。

构建前脚本会校验 `vendor/winfsp-2.1.25156.msi`，不存在时通过系统代理下载固定的官方版本，拒绝直接下载。也可以自行放入哈希匹配的官方 MSI。独立下载命令：

```powershell
./tools/fetch-winfsp.ps1 -ProxyUrl 'http://127.0.0.1:5904'
./tools/fetch-adb.ps1 -ProxyUrl 'http://127.0.0.1:5904'
```

构建还会获取并校验固定版本的官方 Platform-Tools ZIP，仅提取 ADB 所需文件。来源和哈希见 [third_party/WinFsp-Installer.md](third_party/WinFsp-Installer.md) 和 [third_party/ADB.md](third_party/ADB.md)。测试内置资源、缓存修复、安装检测和取消逻辑（不会安装驱动）：

```powershell
dotnet run --project ./tests/BootstrapTests.csproj -c Release
```

Windows 实机集成测试需要连接一台已授权手机、准备一个空闲盘符，并安装 Python。测试只在手机共享存储下新建的 UUID 临时目录内操作，完成后清理；测试会打开并关闭一个实际控制台窗口：

```powershell
python ./tests/integration_windows.py --exe ./dist/AndroidAdbFs-windows-x64-self-contained.exe --drive M:
```

覆盖同手机重复挂载拦截、全部字节值、特殊文件名、偏移写入、截断/扩展、替换、时间戳、复制哈希校验、文件打开时改名、删除和窗口关闭卸载。

无参数运行时进入交互选择模式。如需固定参数，可从命令行执行：

```powershell
./AndroidAdbFs-windows-x64-self-contained.exe --mount M: --root /sdcard --serial PHONE_A
```

命令行模式下按 Ctrl+C 或关闭窗口卸载。高级自动化可用 `--stop-file PATH` 指定停止信号文件；交互模式不需要此文件。`--help` 查看参数，`--license` 显示内嵌的项目、WinFsp 与 ADB 许可。

`--check-winfsp` 只检测安装状态；`--install-winfsp` 在缺少时直接请求 UAC 安装，已有时跳过；`--extract-winfsp PATH` 仅释放校验过的官方 MSI，不执行安装，也不覆盖已有文件。

默认总是使用内置 ADB。`--adb-version` 查看实际版本和路径；如需使用外部 ADB，通过 `--adb 'E:/platform-tools/adb.exe'` 显式覆盖。

## 实现与许可

```text
Windows 程序 → WinFsp → 本项目 → 本机 ADB 服务 → USB → 手机 adbd
```

目录读取使用 ADB Sync v2，批量获取目录项与属性；文件数据通过 Sync `RECV` 流式读取。完整事务结束后的连接可复用，中途停止的读取连接会被丢弃。程序没有额外的整文件内容缓存，向后跳转可能需要重新读取并跳过前面的数据。

写入和文件操作直接使用 ADB shell-v2 原始协议，避开 Windows 命令行标准输入对二进制内容的转换；偏移写入由手机上的 `dd` 完成，不需要整文件拉取后重传。读写锁与缓存失效维护句柄间一致性。每台手机持有一个跨进程互斥量，关闭控制台时取消未完成 I/O，并等待 WinFsp 卸载。

本项目使用 **GPL-3.0-or-later**，见 [LICENSE](LICENSE)。

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos。

WinFsp 仓库：<https://github.com/winfsp/winfsp>。其许可与例外条款保留在 [third_party/WinFsp-License.txt](third_party/WinFsp-License.txt)。两份许可均已内嵌到 EXE，可通过 `--license` 查看。

ADB 取自 Google 官方稳定版 Platform-Tools，原始文件及完整开源许可 `NOTICE.txt` 保持不变；来源、校验值及源码入口见 [third_party/ADB.md](third_party/ADB.md)。许可声明内嵌在 EXE，并保留在运行时 ADB 缓存目录。

旧版脚本分发包仍可从 [v0.1.1](https://github.com/dadaewqq/android-adb-winfsp/releases/tag/v0.1.1) 下载。
