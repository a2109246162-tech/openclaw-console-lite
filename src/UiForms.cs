// OpenClaw 控制台 Lite —— Metro 主界面（Windows 8 ~ Windows 10 早期风格）
// 纯色标题带 + 磁贴 + 扁平直角控件 + 细分隔线；设置入口收在右上角，点击从右侧展开。
// 功能与 EXE 全功能版对齐：网关启停、当前模型、模型添加/排序/删除、微信扫码、
// 今日 Token、防休眠、托盘、日志、四主题；MSIX 不支持开机自启，功能已移除。
// 隔离：独立数据目录 .openclaw-lite-home、端口 18795，绝不触碰生产 18789。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenClawLite
{
    static class ModelInfoCache { public static Core.ModelInfo Last; }

    public class MainForm : Form
    {
        TitleBand band;
        Panel root;
        CardPanel main, settings;
        ModernButton btnSettings, btnMin, btnClose, btnCloseSettings;
        TilePanel tileState, tileModel, tileUsage;
        Label portLabel, usageDetail, clockLabel;
        ModernButton btnStart, btnStop, btnAdd, btnQr, btnTray;
        ModernButton btnUp, btnDown, btnDel, btnSort;
        ChainList chainList;
        TextBox logBox;
        ModernToggle tglPower;
        ModernButton[] themeBtns = new ModernButton[4];
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        ToolStripMenuItem trayState;
        System.Windows.Forms.Timer timer, slide;
        bool busy = false, reallyExit = false;
        int tick = 0;
        int bandHeight;

        [DllImport("psapi.dll")]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        public MainForm()
        {
            SuspendLayout();
            M.Init(this);
            Features.LoadPrefs();
            T.Refresh();
            string forced = Environment.GetEnvironmentVariable("OC_LITE_THEME");
            if (forced == "Light" || forced == "Dark" || forced == "Black")
            {
                T.Mode = forced == "Light" ? T.LIGHT : (forced == "Dark" ? T.DARK : T.BLACK);
                T.Refresh();
            }

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            Text = "OpenClaw 控制台 Lite";
            Font = M.F(10f);
            Icon = T.MakeIcon();
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = T.Bg;
            ForeColor = T.Txt;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(M.S(940), (int)(wa.Width * 0.94)), Math.Min(M.S(700), (int)(wa.Height * 0.94)));
            MinimumSize = new Size(Math.Min(M.S(720), Width), Math.Min(M.S(520), Height));
            FormClosing += OnFormClosing;
            bandHeight = M.S(58);

            TableLayoutPanel host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = T.Bg
            };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, bandHeight));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(host);

            BuildBand();
            host.Controls.Add(band, 0, 0);

            // 设置面板改为浮层：滑入动画只改自己的 Left，主界面完全不重排（流畅关键）
            root = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg };
            host.Controls.Add(root, 0, 1);

            main = new CardPanel { Dock = DockStyle.Fill, BackColor = T.Card, Padding = M.PD(20) };
            settings = new CardPanel
            {
                Dock = DockStyle.None, Width = M.S(320), BackColor = T.Card,
                Padding = M.PD(18), Visible = false, LeftLine = true
            };
            BuildMain(main);
            BuildSettings(settings);
            root.Controls.Add(main);
            root.Controls.Add(settings);
            settings.BringToFront();
            root.Resize += delegate { PlaceSettingsPanel(); };
            PlaceSettingsPanel();

            BuildTray();
            T.Apply(this);
            ApplyTileColors();
            MarkThemeButtons();

            UiLog("[i] OpenClaw 控制台 Lite 已启动");
            UiLog("[i] 独立数据目录与端口 18795，不触碰本地环境");
            UiLog("[i] 主题：" + T.Mode);
            UiLog("[i] 微信插件按需安装，启动不加载后台任务");

            // 启动只做轻量文件 IO；不再 spawn 任何 node 进程（旧版卡顿主因）
            Core.EnsureDataDirs();
            Core.EnsureConfig();
            Core.MergePresetModels();
            Features.ApplyPowerOnBoot();

            timer = new System.Windows.Forms.Timer { Interval = 8000 };
            timer.Tick += delegate
            {
                tick++;
                RefreshStatus(false);
                if (tick % 8 == 0) RefreshUsageAsync();
                if (clockLabel != null) clockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
            };
            timer.Start();

            // 首帧显示前就把数据填好，避免窗口先画出来再一块块补内容
            RefreshChain();
            RefreshStatus(false);
            RefreshUsageSync();
            Load += delegate
            {
                EnsureWindowInWorkingArea();
                DumpWindow();
                // 不再逐帧改 Opacity（分层窗口重绘在 250% DPI 下会卡）；
                // 整窗合成 + 首帧同步填数据，窗口一次完整呈现，不会有逐行刷新感。
                // 启动完成后修剪一次工作集，把暂不用的页换出，低内存机器更友好（只做一次）
                System.Windows.Forms.Timer trim = new System.Windows.Forms.Timer { Interval = 3000 };
                trim.Tick += delegate
                {
                    trim.Stop(); trim.Dispose();
                    try { using (Process me = Process.GetCurrentProcess()) EmptyWorkingSet(me.Handle); }
                    catch (Exception) { }
                };
                trim.Start();
            };
            ResumeLayout(true);
        }

        // 同步版用量刷新，仅供首帧使用；后续轮询走 RefreshUsageAsync 的 60 秒缓存
        void RefreshUsageSync()
        {
            try
            {
                List<Features.UsageRow> rows = Features.TodayUsage();
                long total = Features.GrandTotal;
                tileUsage.Value = total >= 10000 ? (total / 10000.0).ToString("0.0") + "万" : total.ToString("N0");
                tileUsage.Foot = "tokens · 今日";
                if (rows.Count == 0) usageDetail.Text = "按模型明细：暂无数据";
                else
                {
                    StringBuilder sb = new StringBuilder("按模型明细：");
                    int n = 0;
                    foreach (Features.UsageRow r in rows)
                    {
                        if (n++ >= 2) break;
                        string name = r.Model.Length > 20 ? r.Model.Substring(0, 19) + "…" : r.Model;
                        sb.Append(name).Append(" ").Append(r.Total.ToString("N0")).Append("  ");
                    }
                    usageDetail.Text = sb.ToString().TrimEnd();
                }
            }
            catch (Exception) { }
        }

        // ---------- 标题带：设置只在右上角 ----------
        void BuildBand()
        {
            band = new TitleBand { Dock = DockStyle.Fill, BackColor = T.Band };
            Panel right = new Panel { Dock = DockStyle.Right, Width = M.S(226), BackColor = Color.Transparent };
            TableLayoutPanel btns = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26f));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26f));
            btns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            btnSettings = new ModernButton("设置", ModernButton.BAND) { Dock = DockStyle.Fill, Margin = M.PD(6, 8, 4, 8) };
            btnMin = new ModernButton("─", ModernButton.BAND) { Dock = DockStyle.Fill, Margin = M.PD(4, 8, 4, 8) };
            btnClose = new ModernButton("✕", ModernButton.BAND) { Dock = DockStyle.Fill, Margin = M.PD(4, 8, 8, 8) };
            btnSettings.Click += delegate { ToggleSettings(); };
            btnMin.Click += delegate { HideToTray(); };
            btnClose.Click += delegate { Close(); };
            btns.Controls.Add(btnSettings, 0, 0);
            btns.Controls.Add(btnMin, 1, 0);
            btns.Controls.Add(btnClose, 2, 0);
            right.Controls.Add(btns);
            band.Controls.Add(right);
        }

        // ---------- 主体 ----------
        void BuildMain(CardPanel card)
        {
            TableLayoutPanel t = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, BackColor = Color.Transparent };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            int[] hs = new int[] { 108, 22, 46, 30, 0, 42, 46, 26, 110 };
            for (int i = 0; i < hs.Length; i++)
                t.RowStyles.Add(hs[i] == 0 ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.Absolute, M.S(hs[i])));
            card.Controls.Add(t);

            TableLayoutPanel tiles = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
            for (int i = 0; i < 3; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            tileState = new TilePanel { Dock = DockStyle.Fill, Margin = M.PD(0, 0, 8, 4), Head = "网关状态", Value = "检测中", Foot = "端口 18795" };
            tileModel = new TilePanel { Dock = DockStyle.Fill, Margin = M.PD(4, 0, 4, 4), Head = "当前模型", Value = "—", Foot = "优先级链首位" };
            tileUsage = new TilePanel { Dock = DockStyle.Fill, Margin = M.PD(8, 0, 0, 4), Head = "今日 TOKEN", Value = "0", Foot = "tokens" };
            tiles.Controls.Add(tileState, 0, 0); tiles.Controls.Add(tileModel, 1, 0); tiles.Controls.Add(tileUsage, 2, 0);
            t.Controls.Add(tiles, 0, 0);

            usageDetail = SubLabel("按模型明细：暂无数据");
            usageDetail.Dock = DockStyle.Fill;
            t.Controls.Add(usageDetail, 0, 1);

            TableLayoutPanel ctrl = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
            ctrl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            ctrl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            ctrl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
            ctrl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            btnStart = new ModernButton("▶  启动网关", ModernButton.PRIMARY) { Dock = DockStyle.Fill, Margin = M.PD(0, 2, 6, 6) };
            btnStop = new ModernButton("■  关闭网关", ModernButton.GHOST) { Dock = DockStyle.Fill, Margin = M.PD(6, 2, 6, 6) };
            portLabel = SubLabel("端口 18795 · PID —");
            portLabel.Dock = DockStyle.Fill; portLabel.TextAlign = ContentAlignment.MiddleRight;
            btnStart.Click += delegate { Guard(StartGateway); };
            btnStop.Click += delegate { Guard(StopGateway); };
            ctrl.Controls.Add(btnStart, 0, 0); ctrl.Controls.Add(btnStop, 1, 0); ctrl.Controls.Add(portLabel, 2, 0);
            t.Controls.Add(ctrl, 0, 2);

            TableLayoutPanel head = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, M.S(250)));
            head.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Label h1 = Heading("模型优先级");
            Label h2 = SubLabel("第一行优先生效 · 改动自动重启网关");
            h2.Dock = DockStyle.Fill; h2.TextAlign = ContentAlignment.MiddleRight;
            head.Controls.Add(h1, 0, 0); head.Controls.Add(h2, 1, 0);
            t.Controls.Add(head, 0, 3);

            chainList = new ChainList { Dock = DockStyle.Fill, Margin = M.PD(0, 2, 0, 2) };
            t.Controls.Add(chainList, 0, 4);

            TableLayoutPanel tools = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
            for (int i = 0; i < 4; i++) tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            btnUp = new ModernButton("↑ 上移", ModernButton.GHOST) { Dock = DockStyle.Fill, Margin = M.PD(0, 2, 5, 2) };
            btnDown = new ModernButton("↓ 下移", ModernButton.GHOST) { Dock = DockStyle.Fill, Margin = M.PD(5, 2, 5, 2) };
            btnDel = new ModernButton("✕ 删除", ModernButton.DANGER) { Dock = DockStyle.Fill, Margin = M.PD(5, 2, 5, 2) };
            btnSort = new ModernButton("保存顺序", ModernButton.GHOST) { Dock = DockStyle.Fill, Margin = M.PD(5, 2, 0, 2) };
            btnUp.Click += delegate { MoveChain(-1); };
            btnDown.Click += delegate { MoveChain(1); };
            btnDel.Click += delegate { DeleteModel(); };
            btnSort.Click += delegate { Guard(SaveOrder); };
            tools.Controls.Add(btnUp, 0, 0); tools.Controls.Add(btnDown, 1, 0);
            tools.Controls.Add(btnDel, 2, 0); tools.Controls.Add(btnSort, 3, 0);
            t.Controls.Add(tools, 0, 5);

            TableLayoutPanel actions = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
            for (int i = 0; i < 3; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            btnAdd = new ModernButton("＋  添加 API", ModernButton.PRIMARY) { Dock = DockStyle.Fill, Margin = M.PD(0, 4, 6, 2) };
            btnQr = new ModernButton("微信扫码绑定", ModernButton.GHOST) { Dock = DockStyle.Fill, Margin = M.PD(6, 4, 6, 2) };
            btnTray = new ModernButton("最小化到托盘", ModernButton.GHOST) { Dock = DockStyle.Fill, Margin = M.PD(6, 4, 0, 2) };
            btnAdd.Click += delegate { OpenAdd(); };
            btnQr.Click += delegate { new QrDialog().ShowDialog(this); };
            btnTray.Click += delegate { HideToTray(); };
            actions.Controls.Add(btnAdd, 0, 0); actions.Controls.Add(btnQr, 1, 0); actions.Controls.Add(btnTray, 2, 0);
            t.Controls.Add(actions, 0, 6);

            TableLayoutPanel logHead = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            logHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            logHead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, M.S(90)));
            logHead.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Label lh = Heading("操作日志");
            clockLabel = SubLabel(DateTime.Now.ToString("HH:mm:ss"));
            clockLabel.Dock = DockStyle.Fill; clockLabel.TextAlign = ContentAlignment.MiddleRight;
            logHead.Controls.Add(lh, 0, 0); logHead.Controls.Add(clockLabel, 1, 0);
            t.Controls.Add(logHead, 0, 7);

            logBox = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None,
                WordWrap = false, Font = M.Mono(9f),
                BackColor = T.LogBg, ForeColor = T.LogTxt, Margin = M.PD(0, 2, 0, 0)
            };
            t.Controls.Add(logBox, 0, 8);
        }

        // ---------- 右侧设置面板（右上角「设置」展开） ----------
        void BuildSettings(CardPanel card)
        {
            TableLayoutPanel t = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 16, BackColor = Color.Transparent };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            int[] hs = new int[] { 44, 26, 84, 14, 26, 52, 24, 38, 38, 34, 14, 26, 34, 58, 0, 34 };
            for (int i = 0; i < hs.Length; i++)
                t.RowStyles.Add(hs[i] == 0 ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.Absolute, M.S(hs[i])));
            card.Controls.Add(t);

            TableLayoutPanel title = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, M.S(40)));
            title.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Label h = Heading("设置"); h.Font = M.FL(19f);
            btnCloseSettings = new ModernButton("✕", ModernButton.SUBTLE) { Dock = DockStyle.Fill, Margin = M.PD(2) };
            btnCloseSettings.Click += delegate { ToggleSettings(); };
            title.Controls.Add(h, 0, 0); title.Controls.Add(btnCloseSettings, 1, 0);
            t.Controls.Add(title, 0, 0);

            t.Controls.Add(Section("外观 · 颜色"), 0, 1);

            TableLayoutPanel grid = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                ModernButton b = new ModernButton(T.Modes[i], ModernButton.GHOST)
                { Dock = DockStyle.Fill, Margin = M.PD(i % 2 == 0 ? 0 : 4, 3, i % 2 == 0 ? 4 : 0, 3) };
                b.Click += delegate { Features.SaveTheme(T.Modes[idx]); ApplyTheme(); UiLog("[i] 已切换主题：" + T.Modes[idx]); };
                themeBtns[i] = b;
                grid.Controls.Add(b, i % 2, i / 2);
            }
            t.Controls.Add(grid, 0, 2);

            t.Controls.Add(Separator(), 0, 3);
            t.Controls.Add(Section("开机自启"), 0, 4);
            // 按用户要求放弃此功能：MSIX 的 StartupTask 授权方式在本机不被「启动应用」页面识别，
            // 不再留点了没用的按钮，改成静态说明。
            Label tip = SubLabel("此版本不支持开机自启，功能已移除。\r\n" +
                "MSIX 需声明 StartupTask 并由系统授权；\r\n" +
                "本机的「启动应用」页面不识别该登记方式。\r\n" +
                "需要常驻请用托盘：关闭窗口不会退出。");
            tip.Dock = DockStyle.Fill; tip.TextAlign = ContentAlignment.TopLeft;
            t.Controls.Add(tip, 0, 5);
            t.SetRowSpan(tip, 5);

            t.Controls.Add(Separator(), 0, 10);
            t.Controls.Add(Section("电源"), 0, 11);
            t.Controls.Add(ToggleLine("防止系统休眠", out tglPower), 0, 12);
            tglPower.Checked = Features.PowerSaveEnabled();
            tglPower.CheckedChanged += delegate
            {
                Features.SetPowerSave(tglPower.Checked);
                UiLog(tglPower.Checked ? "[OK] 已开启防休眠，机器人持续在线" : "[i] 已恢复系统休眠");
            };

            Label about = SubLabel("OpenClaw 控制台 Lite\r\n独立实例 · 端口 18795\r\n数据目录 .openclaw-lite-home\r\n不影响 EXE 全功能版");
            about.Dock = DockStyle.Fill; about.TextAlign = ContentAlignment.TopLeft;
            t.Controls.Add(about, 0, 13);
            t.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 14);
            Label foot = SubLabel("浅色 / 深色 / 纯黑 · 可跟随系统");
            foot.Dock = DockStyle.Fill;
            t.Controls.Add(foot, 0, 15);
        }

        void PlaceSettingsPanel()
        {
            if (settings == null || root == null) return;
            settings.Top = 0;
            settings.Height = root.Height;
            settings.Left = settings.Visible ? root.Width - settings.Width : root.Width;
        }
        void ToggleSettings()
        {
            bool show = !settings.Visible;
            btnSettings.Active = show;
            btnSettings.Invalidate();
            if (show)
            {
                settings.Visible = true;
                settings.BringToFront();
                settings.Left = root.Width;
            }

            // 浮层滑入：6 帧、每帧只改一个控件的 Left，不触发布局重排，
            // 主界面完全静止，因此动画流畅且不会像旧版那样「整页一起抖」。
            if (slide != null) { slide.Stop(); slide.Dispose(); slide = null; }
            int startX = settings.Left;
            int endX = show ? root.Width - settings.Width : root.Width;
            int frame = 0;
            slide = new System.Windows.Forms.Timer { Interval = 16 };
            slide.Tick += delegate
            {
                frame++;
                settings.Left = startX + (endX - startX) * frame / 6;
                if (frame >= 6)
                {
                    slide.Stop(); slide.Dispose(); slide = null;
                    if (!show) settings.Visible = false;
                    PlaceSettingsPanel();
                }
            };
            slide.Start();
        }
        // 无边框窗口启动时校正一次位置，确保整个窗口都在工作区内、不被任务栏遮住
        void EnsureWindowInWorkingArea()
        {
            try
            {
                Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
                int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
                if (x != Left || y != Top) Location = new Point(x, y);
            }
            catch (Exception) { }
        }

        // ---------- 控件工厂 ----------
        static Label Heading(string text)
        {
            return new Label
            { Text = text, Dock = DockStyle.Fill, Font = M.FL(17f), ForeColor = T.Txt, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
        }
        static Label Section(string text)
        {
            return new Label
            { Text = text, Dock = DockStyle.Fill, Font = M.F(9f), ForeColor = T.Sub, BackColor = Color.Transparent, Tag = "sub", TextAlign = ContentAlignment.MiddleLeft };
        }
        static Label SubLabel(string text)
        {
            return new Label
            { Text = text, Font = M.F(8.5f), ForeColor = T.Sub, BackColor = Color.Transparent, Tag = "sub", TextAlign = ContentAlignment.MiddleLeft };
        }
        static Panel Separator()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(T.Line, Math.Max(1f, M.K)))
                    e.Graphics.DrawLine(pen, 0, p.Height / 2, p.Width, p.Height / 2);
            };
            return p;
        }
        Panel ToggleLine(string text, out ModernToggle sw)
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            sw = new ModernToggle
            {
                Dock = DockStyle.Right, Width = M.S(46), Height = M.S(22),
                MinimumSize = M.SZ(46, 22), MaximumSize = M.SZ(46, 22), Margin = M.PD(0, 6, 0, 6)
            };
            Label l = new Label { Text = text, Dock = DockStyle.Fill, Font = M.F(10f), ForeColor = T.Txt, BackColor = Color.Transparent };
            p.Controls.Add(l); p.Controls.Add(sw);
            return p;
        }
        void ApplyTileColors()
        {
            if (tileState != null) tileState.TileColor = T.TileA;
            if (tileModel != null) tileModel.TileColor = T.TileB;
            if (tileUsage != null) tileUsage.TileColor = T.TileC;
        }
        void MarkThemeButtons()
        {
            for (int i = 0; i < themeBtns.Length; i++)
                if (themeBtns[i] != null) themeBtns[i].Active = (T.Modes[i] == T.Mode);
        }

        // ---------- 无边框窗口拖动 + 系统主题跟随 ----------
        protected override void WndProc(ref Message m)
        {
            const int WM_SETTINGCHANGE = 0x001A;
            if (m.Msg == WM_SETTINGCHANGE)
            {
                string what = "";
                try { if (m.LParam != IntPtr.Zero) what = Marshal.PtrToStringUni(m.LParam) ?? ""; } catch (Exception) { }
                if (what == "ImmersiveColorSet" && T.Mode == T.SYSTEM)
                    BeginInvoke((MethodInvoker)delegate { ApplyTheme(); });
            }
            base.WndProc(ref m);
        }

        void ApplyTheme()
        {
            T.Refresh();
            BackColor = T.Bg;
            Icon oldIcon = Icon;
            Icon = T.MakeIcon();
            if (oldIcon != null) { try { oldIcon.Dispose(); } catch (Exception) { } }
            T.Apply(this);
            ApplyTileColors();
            MarkThemeButtons();
            chainList.ItemHeight = M.S(52);
            chainList.Invalidate();
            for (int i = 0; i < themeBtns.Length; i++) if (themeBtns[i] != null) themeBtns[i].Invalidate();
            RefreshStatus(false);
            Invalidate(true);
        }

        // ---------- 托盘 ----------
        void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            trayState = new ToolStripMenuItem("网关：检测中…") { Enabled = false };
            trayMenu.Items.Add(trayState);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("打开 / 隐藏主界面", null, delegate { if (Visible) HideToTray(); else ShowForm(); });
            trayMenu.Items.Add("启动网关", null, delegate { if (!busy) Guard(StartGateway); });
            trayMenu.Items.Add("关闭网关", null, delegate { if (!busy) Guard(StopGateway); });
            trayMenu.Items.Add("微信扫码绑定", null, delegate { new QrDialog().ShowDialog(this); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, delegate { reallyExit = true; Close(); });
            // 托盘图标启动即常驻，随时可右键操作；关闭窗口只隐藏不退出
            tray = new NotifyIcon { Icon = T.MakeIcon(), Text = "OpenClaw 控制台 Lite", ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { if (Visible) HideToTray(); else ShowForm(); };
        }
        void ShowForm() { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); }
        void HideToTray()
        {
            Hide(); tray.Visible = true;
            tray.ShowBalloonTip(1500, "OpenClaw 控制台 Lite", "已最小化到托盘，网关继续运行", ToolTipIcon.Info);
        }

        // ---------- 日志 / 操作 ----------
        void UiLog(string msg)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { UiLog(msg); }); return; }
            if (logBox == null) return;
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
            // 日志上限，避免长时间驻留托盘后文本无限增长
            try
            {
                if (logBox.Lines.Length > 220)
                {
                    string[] all = logBox.Lines;
                    string[] keep = new string[150];
                    Array.Copy(all, all.Length - 150, keep, 0, 150);
                    logBox.Lines = keep;
                }
            }
            catch (Exception) { }
            try { logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret(); } catch (Exception) { }
        }
        void Guard(Func<bool> action)
        {
            if (busy) { UiLog("[!] 正在执行其他操作"); return; }
            busy = true;
            btnStart.Enabled = btnStop.Enabled = false;
            Task.Run(delegate
            {
                try { action(); }
                catch (Exception e) { UiLog("[x] " + e.Message); }
                BeginInvoke((MethodInvoker)delegate { busy = false; RefreshStatus(false); });
            });
        }
        bool StartGateway()
        {
            if (Core.PortOpen(Core.PORT)) { UiLog("[i] 网关已在运行"); return true; }
            UiLog("[..] 网关启动中…");
            bool ready = Task.Factory.StartNew(delegate
            {
                // 网关本身不依赖微信插件，不再等待插件 CLI（启动更快）；扫码时才按需安装
                Process p = Core.GatewayStart(UiLog);
                if (p == null) return false;
                for (int i = 0; i < 60; i++) { if (Core.PortOpen(Core.PORT)) return true; Thread.Sleep(2000); }
                return false;
            }).Result;
            UiLog(ready ? "[OK] 网关已就绪，微信机器人可以对话了" : "[x] 网关启动失败（见 logs\\lite-gateway.log）");
            return ready;
        }
        bool StopGateway() { return Core.GatewayStop(UiLog); }

        void RefreshStatus(bool full)
        {
            try
            {
                bool running = Core.PortOpen(Core.PORT);
                // 只用 TcpClient 探测 + 缓存 PID，绝不跑 netstat / WMI（旧版轮询卡顿主因）
                int pid = Core.GatewayPid;
                bool ours = !running || Core.GatewayOwned || pid > 0;
                string stateText = !running ? "已停止" : (ours ? "运行中" : "端口占用");
                tileState.Value = stateText;
                tileState.Foot = running
                    ? "端口 " + Core.PORT + " · PID " + (pid > 0 ? pid.ToString() : "外部")
                    : "端口 " + Core.PORT + " 未监听";
                portLabel.Text = "端口 " + Core.PORT + " · PID " + (pid > 0 ? pid.ToString() : "—");
                tileModel.Value = running && ours ? Features.ActiveModel() : "—";
                tileState.Invalidate(); tileModel.Invalidate();
                btnStart.Enabled = !running && !busy;
                btnStop.Enabled = running && ours && !busy;
                if (trayState != null) trayState.Text = "网关：" + stateText;
                if (tray != null) tray.Text = "OpenClaw 控制台 Lite — " + stateText;
                if (full) RefreshChain();
            }
            catch (Exception) { }
        }
        void RefreshUsageAsync()
        {
            Task.Run(delegate
            {
                try
                {
                    List<Features.UsageRow> rows = Features.TodayUsage();
                    long total = Features.GrandTotal;
                    string detail;
                    if (rows.Count == 0) detail = "按模型明细：暂无数据";
                    else
                    {
                        StringBuilder sb = new StringBuilder("按模型明细：");
                        int n = 0;
                        foreach (Features.UsageRow r in rows)
                        {
                            if (n++ >= 2) break;
                            string name = r.Model.Length > 20 ? r.Model.Substring(0, 19) + "…" : r.Model;
                            sb.Append(name).Append(" ").Append(r.Total.ToString("N0")).Append("  ");
                        }
                        detail = sb.ToString().TrimEnd();
                    }
                    BeginInvoke((MethodInvoker)delegate
                    {
                        tileUsage.Value = total >= 10000 ? (total / 10000.0).ToString("0.0") + "万" : total.ToString("N0");
                        tileUsage.Foot = "tokens · 今日";
                        tileUsage.Invalidate();
                        usageDetail.Text = detail;
                    });
                }
                catch (Exception) { }
            });
        }
        void RefreshChain()
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { RefreshChain(); }); return; }
            try
            {
                Core.ModelInfo mi = Core.GetModelInfo();
                ModelInfoCache.Last = mi;
                int sel = chainList.SelectedIndex;
                chainList.BeginUpdate();
                chainList.Items.Clear();
                foreach (string k in mi.Chain) chainList.Items.Add(k);
                chainList.EndUpdate();
                if (sel >= 0 && sel < chainList.Items.Count) chainList.SelectedIndex = sel;
            }
            catch (Exception) { }
        }
        bool SaveOrder()
        {
            List<string> order = new List<string>();
            foreach (object o in chainList.Items) order.Add(o.ToString());
            if (order.Count == 0) { UiLog("[!] 至少保留一个模型"); return false; }
            Core.SetPriority(order);
            UiLog("[OK] 顺序已保存");
            RestartGatewayIfNeeded("[OK] 网关已按新顺序重启", "[!] 网关重启失败，请手动关闭再启动");
            return true;
        }
        void MoveChain(int delta)
        {
            int i = chainList.SelectedIndex;
            if (i < 0) { UiLog("[!] 请先选择一个模型"); return; }
            int j = i + delta;
            if (j < 0 || j >= chainList.Items.Count) return;
            object tmp = chainList.Items[i];
            chainList.Items[i] = chainList.Items[j];
            chainList.Items[j] = tmp;
            chainList.SelectedIndex = j;
            UiLog("[i] 顺序已调整，点「保存顺序」生效");
        }
        void DeleteModel()
        {
            int i = chainList.SelectedIndex;
            if (i < 0) { UiLog("[!] 请先选择一个模型"); return; }
            string key = chainList.Items[i].ToString();
            DialogResult r = MessageBox.Show(this, "从优先级链移除 " + key + " ？\r\n\r\n（同时删除该模型的全部条目与所属 provider）",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            Task.Run(delegate
            {
                Core.RemoveModel(key, true);
                UiLog("[OK] 已删除 " + key);
                RestartGatewayIfNeeded("[OK] 网关已重启应用删除", "[!] 网关重启失败，请手动重启");
            });
        }
        void OpenAdd()
        {
            new AddApiDialog(delegate
            {
                Task.Run(delegate
                {
                    RestartGatewayIfNeeded("[OK] 网关已重启，新模型已生效", "[!] 网关重启失败，请手动关闭再启动");
                    RefreshChain();
                });
            }).ShowDialog(this);
        }
        void RestartGatewayIfNeeded(string okMsg, string failMsg)
        {
            if (!Core.PortOpen(Core.PORT)) { UiLog("[i] 网关未在运行，下次启动时生效"); return; }
            UiLog("[..] 正在重启网关…");
            Core.GatewayStop(delegate(string s) { UiLog(s); });
            Process p = Core.GatewayStart(UiLog);
            bool ok = false;
            if (p != null) for (int i = 0; i < 60; i++) { if (Core.PortOpen(Core.PORT)) { ok = true; break; } Thread.Sleep(2000); }
            UiLog(ok ? okMsg : failMsg);
            BeginInvoke((MethodInvoker)delegate { RefreshStatus(true); RefreshUsageAsync(); });
        }
        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            Core.KillLoginChild();
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; HideToTray(); return;
            }
            timer.Stop();
            if (slide != null) { slide.Stop(); slide.Dispose(); slide = null; }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
        }

        // ---------- 自检辅助（仅环境变量触发，正常启动零开销） ----------
        void RunGatewayTest(string outPath)
        {
            Task.Run(delegate
            {
                bool up = false, listening = false, down = false;
                string err = "";
                try
                {
                    up = StartGateway();
                    listening = Core.PortOpen(Core.PORT);
                    Core.GatewayStop(UiLog);
                    for (int i = 0; i < 10 && Core.PortOpen(Core.PORT); i++) Thread.Sleep(500);
                    down = !Core.PortOpen(Core.PORT);
                }
                catch (Exception e) { err = e.Message; }
                try { File.WriteAllText(outPath, "START=" + up + " LISTEN=" + listening + " STOPPED=" + down + " ERR=" + err, Encoding.UTF8); }
                catch (Exception) { }
            });
        }
        void DumpWindow()
        {
            string shot = Environment.GetEnvironmentVariable("OC_LITE_SHOT");
            if (string.IsNullOrEmpty(shot)) return;
            System.Windows.Forms.Timer once = new System.Windows.Forms.Timer { Interval = 1500 };
            once.Tick += delegate
            {
                once.Stop();
                try
                {
                    if (Environment.GetEnvironmentVariable("OC_LITE_SHOW_SETTINGS") == "1")
                    {
                        // 测试截图跳过滑入动画，直接展开到终态
                        settings.Visible = true;
                        PlaceSettingsPanel();
                        settings.BringToFront();
                        btnSettings.Active = true;
                        Application.DoEvents();
                    }
                    using (Bitmap bmp = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                        bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Rectangle wa2 = Screen.PrimaryScreen.WorkingArea;
                    // 自检：模拟在标题带按下并移动，窗口坐标必须真的变化
                    int dragOk = 0;
                    try { dragOk = band.TestDrag(M.S(40), M.S(25)) ? 1 : 0; } catch (Exception) { }
                    File.WriteAllText(shot + ".txt",
                        "DPI_SCALE=" + M.K.ToString("0.00") + " WINDOW=" + Width + "x" + Height +
                        " WORKAREA=" + wa2.Width + "x" + wa2.Height + " THEME=" + T.Mode +
                        " DARK=" + T.Dark + " CHAIN_ITEMS=" + chainList.Items.Count +
                        " SETTINGS_VISIBLE=" + settings.Visible + " LOG_LINES=" + logBox.Lines.Length +
                        " DRAG_TEST=" + dragOk, Encoding.UTF8);
                    string dlg = Environment.GetEnvironmentVariable("OC_LITE_DIALOG");
                    if (dlg == "add" || dlg == "qr")
                    {
                        Form d = dlg == "add" ? (Form)new AddApiDialog(null) : (Form)new QrDialog();
                        d.Show(this);
                        int hold = 0;
                        int.TryParse(Environment.GetEnvironmentVariable("OC_LITE_DIALOG_HOLD"), out hold);
                        Action snap = delegate
                        {
                            d.Refresh(); Application.DoEvents();
                            using (Bitmap db = new Bitmap(d.Width, d.Height))
                            {
                                d.DrawToBitmap(db, new Rectangle(0, 0, d.Width, d.Height));
                                db.Save(shot.Replace(".png", "-" + dlg + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                            d.Close();
                        };
                        if (hold > 0)
                        {
                            System.Windows.Forms.Timer later = new System.Windows.Forms.Timer { Interval = hold * 1000 };
                            later.Tick += delegate { later.Stop(); try { snap(); } catch (Exception) { } };
                            later.Start();
                        }
                        else snap();
                    }
                    if (Environment.GetEnvironmentVariable("OC_LITE_GATEWAY_TEST") == "1") RunGatewayTest(shot + ".gw.txt");
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(shot + ".txt", "SHOT_ERR=" + ex.Message, Encoding.UTF8); } catch (Exception) { }
                }
            };
            once.Start();
        }
    }

    // ---------- 添加 API 对话框（Metro 风） ----------
    public class AddApiDialog : Form
    {
        ModernInput pkey, plabel, pbase, pkey2, mid, mname;
        ModernButton presetBtn;
        int presetIndex = 0;
        Action onDone;

        static readonly string[][] Presets = new string[][]
        {
            new string[] { "自定义", "", "", "", "" },
            new string[] { "火山方舟 · 豆包", "ark-doubao", "https://ark.cn-beijing.volces.com/api/v3", "doubao-seed-evolving", "Doubao Seed Evolving" },
            new string[] { "DeepSeek 官方", "deepseek", "https://api.deepseek.com/v1", "deepseek-chat", "DeepSeek Chat" },
            new string[] { "OpenAI", "openai", "https://api.openai.com/v1", "gpt-4o", "GPT-4o" },
            new string[] { "智谱 GLM", "zhipu", "https://open.bigmodel.cn/api/paas/v4", "glm-4-plus", "GLM-4-Plus" },
            new string[] { "Moonshot Kimi", "moonshot", "https://api.moonshot.cn/v1", "moonshot-v1-8k", "Moonshot V1 8K" },
        };

        public AddApiDialog(Action done)
        {
            SuspendLayout();
            onDone = done;
            T.Refresh();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            Text = "添加 API 供应商";
            Font = M.F(10f);
            Icon = T.MakeIcon();
            BackColor = T.Bg; ForeColor = T.Txt;
            Size = M.SZ(500, 430);
            StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = T.Bg };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(52)));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(host);

            TitleBand band = new TitleBand { Dock = DockStyle.Fill, BackColor = T.Band, Caption = "添加 API 供应商", SubCaption = "选择一个预设，或填写自定义接口" };
            ModernButton x = new ModernButton("✕", ModernButton.BAND) { Dock = DockStyle.Right, Width = M.S(46), Margin = M.PD(6) };
            x.Click += delegate { Close(); };
            band.Controls.Add(x);
            host.Controls.Add(band, 0, 0);

            CardPanel card = new CardPanel { Dock = DockStyle.Fill, BackColor = T.Card, Padding = M.PD(18) };
            host.Controls.Add(card, 0, 1);
            TableLayoutPanel t = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, BackColor = Color.Transparent };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            int[] hs = new int[] { 54, 54, 54, 54, 54, 0, 46 };
            for (int i = 0; i < hs.Length; i++)
                t.RowStyles.Add(hs[i] == 0 ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.Absolute, M.S(hs[i])));
            card.Controls.Add(t);

            TableLayoutPanel presetWrap = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
            presetWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            presetWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(20)));
            presetWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            presetWrap.Controls.Add(LabelTop("快速预设"), 0, 0);
            presetBtn = new ModernButton(Presets[0][0] + "  ▾", ModernButton.GHOST)
            { Dock = DockStyle.Fill, Font = M.F(10f), AlignLeft = true };
            presetBtn.Click += delegate { ShowPresetMenu(); };
            presetWrap.Controls.Add(presetBtn, 0, 1);
            t.Controls.Add(presetWrap, 0, 0);
            t.SetColumnSpan(presetWrap, 2);

            pkey = Field(t, 1, 0, "供应商标识（英文 / 数字 / _ / -）", "如 myopenai", false, false);
            plabel = Field(t, 1, 1, "显示名称", "如 我的 OpenAI", false, false);
            pbase = Field(t, 2, 0, "API Base URL", "https://api.example.com/v1", false, true);
            pkey2 = Field(t, 3, 0, "API Key", "sk-... 或 ark-...", true, true);
            mid = Field(t, 4, 0, "模型 ID", "如 deepseek-flash", false, false);
            mname = Field(t, 4, 1, "模型名称（可空）", "如 DeepSeek V4 Flash", false, false);

            TableLayoutPanel btns = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            btns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            ModernButton cancel = new ModernButton("取消", ModernButton.GHOST) { Dock = DockStyle.Fill, Margin = M.PD(0, 4, 6, 0) };
            ModernButton ok = new ModernButton("添加", ModernButton.PRIMARY) { Dock = DockStyle.Fill, Margin = M.PD(6, 4, 0, 0) };
            cancel.Click += delegate { Close(); };
            ok.Click += delegate { Submit(); };
            btns.Controls.Add(cancel, 1, 0); btns.Controls.Add(ok, 0, 0);
            t.Controls.Add(btns, 0, 6); t.SetColumnSpan(btns, 2);
            AcceptButton = ok; CancelButton = cancel;
            T.Apply(this);
            ResumeLayout(true);
        }
        static Label LabelTop(string text)
        {
            return new Label
            { Text = text, Dock = DockStyle.Fill, Font = M.F(8.5f), ForeColor = T.Sub, BackColor = Color.Transparent, Tag = "sub", TextAlign = ContentAlignment.BottomLeft };
        }
        ModernInput Field(TableLayoutPanel t, int row, int col, string label, string hint, bool pwd, bool span)
        {
            TableLayoutPanel wrap = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent,
                Margin = span ? M.PD(0) : M.PD(col == 0 ? 0 : 8, 0, col == 0 ? 8 : 0, 0)
            };
            wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(20)));
            wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            wrap.Controls.Add(LabelTop(label), 0, 0);
            ModernInput input = new ModernInput(hint, pwd) { Dock = DockStyle.Fill };
            wrap.Controls.Add(input, 0, 1);
            t.Controls.Add(wrap, col, row);
            if (span) t.SetColumnSpan(wrap, 2);
            return input;
        }
        void ShowPresetMenu()
        {
            ContextMenuStrip cm = new ContextMenuStrip { BackColor = T.Card };
            for (int i = 0; i < Presets.Length; i++)
            {
                int idx = i;
                ToolStripMenuItem it = new ToolStripMenuItem(Presets[i][0]) { BackColor = T.Card, ForeColor = T.Txt, Checked = (i == presetIndex) };
                it.Click += delegate { presetIndex = idx; presetBtn.Text = Presets[idx][0] + "  ▾"; ApplyPreset(); };
                cm.Items.Add(it);
            }
            cm.Show(presetBtn, new Point(0, presetBtn.Height));
        }
        void ApplyPreset()
        {
            string[] p = Presets[presetIndex];
            pkey.Text = p[1]; plabel.Text = p[1] == "" ? "" : p[0];
            pbase.Text = p[2]; mid.Text = p[3]; mname.Text = p[4];
        }
        void Submit()
        {
            string err = Core.AddProvider(pkey.Text, plabel.Text, pbase.Text, pkey2.Text, mid.Text, mname.Text);
            if (err != null) { MessageBox.Show(this, err, "无法添加", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            MessageBox.Show(this, "已添加 " + (plabel.Text.Trim() == "" ? pkey.Text : plabel.Text) + " / " + mid.Text +
                "\r\n网关运行中会自动重启以生效。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (onDone != null) onDone();
            Close();
        }
    }

    // ---------- 微信扫码对话框（Metro 风） ----------
    public class QrDialog : Form
    {
        PictureBox pic;
        Label tip, linkLabel;
        ModernButton retryBtn;
        volatile string url = null;
        volatile bool done = false;

        public QrDialog()
        {
            SuspendLayout();
            T.Refresh();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            Text = "微信扫码绑定";
            Font = M.F(10f);
            Icon = T.MakeIcon();
            BackColor = T.Bg; ForeColor = T.Txt;
            Size = M.SZ(400, 500);
            StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = T.Bg };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(52)));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(host);

            TitleBand band = new TitleBand { Dock = DockStyle.Fill, BackColor = T.Band, Caption = "微信绑定", SubCaption = "用手机微信扫码后确认" };
            ModernButton x = new ModernButton("✕", ModernButton.BAND) { Dock = DockStyle.Right, Width = M.S(46), Margin = M.PD(6) };
            x.Click += delegate { Close(); };
            band.Controls.Add(x);
            host.Controls.Add(band, 0, 0);

            CardPanel card = new CardPanel { Dock = DockStyle.Fill, BackColor = T.Card, Padding = M.PD(18) };
            host.Controls.Add(card, 0, 1);
            TableLayoutPanel t = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(58)));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(66)));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(22)));
            card.Controls.Add(t);

            Panel qrWrap = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = M.PD(6) };
            pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
            qrWrap.Controls.Add(pic);
            t.Controls.Add(qrWrap, 0, 0);

            tip = new Label
            {
                Text = "正在获取二维码…\r\n首次约需 20~40 秒", Dock = DockStyle.Fill,
                Font = M.F(10f), ForeColor = T.Sub, BackColor = Color.Transparent, Tag = "sub",
                TextAlign = ContentAlignment.MiddleCenter
            };
            t.Controls.Add(tip, 0, 1);

            TableLayoutPanel btnWrap = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
            btnWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            btnWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            btnWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, M.S(20)));
            retryBtn = new ModernButton("重试", ModernButton.GHOST)
            { Dock = DockStyle.Fill, Margin = M.PD(90, 2, 90, 2), Visible = false };
            retryBtn.Click += delegate { LoadQr(); };
            linkLabel = new Label
            { Text = "", Dock = DockStyle.Fill, Font = M.Mono(7.5f), ForeColor = T.Sub, BackColor = Color.Transparent, Tag = "sub", TextAlign = ContentAlignment.MiddleCenter };
            btnWrap.Controls.Add(retryBtn, 0, 0);
            btnWrap.Controls.Add(linkLabel, 0, 1);
            t.Controls.Add(btnWrap, 0, 2);

            Label foot = new Label
            { Text = "独立实例 · 需重新扫码绑定一次", Dock = DockStyle.Fill, Font = M.F(8.5f), ForeColor = T.Sub, BackColor = Color.Transparent, Tag = "sub", TextAlign = ContentAlignment.MiddleCenter };
            t.Controls.Add(foot, 0, 3);

            FormClosing += delegate(object s, FormClosingEventArgs e) { Task.Run((Action)Core.KillLoginChild); };
            Load += delegate { T.Apply(this); LoadQr(); };
            ResumeLayout(true);
        }

        void LoadQr()
        {
            pic.Image = null;
            tip.Text = "正在获取二维码…\r\n首次约需 20~40 秒";
            retryBtn.Visible = false;
            done = false; url = null;
            Task.Run(delegate
            {
                Task<bool> t = Core.InstallPluginAsync();
                if (!t.Wait(320000) || !t.Result) { Done("微信插件未就绪，无法获取二维码。请重启程序重试。"); return; }
                string msg = Core.StartLogin(delegate(string u) { OnUrl(u); }, null);
                if (msg != null) { Done(msg); return; }
                for (int i = 0; i < 120 && !done; i++)
                {
                    Thread.Sleep(1000);
                    if (url != null && !done) { done = true; ShowQr(url); return; }
                }
                if (!done) Done("获取超时（120 秒）。请确认网络可访问微信后重试。");
            });
        }
        void OnUrl(string u) { url = u; }
        void ShowQr(string u)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { ShowQr(u); }); return; }
            string dataUrl = Core.QrDataUrl(u);
            if (dataUrl == null) { tip.Text = "二维码本地生成失败，请点「重试」。"; retryBtn.Visible = true; return; }
            string b64 = dataUrl.Substring("data:image/png;base64,".Length);
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(b64)))
            {
                Image old = pic.Image;
                pic.Image = Image.FromStream(ms);
                if (old != null) old.Dispose();
            }
            tip.Text = "用手机微信扫描二维码，\r\n扫码后在手机上确认即可绑定。";
            linkLabel.Text = u.Length > 72 ? u.Substring(0, 72) + "…" : u;
            Core.BootLog("qrcode shown");
        }
        void Done(string msg)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Done(msg); }); return; }
            tip.Text = msg; retryBtn.Visible = true;
            Core.BootLog("qr dialog: " + msg);
        }
    }
}
