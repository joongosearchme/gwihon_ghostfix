using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GhostFix;

public static class Watch
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string RunName = "GhostFix";

	private const string TaskName = "GhostFix";

	// 프로세스를 하나만 띄우기 위한 이름들
	public const string InstanceMutex = "GhostFix";

	public const string ElevatedMutex = "GhostFix-Elevated";

	// 1.0.0 이 쓰던 감시용 프로세스 표시(옛 버전이 떠 있으면 끄라고 알린다)
	public const string OldTrayMutex = "GhostFix-Tray";

	public const string ShowEvent = "GhostFix-Show";

	public const string QuitEvent = "GhostFix-Quit";

	private static bool? registered;

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

	// 자동 시작(로그인 때 감시 시작)이 켜져 있는가. 경로 입력마다 불리므로 한 번만 조회해 둔다.
	public static bool IsRegistered()
	{
		if (!registered.HasValue)
		{
			registered = TaskExists() || LegacyRunExists();
		}
		return registered.Value;
	}

	// 자동 시작을 작업 스케줄러("가장 높은 권한으로 실행")에 등록한다.
	// 시작 프로그램(Run) 방식은 관리자 권한으로 띄울 수 없어 감시가 일반 권한으로 따로 떠야 했다.
	public static void Register(string gameExe)
	{
		string text = Application.ExecutablePath.ToLowerInvariant();
		if (text.Contains("\\downloads\\") || text.Contains("\\temp\\") || text.Contains("\\tmp\\"))
		{
			throw new InvalidOperationException("GhostFix.exe를 다운로드/임시 폴더가 아닌 고정 폴더(예: C:\\GhostFix)에 옮긴 뒤 다시 적용하세요. 자동 시작이 그 위치를 가리킵니다.");
		}
		Directory.CreateDirectory(Dir);
		File.WriteAllText(Path.Combine(Dir, "game.txt"), gameExe);
		CreateTask();
		DeleteLegacyRun();
		registered = true;
	}

	public static void Unregister()
	{
		DeleteLegacyRun();
		if (TaskExists())
		{
			RunSchtasks("/Delete /TN \"" + TaskName + "\" /F", out var output);
			if (TaskExists())
			{
				throw new InvalidOperationException("자동 시작 작업을 지우지 못했습니다. " + output);
			}
		}
		registered = false;
	}

	// 1.0.0 이 시작 프로그램(Run)에 등록해 둔 것을 작업 스케줄러로 옮긴다. 관리자 권한일 때만.
	public static void MigrateLegacy()
	{
		if (!IsAdmin() || !LegacyRunExists())
		{
			return;
		}
		try
		{
			CreateTask();
			DeleteLegacyRun();
			registered = true;
		}
		catch
		{
		}
	}

	private static bool LegacyRunExists()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(RunKey);
		return registryKey != null && registryKey.GetValue(RunName) is string;
	}

	private static void DeleteLegacyRun()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
		registryKey?.DeleteValue(RunName, throwOnMissingValue: false);
	}

	private static bool TaskExists()
	{
		try
		{
			return RunSchtasks("/Query /TN \"" + TaskName + "\"", out var _) == 0;
		}
		catch
		{
			return false;
		}
	}

	private static void CreateTask()
	{
		string user = WindowsIdentity.GetCurrent().Name;
		string exe = InstalledExe;
		string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
			+ "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n"
			+ "  <RegistrationInfo><Description>GhostFix: 로그인 때 트레이로 시작해 귀혼 실행 중 절전 방지·기록</Description></RegistrationInfo>\r\n"
			+ "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Xml(user) + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\r\n"
			+ "  <Principals><Principal id=\"Author\"><UserId>" + Xml(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n"
			+ "  <Settings>\r\n"
			+ "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n"
			+ "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n"
			+ "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n"
			+ "    <AllowHardTerminate>true</AllowHardTerminate>\r\n"
			+ "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n"
			+ "    <Enabled>true</Enabled>\r\n"
			+ "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n"
			+ "    <Priority>6</Priority>\r\n"
			+ "  </Settings>\r\n"
			+ "  <Actions Context=\"Author\"><Exec><Command>" + Xml(exe) + "</Command><Arguments>--tray</Arguments><WorkingDirectory>" + Xml(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>\r\n"
			+ "</Task>\r\n";
		Directory.CreateDirectory(Dir);
		string path = Path.Combine(Dir, "task.xml");
		File.WriteAllText(path, xml, Encoding.Unicode);
		try
		{
			if (RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + path + "\" /F", out var output) != 0)
			{
				throw new InvalidOperationException("자동 시작 작업을 만들지 못했습니다. " + output);
			}
		}
		finally
		{
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
		}
	}

	private static string Xml(string s)
	{
		return SecurityElement.Escape(s);
	}

	private static int RunSchtasks(string arguments, out string output)
	{
		ProcessStartInfo startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		using Process process = Process.Start(startInfo);
		string stderr = process.StandardError.ReadToEnd();
		string stdout = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		output = (stderr + " " + stdout).Trim();
		return process.ExitCode;
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
