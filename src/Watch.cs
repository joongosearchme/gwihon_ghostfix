using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GhostFix;

public static class Watch
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string RunName = "GhostFix";

	private const string TaskName = "GhostFix";

	// 작업 스케줄러 상수
	private const int TASK_CREATE_OR_UPDATE = 6;

	private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

	private const int TASK_ACTION_EXEC = 0;

	// 프로세스를 하나만 띄우기 위한 이름들
	public const string InstanceMutex = "GhostFix";

	public const string ElevatedMutex = "GhostFix-Elevated";

	// 1.0.0 이 쓰던 감시용 프로세스 표시(옛 버전이 떠 있으면 끄라고 알린다)
	public const string OldTrayMutex = "GhostFix-Tray";

	public const string ShowEvent = "GhostFix-Show";

	public const string QuitEvent = "GhostFix-Quit";

	private static bool? registered;

	private static readonly Lazy<bool> admin = new Lazy<bool>(CheckAdmin);

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

	// 자동 시작이 실행하는 exe는 Program Files\GhostFix 에 복사해 둔다.
	// 작업 스케줄러는 "가장 높은 권한"으로 실행하므로, 일반 권한으로 바꿔치기할 수 있는 곳(바탕화면·C:\GhostFix 등)의 exe를 가리키면 안 된다.
	public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GhostFix");

	public static string InstalledExe => Path.Combine(InstallDir, "GhostFix.exe");

	// 기록(session.log)·게임 경로(game.txt)·유지 관리 표시(maint-off.flag)를 두는 곳.
	// 관리자 권한 GhostFix 는 일반 권한으로 바꿀 수 없는 Program Files\GhostFix\data 를 쓴다(링크로 다른 파일을 지우거나 덮어쓰게 만드는 것을 막는다).
	// 일반 권한으로 떠 있을 때(1.0.0 방식 자동 시작)만 예전처럼 사용자 폴더를 쓴다.
	public static string Dir => IsAdmin() ? Path.Combine(InstallDir, "data") : LegacyDir;

	private static string LegacyDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostFix");

	public static string FlagPath => Path.Combine(Dir, "maint-off.flag");

	private static string GamePath => Path.Combine(Dir, "game.txt");

	public static bool IsAdmin()
	{
		return admin.Value;
	}

	private static bool CheckAdmin()
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

	// 관리자 권한으로 이 exe를 다시 실행한다. 사용자가 권한 확인(UAC)을 거절하면 false.
	public static bool RelaunchElevated()
	{
		try
		{
			Process.Start(new ProcessStartInfo(Application.ExecutablePath)
			{
				UseShellExecute = true,
				Verb = "runas"
			});
			return true;
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
			registered = TaskCommand() != null || LegacyRunExists();
		}
		return registered.Value;
	}

	// 자동 시작을 작업 스케줄러("가장 높은 권한으로 실행")에 등록한다.
	// 시작 프로그램(Run) 방식은 관리자 권한으로 띄울 수 없어 감시가 일반 권한으로 따로 떠야 했다.
	public static void Register(string gameExe)
	{
		Directory.CreateDirectory(Dir);
		File.WriteAllText(GamePath, gameExe);
		Install();
		CreateTask();
		DeleteLegacyRun();
		registered = true;
	}

	public static void Unregister()
	{
		DeleteLegacyRun();
		if (TaskCommand() != null)
		{
			try
			{
				TaskFolder().DeleteTask(TaskName, 0);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("자동 시작 작업을 지우지 못했습니다. " + ex.Message);
			}
		}
		// 복사해 둔 exe도 지운다. 지금 실행 중인 것이 그 exe면 지울 수 없으니 남긴다(다음에 등록할 때 덮어쓴다).
		if (!IsInstalledExe(Application.ExecutablePath))
		{
			try
			{
				File.Delete(InstalledExe);
			}
			catch
			{
			}
		}
		registered = false;
	}

	// 관리자 권한으로 시작할 때 예전 방식을 옮긴다.
	// - 1.0.0: 시작 프로그램(Run) 등록 → 작업 스케줄러로
	// - 1.1.0: 작업이 사용자가 고른 폴더의 exe를 가리킴 → Program Files 복사본으로
	// - 1.1.0 까지 사용자 폴더에 두던 게임 경로·유지 관리 표시 → 새 폴더로
	// 이 exe가 복사본보다 새 버전이면 복사본을 바꾼다(새 버전을 받아 실행하면 자동 시작도 새 버전으로).
	public static void Migrate()
	{
		if (!IsAdmin())
		{
			return;
		}
		try
		{
			MigrateData();
		}
		catch
		{
		}
		try
		{
			if (LegacyRunExists())
			{
				Install();
				CreateTask();
				DeleteLegacyRun();
				registered = true;
				return;
			}
			string command = TaskCommand();
			if (command == null)
			{
				return;
			}
			bool pointsToInstalled = IsInstalledExe(command);
			if (!pointsToInstalled || IsNewerThanInstalled())
			{
				Install();
			}
			if (!pointsToInstalled)
			{
				CreateTask();
			}
		}
		catch
		{
		}
	}

	private static void MigrateData()
	{
		string marker = Path.Combine(Dir, "migrated");
		if (!Directory.Exists(LegacyDir) || File.Exists(marker))
		{
			return;
		}
		Directory.CreateDirectory(Dir);
		// 예전 폴더의 파일은 읽기만 한다(관리자 권한으로 사용자 폴더의 파일을 지우거나 쓰지 않는다).
		string oldGame = Path.Combine(LegacyDir, "game.txt");
		if (!File.Exists(GamePath) && File.Exists(oldGame) && new FileInfo(oldGame).Length < 4096)
		{
			File.WriteAllText(GamePath, File.ReadAllText(oldGame).Trim());
		}
		// 예전 버전이 게임 중에 자동 유지 관리를 끈 채로 끝났으면, 새 표시를 남겨 감시가 되돌리게 한다.
		if (File.Exists(Path.Combine(LegacyDir, "maint-off.flag")) && !File.Exists(FlagPath))
		{
			File.WriteAllText(FlagPath, DateTime.Now.ToString("s"));
		}
		File.WriteAllText(marker, DateTime.Now.ToString("s"));
	}

	private static bool IsInstalledExe(string path)
	{
		try
		{
			return string.Equals(Path.GetFullPath(path.Trim('"')), InstalledExe, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsNewerThanInstalled()
	{
		if (!File.Exists(InstalledExe))
		{
			return true;
		}
		try
		{
			Version installed = new Version(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion);
			Version current = new Version(FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion);
			return current > installed;
		}
		catch
		{
			return false;
		}
	}

	// 지금 실행 중인 exe를 Program Files\GhostFix\GhostFix.exe 로 복사한다. 이미 그 exe면 그대로 둔다.
	// 복사본이 실행 중이라 덮어쓸 수 없으면, 실행 중인 파일은 이름을 바꿀 수는 있으므로 옆으로 치우고 복사한다.
	private static void Install()
	{
		string source = Application.ExecutablePath;
		if (IsInstalledExe(source))
		{
			return;
		}
		Directory.CreateDirectory(InstallDir);
		string target = InstalledExe;
		string temp = target + ".new";
		string old = target + ".old";
		File.Copy(source, temp, overwrite: true);
		try
		{
			File.Delete(old);
		}
		catch
		{
		}
		if (File.Exists(target))
		{
			try
			{
				File.Delete(target);
			}
			catch
			{
				File.Move(target, old);
			}
		}
		File.Move(temp, target);
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

	// 작업 스케줄러는 COM(Schedule.Service)으로 다룬다. 작업 정의를 파일로 쓰지 않고 문자열로 바로 넘긴다.
	private static dynamic TaskFolder()
	{
		dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true));
		service.Connect();
		return service.GetFolder("\\");
	}

	// 등록된 자동 시작 작업이 실행하는 exe 경로. 작업이 없거나 읽지 못하면 null.
	private static string TaskCommand()
	{
		try
		{
			dynamic task = TaskFolder().GetTask(TaskName);
			foreach (dynamic action in task.Definition.Actions)
			{
				if ((int)action.Type == TASK_ACTION_EXEC)
				{
					return (string)action.Path ?? "";
				}
			}
			return "";
		}
		catch
		{
			return null;
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
		try
		{
			TaskFolder().RegisterTask(TaskName, xml, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("자동 시작 작업을 만들지 못했습니다. " + ex.Message);
		}
	}

	private static string Xml(string s)
	{
		return SecurityElement.Escape(s);
	}

	public static string SavedGameExe()
	{
		try
		{
			return File.Exists(GamePath) ? File.ReadAllText(GamePath).Trim() : null;
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
