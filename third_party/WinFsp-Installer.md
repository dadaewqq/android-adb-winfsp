# 内嵌 WinFsp 安装包

- 官方版本：WinFsp 2.1，`winfsp-2.1.25156.msi`。
- 来源：<https://github.com/winfsp/winfsp/releases/tag/v2.1>。
- 下载：<https://github.com/winfsp/winfsp/releases/download/v2.1/winfsp-2.1.25156.msi>。
- SHA-256：`073a70e00f77423e34bed98b86e600def93393ba5822204fac57a29324db9f7a`。
- 官方安装包保持原样，不修改或重新签名；下载和构建时校验哈希及 NAVIMATICS LLC 的 Authenticode 签名。
- 原始 MSI 不提交到 Git；构建脚本从 `vendor` 读取或通过代理下载，然后作为资源内嵌。
- 许可见 [WinFsp-License.txt](WinFsp-License.txt)，包含对符合条件的 FLOSS 项目分发原版安装包的额外许可。

安装命令选择 MSI 中的运行组件 `F.User,F.Main`，不安装 Developer 组件，不自动重启。仅 Windows Installer 进程通过 UAC 提权，文件盘主程序保持原权限。

安装程序返回 3010/1641 时，主程序提示重启并停止挂载。取消 UAC 或取消安装时退出；其他错误显示 MSI 退出码和日志位置。已安装 WinFsp 时不启动安装器，也不自动升级。
