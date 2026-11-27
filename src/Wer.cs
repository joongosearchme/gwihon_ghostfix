using System;
using System.IO;

namespace GhostFix;

public static class Wer
{
	private static readonly string[][] Known = new string[24][]
	{
		new string[2] { "nvspcap", "NVIDIA 오버레이(ShadowPlay/엔비디아 앱)" },
		new string[2] { "nvinject", "NVIDIA 오버레이" },
		new string[2] { "discordhook", "디스코드 오버레이" },
		new string[2] { "gameoverlayrenderer", "Steam 오버레이" },
		new string[2] { "rtsshooks", "RivaTuner/MSI Afterburner" },
		new string[2] { "graphics-hook", "OBS 게임 캡처" },
		new string[2] { "bdcap", "반디캠" },
		new string[2] { "fraps", "Fraps" },
		new string[2] { "xsplit", "XSplit" },
		new string[2] { "medal", "Medal" },
		new string[2] { "gamebar", "Xbox Game Bar" },
		new string[2] { "twitchnativeoverlay", "Twitch 오버레이" },
		new string[2] { "npgg", "게임가드(정상)" },
		new string[2] { "npgmup", "게임가드(정상)" },
		new string[2] { "ehsvc", "핵쉴드(정상)" },
		new string[2] { "xigncode", "XIGNCODE(정상)" },
		new string[2] { "aswhook", "Avast/AVG 후킹" },
		new string[2] { "klhk", "Kaspersky 후킹" },
		new string[2] { "asdnet", "V3 후킹" },
		new string[2] { "ahn", "AhnLab 후킹" },
		new string[2] { "mcsc", "McAfee 후킹" },
		new string[2] { "mbae", "Malwarebytes 후킹" },
		new string[2] { "kakao", "카카오 관련" },
		new string[2] { "nmhook", "네이버 관련" }
	};

	public static string Describe(string path)
	{
		string text = path.Substring(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1).ToLowerInvariant();
		string[][] known = Known;
		foreach (string[] array in known)
		{
			if (text.StartsWith(array[0]))
			{
				return array[1];
			}
		}
		return "";
	}

	public static WerReport GetLatest(string exeName, string gameDir)
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string[] obj = new string[4]
		{
			Path.Combine(folderPath, "Microsoft\\Windows\\WER\\ReportArchive"),
			Path.Combine(folderPath, "Microsoft\\Windows\\WER\\ReportQueue"),
			Path.Combine(folderPath2, "Microsoft\\Windows\\WER\\ReportArchive"),
			Path.Combine(folderPath2, "Microsoft\\Windows\\WER\\ReportQueue")
		};
		string value = "AppCrash_" + exeName;
		DirectoryInfo directoryInfo = null;
		string[] array = obj;
		foreach (string path in array)
		{
			try
			{
				if (!Directory.Exists(path))
				{
					continue;
				}
				DirectoryInfo[] directories = new DirectoryInfo(path).GetDirectories();
				foreach (DirectoryInfo directoryInfo2 in directories)
				{
					if (directoryInfo2.Name.StartsWith(value, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(directoryInfo2.FullName, "Report.wer")) && (directoryInfo == null || directoryInfo2.LastWriteTime > directoryInfo.LastWriteTime))
					{
						directoryInfo = directoryInfo2;
					}
				}
			}
			catch
			{
			}
		}
		if (directoryInfo == null)
		{
			return null;
		}
		WerReport werReport = new WerReport
		{
			Time = directoryInfo.LastWriteTime,
			Folder = directoryInfo.FullName
		};
		string folderPath3 = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		array = File.ReadAllLines(Path.Combine(directoryInfo.FullName, "Report.wer"));
		foreach (string text in array)
		{
			if (!text.StartsWith("LoadedModule[", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			int num = text.IndexOf('=');
			if (num >= 0)
			{
				string text2 = text.Substring(num + 1).Trim();
				werReport.Modules.Add(text2);
				bool num2 = text2.StartsWith(folderPath3, StringComparison.OrdinalIgnoreCase);
				bool flag = !string.IsNullOrEmpty(gameDir) && text2.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase);
				if (!num2 && !flag)
				{
					werReport.Foreign.Add(text2);
				}
			}
		}
		return werReport;
	}
}
