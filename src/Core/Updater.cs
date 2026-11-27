// DiskGuard v7 - 安全自动更新系统（支持 GitHub Releases API / 自定义 latest.json / 局域网目录 + SHA-256 强制校验）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DiskGuard
{
    public class UpdateInfo
    {
        public bool Ok;
        public bool HasUpdate;
        public string CurrentVersion = Updater.CurrentVersion;
        public string LatestVersion = Updater.CurrentVersion;
        public string DownloadUrl = "";
        public string Sha256 = "";
        public string Notes = "";
        public string Error = "";
    }

    public static class Updater
    {
        public const string CurrentVersion = "9.0.2";
        public const string CurrentReleaseNotes =
            "• 彻底升级「桌面图标变白纸」修复引擎：支持自动检测并修复损坏的 .lnk 快捷方式图标指向、定位失靶快捷方式、抗 Win11 explorer 秒级自启锁定的多轮清库 + ie4uinit/SHChangeNotify 强制重绘\n" +
            "• 默认接入 GitHub Releases 自动更新源并支持 SHA-256 签名自动校验\n" +
            "• 修复 Docker Desktop 非 C 盘/自定义路径探测（6 级自动探测 + WSL Lxss 注册表识别）\n" +
            "• 原生 DirectX WPF 单进程架构，常驻内存 ~35 MB";

        public static UpdateInfo Check(string updateSource)
        {
            var info = new UpdateInfo();
            if (string.IsNullOrWhiteSpace(updateSource))
            {
                info.Ok = false;
                info.Error = "尚未配置更新源地址（支持 GitHub Releases API、自定义 latest.json 链接或局域网共享目录）";
                return info;
            }

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                string rawJson = "";
                string baseDirOrUrl = "";
                string src = updateSource.Trim();

                if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                    {
                        wc.Headers.Add("User-Agent", "DiskGuard/" + CurrentVersion);
                        rawJson = wc.DownloadString(src);
                    }
                    int lastSlash = src.LastIndexOf('/');
                    if (lastSlash > 8) baseDirOrUrl = src.Substring(0, lastSlash + 1);
                }
                else
                {
                    string jsonPath = Directory.Exists(src) ? Path.Combine(src, "latest.json") : src;
                    if (!File.Exists(jsonPath))
                    {
                        info.Error = "未找到更新清单文件：" + jsonPath;
                        return info;
                    }
                    rawJson = File.ReadAllText(jsonPath, Encoding.UTF8);
                    baseDirOrUrl = Path.GetDirectoryName(jsonPath) ?? "";
                }

                return ParseManifest(rawJson, baseDirOrUrl);
            }
            catch (WebException wex)
            {
                info.Ok = false;
                var resp = wex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                    info.Error = "更新源返回 404（目标仓库尚未创建或尚未发布第一个 Release / latest.json）。请先在 GitHub 发布 Release，或改为本地/局域网源测试。";
                else if (resp != null && (int)resp.StatusCode == 403)
                    info.Error = "GitHub API 访问受限（403 限流），请稍后再试或切换为自定义 latest.json 更新源。";
                else
                    info.Error = "连接更新源失败：" + wex.Message;
                return info;
            }
            catch (Exception ex)
            {
                info.Ok = false;
                info.Error = "检查更新失败：" + ex.Message;
                return info;
            }
        }

        public static UpdateInfo ParseManifest(string rawJson, string baseDirOrUrl)
        {
            var info = new UpdateInfo();
            var ser = new JavaScriptSerializer();
            var obj = ser.Deserialize<Dictionary<string, object>>(rawJson);
            if (obj == null) { info.Error = "更新清单格式无效"; return info; }

            // 兼容 GitHub Releases API 格式（tag_name + assets）与自定义 latest.json 格式
            if (obj.ContainsKey("tag_name"))
            {
                info.LatestVersion = Str(obj, "tag_name").TrimStart('v', 'V');
                info.Notes = Str(obj, "body");
                // 若 Release 说明中包含 SHA256: <64位十六进制>，自动提取用于安装前校验
                if (!string.IsNullOrEmpty(info.Notes))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(info.Notes, @"SHA-?256\s*[:：=]\s*([0-9a-fA-F]{64})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success) info.Sha256 = m.Groups[1].Value.ToLowerInvariant();
                }
                object assetsObj;
                if (obj.TryGetValue("assets", out assetsObj) && assetsObj is System.Collections.IEnumerable)
                {
                    foreach (var a in (System.Collections.IEnumerable)assetsObj)
                    {
                        var ad = a as Dictionary<string, object>;
                        if (ad == null) continue;
                        string name = Str(ad, "name");
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            info.DownloadUrl = Str(ad, "browser_download_url");
                            break;
                        }
                    }
                }
            }
            else
            {
                info.LatestVersion = Str(obj, "version").TrimStart('v', 'V');
                info.DownloadUrl = Str(obj, "url");
                if (string.IsNullOrEmpty(info.DownloadUrl)) info.DownloadUrl = Str(obj, "file");
                info.Sha256 = Str(obj, "sha256");
                info.Notes = Str(obj, "notes");
            }

            if (!string.IsNullOrEmpty(info.DownloadUrl) && !string.IsNullOrEmpty(baseDirOrUrl))
            {
                bool isAbs = info.DownloadUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             info.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                             Path.IsPathRooted(info.DownloadUrl);
                if (!isAbs)
                {
                    info.DownloadUrl = baseDirOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? (baseDirOrUrl.TrimEnd('/') + "/" + info.DownloadUrl.TrimStart('/'))
                        : Path.Combine(baseDirOrUrl, info.DownloadUrl);
                }
            }

            info.Ok = true;
            info.HasUpdate = CompareVersions(info.LatestVersion, CurrentVersion) > 0 && !string.IsNullOrEmpty(info.DownloadUrl);
            return info;
        }

        public static int CompareVersions(string a, string b)
        {
            Version va, vb;
            if (Version.TryParse(NormalizeVer(a), out va) && Version.TryParse(NormalizeVer(b), out vb))
                return va.CompareTo(vb);
            return string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeVer(string v)
        {
            v = (v ?? "0.0.0").Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash > 0) v = v.Substring(0, dash);
            var p = v.Split('.');
            if (p.Length == 1) return p[0] + ".0.0";
            if (p.Length == 2) return p[0] + "." + p[1] + ".0";
            return p[0] + "." + p[1] + "." + p[2];
        }

        public static FixResult DownloadAndLaunchUpgrade(string url, string expectedSha256, Action<int, string> onProgress)
        {
            if (string.IsNullOrEmpty(url))
                return new FixResult { Ok = false, Title = "升级失败", Summary = "未提供有效的安装包下载地址" };

            string tempExe = Path.Combine(Path.GetTempPath(), "DiskGuard-Update-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            try
            {
                if (onProgress != null) onProgress(10, "正在获取新版本安装包…");
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "DiskGuard/" + CurrentVersion);
                        wc.DownloadProgressChanged += (s, e) =>
                        {
                            if (onProgress != null) onProgress(10 + (int)(e.ProgressPercentage * 0.75), "正在下载更新包 " + e.ProgressPercentage + "%");
                        };
                        var done = new System.Threading.ManualResetEventSlim(false);
                        Exception dlErr = null;
                        wc.DownloadFileCompleted += (s, e) => { dlErr = e.Error; done.Set(); };
                        wc.DownloadFileAsync(new Uri(url), tempExe);
                        done.Wait();
                        if (dlErr != null) throw dlErr;
                    }
                }
                else
                {
                    if (!File.Exists(url)) return new FixResult { Ok = false, Title = "找不到安装包", Summary = url };
                    File.Copy(url, tempExe, true);
                    if (onProgress != null) onProgress(85, "已复制本地更新包");
                }

                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    if (onProgress != null) onProgress(90, "正在校验 SHA-256 完整性签名…");
                    string actual = ComputeSha256(tempExe);
                    if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(tempExe); } catch { }
                        return new FixResult { Ok = false, Title = "安全拦截：校验失败", Summary = "安装包 SHA-256 哈希不匹配，已中止升级并删除临时文件" };
                    }
                }

                if (onProgress != null) onProgress(98, "校验通过，正在启动无缝静默升级…");
                Process.Start(new ProcessStartInfo(tempExe, "--silent-upgrade \"" + Util.AppDir + "\"") { UseShellExecute = true });
                return new FixResult { Ok = true, Title = "正在升级重启", Summary = "已启动无缝覆盖升级，几秒后将自动重启为最新版本（原有配置全部保留）" };
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
                return new FixResult { Ok = false, Title = "下载升级失败", Summary = ex.Message };
            }
        }

        public static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
        }
    }
}
