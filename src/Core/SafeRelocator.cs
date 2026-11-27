// DiskGuard v8 - 已安装软件缓存位置查询与安全改位 + 零丢失大文件/大目录原子搬家引擎（拷贝→校验→切换→软链→一键迁回）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskGuard
{
    public class AppCacheLocation
    {
        public string Id;
        public string AppName;
        public string Category;
        public string CurrentPath;
        public string Drive;
        public long SizeBytes;
        public bool Exists;
        public bool IsOnSystemDrive;
        public bool IsJunction;
        public string Method;   // env / npm / pnpm / go / junction
        public string EnvKey;
        public string Advice;
        public bool CanRelocate;
    }

    public class MigrationRecord
    {
        public string Id;
        public string Time;
        public string SourcePath;
        public string TargetPath;
        public string Mode; // junction / move
        public long Bytes;
        public int FileCount;
        public bool IsDir;
        public bool Active;
    }

    public static class SafeRelocator
    {
        static string HistoryFile
        {
            get
            {
                string dir = Path.Combine(Util.LocalAppData, "DiskGuard");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "migrations.json");
            }
        }

        // ==================== 1. 已安装软件默认缓存位置查询 ====================
        public static List<AppCacheLocation> InspectAppCaches(CancellationToken ct)
        {
            string user = Util.UserProfile;
            string local = Util.LocalAppData;

            var list = new List<AppCacheLocation>();
            AddCache(list, "temp", "系统用户临时目录 (TEMP / TMP)", "系统",
                EnvOr("TEMP", Path.Combine(local, "Temp")), "env", "TEMP",
                "修改用户环境变量 TEMP 与 TMP 到数据盘，彻底减轻 C 盘解压/安装写入", ct);

            AddCache(list, "npm", "Node.js npm 缓存", "前端开发",
                GetToolOrDefault("npm", "config get cache", Path.Combine(local, "npm-cache")), "npm", "npm_config_cache",
                "调用 npm config set cache 原生改位并搬运现有缓存，完全兼容 Node 项目", ct);

            AddCache(list, "pnpm", "pnpm 全局包仓库", "前端开发",
                GetToolOrDefault("pnpm", "store path", Path.Combine(local, "pnpm", "store")), "pnpm", "",
                "调用 pnpm config set store-dir 原生改位，节省 C 盘数 GB 重复依赖", ct);

            AddCache(list, "pip", "Python pip 缓存", "Python / AI",
                EnvOr("PIP_CACHE_DIR", Path.Combine(local, "pip", "cache")), "env", "PIP_CACHE_DIR",
                "设置用户环境变量 PIP_CACHE_DIR 并迁移缓存", ct);

            AddCache(list, "uv", "Python uv 缓存", "Python / AI",
                EnvOr("UV_CACHE_DIR", Path.Combine(local, "uv", "cache")), "env", "UV_CACHE_DIR",
                "设置用户环境变量 UV_CACHE_DIR 并迁移缓存", ct);

            AddCache(list, "gomod", "Go 模块下载缓存 (GOMODCACHE)", "后端开发",
                EnvOr("GOMODCACHE", Path.Combine(user, "go", "pkg", "mod")), "env", "GOMODCACHE",
                "设置 GOMODCACHE 环境变量并安全迁移已下载模块", ct);

            AddCache(list, "nuget", ".NET NuGet 包仓库", "后端开发",
                EnvOr("NUGET_PACKAGES", Path.Combine(user, ".nuget", "packages")), "env", "NUGET_PACKAGES",
                "设置 NUGET_PACKAGES 环境变量并安全迁移包文件", ct);

            AddCache(list, "gradle", "Java / Android Gradle 缓存", "后端开发",
                EnvOr("GRADLE_USER_HOME", Path.Combine(user, ".gradle")), "env", "GRADLE_USER_HOME",
                "设置 GRADLE_USER_HOME 环境变量并迁移缓存（重启 IDE 后生效）", ct);

            AddCache(list, "maven", "Java Maven 本地仓库 (.m2)", "后端开发",
                Path.Combine(user, ".m2", "repository"), "junction", "",
                "通过原子拷贝校验 + NTFS 透明软链接迁移至数据盘，IDEA/Maven 零配置感知", ct);

            AddCache(list, "cargo", "Rust Cargo 依赖仓库", "后端开发",
                EnvOr("CARGO_HOME", Path.Combine(user, ".cargo")), "env", "CARGO_HOME",
                "设置 CARGO_HOME 环境变量并迁移工具链与 crate 缓存", ct);

            AddCache(list, "hf", "HuggingFace AI 模型缓存", "Python / AI",
                EnvOr("HF_HOME", Path.Combine(user, ".cache", "huggingface")), "env", "HF_HOME",
                "设置 HF_HOME 将数十 GB AI 模型权重移至数据盘", ct);

            AddCache(list, "ollama", "Ollama 本地大模型目录", "Python / AI",
                EnvOr("OLLAMA_MODELS", Path.Combine(user, ".ollama", "models")), "env", "OLLAMA_MODELS",
                "设置 OLLAMA_MODELS 环境变量并将模型迁至数据盘（重启 Ollama 生效）", ct);

            AddCache(list, "playwright", "Playwright 浏览器缓存", "前端开发",
                EnvOr("PLAYWRIGHT_BROWSERS_PATH", Path.Combine(local, "ms-playwright")), "env", "PLAYWRIGHT_BROWSERS_PATH",
                "设置 PLAYWRIGHT_BROWSERS_PATH 避免浏览器内核占用 C 盘", ct);

            return list.OrderByDescending(x => x.IsOnSystemDrive && x.SizeBytes > 0)
                       .ThenByDescending(x => x.SizeBytes)
                       .ToList();
        }

        static void AddCache(List<AppCacheLocation> list, string id, string name, string cat, string path, string method, string envKey, string advice, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = path.Trim().Trim('"');
            bool exists = Directory.Exists(path);
            bool isJunc = exists && UserFolderAudit.IsJunctionDir(path);
            long sz = (exists && !isJunc) ? Fs.DirSize(path, null, ct, false) : 0;
            string drv = (path.Length >= 2 && path[1] == ':') ? path.Substring(0, 1).ToUpperInvariant() : "?";
            string sysDrv = (Environment.GetFolderPath(Environment.SpecialFolder.Windows) ?? "C:").Substring(0, 1).ToUpperInvariant();
            bool onSys = drv == sysDrv;

            list.Add(new AppCacheLocation
            {
                Id = id,
                AppName = name,
                Category = cat,
                CurrentPath = path,
                Drive = drv,
                SizeBytes = sz,
                Exists = exists,
                IsOnSystemDrive = onSys,
                IsJunction = isJunc,
                Method = method,
                EnvKey = envKey,
                Advice = isJunc ? ("已通过软链接迁移至 " + TryReadJunctionTarget(path)) : advice,
                CanRelocate = !isJunc
            });
        }

        static string EnvOr(string key, string fallback)
        {
            try
            {
                string v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(v)) return Environment.ExpandEnvironmentVariables(v.Trim());
                v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(v)) return Environment.ExpandEnvironmentVariables(v.Trim());
            }
            catch { }
            return fallback;
        }

        static string GetToolOrDefault(string exe, string args, string fallback)
        {
            try
            {
                int ex;
                string o = Util.Run("cmd.exe", "/c " + exe + " " + args + " 2>nul", 3000, out ex);
                if (ex == 0 && !string.IsNullOrWhiteSpace(o))
                {
                    string line = o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    if (!string.IsNullOrWhiteSpace(line) && line.Length >= 3 && line[1] == ':') return line.Trim();
                }
            }
            catch { }
            return fallback;
        }

        // 安全更改软件默认缓存位置（先建立目标目录 + 如有原数据则可选迁移 + 原生配置/环境变量生效 + 广播）
        public static FixResult RelocateAppCache(string id, string targetBaseDir, bool moveExisting, Action<string, int> onProg)
        {
            if (string.IsNullOrWhiteSpace(targetBaseDir) || !Directory.Exists(targetBaseDir))
                return new FixResult { Ok = false, Title = "目标目录无效", Summary = "请先选择一个已存在的目标数据盘目录" };

            var all = InspectAppCaches(CancellationToken.None);
            var item = all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item == null) return new FixResult { Ok = false, Title = "未找到缓存项", Summary = "指定的缓存项不存在：" + id };

            string destDir = Path.Combine(targetBaseDir.TrimEnd('\\'), "AppCache_" + item.Id);
            if (string.Equals(item.CurrentPath.TrimEnd('\\'), destDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return new FixResult { Ok = false, Title = "路径相同", Summary = "目标位置与当前缓存位置一致，无需更改" };

            if (item.Method == "junction")
            {
                if (!Directory.Exists(item.CurrentPath))
                    return new FixResult { Ok = false, Title = "原目录不存在", Summary = "当前电脑尚未产生 " + item.AppName + " 目录，无需迁移" };
                return MigratePath(item.CurrentPath, destDir, "junction", CancellationToken.None, onProg);
            }

            try
            {
                Directory.CreateDirectory(destDir);
                if (onProg != null) onProg("正在应用新缓存配置…", 25);

                // 1. 先在环境变量或原生工具中写入新路径（先备份环境变量）
                if (item.Id == "temp")
                {
                    NetworkEnvManager.BackupEnvVars("更改 TEMP/TMP 缓存位置");
                    NetworkEnvManager.SetUserEnvRaw("TEMP", destDir);
                    NetworkEnvManager.SetUserEnvRaw("TMP", destDir);
                    NetworkEnvManager.BroadcastEnvChange();
                }
                else if (item.Method == "npm")
                {
                    int ex;
                    Util.Run("cmd.exe", "/c npm config set cache \"" + destDir + "\"", 8000, out ex);
                    NetworkEnvManager.BackupEnvVars("更改 npm 缓存位置");
                    NetworkEnvManager.SetUserEnvRaw("npm_config_cache", destDir);
                    NetworkEnvManager.BroadcastEnvChange();
                }
                else if (item.Method == "pnpm")
                {
                    int ex;
                    Util.Run("cmd.exe", "/c pnpm config set store-dir \"" + destDir + "\"", 8000, out ex);
                }
                else if (item.Method == "env" && !string.IsNullOrEmpty(item.EnvKey))
                {
                    NetworkEnvManager.BackupEnvVars("更改 " + item.EnvKey + " 缓存位置");
                    NetworkEnvManager.SetUserEnvRaw(item.EnvKey, destDir);
                    NetworkEnvManager.BroadcastEnvChange();
                }

                // 2. 如用户选择搬运现有数据且旧目录存在（TEMP 除外，TEMP 新建干净目录更安全）
                string extraMsg = "";
                if (moveExisting && item.Id != "temp" && Directory.Exists(item.CurrentPath) && item.SizeBytes > 0)
                {
                    if (onProg != null) onProg("正在安全搬运现有缓存数据…", 55);
                    long srcBytes; int srcFiles;
                    MeasureTree(item.CurrentPath, out srcBytes, out srcFiles, CancellationToken.None);
                    if (HasEnoughSpace(destDir, srcBytes))
                    {
                        long copied = 0; int copiedFiles = 0;
                        CopyDirectorySafe(item.CurrentPath, destDir, ref copied, ref copiedFiles, srcBytes, CancellationToken.None, onProg);
                        if (copiedFiles == srcFiles && copied == srcBytes)
                        {
                            Win32Native.SendToRecycleBin(item.CurrentPath);
                            extraMsg = "，原缓存 " + Util.Fmt(srcBytes) + " 已完整校验搬运并将旧目录移入回收站";
                        }
                        else
                        {
                            extraMsg = "，部分旧缓存文件正被占用，已安全保留旧目录（新配置已生效）";
                        }
                    }
                    else
                    {
                        extraMsg = "，但目标盘剩余空间不足以搬运旧文件（仅已切换新缓存路径）";
                    }
                }

                if (onProg != null) onProg("缓存位置更改完成", 100);
                return new FixResult
                {
                    Ok = true,
                    Title = item.AppName + " 改位成功",
                    Summary = "新缓存路径已设为 " + destDir + extraMsg + "（新打开的终端/软件将立即生效）"
                };
            }
            catch (Exception ex)
            {
                return new FixResult { Ok = false, Title = "改位失败", Summary = ex.Message };
            }
        }

        // ==================== 2. 零丢失大文件 / 大目录原子安全搬家 ====================
        public static FixResult MigratePath(string sourcePath, string targetPath, string mode, CancellationToken ct, Action<string, int> onProg)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
                return new FixResult { Ok = false, Title = "参数无效", Summary = "源路径与目标路径不能为空" };

            string src, dst;
            try
            {
                src = Path.GetFullPath(sourcePath.Trim()).TrimEnd('\\');
                dst = Path.GetFullPath(targetPath.Trim()).TrimEnd('\\');
            }
            catch (Exception ex) { return new FixResult { Ok = false, Title = "路径格式无效", Summary = ex.Message }; }

            bool isDir = Directory.Exists(src);
            bool isFile = !isDir && File.Exists(src);
            if (!isDir && !isFile)
                return new FixResult { Ok = false, Title = "源路径不存在", Summary = "找不到：" + src };

            // 安全红线检查（绝对禁止搬运系统核心、用户根目录、AppData 根、已是软链的目录、包含 .git 的根）
            if (Fs.IsProtected(src) || Fs.IsSystemCriticalPath(src))
                return new FixResult { Ok = false, Title = "安全拦截", Summary = "该路径属于受保护系统核心目录或根路径，禁止迁移：" + src };

            string lowSrc = src.ToLowerInvariant();
            if (lowSrc.EndsWith(@"\.ssh") || lowSrc.EndsWith(@"\.git") || lowSrc.Contains(@"\windows\") || lowSrc.Contains(@"\program files"))
                return new FixResult { Ok = false, Title = "安全拦截", Summary = "禁止迁移 Windows、Program Files 或密钥/Git 核心目录：" + src };

            if (isDir && UserFolderAudit.IsJunctionDir(src))
                return new FixResult { Ok = false, Title = "已是软链接", Summary = "源目录已经是一个目录联接（已迁移至 " + TryReadJunctionTarget(src) + "），无需重复搬家" };

            if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
                return new FixResult { Ok = false, Title = "路径相同", Summary = "目标路径不能与源路径相同" };

            if (isDir && dst.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase))
                return new FixResult { Ok = false, Title = "路径非法", Summary = "目标目录不能位于源目录内部" };

            if (Directory.Exists(dst) || File.Exists(dst))
                return new FixResult { Ok = false, Title = "目标已存在", Summary = "为防止数据覆盖，禁止覆盖已存在的目标路径：" + dst };

            if (mode != "junction" && mode != "move") mode = isDir ? "junction" : "move";
            if (isFile) mode = "move"; // 单个文件使用纯物理移动（无需管理员符号链接权限）

            // 统计总大小与文件数
            if (onProg != null) onProg("正在安全体检源文件并校验目标盘空间…", 5);
            long totalBytes = 0; int totalFiles = 0;
            if (isDir) MeasureTree(src, out totalBytes, out totalFiles, ct);
            else { totalBytes = Util.FileLen(src); totalFiles = 1; }

            if (!HasEnoughSpace(dst, totalBytes))
                return new FixResult { Ok = false, Title = "目标盘空间不足", Summary = "目标分区剩余空间不足（需保留至少源大小 + 10% 安全余量，源大小 " + Util.Fmt(totalBytes) + "）" };

            string dstParent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstParent)) Directory.CreateDirectory(dstParent);

            string staging = dst + ".__dg_staging__";
            string backup = src + ".__dg_backup__";

            try
            {
                // Phase 1: 完整拷贝至临时暂存目标
                if (onProg != null) onProg("第 1/4 步：安全拷贝数据（源数据全程只读零改动）…", 15);
                long copiedBytes = 0; int copiedFiles = 0;
                if (isDir)
                {
                    CopyDirectorySafe(src, staging, ref copiedBytes, ref copiedFiles, Math.Max(1, totalBytes), ct, onProg);
                }
                else
                {
                    CopyFileChunked(src, staging, totalBytes, ct, p => { if (onProg != null) onProg("第 1/4 步：安全拷贝大文件…", 15 + (int)(p * 55)); });
                    copiedBytes = Util.FileLen(staging);
                    copiedFiles = 1;
                }

                if (ct.IsCancellationRequested)
                {
                    CleanupTempPath(staging);
                    return new FixResult { Ok = false, Title = "已安全取消", Summary = "已清理暂存副本，源路径数据 100% 未受影响" };
                }

                // Phase 2: 100% 字节与文件数双校验
                if (onProg != null) onProg("第 2/4 步：执行 100% 文件数与字节数完整性校验…", 75);
                long verifyBytes = 0; int verifyFiles = 0;
                if (isDir) MeasureTree(staging, out verifyBytes, out verifyFiles, ct);
                else { verifyBytes = Util.FileLen(staging); verifyFiles = 1; }

                if (verifyBytes != totalBytes || verifyFiles != totalFiles)
                {
                    CleanupTempPath(staging);
                    return new FixResult
                    {
                        Ok = false,
                        Title = "校验不通过，已自动中止保护数据",
                        Summary = string.Format("源 {0} 文件 ({1}) 与副本 {2} 文件 ({3}) 不一致（可能有程序正在写入该目录），已清除暂存副本，源数据完好无损",
                            totalFiles, Util.Fmt(totalBytes), verifyFiles, Util.Fmt(verifyBytes))
                    };
                }

                // Phase 3: 原子切换（先将源改名为 backup 检测是否被占用，再把 staging 改名为正式 dst）
                if (onProg != null) onProg("第 3/4 步：检测占用并执行原子路径切换…", 85);
                try
                {
                    if (isDir) Directory.Move(src, backup);
                    else File.Move(src, backup);
                }
                catch (Exception lockEx)
                {
                    CleanupTempPath(staging);
                    return new FixResult
                    {
                        Ok = false,
                        Title = "源文件正被其他程序占用，已安全中止",
                        Summary = "无法锁定切换原路径（请先关闭正在使用该文件夹的软件后再试），源数据 100% 完好无损：" + lockEx.Message
                    };
                }

                try
                {
                    if (isDir) Directory.Move(staging, dst);
                    else File.Move(staging, dst);
                }
                catch (Exception mvEx)
                {
                    // 瞬间回滚！
                    try { if (isDir) Directory.Move(backup, src); else File.Move(backup, src); } catch { }
                    CleanupTempPath(staging);
                    return new FixResult { Ok = false, Title = "目标重命名失败，已自动回滚", Summary = mvEx.Message };
                }

                // Phase 4: 如选透明软链接搬家，在原位置建立 NTFS Junction 并二次验证
                if (isDir && mode == "junction")
                {
                    if (onProg != null) onProg("第 4/4 步：在原位置建立 NTFS 透明软链接并验证…", 92);
                    int ex;
                    Util.Run("cmd.exe", "/c mklink /J \"" + src + "\" \"" + dst + "\"", 10000, out ex);
                    if (ex != 0 || !UserFolderAudit.IsJunctionDir(src))
                    {
                        // 联接失败 -> 立即无缝回滚！
                        try { if (Directory.Exists(src)) Directory.Delete(src, false); } catch { }
                        try { Directory.Move(backup, src); } catch { }
                        return new FixResult { Ok = false, Title = "软链接创建失败，已完整回滚", Summary = "原目录已无损恢复，目标副本保留在 " + dst };
                    }
                }

                // 验证全部通过后，安全清理源位置 backup 释放原盘空间
                if (onProg != null) onProg("正在清理旧备份释放原分区空间…", 96);
                var delLog = new StringBuilder();
                if (isDir) Fs.DeleteDirSafe(backup, delLog);
                else { try { File.SetAttributes(backup, FileAttributes.Normal); File.Delete(backup); } catch { } }

                var rec = new MigrationRecord
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    SourcePath = src,
                    TargetPath = dst,
                    Mode = mode,
                    Bytes = totalBytes,
                    FileCount = totalFiles,
                    IsDir = isDir,
                    Active = true
                };
                SaveRecord(rec);
                if (onProg != null) onProg("安全搬家完成", 100);

                return new FixResult
                {
                    Ok = true,
                    Title = "安全搬家完成（100% 校验一致）",
                    Summary = string.Format("已将 {0}（{1}，{2} 个文件）安全迁至 {3}{4}",
                        Path.GetFileName(src), Util.Fmt(totalBytes), totalFiles, dst,
                        mode == "junction" ? "，原路径已建立透明软链接（软件无感知，随时可一键迁回）" : "")
                };
            }
            catch (Exception ex)
            {
                CleanupTempPath(staging);
                // 若 backup 存在而 src 不存在，确保回滚
                if ((Directory.Exists(backup) || File.Exists(backup)) && !Directory.Exists(src) && !File.Exists(src))
                {
                    try { if (isDir) Directory.Move(backup, src); else File.Move(backup, src); } catch { }
                }
                return new FixResult { Ok = false, Title = "搬家异常已保护回滚", Summary = ex.Message };
            }
        }

        // 一键迁回原位（反向安全校验迁移）
        public static FixResult RestoreMigration(string id, Action<string, int> onProg)
        {
            var list = ListAllMigrationsRaw();
            var rec = list.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (rec == null) return new FixResult { Ok = false, Title = "记录不存在", Summary = "未找到该迁移记录" };

            bool srcIsJunc = rec.IsDir && UserFolderAudit.IsJunctionDir(rec.SourcePath);
            if (!Directory.Exists(rec.TargetPath) && !File.Exists(rec.TargetPath))
                return new FixResult { Ok = false, Title = "目标已不存在", Summary = "目标路径已不存在，无法自动迁回：" + rec.TargetPath };

            // 先检查原盘空间是否足够
            long sz = 0; int cnt = 0;
            if (rec.IsDir) MeasureTree(rec.TargetPath, out sz, out cnt, CancellationToken.None);
            else { sz = Util.FileLen(rec.TargetPath); cnt = 1; }
            if (!HasEnoughSpace(rec.SourcePath, sz))
                return new FixResult { Ok = false, Title = "原分区空间不足", Summary = "原位置所在分区剩余空间不足以容纳 " + Util.Fmt(sz) };

            string staging = rec.SourcePath + ".__dg_restore__";
            try
            {
                if (onProg != null) onProg("正在将数据校验拷贝回原分区…", 20);
                long copied = 0; int copiedFiles = 0;
                if (rec.IsDir) CopyDirectorySafe(rec.TargetPath, staging, ref copied, ref copiedFiles, Math.Max(1, sz), CancellationToken.None, onProg);
                else { CopyFileChunked(rec.TargetPath, staging, sz, CancellationToken.None, null); copied = Util.FileLen(staging); copiedFiles = 1; }

                if (copied != sz || copiedFiles != cnt)
                {
                    CleanupTempPath(staging);
                    return new FixResult { Ok = false, Title = "回迁校验失败", Summary = "文件正被占用导致拷贝不完整，已保持现状零丢失" };
                }

                // 移除软链接（仅删联接节点本身，绝不删目标内容）
                if (srcIsJunc) Directory.Delete(rec.SourcePath, false);
                else if (Directory.Exists(rec.SourcePath) || File.Exists(rec.SourcePath))
                {
                    CleanupTempPath(staging);
                    return new FixResult { Ok = false, Title = "原路径已被占用", Summary = "原位置已存在同名文件或目录，请先手动处理：" + rec.SourcePath };
                }

                if (rec.IsDir) Directory.Move(staging, rec.SourcePath);
                else File.Move(staging, rec.SourcePath);

                // 清理目标副本
                var log = new StringBuilder();
                if (rec.IsDir) Fs.DeleteDirSafe(rec.TargetPath, log);
                else { try { File.Delete(rec.TargetPath); } catch { } }

                rec.Active = false;
                WriteAllRecords(list);
                if (onProg != null) onProg("已无损迁回原位", 100);
                return new FixResult { Ok = true, Title = "已一键迁回原位", Summary = "已将 " + Path.GetFileName(rec.SourcePath) + "（" + Util.Fmt(sz) + "）完整迁回 " + rec.SourcePath };
            }
            catch (Exception ex)
            {
                CleanupTempPath(staging);
                return new FixResult { Ok = false, Title = "迁回失败", Summary = ex.Message };
            }
        }

        public static List<MigrationRecord> ListMigrations()
        {
            try
            {
                if (!File.Exists(HistoryFile)) return new List<MigrationRecord>();
                string txt = File.ReadAllText(HistoryFile, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var list = ser.Deserialize<List<MigrationRecord>>(txt);
                if (list == null) return new List<MigrationRecord>();
                return list.Where(x => x != null && (x.SourcePath ?? "").IndexOf("dg_reloctest_", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            }
            catch { return new List<MigrationRecord>(); }
        }

        public static List<MigrationRecord> ListAllMigrationsRaw()
        {
            try
            {
                if (!File.Exists(HistoryFile)) return new List<MigrationRecord>();
                string txt = File.ReadAllText(HistoryFile, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                return ser.Deserialize<List<MigrationRecord>>(txt) ?? new List<MigrationRecord>();
            }
            catch { return new List<MigrationRecord>(); }
        }

        public static void RemoveTestRecords()
        {
            var clean = ListAllMigrationsRaw().Where(x => x != null && (x.SourcePath ?? "").IndexOf("dg_reloctest_", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            WriteAllRecords(clean);
        }

        static void SaveRecord(MigrationRecord rec)
        {
            var list = ListMigrations();
            list.Insert(0, rec);
            if (list.Count > 60) list = list.Take(60).ToList();
            WriteAllRecords(list);
        }

        static void WriteAllRecords(List<MigrationRecord> list)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(HistoryFile, ser.Serialize(list), new UTF8Encoding(false));
            }
            catch { }
        }

        public static string TryReadJunctionTarget(string dir)
        {
            try
            {
                int ex;
                string parent = Path.GetDirectoryName(dir.TrimEnd('\\'));
                string name = Path.GetFileName(dir.TrimEnd('\\'));
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return "";
                string o = Util.Run("cmd.exe", "/c dir /AL \"" + parent + "\"", 2500, out ex);
                foreach (var raw in o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (raw.IndexOf(name + " [", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int lb = raw.IndexOf('['), rb = raw.LastIndexOf(']');
                        if (lb >= 0 && rb > lb) return raw.Substring(lb + 1, rb - lb - 1);
                    }
                }
            }
            catch { }
            return "";
        }

        static bool HasEnoughSpace(string destPath, long neededBytes)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(destPath));
                var di = new DriveInfo(root);
                long margin = Math.Max(64L * 1024 * 1024, neededBytes / 10);
                return di.IsReady && di.AvailableFreeSpace >= neededBytes + margin;
            }
            catch { return false; }
        }

        static void MeasureTree(string root, out long totalBytes, out int totalFiles, CancellationToken ct)
        {
            totalBytes = 0; totalFiles = 0;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && !ct.IsCancellationRequested)
            {
                string cur = stack.Pop();
                Win32Native.WIN32_FIND_DATAW fd;
                IntPtr h = Win32Native.FindFirstFileExW(Fs.ToLongPath(cur.TrimEnd('\\') + @"\*"),
                    Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic, out fd,
                    Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, Win32Native.FIND_FIRST_EX_LARGE_FETCH);
                if (h == Win32Native.INVALID_HANDLE_VALUE) continue;
                try
                {
                    do
                    {
                        string name = fd.cFileName;
                        if (name == "." || name == "..") continue;
                        if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;
                        if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_DIRECTORY) != 0)
                            stack.Push(cur.TrimEnd('\\') + "\\" + name);
                        else
                        {
                            totalFiles++;
                            totalBytes += (((long)fd.nFileSizeHigh) << 32) | fd.nFileSizeLow;
                        }
                    } while (!ct.IsCancellationRequested && Win32Native.FindNextFileW(h, out fd));
                }
                finally { Win32Native.FindClose(h); }
            }
        }

        static void CopyDirectorySafe(string srcRoot, string dstRoot, ref long copiedBytes, ref int copiedFiles, long totalBytes, CancellationToken ct, Action<string, int> onProg)
        {
            Directory.CreateDirectory(dstRoot);
            var stack = new Stack<KeyValuePair<string, string>>();
            stack.Push(new KeyValuePair<string, string>(srcRoot, dstRoot));
            int lastPct = -1;

            while (stack.Count > 0 && !ct.IsCancellationRequested)
            {
                var pair = stack.Pop();
                string sDir = pair.Key, dDir = pair.Value;
                Directory.CreateDirectory(dDir);

                Win32Native.WIN32_FIND_DATAW fd;
                IntPtr h = Win32Native.FindFirstFileExW(Fs.ToLongPath(sDir.TrimEnd('\\') + @"\*"),
                    Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic, out fd,
                    Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, Win32Native.FIND_FIRST_EX_LARGE_FETCH);
                if (h == Win32Native.INVALID_HANDLE_VALUE) continue;
                try
                {
                    do
                    {
                        string name = fd.cFileName;
                        if (name == "." || name == "..") continue;
                        if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;
                        string sPath = sDir.TrimEnd('\\') + "\\" + name;
                        string dPath = dDir.TrimEnd('\\') + "\\" + name;
                        if ((fd.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_DIRECTORY) != 0)
                        {
                            stack.Push(new KeyValuePair<string, string>(sPath, dPath));
                        }
                        else
                        {
                            long fLen = (((long)fd.nFileSizeHigh) << 32) | fd.nFileSizeLow;
                            File.Copy(sPath, dPath, true);
                            try { File.SetLastWriteTimeUtc(dPath, File.GetLastWriteTimeUtc(sPath)); } catch { }
                            copiedFiles++;
                            copiedBytes += fLen;
                            if (onProg != null && totalBytes > 0)
                            {
                                int pct = 15 + (int)Math.Min(55, copiedBytes * 55 / totalBytes);
                                if (pct != lastPct) { lastPct = pct; onProg("第 1/4 步：安全拷贝中 (" + Util.Fmt(copiedBytes) + " / " + Util.Fmt(totalBytes) + ")…", pct); }
                            }
                        }
                    } while (!ct.IsCancellationRequested && Win32Native.FindNextFileW(h, out fd));
                }
                finally { Win32Native.FindClose(h); }
            }
        }

        static void CopyFileChunked(string src, string dst, long totalBytes, CancellationToken ct, Action<double> onFrac)
        {
            byte[] buf = new byte[1024 * 1024]; // 1MB buffer
            long done = 0;
            using (var r = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var w = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int n;
                while ((n = r.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ct.IsCancellationRequested) break;
                    w.Write(buf, 0, n);
                    done += n;
                    if (onFrac != null && totalBytes > 0) onFrac((double)done / totalBytes);
                }
            }
            try { File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); } catch { }
        }

        static void CleanupTempPath(string path)
        {
            try
            {
                if (Directory.Exists(path)) { var sb = new StringBuilder(); Fs.DeleteDirSafe(path, sb); }
                else if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
            }
            catch { }
        }
    }
}
