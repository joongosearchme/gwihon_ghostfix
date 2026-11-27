using System;
using System.IO;

namespace GhostFix;

public static class Pe
{
	public const ushort LAA = 32;

	public static PeInfo Read(string path)
	{
		PeInfo peInfo = new PeInfo();
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using BinaryReader binaryReader = new BinaryReader(fileStream);
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
		peInfo.Laa = (peInfo.Chars & 0x20) != 0;
		return peInfo;
	}

	public static string BackupPath(string path)
	{
		return path + ".laa-backup";
	}

	public static void SetLaa(string path)
	{
		PeInfo peInfo = Read(path);
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
			File.Copy(path, BackupPath(path));
		}
		ushort value = (ushort)(peInfo.Chars | 0x20u);
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
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
		File.Copy(text, path, overwrite: true);
		File.Delete(text);
		return true;
	}
}
