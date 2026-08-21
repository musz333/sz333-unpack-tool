// ============================================================================
// 密码存取：注册表 / 程序目录 密码列表.txt 两种模式（位置由设置决定）
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DSUnpack
{
    public static class PwdStore
    {
        private const string RegKeyName = @"Software\DSUnpack";

        public static string Mode
        {
            get { return AppSettings.Get("PwdStore", "reg"); }
        }

        private static string PwdFile
        {
            get
            {
                try { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "密码列表.txt"); }
                catch { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "密码列表.txt"); }
            }
        }

        /// <summary>把密码加载到目标列表（两种模式）</summary>
        public static void Load(List<string> target)
        {
            target.Clear();
            try
            {
                if (Mode == "file")
                {
                    if (File.Exists(PwdFile))
                    {
                        foreach (string line in File.ReadAllLines(PwdFile, Encoding.UTF8))
                        {
                            string t = line.Trim();
                            if (t.Length > 0 && !t.StartsWith("#") && !target.Contains(t)) target.Add(t);
                        }
                    }
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKeyName))
                    {
                        string[] arr = key != null ? (key.GetValue("Passwords") as string[]) : null;
                        if (key != null && arr != null)
                        {
                            foreach (string s in arr)
                            {
                                string t = (s ?? "").Trim();
                                if (t.Length > 0 && !target.Contains(t)) target.Add(t);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>把密码列表保存到当前模式</summary>
        public static void Save(List<string> source)
        {
            try
            {
                if (Mode == "file")
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("# 解压密码列表：每行一个密码，# 开头为注释，可直接用记事本编辑");
                    foreach (string p in source) sb.AppendLine(p);
                    File.WriteAllText(PwdFile, sb.ToString(), Encoding.UTF8);
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegKeyName))
                    {
                        if (key != null) key.SetValue("Passwords", source.ToArray(), RegistryValueKind.MultiString);
                    }
                }
            }
            catch { }
        }
    }
}
