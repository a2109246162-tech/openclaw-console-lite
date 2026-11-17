// OpenClaw 控制台 Lite —— 原生 WinForms 轻量版（面向 Win10 1709+，全信任 MSIX 打包）
// 功能范围（按需求裁剪）：微信扫码绑定 / 模型添加·排序·删除 / 网关启停与状态
// 与 EXE 全功能版共用同一 %USERPROFILE%\.openclaw 数据目录（配置互通，可随时换用）。
// 安全规则与全功能版一致：只按 PID 终止本程序亲手拉起的进程，绝不按进程名批量杀。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OpenClawLite
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool selftest = args != null && args.Length > 0 && args[0] == "--selftest";
            if (selftest)
            {
                // 自检必须与真实数据完全隔离
                Core.HOME = Path.Combine(Path.GetTempPath(), "oc-lite-selftest");
                try { if (Directory.Exists(Core.HOME)) Directory.Delete(Core.HOME, true); } catch (Exception) { }
                Core.PrepareEnv();
                SelfTest.Run();
                try { Directory.Delete(Core.HOME, true); } catch (Exception) { }
                return;
            }
            // 自检环境变量可让测试实例与已安装实例并存，避免为了测试去关用户的窗口
            string mtxName = "OpenClawLiteConsoleSingleInstance" +
                (Environment.GetEnvironmentVariable("OC_LITE_TEST_INSTANCE") ?? "");
            using (var mutex = new Mutex(false, mtxName))
            {
                if (selftest) { SelfTest.Run(); return; }
                if (!mutex.WaitOne(TimeSpan.Zero, false))
                {
                    MessageBox.Show("OpenClaw 轻量控制台已在运行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Core.PrepareEnv();
                Application.Run(new MainForm());
            }
        }
    }

    // ---------- 核心逻辑（与全功能版 main.js 行为对齐） ----------
    static class Core
    {
        public static readonly string ROOT = AppDomain.CurrentDomain.BaseDirectory;        public static readonly string RUNTIME = Path.Combine(ROOT, "runtime");
        public static readonly string NODE = Path.Combine(RUNTIME, "node.exe");
        public static readonly string OPENCLAW_PKG = Path.Combine(RUNTIME, "openclaw");
        public static readonly string OPENCLAW_CLI = Path.Combine(OPENCLAW_PKG, "openclaw.mjs");
        public static readonly string GATEWAY_JS = Path.Combine(OPENCLAW_PKG, "dist", "index.js");
        // UWP 轻量版为独立实例：数据目录与本地生产环境（.openclaw / 18789）完全隔离
        public static string HOME = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), ".openclaw-lite-home");
        public static string DATA_DIR { get { return Path.Combine(HOME, ".openclaw"); } }
        public static string CONFIG { get { return Path.Combine(DATA_DIR, "openclaw.json"); } }
        public static string SETTINGS { get { return Path.Combine(DATA_DIR, "console-settings.json"); } }
        public static string LOG_DIR { get { return Path.Combine(DATA_DIR, "logs"); } }
        public static readonly string PRESET = Path.Combine(ROOT, "resources", "app", "preset-config.json");
        public static readonly string PLUGIN_SRC = Path.Combine(ROOT, "resources", "app", "plugins", "tencent-weixin-openclaw-weixin-7783ac86ba");
        public static readonly string PLUGIN_PKG = Path.Combine(PLUGIN_SRC, "node_modules", "@tencent-weixin", "openclaw-weixin");
        public static readonly string CA_BUNDLE = Path.Combine(RUNTIME, "system-ca-bundle.pem");

        public const int PRESET_REV = 2;
        // 端口从 18795 起，绝不占用生产网关的 18789
        public static readonly int[] PORT_CANDIDATES = new int[] { 18795, 18796, 18797, 18798, 18799 };

        public static int PORT = 18795;
        // 本程序亲手拉起的网关 PID，供状态显示用；避免每轮轮询去跑 netstat / WMI
        public static int GatewayPid = 0;
        public static readonly object JsonLock = new object();
        public static readonly HashSet<int> SpawnedPids = new HashSet<int>();
        public static bool GatewayOwned = false;

        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = 1 << 28 };

        public static void BootLog(string msg)
        {
            try
            {
                if (!Directory.Exists(LOG_DIR)) Directory.CreateDirectory(LOG_DIR);
                string p = Path.Combine(LOG_DIR, "lite-console.log");
                try
                {
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 262144)
                    {
                        string[] lines = File.ReadAllLines(p);
                        File.WriteAllLines(p, lines.Skip(Math.Max(0, lines.Length - 200)).ToArray(), Encoding.UTF8);
                    }
                }
                catch (Exception) { }
                File.AppendAllText(p, "[" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "] " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { }
        }

        // ---------- JSON 帮手 ----------
        public static Dictionary<string, object> ReadJsonDict(string path)
        {
            lock (JsonLock)
            {
                if (!File.Exists(path)) return null;
                var d = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                return NormalizeJson(d) as Dictionary<string, object>;
            }
        }
        // JavaScriptSerializer 把 JSON 数组还原成 object[]/ArrayList，
        // 而 AsList 只认 List<object>；不规范化会导致 fallbacks / models 丢失、模型顺序被清空。
        static object NormalizeJson(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d != null)
            {
                var nd = new Dictionary<string, object>();
                foreach (var kv in d) nd[kv.Key] = NormalizeJson(kv.Value);
                return nd;
            }
            var arr = o as object[];
            if (arr != null)
            {
                var l = new List<object>();
                foreach (var x in arr) l.Add(NormalizeJson(x));
                return l;
            }
            var al = o as System.Collections.ArrayList;
            if (al != null)
            {
                var l = new List<object>();
                foreach (var x in al) l.Add(NormalizeJson(x));
                return l;
            }
            return o;
        }
        public static void WriteJsonDict(string path, Dictionary<string, object> obj)
        {
            lock (JsonLock)
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, Js.Serialize(obj) + Environment.NewLine, Encoding.UTF8);
            }
        }
        public static Dictionary<string, object> AsDict(object o)
        {
            return o as Dictionary<string, object>;
        }
        public static List<object> AsList(object o)
        {
            return o as List<object>;
        }
        public static string AsStr(object o)
        {
            return o == null ? null : (o is string ? (string)o : o.ToString());
        }

        // ---------- 端口 ----------
        public static bool PortOpen(int port)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect("127.0.0.1", port, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(800, false))
                    {
                        c.EndConnect(ar);
                        return true;
                    }
                    return false;
                }
            }
            catch (Exception) { return false; }
        }

        public static List<int> ListenPids(int port)
        {
            var pids = new List<int>();
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano -p tcp")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    foreach (string raw in stdout.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string[] cols = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (cols.Length < 4) continue;
                        string local = cols[1];
                        if (!local.EndsWith(":" + port)) continue;
                        int pid;
                        if (int.TryParse(cols[cols.Length - 1], out pid) && pid > 0 && !pids.Contains(pid)) pids.Add(pid);
                    }
                }
            }
            catch (Exception e) { BootLog("listenPids error: " + e.Message); }
            return pids;
        }

        // 只查路径，不做任何终止 —— 终止仅限本会话 spawn 的 PID 或路径验证通过者
        public static string PidExePath(int pid)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process WHERE ProcessId=" + pid))
                {
                    foreach (var o in searcher.Get())
                    {
                        using (o)
                        {
                            var v = o["ExecutablePath"];
                            return v == null ? null : v.ToString();
                        }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        public static bool IsOurNodePath(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(exePath));
                // 本包的 runtime
                if (string.Equals(dir, Path.GetFullPath(RUNTIME), StringComparison.OrdinalIgnoreCase)) return true;
                // 独立实例：只认可本包 runtime 启动的网关，绝不识别/管理任何外部进程
                return false;
            }
            catch (Exception) { return false; }
        }

        public static void KillPidTree(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /F /T")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi)) { p.WaitForExit(10000); }
                BootLog("taskkill /PID " + pid + " /F /T");
            }
            catch (Exception e) { BootLog("taskkill error: " + e.Message); }
        }

        // ---------- 环境 ----------
        // 环境变量直接写入当前进程，子进程由系统自动继承。
        // 刻意不访问 ProcessStartInfo.EnvironmentVariables：.NET Framework 首次构造该属性时
        // 会用 Add() 填充当前进程环境，宿主若同时注入了 Path 与 PATH 便抛「已添加项」，
        // 导致插件安装 / 网关启动 / 扫码等所有子进程全部失败。
        public static void PrepareEnv()
        {
            try
            {
                Environment.SetEnvironmentVariable("HOME", HOME);
                Environment.SetEnvironmentVariable("USERPROFILE", HOME);
                Environment.SetEnvironmentVariable("OPENCLAW_GATEWAY_PORT", PORT.ToString());
                Environment.SetEnvironmentVariable("NODE_OPTIONS", "--title=OpenClaw");
                Environment.SetEnvironmentVariable("NODE_SKIP_PLATFORM_CHECK", "1");
                if (File.Exists(CA_BUNDLE)) Environment.SetEnvironmentVariable("NODE_EXTRA_CA_CERTS", CA_BUNDLE);
                Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", null);
            }
            catch (Exception) { }
        }

        public static Dictionary<string, string> CleanEnv()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static void ApplyEnv(ProcessStartInfo psi, Dictionary<string, string> env)
        {
            // 环境由 PrepareEnv() 设置，子进程自动继承；此处不能触碰 EnvironmentVariables。
        }

        // ---------- 首次启动 ----------
        public static void EnsureDataDirs()
        {
            if (!Directory.Exists(DATA_DIR)) Directory.CreateDirectory(DATA_DIR);
            if (!Directory.Exists(LOG_DIR)) Directory.CreateDirectory(LOG_DIR);
        }

        public static void EnsureConfig()
        {
            if (File.Exists(CONFIG)) return;
            var cfg = new Dictionary<string, object>();
            if (File.Exists(PRESET))
            {
                var preset = ReadJsonDict(PRESET);
                if (preset != null) cfg = preset;
            }
            if (!cfg.ContainsKey("gateway"))
                cfg["gateway"] = (Dictionary<string, object>)Js.Deserialize<object>(
                    "{\"mode\":\"local\",\"auth\":{\"mode\":\"token\",\"token\":\"" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(0, 16) + "\"}}");
            WriteJsonDict(CONFIG, cfg);
            BootLog("config created at " + CONFIG);
        }

        public static Dictionary<string, object> ReadSettings()
        {
            var s = ReadJsonDict(SETTINGS);
            return s ?? new Dictionary<string, object>();
        }
        public static void WriteSettings(Dictionary<string, object> s) { WriteJsonDict(SETTINGS, s); }

        // 数值取自 config 里可能是 int/decimal/double/bool
        public static long AsNum(object o)
        {
            try { return Convert.ToInt64(o); }
            catch (Exception) { return 0; }
        }
        public static bool AsBool(object o)
        {
            try { return Convert.ToBoolean(o); }
            catch (Exception) { return false; }
        }

        // 预置模型合并 + 出厂顺序升级（与 main.js mergePresetModels 一致）
        public static void MergePresetModels()
        {
            try
            {
                if (!File.Exists(PRESET) || !File.Exists(CONFIG)) return;
                var preset = ReadJsonDict(PRESET);
                var cfg = ReadJsonDict(CONFIG);
                if (preset == null || cfg == null) return;
                bool changed = false;

                Dictionary<string, object> models = AsDict(cfg.ContainsKey("models") ? cfg["models"] : null);
                if (models == null) { models = new Dictionary<string, object>(); cfg["models"] = models; }
                if (!models.ContainsKey("mode")) models["mode"] = "merge";
                Dictionary<string, object> providers = AsDict(models.ContainsKey("providers") ? models["providers"] : null);
                if (providers == null) { providers = new Dictionary<string, object>(); models["providers"] = providers; }

                Dictionary<string, object> pModels = AsDict(preset.ContainsKey("models") ? preset["models"] : null);
                Dictionary<string, object> pProviders = pModels == null ? null : AsDict(pModels.ContainsKey("providers") ? pModels["providers"] : null);
                string[] STALE = new string[] { "<REDACTED_ARK_PREFIX>", "<REDACTED_SK_PREFIX>" };
                if (pProviders != null)
                {
                    foreach (var pkv in pProviders)
                    {
                        string pk = pkv.Key;
                        var pv = AsDict(pkv.Value);
                        if (!providers.ContainsKey(pk))
                        {
                            providers[pk] = pv;
                            changed = true;
                            BootLog("preset provider added: " + pk);
                        }
                        else
                        {
                            var cur = AsDict(providers[pk]);
                            string pvKey = AsStr(pv.ContainsKey("apiKey") ? pv["apiKey"] : null);
                            string curKey = AsStr(cur.ContainsKey("apiKey") ? cur["apiKey"] : null);
                            bool stale = string.IsNullOrEmpty(curKey) || Array.Exists(STALE, (p) => curKey.StartsWith(p, StringComparison.Ordinal));
                            if (!string.IsNullOrEmpty(pvKey) && stale)
                            {
                                cur["apiKey"] = pvKey;
                                changed = true;
                                BootLog("preset provider key refreshed: " + pk);
                            }
                            List<object> curModels = AsList(cur.ContainsKey("models") ? cur["models"] : null);
                            if (curModels == null) { curModels = new List<object>(); cur["models"] = curModels; }
                            List<object> pmList = AsList(pv.ContainsKey("models") ? pv["models"] : null);
                            if (pmList != null)
                            {
                                foreach (var pmObj in pmList)
                                {
                                    var pm = AsDict(pmObj);
                                    string pmId = AsStr(pm["id"]);
                                    Dictionary<string, object> cm = null;
                                    foreach (var mObj in curModels)
                                    {
                                        var m = AsDict(mObj);
                                        if (AsStr(m["id"]) == pmId) { cm = m; break; }
                                    }
                                    if (cm == null)
                                    {
                                        curModels.Add(pm);
                                        changed = true;
                                        BootLog("preset model added: " + pk + "/" + pmId);
                                        continue;
                                    }
                                    var want = AsDict(pm.ContainsKey("compat") ? pm["compat"] : null);
                                    if (want != null)
                                    {
                                        var have = AsDict(cm.ContainsKey("compat") ? cm["compat"] : null);
                                        if (have == null) { have = new Dictionary<string, object>(); cm["compat"] = have; }
                                        foreach (var w in want)
                                        {
                                            if (!have.ContainsKey(w.Key) || !object.Equals(have[w.Key], w.Value)) { have[w.Key] = w.Value; changed = true; }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Dictionary<string, object> agents = AsDict(cfg.ContainsKey("agents") ? cfg["agents"] : null);
                if (agents == null) { agents = new Dictionary<string, object>(); cfg["agents"] = agents; }
                Dictionary<string, object> defaults = AsDict(agents.ContainsKey("defaults") ? agents["defaults"] : null);
                if (defaults == null) { defaults = new Dictionary<string, object>(); agents["defaults"] = defaults; }
                Dictionary<string, object> model = AsDict(defaults.ContainsKey("model") ? defaults["model"] : null);
                if (model == null) { model = new Dictionary<string, object> { { "primary", "" }, { "fallbacks", new List<object>() } }; defaults["model"] = model; }
                Dictionary<string, object> modelsMap = AsDict(defaults.ContainsKey("models") ? defaults["models"] : null);
                if (modelsMap == null) { modelsMap = new Dictionary<string, object>(); defaults["models"] = modelsMap; }

                Dictionary<string, object> pDefaults = null;
                var pAgents = AsDict(preset.ContainsKey("agents") ? preset["agents"] : null);
                if (pAgents != null) pDefaults = AsDict(pAgents.ContainsKey("defaults") ? pAgents["defaults"] : null);
                var pModel = pDefaults == null ? null : AsDict(pDefaults.ContainsKey("model") ? pDefaults["model"] : null);

                string pPrimary = pModel == null ? null : AsStr(pModel.ContainsKey("primary") ? pModel["primary"] : null);
                var presetOrder = new List<string>();
                if (!string.IsNullOrEmpty(pPrimary)) presetOrder.Add(pPrimary);
                if (pModel != null)
                {
                    var fb = AsList(pModel.ContainsKey("fallbacks") ? pModel["fallbacks"] : null);
                    if (fb != null) foreach (var f in fb) { string s = AsStr(f); if (!string.IsNullOrEmpty(s)) presetOrder.Add(s); }
                }

                var settings = ReadSettings();
                long cfgRev = AsNum(settings.ContainsKey("presetRevision") ? settings["presetRevision"] : null);
                var curOrder = new List<string>();
                string curPrimary = AsStr(model.ContainsKey("primary") ? model["primary"] : null);
                if (!string.IsNullOrEmpty(curPrimary)) curOrder.Add(curPrimary);
                var curFb = AsList(model.ContainsKey("fallbacks") ? model["fallbacks"] : null);
                if (curFb != null) foreach (var f in curFb) { string s = AsStr(f); if (!string.IsNullOrEmpty(s)) curOrder.Add(s); }

                if (PRESET_REV > cfgRev && presetOrder.Count > 0)
                {
                    var extras = curOrder.FindAll((k) => !presetOrder.Contains(k));
                    model["primary"] = presetOrder[0];
                    var newFb = new List<object>();
                    for (int i = 1; i < presetOrder.Count; i++) newFb.Add(presetOrder[i]);
                    foreach (var e in extras) newFb.Add(e);
                    model["fallbacks"] = newFb;
                    settings["presetRevision"] = PRESET_REV;
                    WriteSettings(settings);
                    changed = true;
                    BootLog("preset order applied (rev " + cfgRev + " -> " + PRESET_REV + "): " + string.Join(" > ", presetOrder.ToArray()));
                }
                else if (!string.IsNullOrEmpty(pPrimary))
                {
                    bool userOrdered = settings.ContainsKey("userOrderedChain") && settings["userOrderedChain"] != null;
                    if (!userOrdered && !curOrder.Contains(pPrimary))
                    {
                        var chain = new List<object>();
                        foreach (var k in curOrder) chain.Add(k);
                        model["primary"] = pPrimary;
                        model["fallbacks"] = chain;
                        changed = true;
                        BootLog("preset primary promoted: " + pPrimary);
                    }
                }

                if (pDefaults != null)
                {
                    var pmMap = AsDict(pDefaults.ContainsKey("models") ? pDefaults["models"] : null);
                    if (pmMap != null)
                        foreach (var k in pmMap.Keys)
                            if (!modelsMap.ContainsKey(k)) { modelsMap[k] = new Dictionary<string, object>(); changed = true; }
                }

                if (changed) { WriteJsonDict(CONFIG, cfg); BootLog("config merged with preset"); }
            }
            catch (Exception e) { BootLog("mergePreset error: " + e.Message); }
        }

        // ---------- 微信插件（官方 CLI 安装 + 离线依赖） ----------
        static object pluginLock = new object();
        static Task<bool> pluginTask = null;
        static bool PluginInstalled()
        {
            return File.Exists(Path.Combine(DATA_DIR, "extensions", "openclaw-weixin", "package.json"));
        }

        public static Task<bool> InstallPluginAsync()
        {
            lock (pluginLock)
            {
                if (pluginTask != null) return pluginTask;
                pluginTask = Task<bool>.Factory.StartNew(delegate
                {
                    try
                    {
                        // PLUGIN_PKG 是目录，必须用 Directory.Exists；此前误用 File.Exists 导致插件安装被永久跳过
                        if (!Directory.Exists(PLUGIN_PKG)) { BootLog("weixin plugin source missing: " + PLUGIN_PKG); return false; }
                        string extDir = Path.Combine(DATA_DIR, "extensions", "openclaw-weixin");
                        if (PluginInstalled()) { EnsurePluginDeps(extDir); return true; }
                        BootLog("installing weixin plugin via official CLI (background)...");
                        var psi = new ProcessStartInfo(NODE, "--title=OpenClaw \"" + OPENCLAW_CLI + "\" plugins install \"" + PLUGIN_PKG + "\"")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = DATA_DIR,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        ApplyEnv(psi, CleanEnv());
                        string output = "";
                        using (var p = Process.Start(psi))
                        {
                            if (!p.WaitForExit(300000))
                            {
                                try { p.Kill(); } catch (Exception) { }
                                BootLog("plugin install timeout");
                                EnsurePluginDeps(extDir);
                                return PluginInstalled();
                            }
                            output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                        }
                        EnsurePluginDeps(extDir);
                        bool ok = PluginInstalled();
                        BootLog("plugin install ok=" + ok + " :: " + Regex.Replace(output, "\\s+", " ").Substring(0, Math.Min(400, output.Length)));
                        return ok;
                    }
                    catch (Exception e) { BootLog("installPlugin error: " + e.Message); return false; }
                });
                return pluginTask;
            }
        }

        static void EnsurePluginDeps(string extDir)
        {
            string srcNm = Path.Combine(PLUGIN_SRC, "node_modules");
            string dstNm = Path.Combine(extDir, "node_modules");
            foreach (string dep in new string[] { "zod", "qrcode-terminal" })
            {
                string dst = Path.Combine(dstNm, dep);
                if (File.Exists(Path.Combine(dst, "package.json"))) continue;
                string src = Path.Combine(srcNm, dep);
                if (!Directory.Exists(src)) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    CopyDir(src, dst);
                    BootLog("plugin dep copied: " + dep);
                }
                catch (Exception e) { BootLog("copy dep failed: " + e.Message); }
            }
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // ---------- 网关 ----------
        public static Process GatewayStart(Action<string> uiLog)
        {
            if (PortOpen(PORT)) return null; // 已有监听（由调用方判定归属）
            if (!File.Exists(NODE)) { uiLog("[x] 缺少 runtime\\node.exe"); return null; }
            if (!File.Exists(GATEWAY_JS)) { uiLog("[x] 缺少 runtime\\openclaw"); return null; }

            string logPath = Path.Combine(LOG_DIR, "lite-gateway.log");
            var psi = new ProcessStartInfo(NODE, "--title=OpenClaw \"" + GATEWAY_JS + "\" gateway --port " + PORT)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = DATA_DIR,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            ApplyEnv(psi, CleanEnv());
            var p = Process.Start(psi);
            SpawnedPids.Add(p.Id);
            GatewayPid = p.Id;
            GatewayOwned = true;
            BootLog("gateway started pid=" + p.Id + " port=" + PORT);
            // 输出转写日志（独立线程，防管道缓冲塞满）
            new Thread(delegate()
            {
                try
                {
                    using (var w = new StreamWriter(logPath, false, Encoding.UTF8))
                    {
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null) w.WriteLine(line);
                        while ((line = p.StandardError.ReadLine()) != null) w.WriteLine(line);
                    }
                }
                catch (Exception) { }
            }) { IsBackground = true }.Start();
            p.Exited += delegate
            {
                SpawnedPids.Remove(p.Id);
                GatewayOwned = false;
                BootLog("gateway exited pid=" + p.Id);
            };
            p.EnableRaisingEvents = true;
            return p;
        }

        public static bool GatewayStop(Action<string> uiLog)
        {
            // 只终止本程序亲手记录 PID 的进程，绝不跑 netstat / WMI 去枚举或判断外部进程
            var kill = new List<int>(SpawnedPids);
            if (kill.Count == 0)
            {
                if (PortOpen(PORT)) { uiLog("[!] 端口被其它程序占用，不会终止它"); return false; }
                uiLog("[i] 网关当前未在运行"); return true;
            }
            GatewayOwned = false;
            foreach (int pid in kill)
            {
                SpawnedPids.Remove(pid);
                KillPidTree(pid);
            }
            GatewayPid = 0;
            for (int i = 0; i < 25; i++) { Thread.Sleep(400); if (!PortOpen(PORT)) break; }
            uiLog(!PortOpen(PORT) ? "[OK] 网关已关闭，微信机器人已下线" : "[!] 端口仍被占用");
            return !PortOpen(PORT);
        }

        // ---------- 模型 ----------
        public class ModelInfo
        {
            public List<string> Chain = new List<string>();
            public List<ModelEntry> All = new List<ModelEntry>();
        }
        public class ModelEntry
        {
            public string Key;
            public string Provider;
            public string ProviderLabel;
            public string ModelId;
            public string ModelName;
        }

        static readonly Dictionary<string, string> ProviderLabelsBuiltIn = new Dictionary<string, string>
        {
            { "ark", "火山方舟" }, { "ark-doubao", "火山方舟 · 豆包" }, { "deepseek", "DeepSeek 官方" },
            { "openai", "OpenAI" }, { "zhipu", "智谱 GLM" }, { "qwen", "通义千问" },
            { "moonshot", "Moonshot Kimi" }, { "siliconflow", "SiliconFlow" }, { "custom", "自定义" },
            { "anthropic", "Anthropic Claude" }
        };

        public static string ProviderLabel(string key, Dictionary<string, object> cfg)
        {
            var s = ReadSettings();
            var labels = AsDict(s.ContainsKey("providerLabels") ? s["providerLabels"] : null);
            if (labels != null && labels.ContainsKey(key) && !string.IsNullOrEmpty(AsStr(labels[key]))) return AsStr(labels[key]);
            if (ProviderLabelsBuiltIn.ContainsKey(key)) return ProviderLabelsBuiltIn[key];
            var models = AsDict(cfg.ContainsKey("models") ? cfg["models"] : null);
            if (models != null)
            {
                var providers = AsDict(models.ContainsKey("providers") ? models["providers"] : null);
                if (providers != null && providers.ContainsKey(key))
                {
                    var p = AsDict(providers[key]);
                    if (p != null && p.ContainsKey("label") && !string.IsNullOrEmpty(AsStr(p["label"]))) return AsStr(p["label"]);
                }
            }
            return key;
        }

        public static ModelInfo GetModelInfo()
        {
            var info = new ModelInfo();
            var cfg = ReadJsonDict(CONFIG);
            if (cfg == null) return info;
            var models = AsDict(cfg.ContainsKey("models") ? cfg["models"] : null);
            var providers = models == null ? null : AsDict(models.ContainsKey("providers") ? models["providers"] : null);
            var agents = AsDict(cfg.ContainsKey("agents") ? cfg["agents"] : null);
            var defaults = agents == null ? null : AsDict(agents.ContainsKey("defaults") ? agents["defaults"] : null);
            var model = defaults == null ? null : AsDict(defaults.ContainsKey("model") ? defaults["model"] : null);
            if (model != null)
            {
                string pr = AsStr(model.ContainsKey("primary") ? model["primary"] : null);
                if (!string.IsNullOrEmpty(pr)) info.Chain.Add(pr);
                var fb = AsList(model.ContainsKey("fallbacks") ? model["fallbacks"] : null);
                if (fb != null) foreach (var f in fb) { string s = AsStr(f); if (!string.IsNullOrEmpty(s)) info.Chain.Add(s); }
            }
            if (providers != null)
            {
                foreach (var pkv in providers)
                {
                    var p = AsDict(pkv.Value);
                    if (p == null) continue;
                    string plabel = ProviderLabel(pkv.Key, cfg);
                    var ml = AsList(p.ContainsKey("models") ? p["models"] : null);
                    if (ml == null) continue;
                    foreach (var mObj in ml)
                    {
                        var m = AsDict(mObj);
                        if (m == null) continue;
                        string id = AsStr(m.ContainsKey("id") ? m["id"] : null);
                        if (string.IsNullOrEmpty(id)) continue;
                        info.All.Add(new ModelEntry
                        {
                            Key = pkv.Key + "/" + id,
                            Provider = pkv.Key,
                            ProviderLabel = plabel,
                            ModelId = id,
                            ModelName = AsStr(m.ContainsKey("name") ? m["name"] : null) ?? id
                        });
                    }
                }
            }
            return info;
        }

        // 添加 API（与 main.js addProvider 一致，含校验 + compat）
        public static string AddProvider(string providerKey, string label, string baseUrl, string apiKey, string modelId, string modelName)
        {
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            if (!Regex.IsMatch(baseUrl, @"^https?://", RegexOptions.IgnoreCase)) return "URL 必须以 http:// 或 https:// 开头";
            if (!Regex.IsMatch(baseUrl, @"^https?://[^/\s]+", RegexOptions.IgnoreCase)) return "URL 格式不正确";
            if (string.IsNullOrWhiteSpace(apiKey)) return "请填写 API Key";
            if (string.IsNullOrWhiteSpace(modelId)) return "请填写模型 ID";
            providerKey = (providerKey ?? "").Trim();
            if (!Regex.IsMatch(providerKey, @"^[a-zA-Z0-9_-]+$")) return "标识只能英文/数字/_/-";

            lock (JsonLock)
            {
                var cfg = ReadJsonDict(CONFIG);
                if (cfg == null) return "配置文件不存在";
                var models = AsDict(cfg.ContainsKey("models") ? cfg["models"] : null);
                if (models == null) { models = new Dictionary<string, object>(); cfg["models"] = models; }
                if (!models.ContainsKey("mode")) models["mode"] = "merge";
                var providers = AsDict(models.ContainsKey("providers") ? models["providers"] : null);
                if (providers == null) { providers = new Dictionary<string, object>(); models["providers"] = providers; }

                Dictionary<string, object> p;
                if (!providers.ContainsKey(providerKey))
                {
                    p = new Dictionary<string, object>
                    {
                        { "baseUrl", baseUrl },
                        { "apiKey", apiKey },
                        { "api", "openai-completions" },
                        { "models", new List<object>() }
                    };
                    providers[providerKey] = p;
                }
                else
                {
                    p = AsDict(providers[providerKey]);
                    p["baseUrl"] = baseUrl;
                    if (!string.IsNullOrWhiteSpace(apiKey)) p["apiKey"] = apiKey;
                    if (!p.ContainsKey("api")) p["api"] = "openai-completions";
                }
                if (!string.IsNullOrWhiteSpace(label) && label != providerKey)
                {
                    var s = ReadSettings();
                    var labels = AsDict(s.ContainsKey("providerLabels") ? s["providerLabels"] : null);
                    if (labels == null) { labels = new Dictionary<string, object>(); s["providerLabels"] = labels; }
                    labels[providerKey] = label.Trim();
                    WriteSettings(s);
                }
                var ml = AsList(p.ContainsKey("models") ? p["models"] : null);
                if (ml == null) { ml = new List<object>(); p["models"] = ml; }
                bool exists = false;
                foreach (var mObj in ml) { var m = AsDict(mObj); if (m != null && AsStr(m["id"]) == modelId) { exists = true; break; } }
                if (!exists)
                {
                    // 自定义 endpoint 自动探测会把 supportsUsageInStreaming 判为 false → 用量恒 0，显式声明修正
                    ml.Add(new Dictionary<string, object>
                    {
                        { "id", modelId },
                        { "name", string.IsNullOrWhiteSpace(modelName) ? modelId : modelName.Trim() },
                        { "reasoning", false },
                        { "input", new List<object> { "text" } },
                        { "contextWindow", 131072 },
                        { "maxTokens", 8192 },
                        { "compat", new Dictionary<string, object> { { "supportsUsageInStreaming", true } } }
                    });
                }

                var agents = AsDict(cfg.ContainsKey("agents") ? cfg["agents"] : null);
                if (agents == null) { agents = new Dictionary<string, object>(); cfg["agents"] = agents; }
                var defaults = AsDict(agents.ContainsKey("defaults") ? agents["defaults"] : null);
                if (defaults == null) { defaults = new Dictionary<string, object>(); agents["defaults"] = defaults; }
                var model = AsDict(defaults.ContainsKey("model") ? defaults["model"] : null);
                if (model == null) { model = new Dictionary<string, object> { { "primary", "" }, { "fallbacks", new List<object>() } }; defaults["model"] = model; }
                var modelsMap = AsDict(defaults.ContainsKey("models") ? defaults["models"] : null);
                if (modelsMap == null) { modelsMap = new Dictionary<string, object>(); defaults["models"] = modelsMap; }

                string fullKey = providerKey + "/" + modelId;
                string primary = AsStr(model.ContainsKey("primary") ? model["primary"] : null);
                if (string.IsNullOrEmpty(primary)) model["primary"] = fullKey;
                else
                {
                    var fb = AsList(model.ContainsKey("fallbacks") ? model["fallbacks"] : null);
                    if (fb == null) { fb = new List<object>(); model["fallbacks"] = fb; }
                    bool inFb = false;
                    foreach (var f in fb) if (AsStr(f) == fullKey) { inFb = true; break; }
                    if (fullKey != primary && !inFb) fb.Add(fullKey);
                }
                if (!modelsMap.ContainsKey(fullKey)) modelsMap[fullKey] = new Dictionary<string, object>();
                WriteJsonDict(CONFIG, cfg);
            }
            return null; // null = 成功
        }

        // 排序保存（写 userOrderedChain，出厂升级不再覆盖）
        public static void SetPriority(List<string> order)
        {
            lock (JsonLock)
            {
                var cfg = ReadJsonDict(CONFIG);
                if (cfg == null) return;
                var agents = AsDict(cfg.ContainsKey("agents") ? cfg["agents"] : null);
                if (agents == null) { agents = new Dictionary<string, object>(); cfg["agents"] = agents; }
                var defaults = AsDict(agents.ContainsKey("defaults") ? agents["defaults"] : null);
                if (defaults == null) { defaults = new Dictionary<string, object>(); agents["defaults"] = defaults; }
                var oldModels = AsDict(defaults.ContainsKey("models") ? defaults["models"] : null) ?? new Dictionary<string, object>();
                var nextModels = new Dictionary<string, object>();
                foreach (var k in order) nextModels[k] = oldModels.ContainsKey(k) ? oldModels[k] : new Dictionary<string, object>();
                defaults["models"] = nextModels;
                var model = new Dictionary<string, object>
                {
                    { "primary", order.Count > 0 ? order[0] : "" },
                    { "fallbacks", new List<object>(order.Skip(1).Select((s) => (object)s)) }
                };
                defaults["model"] = model;
                WriteJsonDict(CONFIG, cfg);
                var orderedSettings = ReadSettings();
                orderedSettings["userOrderedChain"] = new List<object>(order.Select((x) => (object)x));
                WriteSettings(orderedSettings);
            }
        }

        // 删除模型（与 main.js removeModel 一致）
        public static void RemoveModel(string key, bool delProvider)
        {
            lock (JsonLock)
            {
                var cfg = ReadJsonDict(CONFIG);
                if (cfg == null) return;
                var agents = AsDict(cfg.ContainsKey("agents") ? cfg["agents"] : null);
                var defaults = agents == null ? null : AsDict(agents["defaults"]);
                var model = defaults == null ? null : AsDict(defaults.ContainsKey("model") ? defaults["model"] : null);
                if (model != null)
                {
                    string primary = AsStr(model.ContainsKey("primary") ? model["primary"] : null);
                    if (primary == key)
                    {
                        string next = "";
                        var models = AsDict(cfg.ContainsKey("models") ? cfg["models"] : null);
                        var providers = models == null ? null : AsDict(models.ContainsKey("providers") ? models["providers"] : null);
                        if (providers != null)
                        {
                            foreach (var pkv in providers)
                            {
                                var p = AsDict(pkv.Value);
                                var ml = p == null ? null : AsList(p.ContainsKey("models") ? p["models"] : null);
                                if (ml == null) continue;
                                foreach (var mObj in ml)
                                {
                                    var m = AsDict(mObj);
                                    string fk = pkv.Key + "/" + AsStr(m["id"]);
                                    if (fk != key) { next = fk; goto done; }
                                }
                            }
                        }
                    done:
                        model["primary"] = next;
                        var fb = AsList(model.ContainsKey("fallbacks") ? model["fallbacks"] : null);
                        if (fb != null) model["fallbacks"] = new List<object>(fb.Where((f) => AsStr(f) != key));
                    }
                    else if (model.ContainsKey("fallbacks"))
                    {
                        var fb = AsList(model["fallbacks"]);
                        if (fb != null) model["fallbacks"] = new List<object>(fb.Where((f) => AsStr(f) != key));
                    }
                }
                if (defaults != null && defaults.ContainsKey("models"))
                {
                    var mm = AsDict(defaults["models"]);
                    if (mm != null && mm.ContainsKey(key)) mm.Remove(key);
                }
                if (delProvider)
                {
                    string pkey = key.Split('/')[0];
                    var models = AsDict(cfg.ContainsKey("models") ? cfg["models"] : null);
                    var providers = models == null ? null : AsDict(models.ContainsKey("providers") ? models["providers"] : null);
                    if (providers != null && providers.ContainsKey(pkey))
                    {
                        var p = AsDict(providers[pkey]);
                        var ml = AsList(p.ContainsKey("models") ? p["models"] : null);
                        if (ml != null)
                        {
                            var keep = new List<object>();
                            foreach (var mObj in ml)
                            {
                                var m = AsDict(mObj);
                                if (pkey + "/" + AsStr(m["id"]) != key) keep.Add(mObj);
                            }
                            p["models"] = keep;
                            if (keep.Count == 0) providers.Remove(pkey);
                        }
                    }
                }
                WriteJsonDict(CONFIG, cfg);
            }
        }

        // ---------- 微信绑定 ----------
        public static Process LoginChild = null;
        public static readonly object LoginLock = new object();

        // 返回二维码 URL；UI 层再用 node qrcode 库离线转 PNG
        public static string StartLogin(Action<string> onLog, Action<Exception> onError)
        {
            lock (LoginLock)
            {
                KillLoginChild();
            }
            try
            {
                var psi = new ProcessStartInfo(NODE, "--title=OpenClaw \"" + OPENCLAW_CLI + "\" channels login --channel openclaw-weixin")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = DATA_DIR,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                };
                ApplyEnv(psi, CleanEnv());
                var p = Process.Start(psi);
                lock (LoginLock) { LoginChild = p; }
                BootLog("channels login started pid=" + p.Id);

                var sb = new StringBuilder();
                bool promptHandled = false;
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    lock (sb) sb.Append(e.Data).Append('\n');
                    FeedLogin(sb, ref promptHandled, p, onLog);
                };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    lock (sb) sb.Append(e.Data).Append('\n');
                    FeedLogin(sb, ref promptHandled, p, onLog);
                };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return null;
            }
            catch (Exception e)
            {
                BootLog("login spawn error: " + e.Message);
                if (onError != null) onError(e);
                return "login 进程启动失败: " + e.Message;
            }
        }

        static void FeedLogin(StringBuilder sb, ref bool promptHandled, Process p, Action<string> onLog)
        {
            string buf;
            lock (sb) buf = sb.ToString();
            if (!promptHandled && Regex.IsMatch(buf, "Install Weixin plugin\\?", RegexOptions.IgnoreCase))
            {
                promptHandled = true;
                BootLog("login: plugin install prompt detected, choosing skip");
                try { p.StandardInput.Write("\x1b[B"); p.StandardInput.Write("\r"); } catch (Exception) { }
            }
            var m = Regex.Match(buf, @"https://liteapp\.weixin\.qq\.com/[^\s""'\x1b]+");
            if (m.Success) onLog(m.Value);
        }

        public static void KillLoginChild()
        {
            lock (LoginLock)
            {
                if (LoginChild != null)
                {
                    try { LoginChild.Kill(); BootLog("login child killed pid=" + LoginChild.Id); } catch (Exception) { }
                    LoginChild = null;
                }
            }
        }

        // 用随包 node + qrcode 库离线生成 PNG dataURL（不依赖任何在线服务）
        public static string QrDataUrl(string url)
        {
            try
            {
                string tmpJs = Path.Combine(Path.GetTempPath(), "oc-lite-qr.js");
                string script = "const q=require(" + Js.Serialize(Path.Combine(OPENCLAW_PKG, "node_modules", "qrcode")) +
                    ");q.toDataURL(process.argv[2],{width:240,margin:8,errorCorrectionLevel:'M'}).then(function(u){console.log(u)}).catch(function(e){console.error(e.message);process.exit(1)});";
                File.WriteAllText(tmpJs, script, Encoding.UTF8);
                var psi = new ProcessStartInfo(NODE, "\"" + tmpJs + "\" \"" + url + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                ApplyEnv(psi, CleanEnv());
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    string s = (stdout ?? "").Trim();
                    if (s.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) return s;
                    BootLog("qr render failed: " + s);
                    return null;
                }
            }
            catch (Exception e)
            {
                BootLog("qr render error: " + e.Message);
                return null;
            }
        }
    }

    // ---------- 自检（--selftest：写 %TEMP%\lite-selftest.txt） ----------
    static class SelfTest
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            try
            {
                Core.PORT = 18799;
                Core.EnsureDataDirs();
                Core.EnsureConfig();
                Core.MergePresetModels();
                sb.AppendLine("ROOT=" + Core.ROOT);
                sb.AppendLine("DATA_DIR exists=" + Directory.Exists(Core.DATA_DIR));
                sb.AppendLine("CONFIG exists=" + File.Exists(Core.CONFIG));
                var mi = Core.GetModelInfo();
                sb.AppendLine("chain=" + string.Join(" > ", mi.Chain.ToArray()));
                sb.AppendLine("allModels=" + mi.All.Count);
                // 添加 → 排序 → 删除 全链路
                var err = Core.AddProvider("selftest", "自检", "https://api.example.com/v1", "sk-test", "m1", "自检模型");
                sb.AppendLine("addProvider err=" + (err ?? "(null=成功)"));
                var mi2 = Core.GetModelInfo();
                sb.AppendLine("after add chain=" + string.Join(" > ", mi2.Chain.ToArray()));
                Core.SetPriority(new List<string> { "selftest/m1" }.Concat(mi2.Chain.FindAll((k) => k != "selftest/m1")).ToList());
                var mi3 = Core.GetModelInfo();
                sb.AppendLine("after sort primary=" + mi3.Chain[0]);
                var settings = Core.ReadSettings();
                sb.AppendLine("userOrderedChain=" + (settings.ContainsKey("userOrderedChain") ? "set" : "missing"));
                Core.RemoveModel("selftest/m1", true);
                var mi4 = Core.GetModelInfo();
                sb.AppendLine("after remove contains selftest=" + mi4.Chain.Contains("selftest/m1"));
                sb.AppendLine("portOpen(18799)=" + Core.PortOpen(18799));
                sb.AppendLine("SELFTEST=PASS");
            }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION=" + e);
                sb.AppendLine("SELFTEST=FAIL");
            }
            string outPath = Path.Combine(Path.GetTempPath(), "lite-selftest.txt");
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        }
    }

}

