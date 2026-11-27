using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GhostFix;

public static class Watch
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string RunName = "GhostFix";

	public const string TrayMutex = "GhostFix-Tray";

	public const string GuiMutex = "GhostFix-GUI";

	public const string ShowEvent = "GhostFix-Show";

	public const string QuitEvent = "GhostFix-Quit";

	private static readonly string[][] KeyServices = new string[17][]
	{
		new string[2] { "Schedule", "작업 스케줄러" },
		new string[2] { "Dhcp", "DHCP 클라이언트" },
		new string[2] { "Dnscache", "DNS 클라이언트" },
		new string[2] { "NlaSvc", "네트워크 위치 인식" },
		new string[2] { "nsi", "네트워크 저장소 인터페이스" },
		new string[2] { "iphlpsvc", "IP Helper" },
		new string[2] { "WlanSvc", "WLAN 자동 구성" },
		new string[2] { "LanmanWorkstation", "Workstation" },
		new string[2] { "EventLog", "이벤트 로그" },
		new string[2] { "Winmgmt", "WMI" },
		new string[2] { "BFE", "기본 필터링 엔진" },
		new string[2] { "mpssvc", "Windows 방화벽" },
		new string[2] { "W32Time", "시간 동기화" },
		new string[2] { "wuauserv", "Windows Update" },
		new string[2] { "WinDefend", "Defender" },
		new string[2] { "Power", "전원" },
		new string[2] { "WerSvc", "오류 보고" }
	};

	public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostFix");

	public static string InstalledExe => Application.ExecutablePath;

	public static string FlagPath => Path.Combine(Dir, "maint-off.flag");

	public static bool IsAdmin()
	{
		try
		{
			return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	public static bool IsRegistered()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		return registryKey != null && registryKey.GetValue("GhostFix") is string;
	}

	public static bool TrayRunning()
	{
		try
		{
			using (Mutex.OpenExisting("GhostFix-Tray"))
			{
				return true;
			}
		}
		catch
		{
			return false;
		}
	}

	public static void Register(string gameExe)
	{
		string text = Application.ExecutablePath.ToLowerInvariant();
		if (text.Contains("\\downloads\\") || text.Contains("\\temp\\") || text.Contains("\\tmp\\"))
		{
			throw new InvalidOperationException("GhostFix.exe를 다운로드/임시 폴더가 아닌 고정 폴더(예: C:\\GhostFix)에 옮긴 뒤 다시 적용하세요. 자동 시작이 그 위치를 가리킵니다.");
		}
		Directory.CreateDirectory(Dir);
		File.WriteAllText(Path.Combine(Dir, "game.txt"), gameExe);
		using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
		{
			registryKey.SetValue("GhostFix", "\"" + InstalledExe + "\" --tray");
		}
		if (!TrayRunning())
		{
			try
			{
				Process.Start(new ProcessStartInfo(InstalledExe, "--tray")
				{
					UseShellExecute = true
				});
			}
			catch
			{
			}
		}
	}

	public static void Unregister()
	{
		using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true))
		{
			registryKey?.DeleteValue("GhostFix", throwOnMissingValue: false);
		}
		try
		{
			using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("GhostFix-Quit");
			eventWaitHandle.Set();
		}
		catch
		{
		}
	}

	public static string SavedGameExe()
	{
		try
		{
			string path = Path.Combine(Dir, "game.txt");
			return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
		}
		catch
		{
			return null;
		}
	}

	public static string ServicesSummary()
	{
		try
		{
			ServiceController[] services = ServiceController.GetServices();
			HashSet<string> hashSet = new HashSet<string>(services.Select((ServiceController s) => s.ServiceName), StringComparer.OrdinalIgnoreCase);
			List<string> list = new List<string>();
			List<string> list2 = new List<string>();
			string[][] keyServices = KeyServices;
			foreach (string[] i in keyServices)
			{
				if (!hashSet.Contains(i[0]))
				{
					list.Add(i[1] + "(" + i[0] + ")");
				}
				else if (services.First((ServiceController s) => string.Equals(s.ServiceName, i[0], StringComparison.OrdinalIgnoreCase)).Status != ServiceControllerStatus.Running)
				{
					list2.Add(i[1] + "(" + i[0] + ")");
				}
			}
			string text = "서비스 " + services.Length + "개 (정식 Windows 11은 보통 250개 이상)";
			if (list.Count > 0)
			{
				text = text + "\n  없는 핵심 서비스: " + string.Join(", ", list) + "  ← 경량화/개조 Windows 의심";
			}
			if (list2.Count > 0)
			{
				text = text + "\n  꺼져 있는 핵심 서비스: " + string.Join(", ", list2);
			}
			return text;
		}
		catch (Exception ex)
		{
			return "서비스 조회 실패: " + ex.Message;
		}
	}
}
