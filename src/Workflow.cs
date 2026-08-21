// ============================================================================
// 工作流：记录"已知来源压缩包的解压链路"，下次按链路直接解压，跳过探测与试错
// 每步记录三要素：该层输入扩展名 → 伪装扩展名/真实格式 → 命中密码
// 存储：exe 同目录 工作流.xml
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace DSUnpack
{
    /// <summary>工作流的一个解压步骤（一层）</summary>
    [Serializable]
    public class WorkflowStep
    {
        public string InputExt = "";      // 该层输入压缩包扩展名（如 .rar / .7z）
        public string FakeExt = "";       // 该层解出的单个文件的伪装扩展名（如 .JPG；无伪装则为真实扩展名）
        public string RealFormat = "";    // 该层真实格式（rar/zip/7z…；空表示该层即得到最终结果）
        public string Password = "";      // 该层命中密码（空 = 无密码）
        public string RenameTo = "";      // 改名目标扩展名（如 .rar；空 = 无需改名）

        public WorkflowStep() { }

        public WorkflowStep(string inputExt, string fakeExt, string realFormat, string password, string renameTo)
        {
            InputExt = inputExt;
            FakeExt = fakeExt;
            RealFormat = realFormat;
            Password = password;
            RenameTo = renameTo;
        }
    }

    [Serializable]
    public class Workflow
    {
        public string Name = "";                                  // 匹配关键字（分组基本名包含它即命中），如 "4114"
        public List<WorkflowStep> Steps = new List<WorkflowStep>(); // 解压链路（外层 → 内层）
        public string Note = "";                                  // 备注
        public DateTime Created = DateTime.Now;
        public bool Enabled = true;

        public Workflow() { }

        public Workflow(string name, List<WorkflowStep> steps, string note)
        {
            Name = name;
            if (steps != null) Steps = new List<WorkflowStep>(steps);
            Note = note;
            Created = DateTime.Now;
        }
    }

    public static class WorkflowManager
    {
        private static string FilePath
        {
            get
            {
                try { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "工作流.xml"); }
                catch { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "工作流.xml"); }
            }
        }

        public static List<Workflow> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(List<Workflow>));
                    using (FileStream fileStream = File.OpenRead(FilePath))
                    {
                        return (List<Workflow>)xmlSerializer.Deserialize(fileStream);
                    }
                }
            }
            catch
            {
            }
            return new List<Workflow>();
        }

        public static void Save(List<Workflow> list)
        {
            try
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(List<Workflow>));
                using (FileStream fileStream = File.Create(FilePath))
                {
                    xmlSerializer.Serialize(fileStream, list);
                }
            }
            catch
            {
            }
        }

        /// <summary>按分组基本名匹配工作流（包含匹配，只匹配启用的），返回第一个命中。
        /// 名称至少 2 个字符才参与匹配，避免单字符名称（如"1"）误命中大量无关分组。</summary>
        public static Workflow Match(List<Workflow> list, string baseName)
        {
            if (list == null || string.IsNullOrEmpty(baseName))
            {
                return null;
            }
            foreach (Workflow workflow in list)
            {
                if (workflow.Enabled && !string.IsNullOrEmpty(workflow.Name) && workflow.Name.Length >= 2 &&
                    baseName.IndexOf(workflow.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return workflow;
                }
            }
            return null;
        }
    }
}
