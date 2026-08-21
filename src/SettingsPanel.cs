// ============================================================================
// 设置面板：嵌入主界面【设置】选项卡（v1.6 起设置不再弹窗）
// 密码列表管理已移至主界面【解压】页，此处仅保留"密码保存位置"选项
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace DSUnpack
{
    public class SettingsPanel : UserControl
    {
        private readonly MainForm main;
        private float dpiFactor = 1f;

        private CheckBox chkUseSourceDir;
        private TextBox txtOutDir;
        private Button btnBrowseOut;
        private CheckBox chkDelete;
        private CheckBox chkDedup;
        private NumericUpDown nudMaxDepth;
        private RadioButton rbPwdReg;
        private RadioButton rbPwdFile;
        private Button btnThemeLight;
        private Button btnThemeDark;
        private CheckBox chkShutdown;
        private RadioButton rbWfAuto;
        private RadioButton rbWfAsk;
        private RadioButton rbWfOff;
        private CheckBox chkFinish;

        public SettingsPanel(MainForm owner)
        {
            main = owner;
            uint sysDpi = GetDpiForSystem();
            dpiFactor = (sysDpi == 0u ? 96u : sysDpi) / 96f;
            if (dpiFactor < 1f) dpiFactor = 1f;

            Font = new Font("Microsoft YaHei UI", 9f * dpiFactor);
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            BuildUI();
            LoadSettings();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private void Place(Control c, int x, int y, int w, int h)
        {
            c.SetBounds((int)Math.Round(x * dpiFactor), (int)Math.Round(y * dpiFactor), (int)Math.Round(w * dpiFactor), (int)Math.Round(h * dpiFactor));
        }

        private void BuildUI()
        {
            // ---- 输出 ----
            GroupBox gbOut = new GroupBox();
            gbOut.Text = "输出";
            Place(gbOut, 10, 8, 540, 66);
            Controls.Add(gbOut);
            chkUseSourceDir = new CheckBox();
            chkUseSourceDir.Text = "解压到源文件所在目录（推荐）";
            chkUseSourceDir.CheckedChanged += delegate { txtOutDir.Enabled = !chkUseSourceDir.Checked; btnBrowseOut.Enabled = !chkUseSourceDir.Checked; };
            Place(chkUseSourceDir, 12, 30, 250, 24);
            gbOut.Controls.Add(chkUseSourceDir);
            txtOutDir = new TextBox();
            Place(txtOutDir, 265, 30, 190, 24);
            gbOut.Controls.Add(txtOutDir);
            btnBrowseOut = new Button();
            btnBrowseOut.Text = "浏览…";
            btnBrowseOut.Click += delegate
            {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                fbd.Description = "选择输出目录";
                if (fbd.ShowDialog(this) == DialogResult.OK) txtOutDir.Text = fbd.SelectedPath;
            };
            Place(btnBrowseOut, 460, 29, 68, 26);
            gbOut.Controls.Add(btnBrowseOut);

            // ---- 解压选项 ----
            GroupBox gbOpt = new GroupBox();
            gbOpt.Text = "解压选项";
            Place(gbOpt, 10, 82, 540, 106);
            Controls.Add(gbOpt);
            chkDelete = new CheckBox();
            chkDelete.Text = "解压成功后彻底删除原始分卷与中间压缩包（不可恢复）";
            Place(chkDelete, 12, 28, 500, 24);
            gbOpt.Controls.Add(chkDelete);
            chkDedup = new CheckBox();
            chkDedup.Text = "自动剔除完全重复的文件（同名且大小相同），大小不同自动重命名";
            Place(chkDedup, 12, 56, 500, 24);
            gbOpt.Controls.Add(chkDedup);
            Label lblDepth = new Label();
            lblDepth.Text = "最大解压层数：";
            lblDepth.TextAlign = ContentAlignment.MiddleLeft;
            Place(lblDepth, 12, 84, 110, 24);
            gbOpt.Controls.Add(lblDepth);
            nudMaxDepth = new NumericUpDown();
            nudMaxDepth.Minimum = 1;
            nudMaxDepth.Maximum = 99;
            nudMaxDepth.Value = 30;
            Place(nudMaxDepth, 130, 82, 70, 24);
            gbOpt.Controls.Add(nudMaxDepth);
            Label lblDepthHint = new Label();
            lblDepthHint.Text = "（达到层数仍未解出文件夹则停止）";
            lblDepthHint.ForeColor = Color.Gray;
            Place(lblDepthHint, 210, 86, 300, 20);
            gbOpt.Controls.Add(lblDepthHint);

            // ---- 密码保存位置 ----
            GroupBox gbPwdLoc = new GroupBox();
            gbPwdLoc.Text = "密码保存位置（密码列表在【解压】页管理）";
            Place(gbPwdLoc, 10, 196, 540, 62);
            Controls.Add(gbPwdLoc);
            rbPwdReg = new RadioButton();
            rbPwdReg.Text = "本机注册表";
            Place(rbPwdReg, 12, 28, 150, 24);
            gbPwdLoc.Controls.Add(rbPwdReg);
            rbPwdFile = new RadioButton();
            rbPwdFile.Text = "程序目录（密码列表.txt）";
            Place(rbPwdFile, 170, 28, 260, 24);
            gbPwdLoc.Controls.Add(rbPwdFile);

            // ---- 解压成功后 ----
            GroupBox gbMisc = new GroupBox();
            gbMisc.Text = "解压成功后";
            Place(gbMisc, 10, 266, 540, 170);
            Controls.Add(gbMisc);
            Label lblWf = new Label();
            lblWf.Text = "工作流保存：";
            lblWf.TextAlign = ContentAlignment.MiddleLeft;
            Place(lblWf, 12, 28, 90, 24);
            gbMisc.Controls.Add(lblWf);
            rbWfAuto = new RadioButton();
            rbWfAuto.Text = "自动保存（推荐）";
            rbWfAuto.Checked = true;
            Place(rbWfAuto, 105, 28, 130, 24);
            gbMisc.Controls.Add(rbWfAuto);
            rbWfAsk = new RadioButton();
            rbWfAsk.Text = "每次询问";
            Place(rbWfAsk, 240, 28, 100, 24);
            gbMisc.Controls.Add(rbWfAsk);
            rbWfOff = new RadioButton();
            rbWfOff.Text = "不保存";
            Place(rbWfOff, 345, 28, 100, 24);
            gbMisc.Controls.Add(rbWfOff);
            chkFinish = new CheckBox();
            chkFinish.Text = "全部完成后弹出提示框（不勾选时用托盘通知代替，少打扰）";
            Place(chkFinish, 12, 56, 510, 24);
            gbMisc.Controls.Add(chkFinish);
            Label lblTheme = new Label();
            lblTheme.Text = "主题：";
            lblTheme.TextAlign = ContentAlignment.MiddleLeft;
            Place(lblTheme, 12, 86, 45, 26);
            gbMisc.Controls.Add(lblTheme);
            btnThemeLight = new Button();
            btnThemeLight.Text = "白色";
            btnThemeLight.FlatStyle = FlatStyle.Flat;
            btnThemeLight.Click += delegate { main.SetTheme("白色"); RefreshThemeButtons(); };
            Place(btnThemeLight, 62, 84, 84, 28);
            gbMisc.Controls.Add(btnThemeLight);
            btnThemeDark = new Button();
            btnThemeDark.Text = "深色";
            btnThemeDark.FlatStyle = FlatStyle.Flat;
            btnThemeDark.Click += delegate { main.SetTheme("深色"); RefreshThemeButtons(); };
            Place(btnThemeDark, 152, 84, 84, 28);
            gbMisc.Controls.Add(btnThemeDark);
            chkShutdown = new CheckBox();
            chkShutdown.Text = "全部完成后自动关机（60 秒倒计时，可运行 shutdown /a 取消）";
            Place(chkShutdown, 12, 120, 500, 24);
            gbMisc.Controls.Add(chkShutdown);

            // ---- 保存 ----
            Button btnSave = new Button();
            btnSave.Text = "保存设置";
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.BackColor = Color.FromArgb(0, 122, 204);
            btnSave.ForeColor = Color.White;
            btnSave.Click += delegate { SaveSettings(); };
            Place(btnSave, 450, 448, 100, 32);
            Controls.Add(btnSave);
            Label lblHint = new Label();
            lblHint.Text = "修改后点【保存设置】生效。密码列表请在【解压】页管理。";
            lblHint.ForeColor = Color.Gray;
            Place(lblHint, 12, 452, 400, 22);
            Controls.Add(lblHint);
        }

        private void LoadSettings()
        {
            chkUseSourceDir.Checked = AppSettings.GetBool("UseSource", true);
            txtOutDir.Text = AppSettings.Get("OutDir", "");
            chkDelete.Checked = AppSettings.GetBool("Delete", true);
            chkDedup.Checked = AppSettings.GetBool("Dedup", true);
            nudMaxDepth.Value = Math.Max(1, Math.Min(99, AppSettings.GetInt("MaxDepth", 30)));
            chkShutdown.Checked = AppSettings.GetBool("Shutdown", false);
            string wfMode = AppSettings.Get("WfSave", "auto");
            rbWfAuto.Checked = (wfMode != "ask" && wfMode != "off");
            rbWfAsk.Checked = (wfMode == "ask");
            rbWfOff.Checked = (wfMode == "off");
            chkFinish.Checked = AppSettings.GetBool("ShowFinishDialog", false);
            string mode = AppSettings.Get("PwdStore", "reg");
            rbPwdReg.Checked = (mode != "file");
            rbPwdFile.Checked = (mode == "file");
            RefreshThemeButtons();
        }

        private void SaveSettings()
        {
            AppSettings.SetBool("UseSource", chkUseSourceDir.Checked);
            AppSettings.Set("OutDir", chkUseSourceDir.Checked ? "" : txtOutDir.Text.Trim());
            AppSettings.SetBool("Delete", chkDelete.Checked);
            AppSettings.SetBool("Dedup", chkDedup.Checked);
            AppSettings.SetInt("MaxDepth", (int)nudMaxDepth.Value);
            AppSettings.SetBool("Shutdown", chkShutdown.Checked);
            AppSettings.Set("WfSave", rbWfAuto.Checked ? "auto" : (rbWfAsk.Checked ? "ask" : "off"));
            AppSettings.SetBool("ShowFinishDialog", chkFinish.Checked);
            AppSettings.Set("PwdStore", rbPwdFile.Checked ? "file" : "reg");
            UnpackCore.DeleteAfterSuccess = chkDelete.Checked;
            UnpackCore.DeduplicateEnabled = chkDedup.Checked;
            UnpackCore.MaxNestDepth = (int)nudMaxDepth.Value;
            // 密码保存位置可能变化，重新按新模式加载密码列表
            PwdStore.Load(UnpackCore.Passwords);
            main.OnSettingsChanged();
            main.AppendLog("✅ 设置已保存。");
        }

        private void RefreshThemeButtons()
        {
            bool dark = AppSettings.Get("Theme", "白色") == "深色";
            btnThemeLight.BackColor = dark ? SystemColors.Control : Color.FromArgb(185, 212, 240);
            btnThemeDark.BackColor = dark ? Color.FromArgb(98, 98, 108) : SystemColors.Control;
        }
    }
}
