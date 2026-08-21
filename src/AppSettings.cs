// ============================================================================
// 应用设置存取（注册表 HKCU\Software\DSUnpack）
// ============================================================================
using Microsoft.Win32;

namespace DSUnpack
{
    public static class AppSettings
    {
        private const string KeyName = @"Software\DSUnpack";

        public static string Get(string name, string def)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyName))
                {
                    string v = key != null ? (key.GetValue(name) as string) : null;
                    return v ?? def;
                }
            }
            catch { return def; }
        }

        public static void Set(string name, string val)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyName))
                {
                    if (key != null) key.SetValue(name, val);
                }
            }
            catch { }
        }

        public static bool GetBool(string name, bool def)
        {
            string v = Get(name, def ? "1" : "0");
            return v == "1";
        }

        public static void SetBool(string name, bool val)
        {
            Set(name, val ? "1" : "0");
        }

        public static int GetInt(string name, int def)
        {
            int r;
            return int.TryParse(Get(name, def.ToString()), out r) ? r : def;
        }

        public static void SetInt(string name, int val)
        {
            Set(name, val.ToString());
        }
    }
}
