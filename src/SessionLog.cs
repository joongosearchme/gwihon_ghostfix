using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GhostFix;

public static class SessionLog
{
	public static string Path_ => Path.Combine(Watch.Dir, "session.log");

	[DllImport("user32.dll")]
	private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

	public static uint Gdi(Process p)
	{
		try
		{
			return GetGuiResources(p.Handle, 0u);
		}
		catch
		{
			return 0u;
		}
	}

	public static uint User(Process p)
	{
		try
		{
			return GetGuiResources(p.Handle, 1u);
		}
		catch
		{
			return 0u;
		}
	}

	public static void Write(string line)
	{
		try
		{
			Directory.CreateDirectory(Watch.Dir);
			FileInfo fileInfo = new FileInfo(Path_);
			if (fileInfo.Exists && fileInfo.Length > 1048576)
			{
				string text = Path_ + ".1";
				if (File.Exists(text))
				{
					File.Delete(text);
				}
				File.Move(Path_, text);
			}
			File.AppendAllText(Path_, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + "\r\n");
		}
		catch
		{
		}
	}

	public static string Tail(int samples)
	{
		try
		{
			if (!File.Exists(Path_))
			{
				return null;
			}
			string[] array = File.ReadAllLines(Path_);
			List<int> list = new List<int>();
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].Contains("  START "))
				{
					list.Add(i);
				}
			}
			if (list.Count == 0)
			{
				return string.Join("\r\n", array.Skip(Math.Max(0, array.Length - samples)));
			}
			int num = -1;
			int num2 = list.Count - 1;
			while (num2 >= 0 && num < 0)
			{
				int num3 = ((num2 + 1 < list.Count) ? list[num2 + 1] : array.Length);
				for (int j = list[num2]; j < num3; j++)
				{
					if (array[j].Contains("EXIT code=") && !array[j].Contains("EXIT code=0x00000000"))
					{
						num = num2;
						break;
					}
				}
				num2--;
			}
			if (num < 0)
			{
				num = list.Count - 1;
			}
			int num4 = list[num];
			int num5 = ((num + 1 < list.Count) ? list[num + 1] : array.Length);
			List<string> source = array.Skip(num4).Take(num5 - num4).ToList();
			List<string> list2 = source.Where((string l) => !l.Contains("EXIT code=") && !l.Contains("마지막 표본(")).ToList();
			List<string> collection = source.Where((string l) => l.Contains("EXIT code=") || l.Contains("마지막 표본(")).ToList();
			List<string> list3 = new List<string>();
			if (list2.Count <= 9)
			{
				list3.AddRange(list2);
			}
			else
			{
				list3.AddRange(list2.Take(3));
				List<string> list4 = (from l in list2.Skip(3).Take(list2.Count - 9)
					where l.Contains("TCP 변화") || l.Contains("WARN")
					select l).ToList();
				int num6 = list2.Count - 9 - list4.Count;
				if (list4.Count > 15)
				{
					list3.Add("  … (연결 변화 " + (list4.Count - 15) + "줄 더 있음) …");
					list4 = list4.Skip(list4.Count - 15).ToList();
				}
				list3.AddRange(list4);
				if (num6 > 0)
				{
					list3.Add("  … (일반 표본 " + num6 + "줄 생략) …");
				}
				list3.AddRange(list2.Skip(list2.Count - 6));
			}
			list3.AddRange(collection);
			return string.Join("\r\n", list3);
		}
		catch
		{
			return null;
		}
	}
}
