// ============================================================================
// sz333解压工具 —— 主窗口 v1.6
// 结构：选项卡（解压 / 工作流 / 设置）+ 底部共用进度与日志（无菜单栏）
// v1.6：设置独立成选项卡（不再弹窗）；密码管理移到主界面解压页；
//       删除批量解压；密码错误弹窗输入并自动重试；文件列表区域放大；
//       密码列表可拖动排序；工作流页说明移到顶部
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DSUnpack
{
    public class MainForm : Form
    {
        // ---- 文件选择区 ----
        private GroupBox gb1;
        private Button btnAddFiles;
        private Button btnAddFolder;
        private Button btnRemoveSel;
        private Button btnClear;
        private ListView lvFiles;
        private Label lblHint;

        // ---- 底部 ----
        private Button btnStart;
        private Button btnCancel;
        private Button btnOpenOut;
        private ProgressBar progTotal;
        private ProgressBar progCurrent;
        private Label lblStatus;
        private TextBox txtLog;
        private StatusStrip statusStrip;
        private NotifyIcon notifyIcon;

        // ---- 选项卡 ----
        private TabControl tabMain;
        private TabPage tabUnpack;
        private TabPage tabWorkflow;
        private TabPage tabSettings;
        private SettingsPanel settingsPanel;
        private ListView lvWorkflow;
        private Label lblWfHint;

        // ---- 解压页：密码区 ----
        private GroupBox gbPwd;
        private TextBox txtNewPwd;
        private Button btnAddPwd;
        private Button btnDelPwd;
        private Button btnClearPwd;
        private ListBox lbPwds;
        private Label lblPwdHint;
        private Point pwdMouseDownPos = Point.Empty;
        private bool pwdDragging = false;

        private readonly List<string> selectedFiles = new List<string>();
        private List<Workflow> workflows = new List<Workflow>();
        private volatile bool cancelFlag = false;
        private bool running = false;
        private int curDone = 0;
        private int curTotal = 0;
        private int curPct = 0;
        private string lastSuccessPath = null;
        private int logAppendOps = 0;

        private float dpiFactor = 1f;
        private int lastRelayoutW = 0;
        private int lastRelayoutH = 0;
        private string currentTheme = "白色";
        private Color listHeaderBg = SystemColors.Control;
        private Color listHeaderFg = SystemColors.ControlText;
        private Color listSelBg = SystemColors.Highlight;

        public MainForm()
        {
            IntPtr ignore = Handle;
            uint sysDpi = GetDpiForSystem();
            dpiFactor = (sysDpi == 0u ? 96u : sysDpi) / 96f;
            if (dpiFactor < 1f) dpiFactor = 1f;

            Text = "sz333解压工具 v1.6 —— 分卷识别 · 多层解压 · 工作流（Created by sz333）";
            Font = new Font("Microsoft YaHei UI", 9f * dpiFactor);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size((int)Math.Round(920 * dpiFactor), (int)Math.Round(840 * dpiFactor));
            MinimumSize = new Size((int)Math.Round(900 * dpiFactor), (int)Math.Round(760 * dpiFactor));
            StartPosition = FormStartPosition.CenterScreen;
            LoadAppIcon();
            BuildUI();
            PwdStore.Load(UnpackCore.Passwords);
            workflows = WorkflowManager.Load();
            LoadTheme();
            // 从注册表加载解压选项（所有入口共用，避免设置跨会话失效）
            UnpackCore.DeleteAfterSuccess = AppSettings.GetBool("Delete", true);
            UnpackCore.DeduplicateEnabled = AppSettings.GetBool("Dedup", true);
            UnpackCore.MaxNestDepth = Math.Max(1, Math.Min(99, AppSettings.GetInt("MaxDepth", 30)));
            SetupDragDrop();
            SetupTray();
            RefreshPwdList();
            Log("欢迎使用 sz333解压工具。");
            Log("① 解压页：添加/拖入分卷文件 → 开始解压，右侧可直接管理密码；");
            Log("② 工作流页：管理已知来源的解压链路；③ 设置页：输出/删除/剔重/主题等选项。");
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (running)
                {
                    MessageBox.Show(this, "正在处理中，请先点【取消】等待当前任务结束，再关闭窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Cancel = true;
                }
            };
            FormClosed += delegate(object s, FormClosedEventArgs e)
            {
                try { if (notifyIcon != null) { notifyIcon.Visible = false; notifyIcon.Dispose(); } } catch { }
            };
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private void Place(Control c, int x, int y, int w, int h)
        {
            c.SetBounds((int)Math.Round(x * dpiFactor), (int)Math.Round(y * dpiFactor), (int)Math.Round(w * dpiFactor), (int)Math.Round(h * dpiFactor));
        }

        private void LoadAppIcon()
        {
            try
            {
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DSUnpack.AppIcon"))
                {
                    if (s != null) Icon = new Icon(s);
                }
            }
            catch { }
        }

        // ================= 拖拽 =================

        private void SetupDragDrop()
        {
            AllowDrop = true;
            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                foreach (string f in files)
                {
                    if (Directory.Exists(f))
                    {
                        try { foreach (string sub in Directory.GetFiles(f, "*", SearchOption.AllDirectories)) AddFile(sub); } catch { }
                    }
                    else AddFile(f);
                }
                Log("已将拖入的 " + files.Length + " 个条目加入列表。");
            };
        }

        // ================= 托盘 =================

        private void SetupTray()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = Icon;
            notifyIcon.Text = "sz333解压工具";
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += delegate { ShowWindow(); };
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示主窗口", null, delegate { ShowWindow(); });
            trayMenu.Items.Add("关于", null, delegate { ShowAbout(); });
            trayMenu.Items.Add("退出", null, delegate { Close(); });
            notifyIcon.ContextMenuStrip = trayMenu;
            Resize += delegate(object s, EventArgs e)
            {
                if (WindowState == FormWindowState.Minimized) Hide();
            };
        }

        private void ShowAbout()
        {
            MessageBox.Show(this, "sz333解压工具 v1.6（Created by sz333）\n本软件完全免费，如通过购买获得，请立即退款。\n无需赞赏，愿天下开源。", "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // ================= 主题 =================

        public void SetTheme(string name)
        {
            currentTheme = name;
            AppSettings.Set("Theme", name);
            ApplyTheme();
        }

        private void LoadTheme()
        {
            string t = AppSettings.Get("Theme", "白色");
            currentTheme = (t == "深色") ? "深色" : "白色";
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            bool dark = currentTheme == "深色";
            Color bg = dark ? Color.FromArgb(42, 42, 46) : SystemColors.Control;
            Color fg = dark ? Color.FromArgb(228, 228, 233) : SystemColors.ControlText;
            Color dim = dark ? Color.FromArgb(150, 150, 156) : Color.Gray;
            Color inputBg = dark ? Color.FromArgb(28, 28, 32) : Color.White;
            Color btnBg = dark ? Color.FromArgb(70, 70, 78) : SystemColors.Control;
            Color btnFg = dark ? Color.FromArgb(235, 235, 240) : SystemColors.ControlText;
            Color border = dark ? Color.FromArgb(56, 56, 62) : SystemColors.ControlDark;

            BackColor = bg;
            btnStart.BackColor = dark ? Color.FromArgb(0, 105, 178) : Color.FromArgb(0, 122, 204);
            btnStart.ForeColor = Color.White;
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.FlatAppearance.BorderColor = dark ? Color.FromArgb(0, 120, 200) : Color.FromArgb(0, 100, 170);
            btnStart.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(0, 130, 210) : Color.FromArgb(0, 140, 225);
            lblStatus.ForeColor = dim;
            lblHint.ForeColor = dim;

            ApplyColors(this, dark, bg, fg, dim, inputBg, btnBg, btnFg, border);

            txtLog.BackColor = dark ? Color.FromArgb(18, 18, 22) : Color.FromArgb(30, 30, 30);
            txtLog.ForeColor = dark ? Color.FromArgb(140, 230, 160) : Color.LightGreen;
            listHeaderBg = dark ? Color.FromArgb(40, 40, 45) : SystemColors.Control;
            listHeaderFg = dark ? Color.FromArgb(210, 210, 215) : SystemColors.ControlText;
            listSelBg = dark ? Color.FromArgb(0, 90, 150) : SystemColors.Highlight;
            lvFiles.Invalidate();
            if (lvWorkflow != null) lvWorkflow.Invalidate();

            if (statusStrip != null)
            {
                statusStrip.BackColor = dark ? Color.FromArgb(35, 35, 39) : SystemColors.Control;
                foreach (ToolStripItem it in statusStrip.Items) it.ForeColor = dark ? Color.FromArgb(165, 165, 172) : Color.FromArgb(90, 90, 90);
            }
        }

        private void ApplyColors(Control root, bool dark, Color bg, Color fg, Color dim, Color inputBg, Color btnBg, Color btnFg, Color border)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ListView)
                {
                    ListView lv = (ListView)c;
                    lv.BackColor = dark ? Color.FromArgb(24, 24, 28) : Color.White;
                    lv.ForeColor = dark ? Color.FromArgb(220, 220, 225) : SystemColors.WindowText;
                    lv.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is ListBox)
                {
                    c.BackColor = dark ? inputBg : Color.White;
                    c.ForeColor = dark ? Color.FromArgb(225, 225, 230) : SystemColors.WindowText;
                }
                else if (c is TextBox)
                {
                    c.BackColor = dark ? inputBg : Color.White;
                    c.ForeColor = dark ? Color.FromArgb(225, 225, 230) : SystemColors.WindowText;
                    ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    if (dark)
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.BackColor = btnBg;
                        b.ForeColor = btnFg;
                        b.FlatAppearance.BorderColor = border;
                        b.FlatAppearance.BorderSize = 1;
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(92, 92, 102);
                        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 55, 62);
                    }
                    else
                    {
                        b.FlatStyle = FlatStyle.System;
                        b.BackColor = SystemColors.Control;
                        b.ForeColor = SystemColors.ControlText;
                    }
                }
                else if (c is CheckBox)
                {
                    c.BackColor = bg;
                    c.ForeColor = fg;
                }
                else if (c is GroupBox)
                {
                    c.BackColor = bg;
                    c.ForeColor = dark ? Color.FromArgb(200, 200, 206) : SystemColors.ControlText;
                }
                else if (c is TabControl)
                {
                    ((TabControl)c).BackColor = bg;
                }
                else if (c is TabPage)
                {
                    c.BackColor = bg;
                }
                else
                {
                    c.BackColor = bg;
                    c.ForeColor = fg;
                }
                ApplyColors(c, dark, bg, fg, dim, inputBg, btnBg, btnFg, border);
            }
        }

        // ================= 界面搭建 =================

        private Button NewBtn(string text, EventHandler onClick, Control parent, int x, int y, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            int hh = (int)Math.Round(h * dpiFactor);
            b.MinimumSize = new Size((int)Math.Round(60 * dpiFactor), hh);
            b.MaximumSize = new Size((int)Math.Round(220 * dpiFactor), hh);
            b.Location = new Point(x, y);
            b.Height = hh;
            if (onClick != null) b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        private void BuildUI()
        {
            // ---- 选项卡 ----
            tabMain = new TabControl();
            tabUnpack = new TabPage("解压");
            tabWorkflow = new TabPage("工作流");
            tabSettings = new TabPage("设置");
            tabMain.TabPages.Add(tabUnpack);
            tabMain.TabPages.Add(tabWorkflow);
            tabMain.TabPages.Add(tabSettings);
            Controls.Add(tabMain);

            // ---- 解压页：文件选择 ----
            gb1 = new GroupBox();
            gb1.Text = "① 选择文件（分卷请全部选中，可直接拖入窗口）";
            tabUnpack.Controls.Add(gb1);

            btnAddFiles = NewBtn("添加文件…", delegate { AddFiles(); }, gb1, 12, 36, 30);
            btnAddFolder = NewBtn("添加文件夹…", delegate { AddFolder(); }, gb1, 0, 36, 30);
            btnRemoveSel = NewBtn("移除选中", delegate { RemoveSelected(); }, gb1, 0, 36, 30);
            btnClear = NewBtn("全部清除", delegate { ClearFiles(); }, gb1, 0, 36, 30);

            lblHint = new Label();
            lblHint.Text = "分卷自动合并（part1/2…、.001、.z01 等）";
            lblHint.ForeColor = Color.Gray;
            gb1.Controls.Add(lblHint);

            lvFiles = new ListView();
            lvFiles.View = View.Details;
            lvFiles.FullRowSelect = true;
            lvFiles.MultiSelect = true;
            lvFiles.GridLines = false;
            lvFiles.Columns.Add("文件", 400);
            lvFiles.Columns.Add("大小", 100);
            lvFiles.Columns.Add("分组", 200);
            try
            {
                ImageList il = new ImageList();
                il.ImageSize = new Size(1, (int)Math.Round(22 * dpiFactor));
                il.TransparentColor = Color.Transparent;
                lvFiles.SmallImageList = il;
            }
            catch { }
            lvFiles.OwnerDraw = true;
            lvFiles.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(listHeaderBg)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, lvFiles.Font, e.Bounds, listHeaderFg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            lvFiles.DrawItem += delegate(object sender, DrawListViewItemEventArgs e)
            {
                bool sel = (e.State & ListViewItemStates.Selected) != 0;
                using (SolidBrush b = new SolidBrush(sel ? listSelBg : lvFiles.BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
                if (sel) e.DrawFocusRectangle();
            };
            lvFiles.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(e.Item.Selected ? listSelBg : lvFiles.BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvFiles.Font, e.Bounds, lvFiles.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            gb1.Controls.Add(lvFiles);

            // ---- 解压页：密码区（主界面直接管理，不再藏在设置里）----
            gbPwd = new GroupBox();
            gbPwd.Text = "② 密码列表（按顺序尝试，可拖动排序）";
            tabUnpack.Controls.Add(gbPwd);
            txtNewPwd = new TextBox();
            gbPwd.Controls.Add(txtNewPwd);
            btnAddPwd = new Button();
            btnAddPwd.Text = "添加";
            btnAddPwd.Click += delegate { AddPassword(); };
            gbPwd.Controls.Add(btnAddPwd);
            lbPwds = new ListBox();
            lbPwds.AllowDrop = true;
            // 拖动排序：按住某个密码移动超过阈值即开始拖动，放到新位置调整尝试顺序并保存
            lbPwds.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && lbPwds.IndexFromPoint(e.Location) >= 0)
                {
                    pwdMouseDownPos = e.Location;
                    pwdDragging = false;
                }
            };
            lbPwds.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && !pwdDragging && pwdMouseDownPos != Point.Empty)
                {
                    Size dragSize = SystemInformation.DragSize;
                    Rectangle r = new Rectangle(pwdMouseDownPos.X - dragSize.Width / 2, pwdMouseDownPos.Y - dragSize.Height / 2, dragSize.Width, dragSize.Height);
                    if (!r.Contains(e.Location))
                    {
                        pwdDragging = true;
                        int idx = lbPwds.IndexFromPoint(pwdMouseDownPos);
                        if (idx >= 0) lbPwds.DoDragDrop((string)lbPwds.Items[idx], DragDropEffects.Move);
                    }
                }
            };
            lbPwds.MouseUp += delegate(object s, MouseEventArgs e) { pwdMouseDownPos = Point.Empty; pwdDragging = false; };
            lbPwds.DragOver += delegate(object s, DragEventArgs e) { e.Effect = DragDropEffects.Move; };
            lbPwds.DragDrop += delegate(object s, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
                string dragged = (string)e.Data.GetData(DataFormats.StringFormat);
                int fromIdx = UnpackCore.Passwords.IndexOf(dragged);
                Point pt = lbPwds.PointToClient(new Point(e.X, e.Y));
                int toIdx = lbPwds.IndexFromPoint(pt);
                if (fromIdx >= 0 && toIdx >= 0 && fromIdx != toIdx)
                {
                    UnpackCore.Passwords.RemoveAt(fromIdx);
                    UnpackCore.Passwords.Insert(toIdx, dragged);
                    PwdStore.Save(UnpackCore.Passwords);
                    RefreshPwdList();
                    lbPwds.SelectedIndex = toIdx;
                    Log("🔀 已调整密码顺序（将按新顺序尝试）");
                }
            };
            gbPwd.Controls.Add(lbPwds);
            btnDelPwd = new Button();
            btnDelPwd.Text = "删除选中";
            btnDelPwd.Click += delegate { DeletePassword(); };
            gbPwd.Controls.Add(btnDelPwd);
            btnClearPwd = new Button();
            btnClearPwd.Text = "清空";
            btnClearPwd.Click += delegate { ClearPasswords(); };
            gbPwd.Controls.Add(btnClearPwd);
            lblPwdHint = new Label();
            lblPwdHint.Text = "拖动可调整尝试顺序；密码错误时会弹窗提示输入；保存位置在【设置】页选择";
            lblPwdHint.ForeColor = Color.Gray;
            gbPwd.Controls.Add(lblPwdHint);

            // ---- 工作流页 ----
            Button btnWfAdd = NewBtn("手动添加", delegate { AddWorkflowDialog(); }, tabWorkflow, 12, 12, 30);
            Button btnWfDel = NewBtn("删除选中", delegate { DeleteWorkflow(); }, tabWorkflow, 132, 12, 30);
            Button btnWfToggle = NewBtn("启用/停用", delegate { ToggleWorkflow(); }, tabWorkflow, 252, 12, 30);
            Button btnWfRefresh = NewBtn("刷新", delegate { RefreshWorkflowList(); }, tabWorkflow, 372, 12, 30);
            lvWorkflow = new ListView();
            lvWorkflow.View = View.Details;
            lvWorkflow.FullRowSelect = true;
            lvWorkflow.Columns.Add("名称", 160);
            lvWorkflow.Columns.Add("步骤数", 60);
            lvWorkflow.Columns.Add("链路", 300);
            lvWorkflow.Columns.Add("备注", 150);
            tabWorkflow.Controls.Add(lvWorkflow);
            lblWfHint = new Label();
            lblWfHint.Text = "工作流记录已知来源的解压链路（每层格式 / 伪装格式 / 命中密码），解压成功后自动保存；下次同来源直接按链路解压、跳过探测与试错。\n存储于程序目录 工作流.xml，可随身携带。";
            lblWfHint.ForeColor = Color.Gray;
            tabWorkflow.Controls.Add(lblWfHint);

            // ---- 设置页（v1.6 起设置独立成选项卡，不再弹窗）----
            settingsPanel = new SettingsPanel(this);
            settingsPanel.Dock = DockStyle.Fill;
            tabSettings.Controls.Add(settingsPanel);

            // ---- 底部：按钮 / 进度 / 日志 ----
            btnStart = new Button();
            btnStart.Text = "开始解压";
            btnStart.BackColor = Color.FromArgb(0, 122, 204);
            btnStart.ForeColor = Color.White;
            btnStart.Font = new Font("Microsoft YaHei UI", 11f * dpiFactor, FontStyle.Bold);
            btnStart.Click += delegate { StartBatch(); };
            Controls.Add(btnStart);

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Enabled = false;
            btnCancel.Click += delegate { cancelFlag = true; UnpackCore.CancelRequested = true; Log("⏹ 已请求取消，正在终止当前任务…"); };
            Controls.Add(btnCancel);

            btnOpenOut = new Button();
            btnOpenOut.Text = "打开输出文件夹";
            btnOpenOut.Enabled = false;
            btnOpenOut.Click += delegate
            {
                if (!string.IsNullOrEmpty(lastSuccessPath))
                {
                    try { Process.Start("explorer.exe", "\"" + lastSuccessPath + "\""); }
                    catch (Exception ex) { Log("⚠ 打开失败：" + ex.Message); }
                }
            };
            Controls.Add(btnOpenOut);

            progTotal = new ProgressBar();
            progTotal.Minimum = 0;
            progTotal.Maximum = 100;
            progTotal.Style = ProgressBarStyle.Continuous;
            progTotal.Visible = false;
            Controls.Add(progTotal);

            progCurrent = new ProgressBar();
            progCurrent.Minimum = 0;
            progCurrent.Maximum = 100;
            progCurrent.Style = ProgressBarStyle.Continuous;
            progCurrent.Visible = false;
            Controls.Add(progCurrent);

            ToolTip tip = new ToolTip();
            tip.SetToolTip(progTotal, "任务总进度：已完成分组数 / 总分组数");
            tip.SetToolTip(progCurrent, "当前文件解压进度（来自 7-Zip 实时输出）");

            lblStatus = new Label();
            lblStatus.Text = "就绪";
            lblStatus.ForeColor = Color.Gray;
            Controls.Add(lblStatus);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(30, 30, 30);
            txtLog.ForeColor = Color.LightGreen;
            txtLog.Font = new Font("Consolas", 9f * dpiFactor);
            Controls.Add(txtLog);

            statusStrip = new StatusStrip();
            statusStrip.SizingGrip = false;
            ToolStripStatusLabel lblAbout = new ToolStripStatusLabel();
            lblAbout.Text = "Created by sz333  ｜  本软件完全免费，如通过购买获得，请立即退款  ｜  无需赞赏，愿天下开源";
            lblAbout.ForeColor = Color.FromArgb(90, 90, 90);
            statusStrip.Items.Add(lblAbout);
            Controls.Add(statusStrip);

            RefreshWorkflowList();
            Relayout();
            Resize += delegate { Relayout(); };
            Shown += delegate { ApplyTheme(); };
        }

        /// <summary>设置面板保存后回调：刷新密码列表等依赖项</summary>
        public void OnSettingsChanged()
        {
            try { RefreshPwdList(); } catch { }
        }

        // ================= 布局 =================

        private void Relayout()
        {
            int W = (int)(ClientSize.Width / dpiFactor);
            int H = (int)(ClientSize.Height / dpiFactor);
            if (W == lastRelayoutW && H == lastRelayoutH) return;
            lastRelayoutW = W;
            lastRelayoutH = H;
            if (W < 560 || H < 520) return;

            int margin = 10;
            int btnY = H - 150;
            int logTop = btnY + 52;
            int logBottom = H - 26;
            if (logBottom - logTop < 50) logBottom = logTop + 50;

            // 无菜单栏，选项卡从顶部开始（充分释放空间）
            Place(tabMain, margin, 8, W - margin * 2, btnY - 40);

            // 解压页：文件区 + 密码区（并排，文件列表最大化）
            int tabH = (btnY - 40) - 24;
            int pwdW = 250;
            int gb1w = W - margin * 2 - pwdW - 14;
            Place(gb1, margin, 0, gb1w, tabH);
            Place(gbPwd, margin + gb1w + 14, 0, pwdW, tabH);
            int bx = (int)Math.Round(12 * dpiFactor);
            int by = (int)Math.Round(36 * dpiFactor);
            int gapb = (int)Math.Round(20 * dpiFactor);
            Button[] fileButtons = new Button[] { btnAddFiles, btnAddFolder, btnRemoveSel, btnClear };
            foreach (Button fb in fileButtons)
            {
                fb.Location = new Point(bx, by);
                bx += fb.Width + gapb;
            }
            int hintX = (int)(bx / dpiFactor) + 14;
            Place(lblHint, hintX, 42, Math.Max(60, gb1w - hintX - 10), 20);
            Place(lvFiles, 12, 84, gb1w - 24, Math.Max(60, tabH - 96));
            try
            {
                int used = lvFiles.Columns[0].Width + lvFiles.Columns[1].Width;
                int remain = lvFiles.ClientSize.Width - used;
                if (remain > 60) lvFiles.Columns[2].Width = remain;
            }
            catch { }
            // 密码区内部
            Place(txtNewPwd, 10, 26, pwdW - 90, 24);
            Place(btnAddPwd, pwdW - 74, 25, 64, 26);
            Place(lbPwds, 10, 56, pwdW - 20, Math.Max(40, tabH - 120));
            Place(btnDelPwd, 10, tabH - 56, 90, 26);
            Place(btnClearPwd, 106, tabH - 56, 70, 26);
            Place(lblPwdHint, 10, tabH - 28, pwdW - 20, 20);

            // 工作流页：说明放顶部（按钮下方），列表占满剩余空间
            Place(lblWfHint, 12, 46, W - 44, 40);
            Place(lvWorkflow, 12, 92, W - 44, (btnY - 40) - 92 - 12);

            // 底部
            Place(btnStart, margin, btnY, 130, 40);
            Place(btnCancel, 146, btnY, 70, 40);
            Place(btnOpenOut, 222, btnY, 120, 40);
            int progW = W - 348 - 220 - margin;
            if (progW < 100) progW = 100;
            Place(progTotal, 348, btnY, progW, 16);
            Place(progCurrent, 348, btnY + 24, progW, 16);
            Place(lblStatus, W - 220 - margin, btnY + 4, 220, 30);
            Place(txtLog, margin, logTop, W - margin * 2, logBottom - logTop);
        }

        // ================= 文件选择 =================

        private void AddFiles()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "选择待解压文件（分卷请全部选中）";
            ofd.Multiselect = true;
            ofd.Filter = "所有文件 (*.*)|*.*";
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                foreach (string f in ofd.FileNames) AddFile(f);
            }
        }

        private void AddFolder()
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = "选择文件夹，将添加其中所有文件（含子文件夹）";
            if (fbd.ShowDialog(this) == DialogResult.OK)
            {
                try { foreach (string f in Directory.GetFiles(fbd.SelectedPath, "*", SearchOption.AllDirectories)) AddFile(f); }
                catch (Exception ex) { Log("⚠ 读取文件夹失败：" + ex.Message); }
            }
        }

        private void AddFile(string f)
        {
            if (!File.Exists(f)) return;
            if (selectedFiles.Contains(f)) return;
            selectedFiles.Add(f);
            RefreshList();
        }

        private void RemoveSelected()
        {
            foreach (ListViewItem item in lvFiles.SelectedItems) selectedFiles.Remove((string)item.Tag);
            RefreshList();
        }

        private void ClearFiles()
        {
            selectedFiles.Clear();
            RefreshList();
        }

        private void RefreshList()
        {
            lvFiles.BeginUpdate();
            lvFiles.Items.Clear();
            Dictionary<string, List<string>> groups = UnpackCore.GroupFiles(selectedFiles);
            Dictionary<string, string> fileToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> kv in groups)
                foreach (string f in kv.Value) fileToKey[f] = kv.Key;
            foreach (string f in selectedFiles)
            {
                string key;
                fileToKey.TryGetValue(f, out key);
                long len = 0;
                try { len = new FileInfo(f).Length; } catch { }
                string sizeText = len >= 1048576 ? string.Format("{0:N1} MB", len / 1048576.0) : (len >= 1024 ? string.Format("{0:N0} KB", len / 1024.0) : len + " B");
                ListViewItem item = new ListViewItem(Path.GetFileName(f));
                item.SubItems.Add(sizeText);
                item.SubItems.Add(key ?? "");
                item.Tag = f;
                lvFiles.Items.Add(item);
            }
            lvFiles.EndUpdate();
            lblStatus.Text = "已选 " + selectedFiles.Count + " 个文件，识别 " + UnpackCore.GroupFiles(selectedFiles).Count + " 组";
        }

        // ================= 工作流页 =================

        private void RefreshWorkflowList()
        {
            if (lvWorkflow == null) return;
            lvWorkflow.BeginUpdate();
            lvWorkflow.Items.Clear();
            foreach (Workflow wf in workflows)
            {
                ListViewItem item = new ListViewItem((wf.Enabled ? "" : "⏸ ") + wf.Name);
                item.SubItems.Add(wf.Steps.Count.ToString());
                StringBuilder sb = new StringBuilder();
                foreach (WorkflowStep st in wf.Steps)
                {
                    if (sb.Length > 0) sb.Append(" → ");
                    sb.Append(st.InputExt);
                    if (!string.IsNullOrEmpty(st.FakeExt) && !st.FakeExt.Equals(st.RealFormat == "" ? st.InputExt : "." + st.RealFormat, StringComparison.OrdinalIgnoreCase))
                        sb.Append("(伪装" + st.FakeExt + ")");
                    sb.Append("[" + (string.IsNullOrEmpty(st.RealFormat) ? "结果" : st.RealFormat) + "]");
                    if (!string.IsNullOrEmpty(st.Password)) sb.Append(" 密码:" + st.Password);
                }
                item.SubItems.Add(sb.ToString());
                item.SubItems.Add(wf.Note);
                item.Tag = wf;
                lvWorkflow.Items.Add(item);
            }
            lvWorkflow.EndUpdate();
        }

        private void AddWorkflowDialog()
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("请输入工作流名称（分组基本名包含它即命中）：", "添加工作流", "");
            if (string.IsNullOrEmpty(name)) return;
            name = name.Trim();
            if (name.Length < 2)
            {
                MessageBox.Show(this, "工作流名称至少 2 个字符，以免误匹配大量无关分组。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Workflow wf = new Workflow(name, null, "手动添加");
            workflows.Add(wf);
            WorkflowManager.Save(workflows);
            RefreshWorkflowList();
            Log("已添加工作流【" + wf.Name + "】。");
        }

        private void DeleteWorkflow()
        {
            foreach (ListViewItem item in lvWorkflow.SelectedItems)
            {
                Workflow wf = item.Tag as Workflow;
                if (wf != null) workflows.Remove(wf);
            }
            WorkflowManager.Save(workflows);
            RefreshWorkflowList();
        }

        private void ToggleWorkflow()
        {
            foreach (ListViewItem item in lvWorkflow.SelectedItems)
            {
                Workflow wf = item.Tag as Workflow;
                if (wf != null) wf.Enabled = !wf.Enabled;
            }
            WorkflowManager.Save(workflows);
            RefreshWorkflowList();
        }

        /// <summary>解压成功后按设置保存工作流（自动保存 / 每次询问 / 不保存）</summary>
        private void OfferSaveWorkflow(GroupResult res)
        {
            if (res == null || !res.Success || res.Steps == null || res.Steps.Count == 0) return;
            if (WorkflowManager.Match(workflows, res.BaseName) != null) return;   // 已命中工作流
            string mode = AppSettings.Get("WfSave", "auto");
            if (mode == "off") return;
            try
            {
                if (mode == "ask")
                {
                    BeginInvoke(new Action(delegate ()
                    {
                        DialogResult dr = MessageBox.Show(this, "分组【" + res.BaseName + "】解压成功。" + Environment.NewLine + "是否保存为工作流？下次同来源可直接按链路解压。" + Environment.NewLine + "（链路：每层格式/伪装格式/命中密码）", "保存工作流", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (dr == DialogResult.Yes) SaveWorkflow(res);
                    }));
                }
                else
                {
                    // 自动保存（静默，不弹窗打扰）
                    BeginInvoke(new Action(delegate () { SaveWorkflow(res); }));
                }
            }
            catch { }
        }

        private void SaveWorkflow(GroupResult res)
        {
            Workflow wf = new Workflow(res.BaseName, res.Steps, "自动记录 " + DateTime.Now.ToString("MM-dd HH:mm"));
            workflows.Add(wf);
            WorkflowManager.Save(workflows);
            RefreshWorkflowList();
            Log("✅ 已保存工作流【" + wf.Name + "】（" + wf.Steps.Count + " 步）。");
        }

        // ================= 开始 / 处理 =================

        private void StartBatch()
        {
            if (running) return;
            if (selectedFiles.Count == 0) { MessageBox.Show(this, "请先添加待解压文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            // 从设置读取并应用
            UnpackCore.DeleteAfterSuccess = AppSettings.GetBool("Delete", true);
            UnpackCore.DeduplicateEnabled = AppSettings.GetBool("Dedup", true);
            UnpackCore.MaxNestDepth = AppSettings.GetInt("MaxDepth", 30);
            bool useSource = AppSettings.GetBool("UseSource", true);
            string outDir = useSource ? null : AppSettings.Get("OutDir", "");
            if (!useSource && outDir.Length == 0) { MessageBox.Show(this, "请先在【设置】页配置输出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            Dictionary<string, List<string>> groups = UnpackCore.GroupFiles(selectedFiles);

            // 空间预检查
            List<string> warnList = new List<string>();
            foreach (KeyValuePair<string, List<string>> kv in groups)
            {
                string primary = UnpackCore.PickPrimary(kv.Value);
                if (primary == null) continue;
                long need = UnpackCore.GetArchiveTotalSize(primary);
                if (need <= 0) continue;
                string targetDir = string.IsNullOrEmpty(outDir) ? Path.GetDirectoryName(primary) : outDir;
                try
                {
                    DriveInfo di = new DriveInfo(Path.GetPathRoot(targetDir));
                    long free = di.AvailableFreeSpace;
                    if (free < need) warnList.Add("【" + kv.Key + "】预计需 " + FormatSize(need) + "，目标盘剩余 " + FormatSize(free) + "，空间不足！");
                }
                catch { }
            }
            if (warnList.Count > 0)
            {
                if (MessageBox.Show(this, "磁盘空间预检查：\n\n" + string.Join("\n", warnList) + "\n\n空间不足的解压很可能失败，是否仍要继续？", "空间不足警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    Log("⏹ 已取消：磁盘空间预检查未通过。");
                    return;
                }
            }

            running = true;
            cancelFlag = false;
            UnpackCore.CancelRequested = false;
            curDone = 0;
            curTotal = groups.Count;
            curPct = 0;
            progTotal.Value = 0;
            progCurrent.Value = 0;
            UnpackCore.OnArchiveProgress = delegate(int pct)
            {
                if (IsDisposed) return;
                try
                {
                    curPct = pct;
                    BeginInvoke(new Action(delegate () { if (progCurrent.Value != pct) progCurrent.Value = pct; UpdateStatusText(); }));
                }
                catch { }
            };
            SetBusy(true);
            Log("");
            Log("========== 开始处理，共 " + groups.Count + " 组 ==========");
            string outRoot = outDir;
            GuiLog logger = new GuiLog(this);
            Thread worker = new Thread(delegate () { RunBatchGroups(groups, outRoot, logger); });
            worker.IsBackground = true;
            worker.Start();
        }

        private void RunBatchGroups(Dictionary<string, List<string>> groups, string outRoot, GuiLog logger)
        {
            int okCount = 0, failCount = 0, cancelCount = 0;
            foreach (KeyValuePair<string, List<string>> kv in groups)
            {
                if (cancelFlag) { logger.Log("⏹ 已取消，剩余分组未处理。"); break; }
                // 工作流匹配：命中则按工作流链路解压
                Workflow wf = WorkflowManager.Match(workflows, kv.Key);
                if (wf != null)
                {
                    UnpackCore.ActiveWorkflow = wf;
                    logger.Log("⚡ 命中工作流【" + wf.Name + "】，按链路解压…");
                }
                GroupResult res = UnpackCore.ProcessGroup(kv.Value, outRoot, logger);
                // 密码错误：弹窗让用户输入正确密码并自动重试（最多 5 次，可取消）
                int pwdRetry = 0;
                while (res != null && !res.Success && !res.Cancelled && IsPwdError(res.Error) && pwdRetry < 5 && !cancelFlag)
                {
                    pwdRetry++;
                    string pwd = AskPassword(kv.Key);
                    if (string.IsNullOrEmpty(pwd))
                    {
                        logger.Log("  ⏹ 未输入新密码，分组【" + kv.Key + "】按失败处理。");
                        break;
                    }
                    if (!UnpackCore.Passwords.Contains(pwd))
                    {
                        UnpackCore.Passwords.Add(pwd);
                        PwdStore.Save(UnpackCore.Passwords);
                        try { BeginInvoke(new Action(delegate () { RefreshPwdList(); })); } catch { }
                        logger.Log("  ➕ 已添加密码：" + pwd + "，重新解压分组【" + kv.Key + "】…");
                    }
                    else
                    {
                        logger.Log("  🔁 使用密码列表中的密码重试分组【" + kv.Key + "】…");
                    }
                    res = UnpackCore.ProcessGroup(kv.Value, outRoot, logger);
                }
                if (res.Success)
                {
                    okCount++;
                    if (res.FinalPath != null) lastSuccessPath = res.FinalPath;
                    OfferSaveWorkflow(res);
                }
                else if (res.Cancelled)
                {
                    cancelCount++;
                }
                else
                {
                    failCount++;
                }
                curDone = okCount + failCount + cancelCount;
                try
                {
                    BeginInvoke(new Action(delegate () { progTotal.Value = curTotal == 0 ? 0 : (int)(curDone * 100.0 / curTotal); UpdateStatusText(); }));
                }
                catch { }
            }
            logger.Log("========== 处理完毕：成功 " + okCount + " 组，失败 " + failCount + " 组" + (cancelCount > 0 ? "，已取消 " + cancelCount + " 组" : "") + " ==========");
            FinishBatch(okCount, failCount, cancelCount);
        }

        // ================= 密码管理（主界面解压页）=================

        private void AddPassword()
        {
            string t = txtNewPwd.Text.Trim();
            if (t.Length == 0) return;
            if (!UnpackCore.Passwords.Contains(t))
            {
                UnpackCore.Passwords.Add(t);
                PwdStore.Save(UnpackCore.Passwords);
            }
            RefreshPwdList();
            txtNewPwd.Clear();
        }

        private void DeletePassword()
        {
            if (lbPwds.SelectedItem == null) return;
            string pwd = (string)lbPwds.SelectedItem;
            if (MessageBox.Show(this, "确定要删除密码【" + pwd + "】吗？", "删除密码", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UnpackCore.Passwords.Remove(pwd);
            PwdStore.Save(UnpackCore.Passwords);
            RefreshPwdList();
        }

        private void ClearPasswords()
        {
            if (UnpackCore.Passwords.Count == 0) return;
            if (MessageBox.Show(this, "确定要清空全部 " + UnpackCore.Passwords.Count + " 个密码吗？", "清空密码", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UnpackCore.Passwords.Clear();
            PwdStore.Save(UnpackCore.Passwords);
            RefreshPwdList();
        }

        private void RefreshPwdList()
        {
            if (lbPwds == null) return;
            lbPwds.BeginUpdate();
            lbPwds.Items.Clear();
            foreach (string p in UnpackCore.Passwords) lbPwds.Items.Add(p);
            lbPwds.EndUpdate();
        }

        private static bool IsPwdError(string error)
        {
            return error != null && error.IndexOf("密码", StringComparison.Ordinal) >= 0;
        }

        /// <summary>弹窗让用户输入正确密码（UI 线程模态输入框，worker 线程等待结果；超时/取消返回 null）</summary>
        private string AskPassword(string groupName)
        {
            string result = null;
            ManualResetEvent evt = new ManualResetEvent(false);
            try
            {
                BeginInvoke(new Action(delegate ()
                {
                    try
                    {
                        result = Microsoft.VisualBasic.Interaction.InputBox(
                            "分组【" + groupName + "】解压失败：密码错误或密码列表中没有正确密码。" + Environment.NewLine +
                            "请输入正确密码（将自动加入密码列表并重新解压）：", "请输入密码", "");
                    }
                    catch { }
                    finally { evt.Set(); }
                }));
                if (!evt.WaitOne(120000)) return null;
            }
            catch { }
            return string.IsNullOrEmpty(result) ? null : result.Trim();
        }

        private void FinishBatch(int okCount, int failCount, int cancelCount)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate ()
                {
                    bool wasCancelled = cancelFlag || cancelCount > 0;
                    running = false;
                    UnpackCore.OnArchiveProgress = null;
                    SetBusy(false);
                    if (wasCancelled)
                    {
                        lblStatus.Text = "已取消（成功 " + okCount + " 组" + (failCount > 0 ? "，失败 " + failCount + " 组" : "") + "）";
                        progTotal.Value = 0;
                        progCurrent.Value = 0;
                    }
                    else
                    {
                        lblStatus.Text = failCount == 0 ? "全部完成 ✔" : "完成（有失败项，详见日志）";
                        progTotal.Value = 100;
                        progCurrent.Value = 100;
                    }
                    if (okCount > 0)
                    {
                        SystemSounds.Exclamation.Play();
                        try { if (notifyIcon != null) notifyIcon.ShowBalloonTip(4000, "sz333解压工具", "处理完成：成功 " + okCount + " 组" + (failCount > 0 ? "，失败 " + failCount + " 组" : "") + (cancelCount > 0 ? "，已取消 " + cancelCount + " 组" : "") + "。", ToolTipIcon.Info); } catch { }
                        if (lastSuccessPath != null) btnOpenOut.Enabled = true;
                        if (AppSettings.GetBool("Shutdown", false))
                        {
                            try
                            {
                                Process.Start("shutdown.exe", "/s /t 60 /c \"sz333解压工具：任务完成，60 秒后自动关机；如需取消请运行 shutdown /a\"");
                                Log("⏻ 已安排 60 秒后自动关机（取消：运行 shutdown /a）");
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        SystemSounds.Asterisk.Play();
                    }
                    if (wasCancelled)
                    {
                        // 用户主动取消：不弹窗打扰，状态栏与日志已说明
                    }
                    else if (failCount > 0)
                    {
                        MessageBox.Show(this, "处理完成：成功 " + okCount + " 组，失败 " + failCount + " 组。" + Environment.NewLine + "失败详情请查看下方日志。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else if (AppSettings.GetBool("ShowFinishDialog", false))
                    {
                        MessageBox.Show(this, "全部处理成功！共 " + okCount + " 组。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }));
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            btnStart.Enabled = !busy;
            btnCancel.Enabled = busy;
            btnAddFiles.Enabled = !busy;
            btnAddFolder.Enabled = !busy;
            btnRemoveSel.Enabled = !busy;
            btnClear.Enabled = !busy;
            btnAddPwd.Enabled = !busy;
            btnDelPwd.Enabled = !busy;
            btnClearPwd.Enabled = !busy;
            progTotal.Visible = busy;
            progCurrent.Visible = busy;
        }

        private void UpdateStatusText()
        {
            string t = "任务 " + curDone + "/" + curTotal;
            if (curPct > 0 && curPct < 100) t += " · 当前解压 " + curPct + "%";
            lblStatus.Text = t;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return string.Format("{0:N1} GB", bytes / 1073741824.0);
            if (bytes >= 1048576) return string.Format("{0:N1} MB", bytes / 1048576.0);
            if (bytes >= 1024) return string.Format("{0:N0} KB", bytes / 1024.0);
            return bytes + " B";
        }

        // ================= 日志 =================

        public void AppendLog(string message)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate ()
                {
                    txtLog.AppendText(message + Environment.NewLine);
                    // 节流：不必每次追加都做 O(n) 截断检查，降低高频日志时的 UI 卡顿
                    logAppendOps++;
                    if (logAppendOps % 60 == 0 && txtLog.TextLength > 200000)
                    {
                        txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - 150000);
                    }
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.ScrollToCaret();
                }));
            }
            catch { }
        }

        private void Log(string message) { AppendLog(message); }

        private class GuiLog : ILog
        {
            private readonly MainForm form;
            public GuiLog(MainForm f) { form = f; }
            public void Log(string message) { form.AppendLog(message); }
        }
    }
}
