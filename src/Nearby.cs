using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GhostFix;

public static class Nearby
{
	public static List<string> Events(DateTime t, int minutes, string exeName)
	{
		List<string> list = new List<string>();
		DateTime dateTime = t.AddMinutes(-minutes);
		DateTime dateTime2 = t.AddMinutes(minutes);
		string[] array = new string[2] { "Application", "System" };
		foreach (string text in array)
		{
			try
			{
				using EventLog eventLog = new EventLog(text);
				EventLogEntryCollection entries = eventLog.Entries;
				int j = 0;
				for (int num = entries.Count - 1; num >= 0 && j < 30000; num--, j++)
				{
					EventLogEntry eventLogEntry;
					try
					{
						eventLogEntry = entries[num];
					}
					catch
					{
						continue;
					}
					if (eventLogEntry.TimeGenerated > dateTime2)
					{
						continue;
					}
					if (eventLogEntry.TimeGenerated < dateTime)
					{
						break;
					}
					long num2 = eventLogEntry.InstanceId & 0xFFFF;
					if ((text == "Application" && (num2 == 1000 || num2 == 1002) && eventLogEntry.ReplacementStrings.Length != 0 && string.Equals(eventLogEntry.ReplacementStrings[0], exeName, StringComparison.OrdinalIgnoreCase)) || (eventLogEntry.EntryType == EventLogEntryType.Information && text == "System" && !IsInteresting(eventLogEntry.Source)))
					{
						continue;
					}
					string text2 = (eventLogEntry.Message ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
					string text3 = "";
					try
					{
						text3 = string.Join(" | ", from x in eventLogEntry.ReplacementStrings
							where !string.IsNullOrWhiteSpace(x)
							select x.Replace("\r", " ").Replace("\n", " ").Trim());
					}
					catch
					{
					}
					string text4 = ResolveGuids(text2 + " " + text3);
					if (text2.Contains("설명을 찾을 수 없습니다") || text2.Contains("description for Event ID"))
					{
						text2 = "(메시지 DLL 없음) 데이터: " + text3;
					}
					if (text2.Length > 160)
					{
						text2 = text2.Substring(0, 160) + "…";
					}
					if (text4.Length > 0)
					{
						text2 = text2 + "  [" + text4 + "]";
					}
					list.Add(string.Format("  {0:HH:mm:ss} [{1}] {2} ({3}) {4}: {5}", eventLogEntry.TimeGenerated, (text == "Application") ? "앱" : "시스템", eventLogEntry.Source, num2, Level(eventLogEntry.EntryType), text2));
				}
			}
			catch
			{
			}
		}
		list.Sort();
		return list;
	}

	private static string ResolveGuids(string msg)
	{
		List<string> list = new List<string>();
		foreach (Match item in Regex.Matches(msg, "\\{[0-9A-Fa-f\\-]{36}\\}"))
		{
			string value = item.Value;
			string text = null;
			try
			{
				using (RegistryKey registryKey = Registry.ClassesRoot.OpenSubKey("CLSID\\" + value))
				{
					if (registryKey != null)
					{
						text = registryKey.GetValue("") as string;
					}
				}
				if (string.IsNullOrEmpty(text))
				{
					using RegistryKey registryKey2 = Registry.ClassesRoot.OpenSubKey("AppID\\" + value);
					if (registryKey2 != null)
					{
						text = registryKey2.GetValue("") as string;
					}
				}
				if (string.IsNullOrEmpty(text))
				{
					using RegistryKey registryKey3 = Registry.ClassesRoot.OpenSubKey("CLSID\\" + value + "\\InprocServer32");
					if (registryKey3 != null)
					{
						text = Path.GetFileName((registryKey3.GetValue("") as string) ?? "");
					}
				}
				if (string.IsNullOrEmpty(text))
				{
					using RegistryKey registryKey4 = Registry.ClassesRoot.OpenSubKey("CLSID\\" + value + "\\LocalServer32");
					if (registryKey4 != null)
					{
						text = Path.GetFileName(((registryKey4.GetValue("") as string) ?? "").Trim('"'));
					}
				}
			}
			catch
			{
			}
			list.Add(value + " = " + (string.IsNullOrEmpty(text) ? "?" : text));
		}
		return string.Join("; ", list.Distinct());
	}

	private static bool IsInteresting(string source)
	{
		string text = (source ?? "").ToLowerInvariant();
		if (!text.Contains("kernel-power") && !text.Contains("eventlog") && !text.Contains("service control") && !text.Contains("tcpip") && !text.Contains("dhcp") && !text.Contains("netbt") && !text.Contains("dns") && !text.Contains("ntfs") && !text.Contains("disk") && !text.Contains("whea") && !text.Contains("display") && !text.Contains("nvlddmkm") && !text.Contains("amdkmdag") && !text.Contains("igfx") && !text.Contains("user32") && !text.Contains("winlogon"))
		{
			return text.Contains("power-troubleshooter");
		}
		return true;
	}

	private static string Level(EventLogEntryType t)
	{
		return t switch
		{
			EventLogEntryType.Error => "오류", 
			EventLogEntryType.Warning => "경고", 
			_ => "정보", 
		};
	}

	public static List<string> GameGuardLogs(string gameDir)
	{
		List<string> list = new List<string>();
		try
		{
			string path = Path.Combine(gameDir ?? "", "GameGuard");
			if (!Directory.Exists(path))
			{
				return list;
			}
			foreach (FileInfo item in (from f in new DirectoryInfo(path).GetFiles("*.erl")
				orderby f.LastWriteTime descending
				select f).Take(6))
			{
				string text = "";
				try
				{
					if (item.Length < 65536)
					{
						byte[] array = File.ReadAllBytes(item.FullName);
						if ((double)array.Take(200).Count((byte b) => b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127)) >= (double)Math.Min(200, array.Length) * 0.9)
						{
							IEnumerable<string> source = (from l in File.ReadAllLines(item.FullName)
								where l.Trim().Length > 0
								select l).Take(2);
							text = "  |  " + string.Join(" / ", source.Select((string l) => (l.Length <= 90) ? l : (l.Substring(0, 90) + "…")));
						}
					}
				}
				catch
				{
				}
				list.Add($"  {item.LastWriteTime:yyyy-MM-dd HH:mm:ss}  {item.Name}  {item.Length:N0}B{text}");
			}
		}
		catch
		{
		}
		return list;
	}

	public static List<string> Defender(int max)
	{
		List<string> list = new List<string>();
		try
		{
			using EventLogReader eventLogReader = new EventLogReader(new EventLogQuery("Microsoft-Windows-Windows Defender/Operational", PathType.LogName, "*[System[(EventID=1116 or EventID=1117 or EventID=1118 or EventID=1119 or EventID=5001 or EventID=5007 or EventID=5010 or EventID=5012)]]")
			{
				ReverseDirection = true
			});
			EventRecord eventRecord;
			while (list.Count < max && (eventRecord = eventLogReader.ReadEvent()) != null)
			{
				string text = "";
				try
				{
					text = (eventRecord.FormatDescription() ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
				}
				catch
				{
				}
				if (text.Length > 160)
				{
					text = text.Substring(0, 160) + "…";
				}
				list.Add($"  {eventRecord.TimeCreated:yyyy-MM-dd HH:mm:ss}  ({eventRecord.Id}) {text}");
				eventRecord.Dispose();
			}
		}
		catch
		{
		}
		return list;
	}
}
