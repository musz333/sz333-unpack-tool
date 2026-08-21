using System;
using System.Windows.Forms;

namespace DSUnpack
{
internal static class Program
{
	[STAThread]
	private static void Main()
	{
		UnpackCore.SevenZipPath = UnpackCore.Resolve7z();
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		if (string.IsNullOrEmpty(UnpackCore.SevenZipPath))
		{
			MessageBox.Show("未找到 7z 解压组件（内嵌资源缺失，且系统未安装 7-Zip）。" + Environment.NewLine + "请重新获取完整版程序，或先安装 7-Zip。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		else
		{
			Application.Run(new MainForm());
		}
	}
}

}
