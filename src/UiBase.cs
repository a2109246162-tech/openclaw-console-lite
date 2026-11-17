// OpenClaw 控制台 Lite —— Metro / Modern UI 基础层（Windows 8 ~ Windows 10 早期风格）
// 纯色标题带、扁平直角控件、Segoe UI Light 大标题、细分隔线、左侧导航。
// 所有像素经 M.S() 按 DPI 缩放；字体用 pt 由系统缩放，二者倍数一致 => 高 DPI 不重叠。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenClawLite
{
    // ---------- DPI 度量 ----------
    static class M
    {
        public static float K = 1f;

        public static void Init(Form f)
        {
            try { using (Graphics g = f.CreateGraphics()) K = g.DpiX / 96f; }
            catch (Exception) { K = 1f; }
            if (K < 1f) K = 1f;
            if (K > 3f) K = 3f;
        }
        public static int S(int v) { return (int)Math.Round(v * K); }
        public static Size SZ(int w, int h) { return new Size(S(w), S(h)); }
        public static Point PT(int x, int y) { return new Point(S(x), S(y)); }
        public static Padding PD(int all) { return new Padding(S(all)); }
        public static Padding PD(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }
        // 赋给控件 Font 属性：每次新建（控件可能自行释放字体）
        public static Font F(float pt) { return new Font("Microsoft YaHei UI", pt, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font FB(float pt) { return new Font("Microsoft YaHei UI", pt, FontStyle.Bold, GraphicsUnit.Point); }
        public static Font FL(float pt) { return new Font("Segoe UI Light", pt, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font Mono(float pt) { return new Font("Consolas", pt, FontStyle.Regular, GraphicsUnit.Point); }

        // 绘制路径用共享缓存：旧版每次 OnPaint 都 new Font，GDI 句柄与内存会持续累积
        static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        static readonly object FontLock = new object();
        public static Font FC(float pt) { return Cached("Microsoft YaHei UI", pt, FontStyle.Regular); }
        public static Font FBC(float pt) { return Cached("Microsoft YaHei UI", pt, FontStyle.Bold); }
        public static Font FLC(float pt) { return Cached("Segoe UI Light", pt, FontStyle.Regular); }
        public static Font MonoC(float pt) { return Cached("Consolas", pt, FontStyle.Regular); }
        static Font Cached(string family, float pt, FontStyle style)
        {
            string key = family + "|" + pt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
            lock (FontLock)
            {
                Font f;
                if (!FontCache.TryGetValue(key, out f))
                {
                    f = new Font(family, pt, style, GraphicsUnit.Point);
                    FontCache[key] = f;
                }
                return f;
            }
        }
    }

    // ---------- Metro 主题 ----------
    static class T
    {
        public const string SYSTEM = "跟随系统";
        public const string LIGHT = "浅色";
        public const string DARK = "深色";
        public const string BLACK = "纯黑";
        public static readonly string[] Modes = new string[] { SYSTEM, LIGHT, DARK, BLACK };

        public static string Mode = SYSTEM;
        public static bool Dark = false;
        public static bool PureBlack = false;

        public static Color Bg, Card, Card2, Line, Txt, Sub, Acc, AccTxt, Ok, Bad;
        public static Color LogBg, LogTxt, Sel, Chip;
        public static Color Band, BandTxt, Nav, NavSel, NavTxt, TileA, TileB, TileC, TileTxt;

        static Color C(string hex) { return ColorTranslator.FromHtml(hex); }

        public static void Refresh()
        {
            bool sysDark = Features.SystemIsDark();
            Dark = (Mode == SYSTEM) ? sysDark : (Mode == DARK || Mode == BLACK);
            PureBlack = (Mode == BLACK);

            if (!Dark)
            {
                Bg = C("#FFFFFF"); Card = C("#FFFFFF"); Card2 = C("#F6F6F6"); Line = C("#E0E0E0");
                Txt = C("#1A1A1A"); Sub = C("#767676"); Acc = C("#0078D7"); AccTxt = C("#FFFFFF");
                Ok = C("#107C10"); Bad = C("#C4314B"); LogBg = C("#FAFAFA"); LogTxt = C("#454545");
                Sel = C("#E8F2FC"); Chip = C("#EDF4FB");
                Band = C("#0078D7"); BandTxt = C("#FFFFFF");
                Nav = C("#F3F3F3"); NavSel = C("#FFFFFF"); NavTxt = C("#2B2B2B");
                TileA = C("#0078D7"); TileB = C("#0099A8"); TileC = C("#4B7A3C"); TileTxt = C("#FFFFFF");
            }
            else if (!PureBlack)
            {
                Bg = C("#1B1B1B"); Card = C("#1B1B1B"); Card2 = C("#252525"); Line = C("#343434");
                Txt = C("#F2F2F2"); Sub = C("#A4A4A4"); Acc = C("#2B9BEF"); AccTxt = C("#0E1419");
                Ok = C("#4CC26A"); Bad = C("#FF7B7B"); LogBg = C("#151515"); LogTxt = C("#C4C4C4");
                Sel = C("#173B57"); Chip = C("#1D3A50");
                Band = C("#005A9E"); BandTxt = C("#FFFFFF");
                Nav = C("#232323"); NavSel = C("#2E2E2E"); NavTxt = C("#E6E6E6");
                TileA = C("#005A9E"); TileB = C("#00757F"); TileC = C("#3B632D"); TileTxt = C("#FFFFFF");
            }
            else
            {
                Bg = C("#000000"); Card = C("#000000"); Card2 = C("#0C0C0C"); Line = C("#202020");
                Txt = C("#EEEEEE"); Sub = C("#8C8C8C"); Acc = C("#1BA3FF"); AccTxt = C("#000000");
                Ok = C("#40C463"); Bad = C("#FF6A6A"); LogBg = C("#050505"); LogTxt = C("#B4B4B4");
                Sel = C("#10304A"); Chip = C("#12263A");
                Band = C("#004C87"); BandTxt = C("#FFFFFF");
                Nav = C("#0A0A0A"); NavSel = C("#151515"); NavTxt = C("#E8E8E8");
                TileA = C("#004C87"); TileB = C("#00666E"); TileC = C("#2E5222"); TileTxt = C("#FFFFFF");
            }
        }

        public static Color ParentBg(Control c)
        {
            Control p = c.Parent;
            while (p != null)
            {
                Color bc = p.BackColor;
                if (bc.A == 255) return bc;
                p = p.Parent;
            }
            return Bg;
        }

        public static void Apply(Control c)
        {
            if (c == null) return;
            if (c is TitleBand)
            {
                c.BackColor = Band; c.Invalidate();
            }
            else if (c is TilePanel) { c.Invalidate(); }
            else if (c is ModernButton || c is ModernToggle || c is ChainList || c is ModernInput)
            {
                c.Invalidate();
            }
            else if (c is CardPanel) { c.BackColor = Card; c.Invalidate(); }
            else if (c is TextBox)
            {
                TextBox tb = (TextBox)c;
                tb.BackColor = tb.Multiline ? LogBg : Card2;
                tb.ForeColor = tb.Multiline ? LogTxt : Txt;
            }
            else if (c is PictureBox) { c.BackColor = Color.White; }
            else if (c is Label)
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = (c.Tag != null && c.Tag.ToString() == "sub") ? Sub : Txt;
            }
            else if (c is Form) { c.BackColor = Bg; }
            else { c.BackColor = Color.Transparent; }
            foreach (Control ch in c.Controls) Apply(ch);
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);
        public static Icon MakeIcon()
        {
            try
            {
                using (Bitmap b = new Bitmap(64, 64))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Acc);
                        using (Pen p = new Pen(BandTxt, 5f)) g.DrawRectangle(p, 13, 13, 38, 38);
                        using (SolidBrush br = new SolidBrush(BandTxt)) g.FillRectangle(br, 27, 27, 10, 10);
                    }
                    IntPtr h = b.GetHicon();
                    using (Icon tmp = Icon.FromHandle(h))
                    {
                        Icon copy = (Icon)tmp.Clone();
                        DestroyIcon(h);
                        return copy;
                    }
                }
            }
            catch (Exception) { return SystemIcons.Application; }
        }
    }

    // ---------- 附加逻辑 ----------
    static class Features
    {
        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string APPROVED_KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string RUN_VALUE = "OpenClaw 控制台 Lite";
        const string AUMID = "OpenClawConsoleLite_9kb6m44qxtkf2!OpenClawLite";
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint esFlags);

        static bool powerOn = false;

        public static bool SystemIsDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("AppsUseLightTheme");
                    return v != null && Convert.ToInt32(v) == 0;
                }
            }
            catch (Exception) { return false; }
        }

        static Dictionary<string, object> Settings()
        {
            Dictionary<string, object> s = Core.ReadSettings();
            return s ?? new Dictionary<string, object>();
        }
        static void Save(Dictionary<string, object> s) { Core.WriteSettings(s); }

        // 清理早期版本「直接开机自启」写入的启动项（只删本应用自己的键值）
        public static void RemoveLegacyAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (k != null) k.DeleteValue(RUN_VALUE, false);
                }
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(APPROVED_KEY, true))
                {
                    if (a != null) a.DeleteValue(RUN_VALUE, false);
                }
            }
            catch (Exception) { }
        }

        // 开机自启由 Windows 系统设置授权：我们只负责「把自己登记进启动列表（默认禁用）」并跳转过去，
        // 是否打勾完全由用户在系统设置 / 任务管理器里决定，不越权替 UWP 申请权限。
        public static bool StartupRegistered()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                {
                    object v = k == null ? null : k.GetValue(RUN_VALUE);
                    return v != null && v.ToString().IndexOf("AppsFolder", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception) { return false; }
        }
        public static bool StartupEnabled()
        {
            if (!StartupRegistered()) return false;
            try
            {
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(APPROVED_KEY))
                {
                    if (a == null) return true;
                    byte[] v = a.GetValue(RUN_VALUE) as byte[];
                    if (v == null || v.Length == 0) return true;
                    return v[0] != 3; // 03 00.. = 任务管理器里的「已禁用」
                }
            }
            catch (Exception) { return false; }
        }
        public static bool RegisterStartup(bool on)
        {
            try
            {
                if (on)
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                        k.SetValue(RUN_VALUE, "explorer.exe shell:AppsFolder\\" + AUMID, RegistryValueKind.String);
                    using (RegistryKey a = Registry.CurrentUser.OpenSubKey(APPROVED_KEY, true))
                        if (a != null) a.SetValue(RUN_VALUE, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                }
                else
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                        if (k != null) k.DeleteValue(RUN_VALUE, false);
                    using (RegistryKey a = Registry.CurrentUser.OpenSubKey(APPROVED_KEY, true))
                        if (a != null) a.DeleteValue(RUN_VALUE, false);
                }
                return true;
            }
            catch (Exception) { return false; }
        }
        public static void OpenStartupSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:startupapps") { UseShellExecute = true });
            }
            catch (Exception)
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:appsfeatures") { UseShellExecute = true }); }
                catch (Exception) { }
            }
        }

        public static void LoadPrefs()
        {
            try
            {
                Dictionary<string, object> s = Settings();
                string m = Core.AsStr(s.ContainsKey("themeMode") ? s["themeMode"] : null);
                T.Mode = (m == T.LIGHT || m == T.DARK || m == T.BLACK) ? m : T.SYSTEM;
                powerOn = s.ContainsKey("autoPowerSave") ? Core.AsBool(s["autoPowerSave"]) : true;
            }
            catch (Exception) { T.Mode = T.SYSTEM; powerOn = true; }
            // 老版本会自作主张写 Run 键，升级到「系统设置授权」模式时清掉一次
            try
            {
                Dictionary<string, object> s = Settings();
                if (!Core.AsBool(s.ContainsKey("startupManaged") ? s["startupManaged"] : false))
                {
                    RemoveLegacyAutoStart();
                    s["startupManaged"] = true;
                    Save(s);
                }
            }
            catch (Exception) { }
            T.Refresh();
        }
        public static void SaveTheme(string mode)
        {
            T.Mode = mode; T.Refresh();
            try { Dictionary<string, object> s = Settings(); s["themeMode"] = mode; Save(s); } catch (Exception) { }
        }

        public static bool PowerSaveEnabled() { return powerOn; }
        public static void SetPowerSave(bool on)
        {
            try
            {
                SetThreadExecutionState(on ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) : ES_CONTINUOUS);
                powerOn = on;
                Dictionary<string, object> s = Settings(); s["autoPowerSave"] = on; Save(s);
            }
            catch (Exception) { }
        }
        public static void ApplyPowerOnBoot() { if (powerOn) SetPowerSave(true); }

        public static string ActiveModel()
        {
            try
            {
                Dictionary<string, object> cfg = Core.ReadJsonDict(Core.CONFIG);
                if (cfg == null) return "—";
                Dictionary<string, object> agents = Core.AsDict(cfg.ContainsKey("agents") ? cfg["agents"] : null);
                Dictionary<string, object> defs = Core.AsDict(agents.ContainsKey("defaults") ? agents["defaults"] : null);
                Dictionary<string, object> model = Core.AsDict(defs.ContainsKey("model") ? defs["model"] : null);
                string p = Core.AsStr(model.ContainsKey("primary") ? model["primary"] : null);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception) { }
            return "—";
        }

        public class UsageRow { public string Model = ""; public long Total = 0; public int Count = 0; }

        public static long GrandTotal;
        static DateTime usageAt = DateTime.MinValue;
        static List<UsageRow> usageCache = new List<UsageRow>();
        static long usageTotalCache = 0;

        public static List<UsageRow> TodayUsage()
        {
            if (DateTime.Now - usageAt < TimeSpan.FromSeconds(60))
            {
                GrandTotal = usageTotalCache;
                return usageCache;
            }
            Dictionary<string, UsageRow> map = new Dictionary<string, UsageRow>();
            long grand = 0;
            try
            {
                string dir = Path.Combine(Core.DATA_DIR, "agents", "main", "sessions");
                if (Directory.Exists(dir))
                {
                    string today = DateTime.Now.ToString("yyyy-MM-dd");
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    foreach (string f in Directory.GetFiles(dir, "*.trajectory.jsonl"))
                    {
                        using (StreamReader sr = new StreamReader(f, Encoding.UTF8))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.Length < 2 || line.IndexOf("model.completed", StringComparison.Ordinal) < 0) continue;
                                Dictionary<string, object> e;
                                try { e = js.Deserialize<Dictionary<string, object>>(line); } catch (Exception) { continue; }
                                if (e == null || Core.AsStr(e.ContainsKey("type") ? e["type"] : null) != "model.completed") continue;
                                object tsObj = e.ContainsKey("ts") ? e["ts"] : null;
                                DateTime ts;
                                if (tsObj is DateTime) ts = (DateTime)tsObj;
                                else
                                {
                                    DateTimeOffset dto;
                                    if (!DateTimeOffset.TryParse(Core.AsStr(tsObj), out dto)) continue;
                                    ts = dto.LocalDateTime;
                                }
                                if (ts.ToString("yyyy-MM-dd") != today) continue;
                                Dictionary<string, object> data = Core.AsDict(e.ContainsKey("data") ? e["data"] : null);
                                Dictionary<string, object> u = Core.AsDict(data.ContainsKey("usage") ? data["usage"] : null);
                                long total = 0;
                                try { total = Convert.ToInt64(u.ContainsKey("total") ? u["total"] : (u.ContainsKey("totalTokens") ? u["totalTokens"] : 0)); }
                                catch (Exception) { }
                                if (total <= 0)
                                {
                                    try { total = Convert.ToInt64(u.ContainsKey("input") ? u["input"] : 0) + Convert.ToInt64(u.ContainsKey("output") ? u["output"] : 0); }
                                    catch (Exception) { }
                                }
                                string model = Core.AsStr(e.ContainsKey("modelId") ? e["modelId"] : null);
                                if (string.IsNullOrEmpty(model)) model = "unknown";
                                UsageRow row;
                                if (!map.TryGetValue(model, out row)) { row = new UsageRow { Model = model }; map[model] = row; }
                                row.Total += total; row.Count += 1;
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            List<UsageRow> list = map.Values.ToList();
            list.Sort(delegate(UsageRow a, UsageRow b) { return b.Total.CompareTo(a.Total); });
            foreach (UsageRow r in list) grand += r.Total;
            usageCache = list; usageTotalCache = grand; usageAt = DateTime.Now;
            GrandTotal = grand;
            return list;
        }
    }

    // ---------- 纯色标题带（Metro 顶栏，兼作窗口拖动区） ----------
    class TitleBand : Panel
    {
        public string Caption = "OpenClaw 控制台 Lite";
        public string SubCaption = "轻量控制台";

        // 纯托管拖动：标题带自身就是鼠标命中的窗口，MouseDown/Move/Up 一定会到，
        // 不依赖 WM_NCHITTEST（子控件盖住标题带时，顶层窗体的命中测试收不到消息）。
        bool dragging = false;
        Point dragAt;

        public TitleBand()
        {
            // 控件自带双缓冲：只重绘自己，不牵动整窗，hover / 点击才不会卡
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragAt = e.Location;
                Capture = true;
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                Form f = FindForm();
                if (f != null)
                {
                    Point p = f.Location;
                    p.Offset(e.X - dragAt.X, e.Y - dragAt.Y);
                    // 别让用户把窗口整个拖出屏幕找不回来
                    Rectangle wa = Screen.FromControl(f).WorkingArea;
                    p.X = Math.Max(wa.Left - f.Width + M.S(100), Math.Min(p.X, wa.Right - M.S(100)));
                    p.Y = Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - M.S(60)));
                    f.Location = p;
                }
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            if (Capture) Capture = false;
            base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            dragging = false;
            base.OnMouseCaptureChanged(e);
        }
        // 自检：模拟一次拖动，确认窗口坐标真的变了
        public bool TestDrag(int dx, int dy)
        {
            Form f = FindForm();
            if (f == null) return false;
            Point before = f.Location;
            dragging = true; dragAt = Point.Empty;
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 1, dx, dy, 0));
            dragging = false;
            return f.Location != before;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(T.Band);
            int pad = M.S(22);
            TextRenderer.DrawText(g, Caption, M.FLC(20f), new Rectangle(pad, M.S(4), Width - pad - M.S(240), M.S(32)),
                T.BandTxt, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, SubCaption, M.FC(8.5f), new Rectangle(pad, M.S(34), Width - pad - M.S(240), M.S(18)),
                Color.FromArgb(195, T.BandTxt), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using (SolidBrush bar = new SolidBrush(T.TileB)) g.FillRectangle(bar, 0, Height - M.S(3), Width, M.S(3));
        }
    }

    // ---------- Metro 磁贴 ----------
    class TilePanel : Panel
    {
        public Color TileColor = Color.Empty;
        public string Head = "";
        public string Value = "";
        public string Foot = "";
        public TilePanel()
        {
            // 控件自带双缓冲：只重绘自己，不牵动整窗，hover / 点击才不会卡
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color fill = TileColor == Color.Empty ? T.TileA : TileColor;
            g.Clear(fill);
            int pad = M.S(14);
            TextRenderer.DrawText(g, Head, M.FC(9f), new Rectangle(pad, M.S(8), Width - pad * 2, M.S(20)),
                Color.FromArgb(215, T.TileTxt), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Value, M.FLC(22f), new Rectangle(pad, M.S(30), Width - pad * 2, M.S(40)),
                T.TileTxt, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Foot, M.FC(8.5f), new Rectangle(pad, Height - M.S(28), Width - pad * 2, M.S(20)),
                Color.FromArgb(205, T.TileTxt), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // ---------- 分区面板：直角、纯色、只有细分隔线 ----------
    class CardPanel : Panel
    {
        public bool BottomLine = false;
        public bool LeftLine = false;
        public CardPanel()
        {
            // 控件自带双缓冲：只重绘自己，不牵动整窗，hover / 点击才不会卡
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(T.Card);
            if (BottomLine)
            {
                using (Pen p = new Pen(T.Line, Math.Max(1f, M.K)))
                    g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
            if (LeftLine)
            {
                using (Pen p = new Pen(T.Line, Math.Max(1f, M.K)))
                    g.DrawLine(p, 0, 0, 0, Height);
            }
        }
    }

    // ---------- 扁平直角按钮 ----------
    class ModernButton : Button
    {
        public const int PRIMARY = 0, GHOST = 1, DANGER = 2, SUBTLE = 3, NAV = 4, BAND = 5;
        public int Kind = GHOST;
        public bool AlignLeft = false;
        public bool Active = false;
        bool hover = false, down = false;

        public ModernButton(string text, int kind)
        {
            Text = text; Kind = kind;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            // 控件自带双缓冲：只重绘自己，不牵动整窗，hover / 点击才不会卡
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = M.F(10f);
            Height = M.S(38);
            TabStop = false;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Color fill = Color.Transparent, fore = T.Txt, edge = Color.Transparent;

            if (Kind == PRIMARY) { fill = down ? Shade(T.Acc, -30) : (hover ? Shade(T.Acc, 18) : T.Acc); fore = T.AccTxt; }
            else if (Kind == DANGER) { fill = down ? Shade(T.Bad, -30) : (hover ? Shade(T.Bad, 20) : Color.Transparent); fore = hover || down ? T.Card : T.Bad; edge = T.Bad; }
            else if (Kind == SUBTLE) { fill = hover ? T.Card2 : Color.Transparent; fore = T.Txt; }
            else if (Kind == GHOST)
            {
                fill = down ? T.Sel : (hover ? T.Card2 : (Active ? T.Sel : T.Card));
                fore = Active ? T.Acc : T.Txt;
                edge = Active ? T.Acc : T.Line;
            }
            else if (Kind == NAV)
            {
                fill = Active ? T.NavSel : (hover ? T.NavSel : Color.Transparent);
                fore = Active ? T.Acc : T.NavTxt;
            }
            else if (Kind == BAND) { fill = (hover || Active) ? Shade(T.Band, 24) : Color.Transparent; fore = T.BandTxt; }

            // 禁用态改成灰底灰字：旧版灰字压在蓝色主按钮上几乎看不见
            if (!Enabled && Kind == PRIMARY) { fill = T.Line; fore = T.Sub; edge = Color.Transparent; }

            g.Clear(fill == Color.Transparent ? T.ParentBg(this) : fill);
            if (fill != Color.Transparent) using (SolidBrush br = new SolidBrush(fill)) g.FillRectangle(br, r);
            if (edge != Color.Transparent) using (Pen p = new Pen(edge, Math.Max(1f, M.K))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (Kind == NAV)
            {
                if (Active) using (SolidBrush bar = new SolidBrush(T.Acc)) g.FillRectangle(bar, 0, 0, M.S(4), Height);
                else if (hover) using (SolidBrush bar = new SolidBrush(T.Sub)) g.FillRectangle(bar, 0, 0, M.S(2), Height);
            }

            Color fc = Enabled ? fore : T.Sub;
            int indent = (Kind == NAV) ? M.S(24) : (AlignLeft ? M.S(14) : 0);
            Rectangle tr = indent > 0 ? new Rectangle(r.X + indent, r.Y, r.Width - indent - M.S(8), r.Height) : r;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                ((AlignLeft || Kind == NAV) ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, Text, Kind == BAND ? M.FC(12f) : Font, tr, fc, flags);
        }
        static Color Shade(Color c, int d)
        {
            int r = c.R + d, gg = c.G + d, b = c.B + d;
            return Color.FromArgb(r < 0 ? 0 : (r > 255 ? 255 : r),
                gg < 0 ? 0 : (gg > 255 ? 255 : gg), b < 0 ? 0 : (b > 255 ? 255 : b));
        }
    }

    // ---------- Metro 方形开关 ----------
    class ModernToggle : Control
    {
        bool chk = false;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return chk; }
            set { if (chk != value) { chk = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }
        public ModernToggle()
        {
            // 控件自带双缓冲：只重绘自己，不牵动整窗，hover / 点击才不会卡
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = M.S(46); Height = M.S(22);
            Cursor = Cursors.Hand;
        }
        protected override void OnClick(EventArgs e) { Checked = !chk; base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(T.ParentBg(this));
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush br = new SolidBrush(chk ? T.Acc : T.Card2)) g.FillRectangle(br, r);
            using (Pen p = new Pen(chk ? T.Acc : T.Sub, Math.Max(1f, M.K))) g.DrawRectangle(p, r);
            int w = M.S(18);
            int x = chk ? Width - w - M.S(2) : M.S(2);
            using (SolidBrush k = new SolidBrush(chk ? T.AccTxt : T.Txt)) g.FillRectangle(k, x, M.S(2), w, Height - M.S(4));
            TextRenderer.DrawText(g, chk ? "开" : "关", M.FC(7.5f),
                chk ? new Rectangle(M.S(3), 0, Width - w - M.S(8), Height) : new Rectangle(w + M.S(5), 0, Width - w - M.S(10), Height),
                chk ? T.AccTxt : T.Sub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // ---------- Metro 下划线输入框 ----------
    class ModernInput : Panel
    {
        public TextBox Box;
        string cue;
        bool focused = false;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        const int EM_SETCUEBANNER = 0x1501;

        public ModernInput(string hint, bool password)
        {
            cue = hint;
            // 控件自带双缓冲：只重绘自己，不牵动整窗，hover / 点击才不会卡
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = M.S(34);
            Padding = new Padding(M.S(2), M.S(4), M.S(2), M.S(3));
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Font = M.F(10f),
                BackColor = T.Card, ForeColor = T.Txt,
                PasswordChar = password ? (char)0x25CF : '\0'
            };
            Box.Enter += delegate { focused = true; Invalidate(); };
            Box.Leave += delegate { focused = false; Invalidate(); };
            Controls.Add(Box);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { if (!string.IsNullOrEmpty(cue)) SendMessage(Box.Handle, EM_SETCUEBANNER, (IntPtr)1, cue); } catch (Exception) { }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(T.ParentBg(this));
            using (Pen p = new Pen(focused ? T.Acc : T.Line, focused ? M.S(2) : Math.Max(1f, M.K)))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
        public override string Text { get { return Box.Text; } set { Box.Text = value; } }
    }

    // ---------- 行式模型列表 ----------
    class ChainList : ListBox
    {
        public ChainList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = M.S(52);
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            BackColor = T.Card;
            ForeColor = T.Txt;
            Font = M.F(10);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool top = e.Index == 0;
            using (SolidBrush back = new SolidBrush(sel ? T.Sel : T.Card)) g.FillRectangle(back, r);
            using (Pen line = new Pen(T.Line, Math.Max(1f, M.K))) g.DrawLine(line, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
            if (sel) using (SolidBrush bar = new SolidBrush(T.Acc)) g.FillRectangle(bar, 0, r.Y, M.S(3), r.Height);

            string key = e.Index < Items.Count ? Items[e.Index].ToString() : "";
            string l1 = key, l2 = key;
            Core.ModelInfo mi = ModelInfoCache.Last;
            if (mi != null)
            {
                Core.ModelEntry ent = mi.All.Find(delegate(Core.ModelEntry m) { return m.Key == key; });
                if (ent != null) { l1 = ent.ProviderLabel + " · " + ent.ModelName; l2 = ent.Key; }
            }

            Rectangle numR = new Rectangle(r.X + M.S(10), r.Y, M.S(42), r.Height);
            TextRenderer.DrawText(g, (e.Index + 1).ToString("00"), M.FLC(20f), numR,
                top ? T.Acc : T.Sub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            int tx = numR.Right + M.S(6);
            int reserve = top ? M.S(64) : M.S(12);
            Rectangle r1 = new Rectangle(tx, r.Y + M.S(6), r.Right - tx - reserve, M.S(22));
            Rectangle r2 = new Rectangle(tx, r.Y + M.S(28), r.Right - tx - reserve, M.S(17));
            TextRenderer.DrawText(g, l1, top ? M.FBC(10f) : M.FC(10f), r1, T.Txt,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, l2, M.MonoC(8.5f), r2, T.Sub,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (top)
                TextRenderer.DrawText(g, "优先", M.FC(9f),
                    new Rectangle(r.Right - M.S(58), r.Y, M.S(50), r.Height), T.Acc,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
