using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GhostFix;

public static class Crashes
{
	public static string HintFor(string module, string code)
	{
		string text = (module + " " + code).ToLowerInvariant();
		if (Regex.IsMatch(text, "d3d8|d3d9|ddraw|nvwgf|nvd3d|atiu|amdxx|igd|ig\\d+icd"))
		{
			return "그래픽 드라이버/DirectX → 드라이버 업데이트, 전체 화면 최적화 끄기, DirectX 런타임(2010) 설치";
		}
		if (Regex.IsMatch(text, "msvcr|msvcp|vcruntime"))
		{
			return "VC++ 런타임 → VC++ 재배포 패키지(x86) 재설치";
		}
		if (text.Contains("c0000409"))
		{
			return "스택 보호 오류 → 게임 자체 버그일 가능성";
		}
		if (text.Contains("c0000374"))
		{
			return "힙 손상 → LAA, 오버레이(엔비디아/디스코드/게임바) 끄기, 새벽 유지 관리 피하기";
		}
		if (Regex.IsMatch(text, "c0000005|c00000fd|ntdll|kernelbase"))
		{
			return "메모리 접근 오류 → LAA, DEP 예외";
		}
		if (Regex.IsMatch(text, "^unknown 0*$"))
		{
			return "게임이 스스로 종료(대개 'Connect Error 132' 서버 연결 끊김) → PC 절전·NIC 절전·공유기·Wi‑Fi 확인";
		}
		return "";
	}

	public static List<CrashEntry> Get(string exeName, int max)
	{
		List<CrashEntry> list = new List<CrashEntry>();
		using (EventLog eventLog = new EventLog("Application"))
		{
			EventLogEntryCollection entries = eventLog.Entries;
			int i = 0;
			for (int num = entries.Count - 1; num >= 0 && list.Count < max && i < 5000; num--, i++)
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
				long num2 = eventLogEntry.InstanceId & 0xFFFF;
				bool flag = num2 == 1000 && eventLogEntry.Source == "Application Error";
				bool flag2 = num2 == 1002 && eventLogEntry.Source == "Application Hang";
				if (!flag && !flag2)
				{
					continue;
				}
				string[] replacementStrings = eventLogEntry.ReplacementStrings;
				if (replacementStrings.Length != 0 && string.Equals(replacementStrings[0], exeName, StringComparison.OrdinalIgnoreCase))
				{
					if (flag2)
					{
						list.Add(new CrashEntry
						{
							Time = eventLogEntry.TimeGenerated,
							Module = "(응답 없음)",
							Code = "hang",
							Hint = "응답 없음 → 그래픽 드라이버·오버레이·디스크 과부하(유지 관리/백신 검사) 가능"
						});
					}
					else
					{
						string module = ((replacementStrings.Length > 3) ? replacementStrings[3] : "?");
						string code = ((replacementStrings.Length > 6) ? replacementStrings[6] : "?");
						list.Add(new CrashEntry
						{
							Time = eventLogEntry.TimeGenerated,
							Module = module,
							Code = code,
							Hint = HintFor(module, code)
						});
					}
				}
			}
		}
		return list;
	}

	public static string FormatForReport(string exeName, List<CrashEntry> list, string statusLine)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("[GhostFix 튕김 제보] " + exeName + " · Windows " + Environment.OSVersion.Version);
		if (!string.IsNullOrEmpty(statusLine))
		{
			stringBuilder.AppendLine("적용 상태: " + statusLine);
		}
		if (list.Count == 0)
		{
			stringBuilder.AppendLine("기록 없음");
		}
		foreach (CrashEntry item in list.Take(10))
		{
			stringBuilder.Append($"{item.Time:yyyy-MM-dd HH:mm}  {item.Module}  {item.Code}");
			if (!string.IsNullOrEmpty(item.Hint))
			{
				stringBuilder.Append("  → " + item.Hint);
			}
			stringBuilder.AppendLine();
		}
		return stringBuilder.ToString().TrimEnd();
	}
}
