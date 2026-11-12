# Android ADB WinFsp 文件盘

将 Android 手机的共享存储挂载为 Windows **只读磁盘**。挂载后，可以用资源管理器浏览文件、复制到电脑，也可以用支持普通盘符的工具统计目录大小。

**v0.2.0 起，下载一个 EXE，双击即可选择手机和盘符。无需解压目录，不用 CMD、PowerShell 脚本或配置文件。** 手机不用安装 APK，不要求 root；电脑仍需准备 ADB 和 WinFsp。

## 1. 选择下载版本

前往 **[最新版本下载页](https://github.com/dadaewqq/android-adb-winfsp/releases/latest)**，在 **Assets** 中下载以下任意一个 EXE：

| 电脑环境 | 下载文件 | 文件大小 |
| --- | --- | ---: |
| 已安装 .NET 10 x64 Runtime | `AndroidAdbFs-windows-x64-framework-dependent.exe` | 约 516 KiB |
| 没装 .NET，或不想额外安装 | `AndroidAdbFs-windows-x64-self-contained.exe` | 约 36 MiB |

- **依赖版**：不带 .NET，使用电脑上安装的 .NET 10 x64 Runtime。
- **自包含版**：自带 .NET 运行时，不需要另外安装 .NET。
- 两版功能相同，都需要 **ADB 和 WinFsp**；它们均不是 Native AOT。

只需下载一个 EXE。页面上的 `Source code` 是源码，`SHA256SUMS.txt` 用于校验，普通使用无需下载它们。

## 2. 准备环境

| 位置 | 需要准备什么 |
| --- | --- |
| 电脑系统 | Windows x64。 |
| WinFsp | 安装 [WinFsp](https://winfsp.dev/) 的运行组件。日常使用不需要 Developer 组件。 |
| ADB | 有可用的 `adb.exe`，手机能被识别。程序会自动查找，也可在窗口中输入完整路径。 |
| .NET | **只有依赖版需要** [.NET 10 x64 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。 |
| 手机 | 开启 USB 调试，通过数据线连接，并允许此电脑调试。 |

ADB 和 WinFsp 安装包不包含在 EXE 中。可访问哪些目录，取决于手机的 ADB 权限。

## 3. 双击、选择、使用

1. **连接手机**，在手机上允许 USB 调试。
2. **双击下载的 EXE**。如果未找到 ADB，按提示输入 `adb.exe` 完整路径，也可以把文件拖入窗口后回车。
3. **选择手机**：窗口会列出已授权设备，输入编号。只有一台时，直接回车即可。
4. **选择盘符**：直接回车采用建议的空闲盘符，或输入指定盘符，例如 `M`。已占用的盘符会提示重新选择。
5. 看到 **“已挂载到……”** 后，在资源管理器的“此电脑”中打开对应磁盘。
6. **使用结束后，回到程序窗口按回车，或按 Ctrl+C**，正常卸载并退出，再拔掉数据线。

**使用过程中请保留程序窗口。** 关闭资源管理器不会卸载磁盘；请通过程序窗口正常卸载。

程序默认展示手机 `/sdcard`，即共享存储。无需编辑配置文件，不会在 EXE 旁创建脚本、日志或配置目录。

### 单文件是否会在运行时释放文件？

会。.NET 单文件程序运行时自动使用临时缓存目录，通常位于 `%TEMP%/.net/AndroidAdbFs/`，用于加载打包的组件。分发给同事时只需一个 EXE，无需手动维护缓存内容。首次启动可能稍慢。

## 4. 连接多台手机

**只挂载一台**：在设备列表中选择对应编号即可，不需要填写序列号配置。

**同时挂载多台**：再次双击同一个 EXE，打开第二个程序窗口，选择另一台手机和另一个空闲盘符。每个窗口管理自己挂载的磁盘，互不共用停止信号文件。

例如：

| 程序窗口 | 选择的手机 | 选择的盘符 |
| --- | --- | --- |
| 第一个窗口 | 手机 A | `K:` |
| 第二个窗口 | 手机 B | `M:` |

使用结束后，分别在两个窗口按回车卸载。无需复制 EXE，也无需复制目录。

## 5. 使用范围与限制

| 操作 | 是否支持 |
| --- | --- |
| 浏览目录、查看属性、复制文件到电脑 | 支持。 |
| 用支持普通盘符的工具统计文件大小 | 支持，统计范围受 ADB 权限限制。 |
| 向手机写入、修改、删除或重命名 | **不支持，本项目为只读盘。** |
| 访问应用私有目录等受限位置 | 取决于手机的 ADB 权限，不保证可访问。 |
| 断线后自动恢复挂载 | 不支持。请正常退出，重新连接后再次运行。 |

其他限制：

- 共享存储不等于手机完整系统分区。
- 目录元数据缓存约 3 秒，手机上刚发生的修改可能稍后显示。
- 适合顺序读取和文件复制；大文件的随机跳转可能较慢，尤其是向后跳转。
- 仅支持本机默认 ADB 服务 `127.0.0.1:5037`，不支持远程服务或自定义服务端口。
- 手机的 ADB 实现必须支持 `stat_v2` 和 `ls_v2`，目前没有旧协议回退。

## 6. 常见问题

| 现象 | 处理方法 |
| --- | --- |
| 找不到 ADB | 在窗口提示时输入 `adb.exe` 完整路径；也可以把 ADB 加入 PATH。 |
| 未找到已授权手机 | 检查数据线、USB 调试、厂商 ADB 驱动和手机上的授权提示，然后回车刷新。 |
| 状态是 `unauthorized` | 在手机上确认允许此电脑调试。 |
| 状态是 `offline` | 检查连接，重新插拔后重试。 |
| 连接了多台手机 | 在设备列表中输入目标手机的编号。 |
| 盘符已占用 | 输入其他盘符；直接回车会使用建议的空闲盘符。 |
| 提示缺少 .NET | 安装 .NET 10 x64 Runtime，或改用自包含 EXE。 |
| 提示缺少 WinFsp、驱动或 DLL | 安装 WinFsp 后重试，自包含版也需要它。 |
| 提示不支持 `stat_v2` / `ls_v2` | 检查电脑和手机的 ADB 版本，旧实现可能无法使用。 |
| 某些目录无法打开 | 检查该目录是否允许 ADB 访问。 |
| 断线后磁盘不可用 | 在程序窗口正常退出，重新连接手机后再次运行。 |

错误会直接显示在程序窗口中。反馈前请按需要隐藏设备序列号和文件路径。

## 性能参考

下面是同一台 Android 手机、同一 USB 连接下的两轮平均值。**这是单台设备上的实测，不代表所有手机。** AFS 是另一个需要手机端 APK 的文件盘方案。

| 测试 | 本项目 | AFS | 直接 `adb pull` |
| --- | ---: | ---: | ---: |
| 复制一个 107.7 MB 文件 | 2.76 秒 | 4.60 秒 | 2.80 秒 |
| 复制 256 个小文件，共约 9 MB | 1.66 秒 | 5.54 秒 | 1.59 秒 |
| 遍历 Pictures，5994 个文件，并获取大小 | 1.93 秒 | 2.06 秒 | — |
| 在同一大文件中随机读取 8 次，每次 64 KiB | 7.09 秒 | 0.137 秒 | — |

文件复制适合本项目；需要频繁跳转读取大文件时，带手机端服务、支持按偏移读取的方案更有优势。

测试未清空电脑及手机缓存，两轮执行顺序相反。复制文件的 SHA-256 和随机读取内容均验证通过；校验、清理不计入耗时。本项目只读，未测试写入性能。

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

自包含版启用单文件压缩；两版均未启用程序集裁剪，以保留 WinFsp 托管绑定所需功能。WinFsp 绑定会自动释放到运行时缓存目录，安装的驱动不打包进 EXE。

无参数运行时进入交互选择模式。如需固定参数，可从命令行执行：

```powershell
./AndroidAdbFs-windows-x64-self-contained.exe --adb 'E:/platform-tools/adb.exe' --mount M: --root /sdcard --serial PHONE_A
```

命令行模式下按 Ctrl+C 正常卸载。高级自动化可用 `--stop-file PATH` 指定停止信号文件；交互模式不需要此文件。`--help` 查看参数，`--license` 显示内嵌的项目与 WinFsp 许可。

ADB 自动查找顺序：EXE 所在目录、PATH、`ANDROID_HOME`、`ANDROID_SDK_ROOT`、默认 Android SDK 目录。找不到时，交互模式提示输入路径。

## 实现与许可

```text
Windows 程序 → WinFsp → 本项目 → 本机 ADB 服务 → USB → 手机 adbd
```

目录读取使用 ADB Sync v2，批量获取目录项与属性；文件数据通过 Sync `RECV` 流式读取。完整事务结束后的连接可复用，中途停止的读取连接会被丢弃。程序没有额外的整文件内容缓存，向后跳转可能需要重新读取并跳过前面的数据。

本项目使用 **GPL-3.0-or-later**，见 [LICENSE](LICENSE)。

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos。

WinFsp 仓库：<https://github.com/winfsp/winfsp>。其许可与例外条款保留在 [third_party/WinFsp-License.txt](third_party/WinFsp-License.txt)。两份许可均已内嵌到 EXE，可通过 `--license` 查看。

旧版脚本分发包仍可从 [v0.1.1](https://github.com/dadaewqq/android-adb-winfsp/releases/tag/v0.1.1) 下载。
