using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GhostFix;

public static class Pe
{
	public const ushort LAA = 32;

	private const uint GENERIC_READ = 0x80000000u;

	private const uint GENERIC_WRITE = 0x40000000u;

	private const uint FILE_ATTRIBUTE_NORMAL = 0x80u;

	private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10u;

	private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400u;

	private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x200000u;

	private struct BY_HANDLE_FILE_INFORMATION
	{
		public uint FileAttributes;

		public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;

		public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;

		public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;

		public uint VolumeSerialNumber;

		public uint FileSizeHigh;

		public uint FileSizeLow;

		public uint NumberOfLinks;

		public uint FileIndexHigh;

		public uint FileIndexLow;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

	public static PeInfo Read(string path)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return Read(fileStream);
	}

	private static PeInfo Read(FileStream fileStream)
	{
		PeInfo peInfo = new PeInfo();
		fileStream.Position = 0L;
		BinaryReader binaryReader = new BinaryReader(fileStream);
		if (fileStream.Length < 64 || binaryReader.ReadUInt16() != 23117)
		{
			return peInfo;
		}
		fileStream.Position = 60L;
		uint num = binaryReader.ReadUInt32();
		if (num + 24 > fileStream.Length)
		{
			return peInfo;
		}
		fileStream.Position = num;
		if (binaryReader.ReadUInt32() != 17744)
		{
			return peInfo;
		}
		ushort num2 = binaryReader.ReadUInt16();
		fileStream.Position = num + 22;
		peInfo.Chars = binaryReader.ReadUInt16();
		peInfo.Valid = true;
		peInfo.Is32 = num2 == 332;
		peInfo.Offset = num + 22;
		peInfo.Laa = (peInfo.Chars & LAA) != 0;
		return peInfo;
	}

	public static string BackupPath(string path)
	{
		return path + ".laa-backup";
	}

	public static void SetLaa(string path)
	{
		// 읽기·백업·쓰기를 모두 처음 연 같은 파일에 한다(확인한 뒤 다른 파일로 바뀌어도 그 파일을 고치지 않게).
		using FileStream fileStream = OpenPlain(path, FileMode.Open, FileAccess.ReadWrite);
		PeInfo peInfo = Read(fileStream);
		if (!peInfo.Valid)
		{
			throw new InvalidDataException("exe 파일이 아닙니다.");
		}
		if (peInfo.Laa)
		{
			return;
		}
		if (!File.Exists(BackupPath(path)))
		{
			using FileStream backup = OpenPlain(BackupPath(path), FileMode.CreateNew, FileAccess.ReadWrite);
			fileStream.Position = 0L;
			fileStream.CopyTo(backup);
		}
		ushort value = (ushort)(peInfo.Chars | LAA);
		fileStream.Position = peInfo.Offset;
		fileStream.Write(BitConverter.GetBytes(value), 0, 2);
	}

	public static bool RestoreLaa(string path)
	{
		string text = BackupPath(path);
		if (!File.Exists(text))
		{
			return false;
		}
		using (FileStream backup = OpenPlain(text, FileMode.Open, FileAccess.Read))
		{
			if (!Read(backup).Valid)
			{
				throw new InvalidDataException("백업 파일(" + Path.GetFileName(text) + ")이 exe 파일이 아닙니다.");
			}
			using FileStream fileStream = OpenPlain(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
			fileStream.SetLength(0L);
			backup.Position = 0L;
			backup.CopyTo(fileStream);
		}
		File.Delete(text);
		return true;
	}

	// 바로 그 경로에 있는 보통 파일만 연다. 링크(심볼릭·하드 링크)이거나, 경로 중간의 폴더 연결(정션)로
	// 다른 곳의 파일이 열리면 거절한다. 관리자 권한으로 일반 사용자가 바꿀 수 있는 폴더의 파일을 고치므로,
	// 링크를 걸어 시스템 파일 같은 다른 파일을 고치게 만드는 것을 막는다.
	private static FileStream OpenPlain(string path, FileMode mode, FileAccess access)
	{
		string full = Path.GetFullPath(path);
		uint desired = ((access == FileAccess.Read) ? GENERIC_READ : (GENERIC_READ | GENERIC_WRITE));
		FileShare share = ((access == FileAccess.Read) ? FileShare.Read : FileShare.None);
		SafeFileHandle handle = CreateFile(full, desired, share, IntPtr.Zero, mode, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
		if (handle.IsInvalid)
		{
			int error = Marshal.GetLastWin32Error();
			handle.Dispose();
			throw new IOException(Path.GetFileName(full) + ": " + new Win32Exception(error).Message);
		}
		try
		{
			if (!GetFileInformationByHandle(handle, out var info))
			{
				throw new IOException(Path.GetFileName(full) + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
			}
			if ((info.FileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) != 0 || info.NumberOfLinks != 1)
			{
				throw new IOException(Path.GetFileName(full) + "이(가) 다른 파일을 가리키는 링크라 고치지 않습니다.");
			}
			string final = FinalPath(handle);
			if (final == null || !string.Equals(final, full, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException(Path.GetFileName(full) + "의 경로가 다른 폴더로 연결되어 있어(정션·링크·짧은 이름) 고치지 않습니다. 실제 폴더 경로로 골라 주세요.");
			}
			return new FileStream(handle, access);
		}
		catch
		{
			handle.Dispose();
			throw;
		}
	}

	// 열린 파일의 실제 경로(링크·정션을 모두 따라간 경로)
	private static string FinalPath(SafeFileHandle handle)
	{
		StringBuilder stringBuilder = new StringBuilder(1024);
		uint length = GetFinalPathNameByHandle(handle, stringBuilder, (uint)stringBuilder.Capacity, 0u);
		if (length == 0)
		{
			return null;
		}
		if (length >= stringBuilder.Capacity)
		{
			stringBuilder = new StringBuilder((int)length + 1);
			length = GetFinalPathNameByHandle(handle, stringBuilder, (uint)stringBuilder.Capacity, 0u);
			if (length == 0 || length >= stringBuilder.Capacity)
			{
				return null;
			}
		}
		string text = stringBuilder.ToString();
		if (text.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
		{
			return "\\\\" + text.Substring(8);
		}
		if (text.StartsWith("\\\\?\\", StringComparison.Ordinal))
		{
			return text.Substring(4);
		}
		return text;
	}
}
