using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DSUnpack
{
internal static class Fs
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
	private struct WIN32_FIND_DATA
	{
		public uint dwFileAttributes;

		public long ftCreationTime;

		public long ftLastAccessTime;

		public long ftLastWriteTime;

		public uint nFileSizeHigh;

		public uint nFileSizeLow;

		public uint dwReserved0;

		public uint dwReserved1;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string cFileName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
		public string cAlternateFileName;
	}

	private const uint INVALID_FILE_ATTRIBUTES = uint.MaxValue;

	private const uint FILE_ATTRIBUTE_DIRECTORY = 16u;

	private const uint FILE_ATTRIBUTE_NORMAL = 128u;

	private const uint GENERIC_READ = 2147483648u;

	private const uint FILE_SHARE_RW = 7u;

	private const uint OPEN_EXISTING = 3u;

	private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool MoveFileW(string s, string d);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool DeleteFileW(string p);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFileAttributesW(string p);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetFileAttributesW(string p, uint a);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateDirectoryW(string p, IntPtr sa);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool RemoveDirectoryW(string p);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CopyFileW(string s, string d, bool failIfExists);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr FindFirstFileW(string p, out WIN32_FIND_DATA d);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern bool FindNextFileW(IntPtr h, out WIN32_FIND_DATA d);

	[DllImport("kernel32.dll")]
	private static extern bool FindClose(IntPtr h);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tpl);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool ReadFile(IntPtr h, byte[] buf, uint n, out uint read, IntPtr ov);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr h);

	public static string L(string p)
	{
		if (string.IsNullOrEmpty(p))
		{
			return p;
		}
		if (p.StartsWith("\\\\?\\") || p.StartsWith("\\\\.\\"))
		{
			return p;
		}
		if (p.Length > 250 && p.Length >= 2 && (p[1] == ':' || p.StartsWith("\\\\")))
		{
			return "\\\\?\\" + p;
		}
		return p;
	}

	private static IOException Win32Error(string op)
	{
		return new IOException(op + "失败（Win32 错误 " + Marshal.GetLastWin32Error() + "）");
	}

	public static bool FileExists(string p)
	{
		uint fileAttributesW = GetFileAttributesW(L(p));
		if (fileAttributesW != uint.MaxValue)
		{
			return (fileAttributesW & 0x10) == 0;
		}
		return false;
	}

	public static bool DirExists(string p)
	{
		uint fileAttributesW = GetFileAttributesW(L(p));
		if (fileAttributesW != uint.MaxValue)
		{
			return (fileAttributesW & 0x10) != 0;
		}
		return false;
	}

	public static void FileMove(string s, string d)
	{
		if (!MoveFileW(L(s), L(d)))
		{
			throw Win32Error("移动");
		}
	}

	public static void DirMove(string s, string d)
	{
		if (!MoveFileW(L(s), L(d)))
		{
			throw Win32Error("移动目录");
		}
	}

	public static void FileDelete(string p)
	{
		if (!DeleteFileW(L(p)) && FileExists(p))
		{
			throw Win32Error("删除");
		}
	}

	public static void DirDelete(string p, bool rec)
	{
		if (rec)
		{
			string[] dirs = GetDirs(p);
			foreach (string p2 in dirs)
			{
				DirDelete(p2, rec: true);
			}
			string[] files = GetFiles(p);
			foreach (string p3 in files)
			{
				FileDelete(p3);
			}
		}
		if (!RemoveDirectoryW(L(p)) && DirExists(p))
		{
			throw Win32Error("删除目录");
		}
	}

	public static void DirCreate(string p)
	{
		if (!CreateDirectoryW(L(p), IntPtr.Zero) && !DirExists(p))
		{
			throw Win32Error("创建目录");
		}
	}

	public static void SetNormal(string p)
	{
		SetFileAttributesW(L(p), 128u);
	}

	public static long FileLength(string p)
	{
		WIN32_FIND_DATA d;
		IntPtr intPtr = FindFirstFileW(L(p), out d);
		if (intPtr == INVALID_HANDLE)
		{
			return -1L;
		}
		FindClose(intPtr);
		return (long)(((ulong)d.nFileSizeHigh << 32) | d.nFileSizeLow);
	}

	public static void FileCopy(string s, string d)
	{
		if (!CopyFileW(L(s), L(d), failIfExists: true))
		{
			throw Win32Error("复制");
		}
	}

	public static byte[] ReadHeader(string p, int count)
	{
		IntPtr intPtr = CreateFileW(L(p), 2147483648u, 7u, IntPtr.Zero, 3u, 128u, IntPtr.Zero);
		if (intPtr == INVALID_HANDLE)
		{
			return null;
		}
		try
		{
			byte[] array = new byte[count];
			uint read;
			if (!ReadFile(intPtr, array, (uint)count, out read, IntPtr.Zero))
			{
				return null;
			}
			Array.Resize(ref array, (int)read);
			return array;
		}
		finally
		{
			CloseHandle(intPtr);
		}
	}

	public static string[] GetFiles(string dir)
	{
		return EnumDir(dir, wantDirs: false);
	}

	public static string[] GetDirs(string dir)
	{
		return EnumDir(dir, wantDirs: true);
	}

	private static string[] EnumDir(string dir, bool wantDirs)
	{
		List<string> list = new List<string>();
		string p = (dir.EndsWith("\\") ? (dir + "*") : (dir + "\\*"));
		WIN32_FIND_DATA d;
		IntPtr intPtr = FindFirstFileW(L(p), out d);
		if (intPtr == INVALID_HANDLE)
		{
			return list.ToArray();
		}
		try
		{
			do
			{
				string cFileName = d.cFileName;
				if (!(cFileName == ".") && !(cFileName == ".."))
				{
					bool flag = (d.dwFileAttributes & 0x10) != 0;
					if (flag == wantDirs)
					{
						list.Add(dir + "\\" + cFileName);
					}
				}
			}
			while (FindNextFileW(intPtr, out d));
		}
		finally
		{
			FindClose(intPtr);
		}
		return list.ToArray();
	}
}

}
