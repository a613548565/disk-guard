# DiskGuard

轻量级 Windows 全盘空间与系统治理工作台（原生 DirectX WPF / 单进程 ~35 MB 内存 / 零 WebView2 依赖 / macOS & iOS 18 极简视觉）。

## 核心特性
- **智能体检与一键修复**：启动即全面诊断全盘空间、内存压力、网络延迟与环境变量失效项，L1 安全项一键清理。
- **深度瘦身**：Win32 `FindFirstFileExW` 并发扫描 L1/L3 缓存、`C:\Users` 目录专清清单、SHA-256 三段式重复大文件去重、微信/QQ 附件定位。
- **软件与搬家**：常见软件默认缓存改位、零丢失大文件安全搬家（复制校验 + NTFS 软链 + 一键迁回）、软件卸载与残留扫描、Docker Desktop（6 级自动探测 + VHDX 瘦身）与 WSL2 `.wslconfig` 调优。
- **加速与修复**：`NtQuerySystemInformation` 低耗性能监控、DNS 测速与代理修复、环境变量/PATH 整理与自动备份回滚、PowerShell 7 升级与 6 项常见系统修复。
- **自动更新**：默认接入 GitHub Releases API，自动弹窗展示更新日志并在安装前进行 SHA-256 完整性校验与无损静默升级。

## 本地编译与打包
无需安装 .NET SDK，仅依赖 Windows 内置 `.NET Framework 4.8`：

```powershell
powershell -ExecutionPolicy Bypass -File .\package.ps1
```

产物输出到 `release\`：
- `DiskGuard-Setup-<version>.exe`（单文件图形安装器）
- `DiskGuard-<version>-Portable-win-x64.zip`（绿色便携版）
- `latest.json`（自动更新清单）

## 自动发版
推送版本标签（如 `git tag v9.0.1 && git push origin v9.0.1`）后，GitHub Actions（`.github/workflows/release.yml`）会自动在 `windows-latest` 上编译、运行核心校验并发布对应的 GitHub Release 与安装包附件。
