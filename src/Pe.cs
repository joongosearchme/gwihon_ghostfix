using System;
using System.Collections.Generic;
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

	private const uint DELETE = 0x10000u;

	private const uint FILE_READ_ATTRIBUTES = 0x80u;

	// 속성만 읽는 핸들은 공유 검사를 거치지 않아 이름 바꾸기를 막지 못한다. 목록 읽기 권한을 함께 받는다.
	private const uint FILE_LIST_DIRECTORY = 0x1u;

	private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x2000000u;

	private const int FileDispositionInfo = 4;

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

	// FILE_DISPOSITION_INFO 는 BOOLEAN(1바이트) 하나뿐이다.
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass, ref byte deleteFile, uint bufferSize);

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
		// 잘린 game.exe(되돌리다 끊긴 것 등)를 원본으로 삼아 백업을 만들거나 덮어쓰지 않는다.
		if (fileStream.Length < RawEnd(fileStream))
		{
			throw new InvalidDataException("game.exe 가 잘려 있습니다. 되돌리기를 다시 하거나 게임 파일 검사로 고쳐 주세요.");
		}
		if (File.Exists(BackupPath(path)))
		{
			// 지금 game.exe 는 LAA가 꺼진 원본 상태다. 남은 백업이 이것과 다르면(게임 업데이트 전 버전이거나 만들다 끊긴 것)
			// 그대로 두면 나중에 되돌릴 때 게임이 옛 버전이나 잘린 파일로 바뀌므로, 지금 game.exe 로 백업을 다시 쓴다.
			using FileStream old = OpenPlain(BackupPath(path), FileMode.Open, FileAccess.ReadWrite);
			// 단, 지금 game.exe 가 백업의 앞부분만 있는 것이면 되돌리다 끊긴 것이다. 온전한 백업을 잘린 파일로 덮지 않는다.
			if (IsCutCopy(fileStream, old))
			{
				throw new InvalidDataException("game.exe 가 되돌리다 끊긴 상태입니다. 되돌리기를 다시 해 주세요.");
			}
			if (!SameContent(fileStream, old))
			{
				old.SetLength(0L);
				fileStream.Position = 0L;
				fileStream.CopyTo(old);
				old.Flush();
			}
		}
		else
		{
			using FileStream backup = OpenPlain(BackupPath(path), FileMode.CreateNew, FileAccess.ReadWrite);
			// 핸들은 쓰기 전에 꺼내 둔다(쓰다 실패한 뒤 SafeFileHandle 을 꺼내면 남은 버퍼를 다시 쓰려다 또 실패한다).
			SafeFileHandle backupHandle = backup.SafeFileHandle;
			try
			{
				fileStream.Position = 0L;
				fileStream.CopyTo(backup);
				backup.Flush();
			}
			catch
			{
				// 덜 쓴 백업을 남기지 않는다(나중에 되돌리기가 그걸로 game.exe 를 덮어쓰지 않게).
				MarkDelete(backupHandle);
				throw;
			}
		}
		ushort value = (ushort)(peInfo.Chars | LAA);
		fileStream.Position = peInfo.Offset;
		fileStream.Write(BitConverter.GetBytes(value), 0, 2);
	}

	private static bool SameContent(FileStream a, FileStream b)
	{
		return a.Length == b.Length && SamePrefix(a, b, a.Length, -1L);
	}

	// a 가 b 의 앞부분만 있는 파일인가(b 를 a 로 복사하다 끊긴 모습).
	private static bool IsCutCopy(FileStream a, FileStream b)
	{
		return a.Length < b.Length && SamePrefix(a, b, a.Length, -1L);
	}

	// game 이 backup 에 GhostFix 가 LAA 표시만 켠 그 파일인가: 길이가 같고, 표시 2바이트만 빼고 같고, 그 자리가 백업 값에 LAA를 더한 값.
	private static bool IsPatchOf(FileStream game, FileStream backup, PeInfo gameInfo, PeInfo backupInfo)
	{
		return game.Length == backup.Length && gameInfo.Offset == backupInfo.Offset && gameInfo.Chars == (ushort)(backupInfo.Chars | LAA) && SamePrefix(game, backup, game.Length, gameInfo.Offset);
	}

	// 두 파일의 앞 length 바이트가 같은가. skipAt 이 0 이상이면 그 자리부터 2바이트는 비교하지 않는다.
	private static bool SamePrefix(FileStream a, FileStream b, long length, long skipAt)
	{
		a.Position = 0L;
		b.Position = 0L;
		byte[] bufferA = new byte[65536];
		byte[] bufferB = new byte[65536];
		long done = 0L;
		while (done < length)
		{
			int want = (int)Math.Min(bufferA.Length, length - done);
			if (!ReadFull(a, bufferA, want) || !ReadFull(b, bufferB, want))
			{
				return false;
			}
			for (int i = 0; i < want; i++)
			{
				long at = done + i;
				if (skipAt >= 0 && at >= skipAt && at < skipAt + 2)
				{
					continue;
				}
				if (bufferA[i] != bufferB[i])
				{
					return false;
				}
			}
			done += want;
		}
		return true;
	}

	private static bool ReadFull(FileStream stream, byte[] buffer, int count)
	{
		int got = 0;
		while (got < count)
		{
			int n = stream.Read(buffer, got, count - got);
			if (n <= 0)
			{
				return false;
			}
			got += n;
		}
		return true;
	}

	public static bool RestoreLaa(string path)
	{
		string text = BackupPath(path);
		if (!File.Exists(text))
		{
			return false;
		}
		using (FileStream backup = OpenPlain(text, FileMode.Open, FileAccess.Read, deletable: true))
		{
			PeInfo backupInfo = Read(backup);
			if (!backupInfo.Valid)
			{
				throw new InvalidDataException("백업 파일(" + Path.GetFileName(text) + ")이 exe 파일이 아닙니다.");
			}
			// 백업을 만들다 끊겨 잘린 채 남은 것이면 game.exe 를 덮어쓰지 않는다.
			if (backup.Length < RawEnd(backup))
			{
				throw new InvalidDataException("백업 파일(" + Path.GetFileName(text) + ")이 잘려 있어 되돌리지 않습니다.");
			}
			// game.exe 가 있으면 그 파일을 그대로 열고(삭제 권한을 요구하지 않는다), 없으면 새로 만든다.
			// 되돌릴지 판단은 이 핸들로 읽은 내용으로 한다(판단한 뒤 게임 업데이트가 파일을 바꿔 놓아도 새 파일을 덮지 않게).
			using (FileStream fileStream = OpenPlain(path, File.Exists(path) ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite))
			{
				PeInfo current = Read(fileStream);
				if (IsCutCopy(fileStream, backup))
				{
					// game.exe 가 백업의 앞부분만 있다(옛 버전 GhostFix 가 되돌리다 끊긴 것, 또는 game.exe 가 없거나 빈 파일).
					// 이미 있는 앞부분은 백업과 같으니 비우지 않고 그 뒤만 이어 쓴다(또 끊겨도 다시 누르면 이어서 마저 쓴다).
					fileStream.Position = fileStream.Length;
					backup.Position = fileStream.Length;
					backup.CopyTo(fileStream);
				}
				else
				{
					// 잘렸거나 exe 가 아닌데 백업의 앞부분도 아니면(게임 업데이트가 끊긴 경우 등) 무슨 파일인지 모르니 옛 백업으로 덮지 않는다.
					if (!current.Valid || fileStream.Length < RawEnd(fileStream))
					{
						throw new InvalidDataException("game.exe 가 깨져 있고 백업과도 다른 파일입니다(게임 업데이트가 끊긴 경우 등). 옛 버전으로 덮지 않습니다. 게임 런처의 파일 검사로 고쳐 주세요.");
					}
					// LAA가 이미 꺼져 있으면(게임 업데이트로 game.exe 가 새로 깔린 경우 등) 되돌릴 것이 없다.
					if (!current.Laa)
					{
						return false;
					}
					// GhostFix 가 백업에 LAA 표시만 켠 그 파일이 아니면(게임 새 버전이 처음부터 LAA를 켜고 나온 경우 등)
					// 백업으로 덮으면 게임이 옛 버전으로 돌아가므로 건드리지 않는다.
					if (!IsPatchOf(fileStream, backup, current, backupInfo))
					{
						throw new InvalidDataException("game.exe 가 백업한 때와 다른 버전입니다(게임 업데이트 등). 옛 버전으로 덮지 않으려고 되돌리지 않습니다. 필요 없으면 " + Path.GetFileName(text) + " 를 지워 주세요.");
					}
					// 백업과 다른 곳은 LAA 표시 2바이트뿐이니 그 자리만 백업 값으로 되돌린다.
					// 파일을 비우고 다시 쓰지 않으므로 중간에 끊겨도 game.exe 가 깨지지 않는다.
					fileStream.Position = current.Offset;
					fileStream.Write(BitConverter.GetBytes(backupInfo.Chars), 0, 2);
				}
				fileStream.Flush(flushToDisk: true);
			}
			// 백업은 검사한 그 핸들로 지운다. 경로로 다시 지우면, 그사이 폴더가 링크로 바뀌었을 때 다른 파일이 지워진다.
			if (!MarkDelete(backup.SafeFileHandle))
			{
				throw new IOException(Path.GetFileName(text) + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
			}
		}
		return true;
	}

	// PE 헤더가 말하는 파일 끝: 섹션 데이터와 서명(인증서 표, 섹션 뒤에 붙음) 중 더 뒤에서 끝나는 자리.
	// 표가 파일보다 길면(잘렸으면) long.MaxValue.
	private static long RawEnd(FileStream fileStream)
	{
		try
		{
			BinaryReader binaryReader = new BinaryReader(fileStream);
			fileStream.Position = 60L;
			long pe = binaryReader.ReadUInt32();
			fileStream.Position = pe + 6;
			int sections = binaryReader.ReadUInt16();
			fileStream.Position = pe + 20;
			int optional = binaryReader.ReadUInt16();
			long end = 0L;
			// 서명 위치: 데이터 디렉터리 4번(보안). 이 값만은 메모리 주소가 아니라 파일 안 위치다.
			fileStream.Position = pe + 24;
			ushort magic = binaryReader.ReadUInt16();
			long directories = pe + 24 + ((magic == 0x20B) ? 112 : 96);
			if (directories + 5 * 8 <= pe + 24 + optional)
			{
				fileStream.Position = directories + 4 * 8;
				long certificates = binaryReader.ReadUInt32();
				long certificatesSize = binaryReader.ReadUInt32();
				if (certificates != 0 && certificatesSize != 0)
				{
					end = certificates + certificatesSize;
				}
			}
			for (int i = 0; i < sections; i++)
			{
				fileStream.Position = pe + 24 + optional + 40L * i + 16;
				long size = binaryReader.ReadUInt32();
				long pointer = binaryReader.ReadUInt32();
				end = Math.Max(end, pointer + size);
			}
			return end;
		}
		catch (EndOfStreamException)
		{
			// 섹션 표가 파일 끝보다 길다 = 잘렸다
			return long.MaxValue;
		}
	}

	// 열린 핸들의 파일을 핸들을 닫을 때 지우게 한다(경로를 다시 찾지 않는다). DELETE 권한으로 연 핸들이어야 한다.
	private static bool MarkDelete(SafeFileHandle handle)
	{
		byte deleteFile = 1;
		return SetFileInformationByHandle(handle, FileDispositionInfo, ref deleteFile, 1u);
	}

	// 바로 그 경로에 있는 보통 파일만 연다. 링크(심볼릭·하드 링크)이거나, 경로 중간의 폴더 연결(정션)로
	// 다른 곳의 파일이 열리면 거절한다. 관리자 권한으로 일반 사용자가 바꿀 수 있는 폴더의 파일을 고치므로,
	// 링크를 걸어 시스템 파일 같은 다른 파일을 고치게 만드는 것을 막는다.
	// 먼저 파일까지 가는 폴더를 하나씩 열어 연결(정션 등)이 아닌 실제 폴더인지 보고, 파일을 열 때까지 연 채로 둔다
	// (삭제 공유 없이 열어 두므로 그사이 폴더를 지우거나 이름을 바꿔 연결로 바꿔치기할 수 없다).
	// 새로 만드는 모드(CreateNew)는 DELETE 권한도 받아, 검사에 걸리면 방금 만든 파일을 그 핸들로 지운다.
	// deletable 은 나중에 그 핸들로 지울 파일.
	private static FileStream OpenPlain(string path, FileMode mode, FileAccess access, bool deletable = false)
	{
		if (mode != FileMode.Open && mode != FileMode.CreateNew)
		{
			throw new ArgumentException("Open 이나 CreateNew 만 씁니다.", nameof(mode));
		}
		string full = Path.GetFullPath(path);
		List<SafeFileHandle> folders = HoldFolders(full, out string parentFinal);
		try
		{
			return OpenPlainIn(full, mode, access, deletable, parentFinal);
		}
		finally
		{
			foreach (SafeFileHandle folder in folders)
			{
				folder.Dispose();
			}
		}
	}

	// 폴더마다 실제 위치(연 핸들 기준)를 구해, 바로 위 폴더의 실제 위치 안에 있는지 대소문자까지 똑같이 확인한다.
	// 검사한 뒤 폴더를 비우고 정션으로 바꿔도, 그 아래를 열면 실제 위치가 달라져 걸린다.
	// parentFinal: 파일이 들어 있어야 할 폴더의 실제 위치(폴더가 없으면, 즉 드라이브 바로 아래면 null).
	private static List<SafeFileHandle> HoldFolders(string full, out string parentFinal)
	{
		parentFinal = null;
		string root = Path.GetPathRoot(full) ?? "";
		List<string> chain = new List<string>();
		for (string dir = Path.GetDirectoryName(full); dir != null && dir.Length > root.Length; dir = Path.GetDirectoryName(dir))
		{
			chain.Add(dir);
		}
		chain.Reverse();
		List<SafeFileHandle> held = new List<SafeFileHandle>();
		try
		{
			foreach (string dir in chain)
			{
				SafeFileHandle handle = CreateFile(dir, FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES, FileShare.Read | FileShare.Write, IntPtr.Zero, FileMode.Open, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
				if (handle.IsInvalid)
				{
					int error = Marshal.GetLastWin32Error();
					handle.Dispose();
					throw new IOException(Path.GetFileName(dir) + " 폴더: " + new Win32Exception(error).Message);
				}
				held.Add(handle);
				if (!GetFileInformationByHandle(handle, out var info) || (info.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 || (info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
				{
					throw new IOException(Path.GetFileName(dir) + " 폴더가 다른 곳을 가리키는 연결(정션·링크)이라 고치지 않습니다. 실제 폴더 경로로 골라 주세요.");
				}
				string final = FinalPath(handle);
				bool inParent = final != null && ((parentFinal == null)
					? string.Equals(Path.GetDirectoryName(final), root, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetDirectoryName(final) + "\\", root, StringComparison.OrdinalIgnoreCase)
					: string.Equals(Path.GetDirectoryName(final), parentFinal, StringComparison.Ordinal));
				if (!inParent || !string.Equals(final, dir, StringComparison.OrdinalIgnoreCase))
				{
					throw new IOException(Path.GetFileName(dir) + " 폴더의 경로가 다른 곳으로 연결되어 있어(정션·링크·짧은 이름) 고치지 않습니다. 실제 폴더 경로로 골라 주세요.");
				}
				parentFinal = final;
			}
			return held;
		}
		catch
		{
			foreach (SafeFileHandle handle in held)
			{
				handle.Dispose();
			}
			throw;
		}
	}

	private static FileStream OpenPlainIn(string full, FileMode mode, FileAccess access, bool deletable, string parentFinal)
	{
		bool created = mode == FileMode.CreateNew;
		uint desired = ((access == FileAccess.Read) ? GENERIC_READ : (GENERIC_READ | GENERIC_WRITE));
		if (created || deletable)
		{
			desired |= DELETE;
		}
		FileShare share = ((access == FileAccess.Read) ? FileShare.Read : FileShare.None);
		SafeFileHandle handle = CreateFile(full, desired, share, IntPtr.Zero, mode, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
		if (handle.IsInvalid)
		{
			int openError = Marshal.GetLastWin32Error();
			handle.Dispose();
			throw new IOException(Path.GetFileName(full) + ": " + new Win32Exception(openError).Message);
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
			if (final == null || !string.Equals(final, full, StringComparison.OrdinalIgnoreCase) || (parentFinal != null && !string.Equals(Path.GetDirectoryName(final), parentFinal, StringComparison.Ordinal)))
			{
				throw new IOException(Path.GetFileName(full) + "의 경로가 다른 폴더로 연결되어 있어(정션·링크·짧은 이름) 고치지 않습니다. 실제 폴더 경로로 골라 주세요.");
			}
			return new FileStream(handle, access);
		}
		catch
		{
			if (created)
			{
				MarkDelete(handle);
			}
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
