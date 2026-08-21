using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DSUnpack
{
public static class UnpackCore
{
	public static int MaxNestDepth = 30;

	public static string SevenZipPath;

	public static readonly List<string> Passwords = new List<string>();

	/// <summary>优先级密码（工作流命中时使用，先于全局密码尝试）；null 表示不使用</summary>
	public static List<string> PriorityPasswords = null;

	/// <summary>当前分组命中的工作流（由界面在调用 ProcessGroup 前设置；null = 不使用工作流）</summary>
	public static Workflow ActiveWorkflow = null;

	public static bool TryNoPasswordFirst = true;

	public static bool DeleteAfterSuccess = true;

	public static bool DeduplicateEnabled = true;

	/// <summary>用户请求取消标志（由界面设置；解压循环与 7z/WinRAR 进程检查后立即中断）</summary>
	public static volatile bool CancelRequested = false;

	/// <summary>当前正在运行的 7z / WinRAR 进程（取消时 Kill）</summary>
	private static Process ActiveProcess = null;

	private static readonly Regex RePart = new Regex("\\.part(\\d+)$", RegexOptions.IgnoreCase);

	private static readonly Regex ReNum = new Regex("\\.(\\d{3})$");

	private static readonly Regex ReZ = new Regex("\\.z(\\d{2})$", RegexOptions.IgnoreCase);

	private static readonly Regex ReR = new Regex("\\.r(\\d{2})$", RegexOptions.IgnoreCase);

	private static readonly string[] KnownExts = new string[9] { ".zip", ".rar", ".7z", ".gz", ".gzip", ".bz2", ".xz", ".tar", ".cab" };

	private static readonly Regex RePartFull = new Regex("\\.part(\\d+)(?:\\.(?:rar|zip|7z))?$", RegexOptions.IgnoreCase);

	private static readonly Regex ReNumFull = new Regex("\\.(\\d{3})$");

	private static readonly Regex ReZFull = new Regex("\\.z(\\d{2})$", RegexOptions.IgnoreCase);

	private static readonly Regex ReRFull = new Regex("\\.r(\\d{2})$", RegexOptions.IgnoreCase);

	public static Action<int> OnArchiveProgress;

	public static bool SanitizeEnabled = true;

	private static readonly Regex RePct = new Regex("(\\d{1,3})\\s*%");

	private static readonly Regex ReSizeLine = new Regex("^Size\\s*=\\s*(\\d+)$", RegexOptions.IgnoreCase);

	private static readonly Regex ReDupSuffix = new Regex("^(.*?)(?:\\((\\d+)\\))?(\\.[^.\\/\\\\]*)?$");

	private static readonly char[] InvalidFileNameChars = new char[9] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

	private static readonly string[] ReservedNames = new string[22]
	{
		"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
		"COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7",
		"LPT8", "LPT9"
	};

	public static string DetectFormat(string path)
	{
		try
		{
			byte[] array = Fs.ReadHeader(path, 520);
			if (array == null)
			{
				return null;
			}
			int num = array.Length;
			if (num >= 4 && array[0] == 80 && array[1] == 75 && (array[2] == 3 || array[2] == 5 || array[2] == 7) && array[3] == 4)
			{
				return "zip";
			}
			if (num >= 8 && array[0] == 82 && array[1] == 97 && array[2] == 114 && array[3] == 33 && array[4] == 26 && array[5] == 7 && array[6] == 1 && array[7] == 0)
			{
				return "rar";
			}
			if (num >= 7 && array[0] == 82 && array[1] == 97 && array[2] == 114 && array[3] == 33 && array[4] == 26 && array[5] == 7 && array[6] == 0)
			{
				return "rar";
			}
			if (num >= 6 && array[0] == 55 && array[1] == 122 && array[2] == 188 && array[3] == 175 && array[4] == 39 && array[5] == 28)
			{
				return "7z";
			}
			if (num >= 3 && array[0] == 31 && array[1] == 139 && array[2] == 8)
			{
				return "gz";
			}
			if (num >= 3 && array[0] == 66 && array[1] == 90 && array[2] == 104)
			{
				return "bz2";
			}
			if (num >= 6 && array[0] == 253 && array[1] == 55 && array[2] == 122 && array[3] == 88 && array[4] == 90 && array[5] == 0)
			{
				return "xz";
			}
			if (num >= 262 && array[257] == 117 && array[258] == 115 && array[259] == 116 && array[260] == 97 && array[261] == 114)
			{
				return "tar";
			}
			if (num >= 4 && array[0] == 77 && array[1] == 83 && array[2] == 67 && array[3] == 70)
			{
				return "cab";
			}
			if (num >= 2 && array[0] == 77 && array[1] == 90)
			{
				return "exe";
			}
		}
		catch
		{
		}
		return null;
	}

	public static string ExtToFormat(string path)
	{
		switch (Path.GetExtension(path).ToLowerInvariant())
		{
		case ".zip":
			return "zip";
		case ".rar":
			return "rar";
		case ".7z":
			return "7z";
		case ".gz":
		case ".gzip":
			return "gz";
		case ".bz2":
			return "bz2";
		case ".xz":
			return "xz";
		case ".tar":
			return "tar";
		case ".cab":
			return "cab";
		default:
			return null;
		}
	}

	public static string NormalizeKey(string fileName)
	{
		string text = fileName;
		int num = text.LastIndexOf('.');
		if (num > 0)
		{
			text = text.Substring(0, num);
		}
		bool flag = true;
		while (flag && text.Length > 0)
		{
			flag = false;
			Match match = RePart.Match(text);
			if (match.Success)
			{
				text = text.Substring(0, match.Index);
				flag = true;
				continue;
			}
			match = ReNum.Match(text);
			if (match.Success)
			{
				text = text.Substring(0, match.Index);
				flag = true;
				continue;
			}
			match = ReZ.Match(text);
			if (match.Success)
			{
				text = text.Substring(0, match.Index);
				flag = true;
				continue;
			}
			match = ReR.Match(text);
			if (match.Success)
			{
				text = text.Substring(0, match.Index);
				flag = true;
				continue;
			}
			string[] knownExts = KnownExts;
			foreach (string text2 in knownExts)
			{
				if (text.EndsWith(text2, StringComparison.OrdinalIgnoreCase))
				{
					text = text.Substring(0, text.Length - text2.Length);
					flag = true;
					break;
				}
			}
		}
		return text;
	}

	public static Dictionary<string, List<string>> GroupFiles(List<string> files)
	{
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string file in files)
		{
			string text = NormalizeKey(GetFileNameSafe(file));
			if (text.Length == 0)
			{
				text = GetFileNameSafe(file);
			}
			List<string> value;
			if (!dictionary.TryGetValue(text, out value))
			{
				value = (dictionary[text] = new List<string>());
			}
			value.Add(file);
		}
		return dictionary;
	}

	public static string PickPrimary(List<string> groupFiles)
	{
		string text = PickMin(groupFiles, RePartFull);
		if (text != null)
		{
			return text;
		}
		text = PickMin(groupFiles, ReNumFull);
		if (text != null)
		{
			return text;
		}
		text = PickMin(groupFiles, ReZFull);
		if (text != null)
		{
			return text;
		}
		text = PickMin(groupFiles, ReRFull);
		if (text != null)
		{
			foreach (string groupFile in groupFiles)
			{
				switch (Path.GetExtension(groupFile).ToLowerInvariant())
				{
				case ".rar":
				case ".zip":
				case ".7z":
					return groupFile;
				}
			}
			return text;
		}
		if (groupFiles.Count > 0)
		{
			return groupFiles[0];
		}
		return null;
	}

	private static string PickMin(List<string> files, Regex rx)
	{
		string result = null;
		int num = int.MaxValue;
		string strB = null;
		foreach (string file in files)
		{
			Match match = rx.Match(GetFileNameSafe(file));
			int result2;
			if (match.Success && int.TryParse(match.Groups[1].Value, out result2))
			{
				string fileName = GetFileNameSafe(file);
				if (result2 < num || (result2 == num && string.Compare(fileName, strB, StringComparison.OrdinalIgnoreCase) < 0))
				{
					num = result2;
					result = file;
					strB = fileName;
				}
			}
		}
		return result;
	}

	public static string Resolve7z()
	{
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "DSUnpack_7z");
			Directory.CreateDirectory(text);
			Assembly executingAssembly = Assembly.GetExecutingAssembly();
			ExtractResource(executingAssembly, "DSUnpack.7zExe", Path.Combine(text, "7z.exe"));
			ExtractResource(executingAssembly, "DSUnpack.7zDll", Path.Combine(text, "7z.dll"));
			return Path.Combine(text, "7z.exe");
		}
		catch
		{
		}
		string text2 = "C:\\Program Files\\7-Zip\\7z.exe";
		if (File.Exists(text2))
		{
			return text2;
		}
		return null;
	}

	private static void ExtractResource(Assembly asm, string name, string target)
	{
		using (Stream stream = asm.GetManifestResourceStream(name))
		{
			if (stream == null)
			{
				throw new FileNotFoundException("缺少内嵌资源：" + name);
			}
			using (FileStream fileStream = new FileStream(target, FileMode.Create, FileAccess.Write))
			{
				byte[] array = new byte[65536];
				int count;
				while ((count = stream.Read(array, 0, array.Length)) > 0)
				{
					fileStream.Write(array, 0, count);
				}
			}
		}
	}

	private static int Run7z(string args, out string output)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo();
		processStartInfo.FileName = SevenZipPath;
		processStartInfo.Arguments = args;
		processStartInfo.UseShellExecute = false;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.StandardOutputEncoding = Encoding.UTF8;
		processStartInfo.StandardErrorEncoding = Encoding.UTF8;
		StringBuilder sb = new StringBuilder();
		using (Process process = Process.Start(processStartInfo))
		{
			ActiveProcess = process;
			try
			{
			process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				lock (sb)
				{
					sb.AppendLine(e.Data);
				}
			}
		};
		process.BeginErrorReadLine();
		StringBuilder stringBuilder = new StringBuilder();
		char[] array = new char[4096];
		int num = -1;
		while (true)
		{
			int num2;
			try
			{
				num2 = process.StandardOutput.Read(array, 0, array.Length);
			}
			catch
			{
				break;
			}
			if (num2 <= 0)
			{
				break;
			}
			for (int i = 0; i < num2; i++)
			{
				char c = array[i];
				switch (c)
				{
				case '\r':
				{
					string text = stringBuilder.ToString();
					stringBuilder.Length = 0;
					Match match = RePct.Match(text);
					if (match.Success)
					{
						int result;
						if (int.TryParse(match.Groups[1].Value, out result) && result != num && OnArchiveProgress != null)
						{
							num = result;
							OnArchiveProgress(result);
						}
					}
					else if (text.Trim().Length > 0)
					{
						lock (sb)
						{
							sb.AppendLine(text.Trim());
						}
					}
					break;
				}
				case '\n':
					lock (sb)
					{
						sb.AppendLine(stringBuilder.ToString());
					}
					stringBuilder.Length = 0;
					break;
				default:
					stringBuilder.Append(c);
					break;
				}
			}
			// 取消检查：用户点【取消】立即终止 7z 进程
			if (CancelRequested)
			{
				try { process.Kill(); } catch { }
				break;
			}
		}
		if (stringBuilder.Length > 0)
		{
			lock (sb)
			{
				sb.AppendLine(stringBuilder.ToString());
			}
		}
			process.WaitForExit();
			output = sb.ToString();
			return process.ExitCode;
			}
			finally
			{
				ActiveProcess = null;
			}
		}
	}

	public static ExtractResult ExtractArchive(string archivePath, string outDir, string password)
	{
		try
		{
			Fs.DirCreate(outDir);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("x -y -sccUTF-8");
			stringBuilder.Append(" -o\"").Append(outDir).Append('"');
			stringBuilder.Append(" -p\"").Append(password ?? "").Append('"');
			stringBuilder.Append(" -- \"").Append(archivePath).Append('"');
			string output;
			int num = Run7z(stringBuilder.ToString(), out output);
			if (CancelRequested)
			{
				ExtractResult cancelResult = new ExtractResult();
				cancelResult.Ok = false;
				cancelResult.Cancelled = true;
				cancelResult.Error = "已取消";
				return cancelResult;
			}
			bool flag = num == 2 || num == 255 || output.IndexOf("Wrong password", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("Cannot open encrypted archive", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("Incorrect password", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("Enter password", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("Break signaled", StringComparison.OrdinalIgnoreCase) >= 0;
			if (num == 0 || num == 1)
			{
				ExtractResult extractResult = new ExtractResult();
				extractResult.Ok = true;
				extractResult.UsedPassword = password;
				return extractResult;
			}
			if (flag)
			{
				ExtractResult extractResult2 = new ExtractResult();
				extractResult2.Ok = false;
				extractResult2.PasswordError = true;
				extractResult2.Error = "密码错误或文件损坏（密码：" + (password ?? "无密码") + "）";
				return extractResult2;
			}
			ExtractResult extractResult3 = new ExtractResult();
			extractResult3.Ok = false;
			extractResult3.Error = "解压失败（7z 返回码 " + num + "）：" + Tail(output);
			return extractResult3;
		}
		catch (Exception ex)
		{
			ExtractResult extractResult4 = new ExtractResult();
			extractResult4.Ok = false;
			extractResult4.Error = "解压异常：" + ex.Message;
			return extractResult4;
		}
	}

	public static ExtractResult ExtractWithPasswords(string archivePath, string outDir, ILog log)
	{
		List<string> list = new List<string>();
		if (TryNoPasswordFirst)
		{
			list.Add(null);
		}
		// 优先级密码（工作流）在前
		if (PriorityPasswords != null)
		{
			foreach (string password2 in PriorityPasswords)
			{
				if (!string.IsNullOrEmpty(password2) && !list.Contains(password2))
				{
					list.Add(password2);
				}
			}
		}
		foreach (string password in Passwords)
		{
			if (!string.IsNullOrEmpty(password) && !list.Contains(password))
			{
				list.Add(password);
			}
		}
		if (list.Count == 0)
		{
			list.Add(null);
		}
		string text = null;
		foreach (string item in list)
		{
			if (CancelRequested)
			{
				break;
			}
			ExtractResult extractResult = ExtractArchive(archivePath, outDir, item);
			if (extractResult.Ok)
			{
				return extractResult;
			}
			if (extractResult.Cancelled)
			{
				return extractResult;
			}
			text = extractResult.Error;
			if (!extractResult.PasswordError)
			{
				break;
			}
		}
		if (CancelRequested)
		{
			ExtractResult cancelResult = new ExtractResult();
			cancelResult.Ok = false;
			cancelResult.Cancelled = true;
			cancelResult.Error = "已取消";
			return cancelResult;
		}
		if (text != null && text.IndexOf("密码", StringComparison.Ordinal) >= 0)
		{
			text = "密码错误或文件损坏（已尝试无密码及密码列表中的全部密码）";
		}
		// 7z 全部失败后，若本机安装了 WinRAR，回退用 WinRAR 再试一遍（RAR 等格式兼容性更好）
		string winRar = FindWinRar();
		if (winRar != null)
		{
			foreach (string item2 in list)
			{
				if (CancelRequested)
				{
					break;
				}
				ExtractResult extractResult3 = ExtractWithWinRar(winRar, archivePath, outDir, item2);
				if (extractResult3.Ok)
				{
					log.Log("  （7z 失败，回退 WinRAR 解压成功" + (extractResult3.UsedPassword != null ? "，密码：" + extractResult3.UsedPassword : "") + "）");
					return extractResult3;
				}
				if (extractResult3.Cancelled)
				{
					return extractResult3;
				}
			}
		}
		if (CancelRequested)
		{
			ExtractResult cancelResult2 = new ExtractResult();
			cancelResult2.Ok = false;
			cancelResult2.Cancelled = true;
			cancelResult2.Error = "已取消";
			return cancelResult2;
		}
		ExtractResult extractResult2 = new ExtractResult();
		extractResult2.Ok = false;
		extractResult2.Error = text;
		return extractResult2;
	}

	// ---- WinRAR 回退（7z 解压失败时使用本机安装的 WinRAR） ----

	private static string winRarPath;
	private static bool winRarChecked;

	private static string FindWinRar()
	{
		if (winRarChecked)
		{
			return winRarPath;
		}
		winRarChecked = true;
		string[] array = new string[]
		{
			@"C:\Program Files\WinRAR\UnRAR.exe",
			@"C:\Program Files\WinRAR\Rar.exe",
			@"C:\Program Files (x86)\WinRAR\UnRAR.exe",
			@"C:\Program Files (x86)\WinRAR\Rar.exe"
		};
		foreach (string text in array)
		{
			if (Fs.FileExists(text))
			{
				winRarPath = text;
				return text;
			}
		}
		winRarPath = null;
		return null;
	}

	private static ExtractResult ExtractWithWinRar(string winRar, string archivePath, string outDir, string password)
	{
		try
		{
			Fs.DirCreate(outDir);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("x -y -o+ ");
			if (string.IsNullOrEmpty(password))
			{
				stringBuilder.Append("-p- ");
			}
			else
			{
				stringBuilder.Append("-p\"").Append(password).Append("\" ");
			}
			stringBuilder.Append("\"").Append(archivePath).Append("\" \"").Append(outDir).Append("\\\"");
			string output;
			int num = RunProcess(winRar, stringBuilder.ToString(), out output);
			if (CancelRequested)
			{
				ExtractResult cancelResult = new ExtractResult();
				cancelResult.Ok = false;
				cancelResult.Cancelled = true;
				cancelResult.Error = "已取消";
				return cancelResult;
			}
			bool flag = output.IndexOf("Wrong password", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("password incorrect", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("CRC failed", StringComparison.OrdinalIgnoreCase) >= 0;
			if (num == 0 || num == 1)
			{
				ExtractResult extractResult = new ExtractResult();
				extractResult.Ok = true;
				extractResult.UsedPassword = password;
				return extractResult;
			}
			ExtractResult extractResult2 = new ExtractResult();
			extractResult2.Ok = false;
			extractResult2.PasswordError = flag;
			extractResult2.Error = "WinRAR 解压失败（返回码 " + num + "）：" + Tail(output);
			return extractResult2;
		}
		catch (Exception ex)
		{
			ExtractResult extractResult3 = new ExtractResult();
			extractResult3.Ok = false;
			extractResult3.Error = "WinRAR 解压异常：" + ex.Message;
			return extractResult3;
		}
	}

	private static int RunProcess(string exe, string args, out string output)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo();
		processStartInfo.FileName = exe;
		processStartInfo.Arguments = args;
		processStartInfo.UseShellExecute = false;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		processStartInfo.CreateNoWindow = true;
		// WinRAR 输出使用系统代码页（中文 Windows 为 GBK），UTF-8 解码会乱码
		processStartInfo.StandardOutputEncoding = Encoding.Default;
		processStartInfo.StandardErrorEncoding = Encoding.Default;
		StringBuilder sb = new StringBuilder();
		using (Process process = Process.Start(processStartInfo))
		{
			ActiveProcess = process;
			try
			{
			process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					lock (sb)
					{
						sb.AppendLine(e.Data);
					}
				}
			};
			process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					lock (sb)
					{
						sb.AppendLine(e.Data);
					}
				}
			};
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			// 轮询等待，期间支持用户取消（立即 Kill WinRAR 进程）
			while (!process.WaitForExit(200))
			{
				if (CancelRequested)
				{
					try { process.Kill(); } catch { }
					break;
				}
			}
			output = sb.ToString();
			return process.ExitCode;
			}
			finally
			{
				ActiveProcess = null;
			}
		}
	}

	private static string Tail(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "（无输出）";
		}
		string[] array = text.Replace("\r", "").Split('\n');
		int num = Math.Max(0, array.Length - 3);
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = num; i < array.Length; i++)
		{
			if (array[i].Trim().Length > 0)
			{
				stringBuilder.Append(array[i].Trim()).Append("；");
			}
		}
		string text2 = stringBuilder.ToString();
		if (text2.Length > 500)
		{
			text2 = text2.Substring(text2.Length - 500);
		}
		return text2;
	}

	private static void CollectTree(string root, List<string> files, List<string> dirs)
	{
		try
		{
			string[] dirs2 = Fs.GetDirs(root);
			foreach (string text in dirs2)
			{
				dirs.Add(text);
				CollectTree(text, files, dirs);
			}
			string[] files2 = Fs.GetFiles(root);
			foreach (string item in files2)
			{
				files.Add(item);
			}
		}
		catch
		{
		}
	}

	public static void TryDeleteFile(string f, ILog log)
	{
		try
		{
			if (Fs.FileExists(f))
			{
				Fs.SetNormal(f);
				Fs.FileDelete(f);
				log.Log("  已删除：" + GetFileNameSafe(f));
			}
		}
		catch (Exception ex)
		{
			log.Log("  ⚠ 删除失败：" + f + "（" + ex.Message + "）");
		}
	}

	public static long GetArchiveTotalSize(string archivePath)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("l -slt -sccUTF-8 -p\"\" -- \"").Append(archivePath).Append('"');
			string output;
			int num = Run7z(stringBuilder.ToString(), out output);
			if (num != 0 && num != 1)
			{
				return -1L;
			}
			long num2 = 0L;
			string[] array = output.Split('\n');
			foreach (string text in array)
			{
				Match match = ReSizeLine.Match(text.Trim());
				long result;
				if (match.Success && long.TryParse(match.Groups[1].Value, out result))
				{
					num2 += result;
				}
			}
			return num2;
		}
		catch
		{
			return -1L;
		}
	}

	public static void SanitizeNames(string root, ILog log)
	{
		if (!SanitizeEnabled || !Fs.DirExists(root))
		{
			return;
		}
		int num = 0;
		try
		{
			List<string> list = new List<string>();
			CollectDirs(root, list);
			list.Sort((string a, string b) => b.Length.CompareTo(a.Length));
			foreach (string item in list)
			{
				if (RenameIfNeeded(item, false, log))
				{
					num++;
				}
			}
			List<string> list2 = new List<string>();
			CollectTree(root, list2, new List<string>());
			foreach (string item2 in list2)
			{
				if (RenameIfNeeded(item2, true, log))
				{
					num++;
				}
			}
			if (num > 0)
			{
				log.Log("  （名称清洗：修正 " + num + " 个非法名称/超长路径）");
			}
		}
		catch (Exception ex)
		{
			log.Log("  ⚠ 名称清洗异常：" + ex.Message);
		}
	}

	private static void CollectDirs(string root, List<string> dirs)
	{
		try
		{
			string[] dirs2 = Fs.GetDirs(root);
			foreach (string text in dirs2)
			{
				dirs.Add(text);
				CollectDirs(text, dirs);
			}
		}
		catch
		{
		}
	}

	private static bool RenameIfNeeded(string path, bool isFile, ILog log)
	{
		string fileNameSafe = GetFileNameSafe(path);
		string text = CleanName(fileNameSafe);
		string dirNameSafe = GetDirNameSafe(path);
		if (dirNameSafe.Length > 0 && dirNameSafe.Length + 1 + text.Length > 240)
		{
			int length = GetExtSafe(text).Length;
			int num = Math.Max(8, 239 - dirNameSafe.Length - length);
			string stemSafe = GetStemSafe(text);
			text = ((stemSafe.Length > num) ? stemSafe.Substring(0, num) : stemSafe) + GetExtSafe(text);
		}
		if (text == fileNameSafe)
		{
			return false;
		}
		string d = ((dirNameSafe.Length > 0) ? (dirNameSafe + "\\" + text) : text);
		try
		{
			if (isFile)
			{
				Fs.FileMove(path, d);
			}
			else
			{
				Fs.DirMove(path, d);
			}
			log.Log("  ✏ 清洗名称：" + fileNameSafe + " → " + text);
			return true;
		}
		catch (Exception ex)
		{
			log.Log("  ⚠ 改名失败：" + fileNameSafe + "（" + ex.Message + "）");
			return false;
		}
	}

	private static string GetFileNameSafe(string p)
	{
		int num = p.LastIndexOf('\\');
		if (num < 0)
		{
			return p;
		}
		return p.Substring(num + 1);
	}

	private static string GetDirNameSafe(string p)
	{
		int num = p.LastIndexOf('\\');
		if (num <= 0)
		{
			return "";
		}
		return p.Substring(0, num);
	}

	private static string GetExtSafe(string n)
	{
		int num = n.LastIndexOf('.');
		if (num <= 0)
		{
			return "";
		}
		return n.Substring(num);
	}

	private static string GetStemSafe(string n)
	{
		int num = n.LastIndexOf('.');
		if (num <= 0)
		{
			return n;
		}
		return n.Substring(0, num);
	}

	/// <summary>纯字符串改扩展名（替代 Path.ChangeExtension，超长路径安全）</summary>
	private static string ChangeExtSafe(string path, string ext)
	{
		int num = path.LastIndexOf('.');
		if (num > 0)
		{
			return path.Substring(0, num) + ext;
		}
		return path + ext;
	}

	private static string CleanName(string name)
	{
		string text = name;
		char[] invalidFileNameChars = InvalidFileNameChars;
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		text = text.TrimEnd('.', ' ');
		if (text.Length == 0)
		{
			text = "_";
		}
		int num = text.IndexOf('.');
		string a = ((num >= 0) ? text.Substring(0, num) : text);
		string[] reservedNames = ReservedNames;
		foreach (string b in reservedNames)
		{
			if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
			{
				text = "_" + text;
				break;
			}
		}
		if (text.Length > 180)
		{
			text = text.Substring(0, 180);
		}
		return text;
	}

	public static void Deduplicate(string root, ILog log)
	{
		if (!DeduplicateEnabled || !Fs.DirExists(root))
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		try
		{
			List<string> list = new List<string>();
			list.Add(root);
			try
			{
				string[] dirs = Fs.GetDirs(root);
				foreach (string item in dirs)
				{
					list.Add(item);
				}
			}
			catch
			{
			}
			foreach (string item2 in list)
			{
				string[] files;
				try
				{
					files = Fs.GetFiles(item2);
				}
				catch
				{
					continue;
				}
				if (files.Length < 2)
				{
					continue;
				}
				Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
				string[] array = files;
				foreach (string text in array)
				{
					string key = NormalizeDupKey(GetFileNameSafe(text));
					List<string> value;
					if (!dictionary.TryGetValue(key, out value))
					{
						value = (dictionary[key] = new List<string>());
					}
					value.Add(text);
				}
				foreach (KeyValuePair<string, List<string>> item3 in dictionary)
				{
					List<string> value2 = item3.Value;
					if (value2.Count < 2)
					{
						continue;
					}
					value2.Sort(delegate(string a, string b)
					{
						bool success = ReDupSuffix.Match(GetFileNameSafe(a)).Groups[2].Success;
						bool success2 = ReDupSuffix.Match(GetFileNameSafe(b)).Groups[2].Success;
						return (success != success2) ? (success ? 1 : (-1)) : string.Compare(GetFileNameSafe(a), GetFileNameSafe(b), StringComparison.OrdinalIgnoreCase);
					});
					long num3 = FileSizeSafe(value2[0]);
					List<string> list3 = new List<string>();
					list3.Add(value2[0]);
					for (int k = 1; k < value2.Count; k++)
					{
						if (FileSizeSafe(value2[k]) == num3)
						{
							log.Log("  🗑 剔除完全重复文件：" + GetFileNameSafe(value2[k]) + "（与 " + GetFileNameSafe(value2[0]) + " 大小相同）");
							TryDeleteFile(value2[k], log);
							num++;
						}
						else
						{
							list3.Add(value2[k]);
						}
					}
					if (list3.Count <= 1)
					{
						continue;
					}
					HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (string item4 in list3)
					{
						hashSet.Add(GetFileNameSafe(item4));
					}
					for (int l = 1; l < list3.Count; l++)
					{
						string fileName = GetFileNameSafe(list3[l]);
						string text2 = UniqueTargetName(fileName, hashSet);
						if (text2 != fileName)
						{
							string d = Path.Combine(item2, text2);
							try
							{
								Fs.FileMove(list3[l], d);
								log.Log("  ✏ 同名但内容不同，自动重命名：" + fileName + " → " + text2);
								hashSet.Remove(fileName);
								hashSet.Add(text2);
								num2++;
							}
							catch (Exception ex)
							{
								log.Log("  ⚠ 重命名失败：" + fileName + "（" + ex.Message + "）");
							}
						}
					}
				}
			}
			if (num > 0 || num2 > 0)
			{
				log.Log("  （剔重完成：剔除 " + num + " 个重复文件，重命名 " + num2 + " 个）");
			}
			else
			{
				log.Log("  （剔重：未发现完全重复的文件）");
			}
		}
		catch (Exception ex2)
		{
			log.Log("  ⚠ 剔重过程异常：" + ex2.Message);
		}
	}

	private static string NormalizeDupKey(string fileName)
	{
		Match match = ReDupSuffix.Match(fileName);
		if (match.Success)
		{
			return (match.Groups[1].Value + match.Groups[3].Value).ToLowerInvariant();
		}
		return fileName.ToLowerInvariant();
	}

	private static long FileSizeSafe(string f)
	{
		try
		{
			return Fs.FileLength(f);
		}
		catch
		{
			return -1L;
		}
	}

	private static string UniqueTargetName(string currentName, HashSet<string> occupied)
	{
		Match match = ReDupSuffix.Match(currentName);
		if (!match.Success)
		{
			return currentName;
		}
		string value = match.Groups[1].Value;
		string value2 = match.Groups[3].Value;
		string text = value + value2;
		if (!occupied.Contains(text))
		{
			return text;
		}
		for (int i = 1; i < 10000; i++)
		{
			string text2 = value + "(" + i + ")" + value2;
			if (text2 == currentName)
			{
				return currentName;
			}
			if (!occupied.Contains(text2))
			{
				return text2;
			}
		}
		return currentName;
	}

	public static void MoveDirectory(string src, string dst)
	{
		try
		{
			Fs.DirMove(src, dst);
		}
		catch (IOException)
		{
			CopyDirectory(src, dst);
			Fs.DirDelete(src, rec: true);
		}
	}

	private static void CopyDirectory(string src, string dst)
	{
		Fs.DirCreate(dst);
		string[] dirs = Fs.GetDirs(src);
		foreach (string text in dirs)
		{
			Fs.DirCreate(text.Replace(src, dst));
		}
		string[] files = Fs.GetFiles(src);
		foreach (string text2 in files)
		{
			Fs.FileCopy(text2, text2.Replace(src, dst));
		}
	}

	public static GroupResult ProcessGroup(List<string> groupFiles, string outputRoot, ILog log)
	{
		List<string> usedPwds = new List<string>();
		string text = PickPrimary(groupFiles);
		if (text == null)
		{
			GroupResult groupResult = new GroupResult();
			groupResult.Success = false;
			groupResult.Error = "无法确定主分卷";
			return groupResult;
		}
		string text2 = NormalizeKey(GetFileNameSafe(text));
		if (text2.Length == 0)
		{
			text2 = Path.GetFileNameWithoutExtension(text);
		}
		if (text2.Length == 0)
		{
			text2 = "解压结果";
		}
		log.Log("────────────────────────────");
		log.Log("▶ 处理分组【" + text2 + "】（共 " + groupFiles.Count + " 个文件，主分卷：" + GetFileNameSafe(text) + "）");
		string text3 = DetectFormat(text);
		if (text3 == null)
		{
			text3 = ExtToFormat(text);
		}
		if (text3 == null || text3 == "exe")
		{
			log.Log("  ⚠ 主文件不是可识别的压缩格式，跳过：" + GetFileNameSafe(text));
			GroupResult groupResult2 = new GroupResult();
			groupResult2.Success = false;
			groupResult2.BaseName = text2;
			groupResult2.Error = "不是压缩文件：" + GetFileNameSafe(text);
			return groupResult2;
		}
		log.Log("  识别格式：" + text3.ToUpperInvariant());
		string directoryName = Path.GetDirectoryName(text);
		if (string.IsNullOrEmpty(outputRoot))
		{
			outputRoot = directoryName;
		}
		string text4 = CreateStagingDir(directoryName);
		string text5 = text;
		string text6 = Path.Combine(text4, "level0");
		List<string> list = new List<string>();
		int num = 0;
		bool flag = true;
		List<WorkflowStep> steps = new List<WorkflowStep>();
		int activeStepIdx = 0;
		try
		{
			while (num < MaxNestDepth)
			{
				// 用户取消：立即停止，清理临时目录，原始文件全部保留
				if (CancelRequested)
				{
					log.Log("  ⏹ 已取消（用户中断），停止处理该分组");
					CleanupStaging(text4);
					log.Log("  已清理临时目录，原始文件全部保留。");
					GroupResult cancelGroup = new GroupResult();
					cancelGroup.Success = false;
					cancelGroup.Cancelled = true;
					cancelGroup.BaseName = text2;
					cancelGroup.Error = "已取消";
					return cancelGroup;
				}
				if (flag)
				{
					// 命中工作流：该层优先用工作流记录的密码
					if (ActiveWorkflow != null && ActiveWorkflow.Steps.Count > activeStepIdx)
					{
						string stepPwd = ActiveWorkflow.Steps[activeStepIdx].Password;
						PriorityPasswords = string.IsNullOrEmpty(stepPwd) ? null : new List<string> { stepPwd };
					}
					else
					{
						PriorityPasswords = null;
					}
					ExtractResult extractResult = ExtractWithPasswords(text5, text6, log);
					if (extractResult.Ok && extractResult.UsedPassword != null && !usedPwds.Contains(extractResult.UsedPassword))
					{
						usedPwds.Add(extractResult.UsedPassword);
					}
					if (!extractResult.Ok)
					{
						if (extractResult.Cancelled || CancelRequested)
						{
							log.Log("  ⏹ 已取消（用户中断），停止处理该分组");
							CleanupStaging(text4);
							log.Log("  已清理临时目录，原始文件全部保留。");
							GroupResult cancelGroup2 = new GroupResult();
							cancelGroup2.Success = false;
							cancelGroup2.Cancelled = true;
							cancelGroup2.BaseName = text2;
							cancelGroup2.Error = "已取消";
							return cancelGroup2;
						}
						log.Log("  ❌ " + extractResult.Error);
						log.Log("  已保留中间文件供检查：" + text4);
						GroupResult groupResult3 = new GroupResult();
						groupResult3.Success = false;
						groupResult3.BaseName = text2;
						groupResult3.Error = "解压失败：" + extractResult.Error;
						return groupResult3;
					}
					log.Log("  ✅ 本层解压成功" + ((extractResult.UsedPassword != null) ? ("（密码：" + extractResult.UsedPassword + "）") : "（无密码）"));
					// 记录本层步骤（输入格式 + 命中密码）
					WorkflowStep step = new WorkflowStep();
					step.InputExt = GetExtSafe(text5);
					step.Password = extractResult.UsedPassword ?? "";
					steps.Add(step);
					foreach (string item in list)
					{
						TryDeleteFile(item, log);
					}
					list.Clear();
				}
				SanitizeNames(text6, log);
				Deduplicate(text6, log);
				List<string> list2 = new List<string>();
				List<string> dirs = new List<string>();
				CollectTree(text6, list2, dirs);
				if (list2.Count == 1)
				{
					string text7 = list2[0];
					string text8 = DetectFormat(text7);
					if (text8 == null)
					{
						text8 = ExtToFormat(text7);
					}
					if (text8 == null)
					{
						log.Log("  本层得到单个文件 " + GetFileNameSafe(text7) + "，未识别为压缩格式，按规则改后缀 .zip 再试…");
						string text9 = text7;
						string text10 = ChangeExtSafe(text7, ".zip");
						if (text10 == text7)
						{
							text10 = text7 + ".zip";
						}
						try
						{
							Fs.FileMove(text7, text10);
							text9 = text10;
							log.Log("  重命名：" + GetFileNameSafe(text7) + " → " + GetFileNameSafe(text9));
						}
						catch
						{
						}
						string text11 = Path.Combine(text4, "level" + (num + 1));
						ExtractResult extractResult2 = ExtractWithPasswords(text9, text11, log);
						if (extractResult2.Ok && extractResult2.UsedPassword != null && !usedPwds.Contains(extractResult2.UsedPassword))
						{
							usedPwds.Add(extractResult2.UsedPassword);
						}
						if (!extractResult2.Ok)
						{
							if (extractResult2.Cancelled || CancelRequested)
							{
								log.Log("  ⏹ 已取消（用户中断），停止处理该分组");
								CleanupStaging(text4);
								log.Log("  已清理临时目录，原始文件全部保留。");
								GroupResult cancelGroup3 = new GroupResult();
								cancelGroup3.Success = false;
								cancelGroup3.Cancelled = true;
								cancelGroup3.BaseName = text2;
								cancelGroup3.Error = "已取消";
								return cancelGroup3;
							}
							if (text9 != text7 && Fs.FileExists(text9) && !Fs.FileExists(text7))
							{
								try
								{
									Fs.FileMove(text9, text7);
									log.Log("  恢复文件名：" + GetFileNameSafe(text7));
								}
								catch
								{
								}
							}
							log.Log("  ⚠ 改后缀后仍无法解压，视为最终结果（单个非压缩文件）：" + GetFileNameSafe(text7));
							string finalPath = MoveToFinal(text7, outputRoot, text2, log);
							CleanupStaging(text4);
							DeleteOriginals(groupFiles, log);
							GroupResult groupResult4 = new GroupResult();
							groupResult4.Success = true;
							groupResult4.BaseName = text2;
							groupResult4.FinalPath = finalPath;
							groupResult4.NestLevels = num + 1;
							groupResult4.UsedPasswords = usedPwds;
							groupResult4.Steps = steps;
							return groupResult4;
						}
						log.Log("  ✅ 该文件实为压缩包（改后缀后解压成功），继续下一层");
						if (steps.Count > 0)
						{
							steps[steps.Count - 1].FakeExt = GetExtSafe(text7);
							steps[steps.Count - 1].RenameTo = ".zip";
						}
						activeStepIdx++;
						list.Add(text9);
						num++;
						text6 = text11;
						flag = false;
						continue;
					}
					log.Log("  本层得到单个文件 " + GetFileNameSafe(text7) + "，真实格式为 " + text8.ToUpperInvariant() + "，改后缀后继续解压…");
					string text12 = "." + text8;
					string text13;
					if (GetExtSafe(text7).Equals(text12, StringComparison.OrdinalIgnoreCase))
					{
						text13 = text7;
					}
					else
					{
						text13 = ChangeExtSafe(text7, text12);
						if (text13 == text7)
						{
							text13 = text7 + text12;
						}
						try
						{
							Fs.FileMove(text7, text13);
							log.Log("  重命名：" + GetFileNameSafe(text7) + " → " + GetFileNameSafe(text13));
						}
						catch
						{
							text13 = text7;
						}
					}
					if (steps.Count > 0)
					{
						steps[steps.Count - 1].FakeExt = GetExtSafe(text7);
						steps[steps.Count - 1].RealFormat = text8;
						steps[steps.Count - 1].RenameTo = text12;
					}
					activeStepIdx++;
					list.Add(text13);
					num++;
					text5 = text13;
					text6 = Path.Combine(text4, "level" + num);
					flag = true;
					continue;
				}
				if (list2.Count == 0)
				{
					log.Log("  ❌ 解压结果为空（" + GetFileNameSafe(text5) + "）");
					log.Log("  已保留中间文件供检查：" + text4);
					GroupResult groupResult5 = new GroupResult();
					groupResult5.Success = false;
					groupResult5.BaseName = text2;
					groupResult5.Error = "解压结果为空";
					return groupResult5;
				}
				log.Log("  🎉 得到目录，共 " + list2.Count + " 个文件，处理完成");
				string finalPath2 = MoveToFinal(text6, outputRoot, text2, log);
				CleanupStaging(text4);
				DeleteOriginals(groupFiles, log);
				GroupResult groupResult6 = new GroupResult();
				groupResult6.Success = true;
				groupResult6.BaseName = text2;
				groupResult6.FinalPath = finalPath2;
				groupResult6.NestLevels = num + 1;
				groupResult6.UsedPasswords = usedPwds;
				groupResult6.Steps = steps;
				return groupResult6;
			}
			log.Log("  ❌ 嵌套层数超过 " + MaxNestDepth + "，停止");
			log.Log("  已保留中间文件供检查：" + text4);
			GroupResult groupResult7 = new GroupResult();
			groupResult7.Success = false;
			groupResult7.BaseName = text2;
			groupResult7.Error = "嵌套层数过深";
			return groupResult7;
		}
		catch (Exception ex)
		{
			log.Log("  ❌ 处理异常：" + ex.Message);
			GroupResult groupResult8 = new GroupResult();
			groupResult8.Success = false;
			groupResult8.BaseName = text2;
			groupResult8.Error = "处理异常：" + ex.Message;
			return groupResult8;
		}
		finally
		{
			ActiveWorkflow = null;
			PriorityPasswords = null;
		}
	}

	private static string MoveToFinal(string resultDir, string outputRoot, string baseName, ILog log)
	{
		Fs.DirCreate(outputRoot);
		string text = Path.Combine(outputRoot, baseName);
		int num = 2;
		while (Fs.DirExists(text) || Fs.FileExists(text))
		{
			text = Path.Combine(outputRoot, baseName + "_" + num);
			num++;
		}
		if (Fs.FileExists(resultDir))
		{
			Fs.DirCreate(text);
			string d = Path.Combine(text, GetFileNameSafe(resultDir));
			Fs.FileMove(resultDir, d);
			log.Log("  📁 最终结果：" + text);
			return text;
		}
		string[] dirs = Fs.GetDirs(resultDir);
		if (dirs.Length == 1 && Fs.GetFiles(resultDir).Length == 0)
		{
			Fs.DirMove(dirs[0], text);
		}
		else
		{
			MoveDirectory(resultDir, text);
		}
		log.Log("  📁 最终结果：" + text);
		return text;
	}

	private static void CleanupStaging(string stagingRoot)
	{
		try
		{
			if (Fs.DirExists(stagingRoot))
			{
				Fs.DirDelete(stagingRoot, rec: true);
			}
		}
		catch
		{
		}
	}

	private static string CreateStagingDir(string srcDir)
	{
		string text = Path.Combine(srcDir, ".sz333_tmp");
		try
		{
			if (Fs.DirExists(text))
			{
				Fs.DirDelete(text, rec: true);
			}
			Fs.DirCreate(text);
			try
			{
				new DirectoryInfo(Fs.L(text)).Attributes |= FileAttributes.Hidden;
			}
			catch
			{
			}
			return text;
		}
		catch
		{
			string text2 = Path.Combine(Path.GetTempPath(), "DSUnpack_" + Guid.NewGuid().ToString("N"));
			Fs.DirCreate(text2);
			return text2;
		}
	}

	private static void DeleteOriginals(List<string> groupFiles, ILog log)
	{
		if (!DeleteAfterSuccess)
		{
			log.Log("  （已按设置保留原始文件）");
			return;
		}
		log.Log("  —— 删除原始分卷与中间文件（彻底删除，不可恢复）——");
		foreach (string groupFile in groupFiles)
		{
			TryDeleteFile(groupFile, log);
		}
	}
}

}
