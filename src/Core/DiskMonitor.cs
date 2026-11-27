// DiskGuard v3 - 磁盘状态读取、30天历史采样与趋势预测
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DiskGuard
{
    public class DriveStat
    {
        public string Name;
        public string Label;
        public bool Ready;
        public long Free;
        public long Total;

        public double FreeGB { get { return Free / 1073741824.0; } }
        public double TotalGB { get { return Total / 1073741824.0; } }
        public double UsedRatio { get { return (Ready && Total > 0) ? (double)(Total - Free) / Total : 0; } }

        public int Level(Config c)
        {
            if (!Ready) return 0;
            if (FreeGB < c.CritGB || UsedRatio >= 0.96) return 2;
            if (FreeGB < c.WarnGB || UsedRatio >= 0.88) return 1;
            return 0;
        }

        public string HealthLabel(Config c)
        {
            if (!Ready) return "未就绪";
            int lvl = Level(c);
            if (lvl == 2) return "空间紧张";
            if (lvl == 1) return "空间偏紧";
            return "空间充裕";
        }

        public static List<DriveStat> Read(Config c)
        {
            var list = new List<DriveStat>();
            foreach (var n in c.EffectiveDrives())
            {
                var s = new DriveStat { Name = n, Label = "" };
                try
                {
                    var d = new DriveInfo(n + ":\\");
                    if (d.IsReady)
                    {
                        s.Ready = true;
                        s.Free = d.AvailableFreeSpace;
                        s.Total = d.TotalSize;
                        s.Label = d.VolumeLabel ?? "";
                    }
                }
                catch { }
                list.Add(s);
            }
            return list;
        }

        public static List<string> AllFixedDrives()
        {
            var res = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.Fixed && d.IsReady)
                        res.Add(d.Name.Substring(0, 1).ToUpperInvariant());
                }
            }
            catch { }
            return res;
        }

        public static long TotalFree(List<DriveStat> l)
        {
            return l.Where(x => x.Ready).Sum(x => x.Free);
        }
    }

    public class Sample
    {
        public DateTime T;
        public string D;
        public long Free;
    }

    public static class History
    {
        static readonly object L = new object();
        static readonly List<Sample> samples = new List<Sample>();
        static DateTime lastWrite = DateTime.MinValue;
        const string TimeFmt = "yyyy-MM-dd HH:mm:ss";
        static string FilePath { get { return Path.Combine(Util.LogDir, "history.csv"); } }

        public static int Count { get { lock (L) return samples.Count; } }

        public static void Load()
        {
            lock (L)
            {
                samples.Clear();
                try
                {
                    if (!File.Exists(FilePath)) return;
                    var cutoff = DateTime.Now.AddDays(-30);
                    var keep = new List<string>();
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var p = line.Split(',');
                        if (p.Length != 3) continue;
                        DateTime t; long f;
                        if (!DateTime.TryParseExact(p[0], TimeFmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                        if (!long.TryParse(p[2], out f) || t < cutoff) continue;
                        samples.Add(new Sample { T = t, D = p[1], Free = f });
                        keep.Add(line);
                    }
                    File.WriteAllLines(FilePath, keep);
                    if (samples.Count > 0) lastWrite = samples.Max(x => x.T);
                }
                catch { }
            }
        }

        public static void Record(List<DriveStat> stats)
        {
            lock (L)
            {
                var now = DateTime.Now;
                if (now - lastWrite < TimeSpan.FromMinutes(10)) return;
                lastWrite = now;
                var sb = new StringBuilder();
                foreach (var d in stats)
                {
                    if (!d.Ready) continue;
                    samples.Add(new Sample { T = now, D = d.Name, Free = d.Free });
                    sb.AppendLine(now.ToString(TimeFmt, CultureInfo.InvariantCulture) + "," + d.Name + "," + d.Free);
                }
                try { File.AppendAllText(FilePath, sb.ToString()); } catch { }
            }
        }

        public static List<Sample> GetSeries(string drive, TimeSpan window)
        {
            lock (L)
            {
                var from = DateTime.Now - window;
                return samples.Where(s => s.D == drive && s.T >= from).OrderBy(s => s.T).ToList();
            }
        }

        public static bool Change(string drive, TimeSpan window, long nowFree, out long delta, out TimeSpan span)
        {
            delta = 0; span = TimeSpan.Zero;
            lock (L)
            {
                var from = DateTime.Now - window;
                Sample oldest = null;
                foreach (var s in samples)
                    if (s.D == drive && s.T >= from && (oldest == null || s.T < oldest.T)) oldest = s;
                if (oldest == null) return false;
                delta = nowFree - oldest.Free;
                span = DateTime.Now - oldest.T;
                return true;
            }
        }

        public static string ChangeText(DriveStat d)
        {
            long delta; TimeSpan span;
            if (!d.Ready || !Change(d.Name, TimeSpan.FromHours(24), d.Free, out delta, out span) || span.TotalMinutes < 20) return "";
            string label = span.TotalHours >= 23 ? "24h" : "近" + Math.Max(1, (int)Math.Round(span.TotalHours)) + "h";
            return label + " " + Util.FmtDelta(delta);
        }

        // 计算日均空间变化速率（GB/天）及预计耗尽天数（<0 表示增长或样本不足）
        public static string ForecastText(DriveStat d)
        {
            if (!d.Ready) return "不可用";
            long delta; TimeSpan span;
            if (!Change(d.Name, TimeSpan.FromDays(7), d.Free, out delta, out span) || span.TotalHours < 2)
                return "采样积累中（运行 2 小时后生成趋势预测）";
            double days = span.TotalDays;
            double gbPerDay = (delta / 1073741824.0) / days;
            if (gbPerDay >= -0.05)
                return string.Format("近 {0} 趋势平稳（日均 {1} GB）", span.TotalHours >= 24 ? ((int)Math.Round(days) + " 天") : ((int)Math.Round(span.TotalHours) + " 小时"), (gbPerDay >= 0 ? "+" : "") + gbPerDay.ToString("0.00"));
            double daysLeft = d.FreeGB / (-gbPerDay);
            if (daysLeft > 365) return string.Format("日均消耗 {0:0.00} GB，空间充裕", -gbPerDay);
            return string.Format("日均消耗 {0:0.00} GB，按此速度约 {1:0} 天耗尽", -gbPerDay, Math.Max(1, daysLeft));
        }
    }
}
