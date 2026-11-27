// DiskGuard v8 - C:\Users\<User> 文件夹深度体检与安全可清理清单引擎（绝不动个人文档、SSH、账号 Token）
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiskGuard
{
    public class UserAuditItem
    {
        public string Id;
        public string Name;
        public string FullPath;
        public string RelPath;
        public string Category;   // 缓存与临时 / 开发与AI模型 / 个人与下载 / 系统与账号核心
        public string Safety;     // L1 / L2 / L4
        public string SafetyText; // 可安全清理 / 建议迁移或审清 / 核心不可动
        public string ActionType; // clean / migrate / inspect
        public string Desc;
        public long SizeBytes;
        public bool IsJunction;
        public string JunctionTarget;
    }

    public class UserAuditResult
    {
        public string UserRoot;
        public long ElapsedMs;
        public long L1TotalBytes;
        public long L2TotalBytes;
        public long L4TotalBytes;
        public List<UserAuditItem> Items = new List<UserAuditItem>();
    }

    public static class UserFolderAudit
    {
        class KnownRule
        {
            public string Rel;
            public string Name;
            public string Cat;
            public string Safety;
            public string Action;
            public string Desc;
        }

        // 明确白名单化的 User 子目录规则（严守安全边界）
        static readonly KnownRule[] KnownRules = new[]
        {
            // L1: 纯缓存，可安全一键清空
            new KnownRule { Rel = @"AppData\Local\Temp", Name = "用户临时目录 (Temp)", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "安装程序与软件运行留下的临时文件，清空不影响任何软件配置" },
            new KnownRule { Rel = @"AppData\Local\CrashDumps", Name = "程序崩溃转储 (CrashDumps)", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "软件闪退生成的 .dmp 排错文件，普通用户无需保留" },
            new KnownRule { Rel = @"AppData\Local\pip\cache", Name = "Python pip 下载缓存", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "已下载的 whl 安装包缓存，删后重新 pip install 自动拉取" },
            new KnownRule { Rel = @"AppData\Local\uv\cache", Name = "Python uv 缓存", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "uv 包管理下载缓存，可随时安全清空" },
            new KnownRule { Rel = @"AppData\Local\npm-cache", Name = "npm 下载缓存", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "Node.js npm 包缓存，建议清理或改位至数据盘" },
            new KnownRule { Rel = @"AppData\Local\go-build", Name = "Go 编译构建缓存", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "Go 增量编译中间缓存，清理后首次 go build 会自动重建" },
            new KnownRule { Rel = @"AppData\Local\Yarn\Cache", Name = "Yarn 包缓存", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "Yarn 离线包缓存，清空不影响现有项目的 node_modules" },
            new KnownRule { Rel = @"AppData\Local\SquirrelTemp", Name = "桌面应用更新残留 (SquirrelTemp)", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "Electron 类应用自动更新遗留的安装包，可安全清空" },
            new KnownRule { Rel = @"AppData\Local\D3DSCache", Name = "DirectX 着色器缓存", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "显卡渲染编译缓存，清空后打开游戏/软件自动重建" },
            new KnownRule { Rel = @"AppData\Local\NVIDIA\DXCache", Name = "NVIDIA 显卡着色器缓存", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "NVIDIA 驱动着色器缓存，可安全清理释放数百 MB" },
            new KnownRule { Rel = @"AppData\Local\JetBrains", Name = "JetBrains IDE 缓存与索引", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "IDEA / PyCharm / WebStorm 缓存与索引，体积常达数 GB，建议迁往数据盘" },
            new KnownRule { Rel = @"AppData\Local\ms-playwright", Name = "Playwright 浏览器内核", Cat = "缓存与临时", Safety = "L1", Action = "clean", Desc = "自动化测试下载的 Chromium/Firefox，随时可 npx playwright install 重装" },
            new KnownRule { Rel = @"AppData\Local\pnpm\store", Name = "pnpm 全局硬链仓库", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "所有 pnpm 项目共用的包仓库，强烈建议改位迁移到 D/E 盘" },
            new KnownRule { Rel = @"AppData\Local\Docker", Name = "Docker Desktop 本地数据", Cat = "开发与AI模型", Safety = "L2", Action = "inspect", Desc = "Docker 配置与日志，虚拟磁盘瘦身请前往「Docker 与 WSL2」专治中心" },
            new KnownRule { Rel = @"AppData\Local\Packages", Name = "UWP / 微软商店应用与 WSL 容器", Cat = "系统与账号核心", Safety = "L4", Action = "inspect", Desc = "包含商店应用与 WSL 发行版虚拟磁盘，严禁直接删除（WSL 请用专用调优）" },

            // 根目录常见高占用点目录
            new KnownRule { Rel = @".nuget\packages", Name = "NuGet (.NET) 全局包缓存", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = ".NET 项目依赖包缓存，可安全迁往数据盘并设置 NUGET_PACKAGES" },
            new KnownRule { Rel = @".m2\repository", Name = "Maven (Java) 本地仓库", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "Java Maven 依赖包仓库，体积常达数 GB，支持安全软链搬家" },
            new KnownRule { Rel = @".gradle\caches", Name = "Gradle 构建与依赖缓存", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "Android / Java Gradle 缓存，建议迁往数据盘并设置 GRADLE_USER_HOME" },
            new KnownRule { Rel = @".cargo", Name = "Rust Cargo 依赖与工具链", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "Rust crate 缓存，可通过设置 CARGO_HOME 迁至数据盘" },
            new KnownRule { Rel = @".ollama", Name = "Ollama 本地大模型权重", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "本地 LLM 模型文件常达数十 GB，强烈建议迁往 D/E 盘并设置 OLLAMA_MODELS" },
            new KnownRule { Rel = @".cache", Name = "通用工具与 AI 模型缓存 (.cache)", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "含 HuggingFace、Whisper、Pre-commit 等缓存，建议迁移或按需审清" },
            new KnownRule { Rel = @".conda", Name = "Conda Python 虚拟环境", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "Anaconda/Miniconda 虚拟环境与包缓存，体积巨大建议迁移至数据盘" },
            new KnownRule { Rel = @".android", Name = "Android 模拟器与构建缓存", Cat = "开发与AI模型", Safety = "L2", Action = "migrate", Desc = "含 AVD 虚拟机镜像与缓存，体积大建议迁至数据盘" },
            new KnownRule { Rel = @".vscode", Name = "VS Code 扩展插件", Cat = "开发与AI模型", Safety = "L2", Action = "inspect", Desc = "已安装的 VS Code 插件目录，无需删除，异常膨胀可检查旧插件版本" },
            new KnownRule { Rel = @".cursor", Name = "Cursor 扩展与索引数据", Cat = "开发与AI模型", Safety = "L2", Action = "inspect", Desc = "Cursor IDE 插件与工作区索引，建议保留以免重装插件" },

            // 个人常用大文件夹
            new KnownRule { Rel = @"Downloads", Name = "下载文件夹 (Downloads)", Cat = "个人与下载", Safety = "L2", Action = "migrate", Desc = "浏览器下载的安装包、压缩包与视频重灾区，支持安全搬家到 D/E 盘或手动审清" },
            new KnownRule { Rel = @"Videos", Name = "视频文件夹 (Videos)", Cat = "个人与下载", Safety = "L2", Action = "migrate", Desc = "录屏与本地视频文件，支持零丢失安全搬家到数据盘" },
            new KnownRule { Rel = @"Desktop", Name = "桌面文件 (Desktop)", Cat = "系统与账号核心", Safety = "L4", Action = "inspect", Desc = "个人桌面文档与快捷方式，受保护禁止自动清理（可手动整理大安装包）" },
            new KnownRule { Rel = @"Documents", Name = "我的文档 (Documents)", Cat = "系统与账号核心", Safety = "L4", Action = "inspect", Desc = "含微信/QQ 文件与个人文档，聊天大附件请用「微信/QQ 大附件专清」" },
            new KnownRule { Rel = @"Pictures", Name = "图片与截图 (Pictures)", Cat = "系统与账号核心", Safety = "L4", Action = "inspect", Desc = "个人相册与截图，受保护禁止自动清理" },
            new KnownRule { Rel = @".ssh", Name = "SSH 密钥与配置 (.ssh)", Cat = "系统与账号核心", Safety = "L4", Action = "inspect", Desc = "服务器私钥与 Git 密钥，永久红线锁定绝不触碰" },
            new KnownRule { Rel = @"AppData\Roaming", Name = "软件配置与账号数据 (Roaming)", Cat = "系统与账号核心", Safety = "L4", Action = "inspect", Desc = "存放软件配置、登录态与关键业务数据，严禁整目录清理" }
        };

        // 允许执行「安全清空」的精确白名单相对路径（双重防线，防止前端传非法路径）
        static readonly HashSet<string> CleanableRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"AppData\Local\Temp",
            @"AppData\Local\CrashDumps",
            @"AppData\Local\pip\cache",
            @"AppData\Local\uv\cache",
            @"AppData\Local\npm-cache",
            @"AppData\Local\go-build",
            @"AppData\Local\Yarn\Cache",
            @"AppData\Local\SquirrelTemp",
            @"AppData\Local\D3DSCache",
            @"AppData\Local\NVIDIA\DXCache",
            @"AppData\Local\ms-playwright"
        };

        public static UserAuditResult Scan(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            string root = Util.UserProfile;
            var res = new UserAuditResult { UserRoot = root };
            var bag = new ConcurrentBag<UserAuditItem>();
            var knownFullSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var tasks = new List<KnownRule>();
            foreach (var r in KnownRules)
            {
                string full = Path.Combine(root, r.Rel);
                knownFullSet.Add(full.TrimEnd('\\'));
                if (Directory.Exists(full)) tasks.Add(r);
            }

            // 额外发现 User 根目录下未在规则表里的其他大目录（如 go、scoop、.npm、miniconda3 等）
            var extraTopDirs = new List<string>();
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
                    if (knownFullSet.Contains(dir.TrimEnd('\\'))) continue;
                    // 跳过系统隐藏联接如 Application Data / Local Settings / Cookies
                    try
                    {
                        var attr = File.GetAttributes(dir);
                        if ((attr & FileAttributes.ReparsePoint) != 0 && (attr & FileAttributes.Hidden) != 0) continue;
                    }
                    catch { continue; }
                    extraTopDirs.Add(dir);
                }
            }
            catch { }

            Parallel.ForEach(tasks, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct }, r =>
            {
                if (ct.IsCancellationRequested) return;
                string full = Path.Combine(root, r.Rel);
                bool isJunc = IsJunctionDir(full);
                long sz = isJunc ? 0 : Fs.DirSize(full, null, ct, false);
                if (sz < 512 * 1024 && !isJunc) return; // 小于 512KB 且非已迁移软链不刷屏

                bag.Add(new UserAuditItem
                {
                    Id = r.Rel.ToLowerInvariant().Replace('\\', '_'),
                    Name = r.Name,
                    FullPath = full,
                    RelPath = r.Rel,
                    Category = r.Cat,
                    Safety = r.Safety,
                    SafetyText = r.Safety == "L1" ? "可安全清理" : (r.Safety == "L2" ? "建议迁移/审清" : "核心不可动"),
                    ActionType = isJunc ? "inspect" : r.Action,
                    Desc = isJunc ? ("已通过安全软链接迁移至其他分区，当前不占用 C 盘空间") : r.Desc,
                    SizeBytes = sz,
                    IsJunction = isJunc,
                    JunctionTarget = isJunc ? SafeRelocator.TryReadJunctionTarget(full) : ""
                });
            });

            Parallel.ForEach(extraTopDirs, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, dir =>
            {
                if (ct.IsCancellationRequested) return;
                bool isJunc = IsJunctionDir(dir);
                long sz = isJunc ? 0 : Fs.DirSize(dir, null, ct, false);
                if (sz < 50L * 1024 * 1024 && !isJunc) return; // 仅展示 >=50MB 的其他顶层目录
                string name = Path.GetFileName(dir);
                bool isDotOrDev = name.StartsWith(".") || name.Equals("go", StringComparison.OrdinalIgnoreCase) || name.Equals("scoop", StringComparison.OrdinalIgnoreCase);
                bag.Add(new UserAuditItem
                {
                    Id = "extra_" + name.ToLowerInvariant(),
                    Name = name + "（用户目录）",
                    FullPath = dir,
                    RelPath = name,
                    Category = isDotOrDev ? "开发与AI模型" : "个人与下载",
                    Safety = "L2",
                    SafetyText = "建议迁移/审清",
                    ActionType = isJunc ? "inspect" : "migrate",
                    Desc = isJunc ? "已迁移至数据盘，不占 C 盘空间" : "用户主目录下的大体积文件夹，可先打开核对或安全搬家到其他盘",
                    SizeBytes = sz,
                    IsJunction = isJunc,
                    JunctionTarget = isJunc ? SafeRelocator.TryReadJunctionTarget(dir) : ""
                });
            });

            var list = bag.OrderBy(x => x.Safety == "L1" ? 0 : (x.Safety == "L2" ? 1 : 2))
                          .ThenByDescending(x => x.SizeBytes)
                          .ToList();
            res.Items = list;
            res.L1TotalBytes = list.Where(x => x.Safety == "L1").Sum(x => x.SizeBytes);
            res.L2TotalBytes = list.Where(x => x.Safety == "L2").Sum(x => x.SizeBytes);
            res.L4TotalBytes = list.Where(x => x.Safety == "L4").Sum(x => x.SizeBytes);
            sw.Stop();
            res.ElapsedMs = sw.ElapsedMilliseconds;
            return res;
        }

        public static FixResult CleanSafeUserItem(string relPath)
        {
            if (string.IsNullOrWhiteSpace(relPath) || !CleanableRelPaths.Contains(relPath.Trim()))
                return new FixResult { Ok = false, Title = "安全拦截", Summary = "该目录不在 L1 安全可清空白名单内，禁止自动删除" };

            string full = Path.Combine(Util.UserProfile, relPath.Trim());
            if (!Directory.Exists(full) || Fs.IsProtected(full) || Fs.IsSystemCriticalPath(full) || IsJunctionDir(full))
                return new FixResult { Ok = false, Title = "安全跳过", Summary = "目录不存在、已迁移或受系统保护" };

            var log = new StringBuilder();
            log.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " User 目录专清：" + full + " ====");
            TimeSpan? minAge = relPath.Equals(@"AppData\Local\Temp", StringComparison.OrdinalIgnoreCase)
                ? (TimeSpan?)TimeSpan.FromHours(12)
                : null;
            long freed = Fs.EmptyDir(full, minAge, log);
            log.AppendLine("合计：删除文件 " + Util.Fmt(freed));
            log.AppendLine();
            try { File.AppendAllText(Util.LogFile, log.ToString(), Encoding.UTF8); } catch { }

            return new FixResult
            {
                Ok = true,
                Title = "User 目录安全清理完成",
                Summary = "已清理 " + relPath + "，释放 " + Util.Fmt(freed) + "（正在被程序占用的文件已安全跳过）"
            };
        }

        public static bool IsJunctionDir(string path)
        {
            try { return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return false; }
        }
    }
}
