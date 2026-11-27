using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

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
		using (LockInstall(InstallDir))
		{
			Install();
			CreateTask();
			DeleteLegacyRun();
		}
		registered = true;
	}

	public static void Unregister()
	{
		using (LockInstall(InstallDir))
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
			// 작업·복사본을 읽고 바꾸는 것 전체를 설치 잠금 안에서 한다(다른 Windows 계정과 겹치지 않게).
			using (LockInstall(InstallDir))
			{
				MigrateTask();
			}
		}
		catch
		{
		}
	}

	private static void MigrateTask()
	{
		try
		{
			bool legacy = LegacyRunExists();
			string command = TaskCommand();
			if (!legacy && command == null)
			{
				return;
			}
			bool pointsToInstalled = command != null && IsInstalledExe(command);
			string external = (!string.IsNullOrEmpty(command) && !pointsToInstalled) ? command.Trim('"') : null;
			if (external != null && !SamePath(external, Application.ExecutablePath) && !SurelyMissing(external))
			{
				// 작업이 가리키는 다른 exe의 버전은 한 번만 읽고 그 값으로 정한다.
				Version target = VersionOf(external);
				Version self = SelfVersion;
				// 버전을 못 읽으면(네트워크 드라이브가 잠시 끊긴 경우 등) 이번에는 건드리지 않는다.
				if (target == null || self == null)
				{
					return;
				}
				// 작업이 이 exe보다 새 버전을 가리키면 자동 시작을 낮은 버전으로 바꾸지 않는다.
				// (옛 Run 항목이 같이 남아 있으면 그것만 지운다. 새 버전이 작업을 만든 뒤 지우다 끊긴 것이다.)
				if (target > self)
				{
					if (legacy)
					{
						DeleteLegacyRun();
					}
					return;
				}
			}
			// 복사본은 이 exe가 더 새 버전이거나 복사본이 없을 때만 바꾼다(작업이 복사본을 가리키면 낮은 버전으로 덮지 않는다).
			if (!pointsToInstalled || IsNewerThanInstalled())
			{
				Install();
			}
			if (legacy)
			{
				CreateTask();
				DeleteLegacyRun();
				registered = true;
				return;
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

	// 새 버전을 받아 실행한 것인가: 자동 시작이 실행하는 exe(1.2.0 부터는 Program Files 복사본,
	// 1.1.0 은 사용자가 고른 곳의 exe)보다 지금 exe가 새 버전이면 true.
	// 이때는 떠 있는 옛 GhostFix 창을 띄우지 않고 넘겨받아야 자동 시작도 새 버전으로 바뀐다.
	// 작업 스케줄러의 작업은 일반 권한으로도 읽힌다.
	public static bool IsUpgrade()
	{
		string self = Application.ExecutablePath;
		if (IsInstalledExe(self))
		{
			return false;
		}
		// 관리자 권한이면 설치 잠금을 잡은 채 작업과 복사본을 모두 읽고 판단한다. 다른 Windows 계정의 GhostFix 가
		// 복사본을 바꾸거나 작업을 옮기는 중이면 끝날 때까지 기다리므로, 바꾸는 도중의 상태로 판단하지 않는다.
		// (일반 권한일 때의 판단은 관리자 권한으로 다시 실행할지만 정하고, 끄는 신호는 관리자 권한에서 다시 판단한 뒤에 보낸다.)
		FileStream held = null;
		if (IsAdmin())
		{
			try
			{
				held = LockInstall(InstallDir);
			}
			catch
			{
				return false;
			}
		}
		using (held)
		{
			return IsUpgradeLocked(self);
		}
	}

	private static bool IsUpgradeLocked(string self)
	{
		// 작업이 아직 Program Files 복사본이 아닌 곳(1.1.0 방식)을 가리키면, 복사본이 있어도(옮기다 끊긴 경우) 작업부터 본다.
		string command = TaskCommand()?.Trim('"');
		if (!string.IsNullOrEmpty(command) && !IsInstalledExe(command))
		{
			try
			{
				// 가리키는 exe가 지워졌거나 옮겨졌으면 자동 시작이 이미 깨진 것이니 넘겨받아 고친다.
				// 접근 오류(네트워크 드라이브가 잠시 끊긴 경우 등)는 사라진 것으로 보지 않는다.
				if (SurelyMissing(command) && !SamePath(command, self))
				{
					return true;
				}
			}
			catch
			{
			}
			// 자동 시작은 그 exe를 실행하니 그것과만 비교한다(남은 옛 복사본과 다시 비교하면, 작업이 가리키는
			// 더 새 버전을 낮은 버전으로 바꾸게 된다).
			return SelfNewerThan(command);
		}
		// 자동 시작 작업이 없으면 넘겨받을 까닭이 없다. 작업을 지운 뒤 남은 옛 복사본(실행 중이라 못 지운 것)과 비교하면
		// 떠 있는 것이 같은 새 버전이어도 끄게 되므로, 작업이 복사본을 가리킬 때만 복사본과 비교한다.
		if (string.IsNullOrEmpty(command))
		{
			return false;
		}
		// 작업이 가리키는 복사본이 확실히 사라졌으면(복사본을 바꾸다 끊긴 경우 등) 자동 시작이 깨진 것이니 넘겨받아 다시 설치한다.
		return SurelyMissing(InstalledExe) || SelfNewerThan(InstalledExe);
	}

	// Install 을 부를지 정하는 데만 쓴다. 덮을지 말지는 Install 이 잠근 채 다시 따진다.
	private static bool IsNewerThanInstalled()
	{
		return !File.Exists(InstalledExe) || SelfNewerThan(InstalledExe);
	}

	private static bool SamePath(string a, string b)
	{
		try
		{
			return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	// 지금 실행 중인 GhostFix 의 버전. 디스크의 exe 경로가 아니라 메모리에 올라온 이 프로그램에서 읽는다
	// (실행한 뒤 그 경로의 파일이 바뀌어도 속지 않게).
	private static Version SelfVersion
	{
		get
		{
			try
			{
				return new Version(typeof(Watch).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
			}
			catch
			{
				return null;
			}
		}
	}

	// 지금 실행 중인 GhostFix 가 path 의 exe보다 새 버전인가. path 가 이 exe 경로이거나 버전을 못 읽으면 false.
	private static bool SelfNewerThan(string path)
	{
		if (SamePath(path, Application.ExecutablePath))
		{
			return false;
		}
		Version self = SelfVersion;
		Version other = VersionOf(path);
		return self != null && other != null && self > other;
	}

	// 파일이 확실히 없으면 true. 접근 오류 등으로 있는지 알 수 없으면 false.
	private static bool SurelyMissing(string path)
	{
		try
		{
			File.GetAttributes(path);
			return false;
		}
		catch (FileNotFoundException)
		{
			return true;
		}
		catch (DirectoryNotFoundException)
		{
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static Version VersionOf(string path)
	{
		try
		{
			return new Version(FileVersionInfo.GetVersionInfo(path).FileVersion);
		}
		catch
		{
			return null;
		}
	}

	// 지금 실행 중인 exe를 Program Files\GhostFix\GhostFix.exe 로 복사한다. 이미 그 exe면 그대로 둔다.
	// 복사본이 실행 중이라 덮어쓸 수 없으면, 실행 중인 파일은 이름을 바꿀 수는 있으므로 옆으로 치우고 복사한다.
	// 부르는 쪽이 설치 잠금(LockInstall)을 잡고 있어야 한다. 복사본·작업은 모든 Windows 계정이 같이 쓰므로,
	// 버전 확인부터 복사본·작업을 바꿔 놓기까지를 잠근 채 한 번에 해야 그사이 다른 계정이 바꾼 것을 덮지 않는다.
	private static void Install()
	{
		string source = Application.ExecutablePath;
		if (IsInstalledExe(source))
		{
			return;
		}
		InstallLocked(source);
	}

	// 설치 잠금: Program Files\GhostFix\install.lock 을 독점으로 연다. 이 파일은 관리자·SYSTEM 만 열 수 있게 만들어
	// (일반 사용자는 읽기로도 못 연다) 다른 프로그램이 먼저 잡아 설치를 막을 수 없다. 폴더도 관리자만 쓸 수 있다.
	private static FileStream LockInstall(string dir)
	{
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "install.lock");
		FileSecurity security = new FileSecurity();
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
		security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
		for (int i = 0; ; i++)
		{
			try
			{
				FileStream held = new FileStream(path, FileMode.OpenOrCreate, FileSystemRights.ReadData | FileSystemRights.WriteData | FileSystemRights.ReadPermissions | FileSystemRights.ChangePermissions, FileShare.None, 1, FileOptions.None, security);
				try
				{
					// 예전에 다른 권한으로 만들어진 파일이면 권한을 다시 좁힌다.
					held.SetAccessControl(security);
				}
				catch
				{
					held.Dispose();
					throw;
				}
				return held;
			}
			catch (IOException) when (i < 150)
			{
				Thread.Sleep(200);
			}
		}
	}

	private static void InstallLocked(string source)
	{
		string target = InstalledExe;
		// 복사본이 지금 실행 중인 이 GhostFix 보다 새 버전이면 덮지 않는다(자동 시작을 낮은 버전으로 바꾸지 않게).
		// 작업은 그대로 복사본을 가리키게 둔다. 복사본 버전을 못 읽으면(깨진 경우) 고치도록 덮는다.
		Version installed = File.Exists(target) ? VersionOf(target) : null;
		Version self = SelfVersion;
		if (installed != null && self != null && installed > self)
		{
			return;
		}
		string temp = target + ".new";
		string old = target + ".old";
		using (FileStream running = OpenRunningImage(source))
		using (FileStream copy = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			running.CopyTo(copy);
		}
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

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr GetModuleHandle(string moduleName);

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetMappedFileName(IntPtr process, IntPtr address, StringBuilder fileName, uint size);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

	private const uint VOLUME_NAME_NT = 2u;

	// 지금 실행 중인 바로 그 exe 파일을 연다.
	// 실행 중인 exe는 이름을 바꿀 수 있어서, 일반 권한 프로그램이 원래 이름을 옮겨 두고 그 자리에 다른 exe를 놓으면
	// 경로로 연 파일은 가짜가 된다(그걸 Program Files 에 복사하면 로그인 때마다 관리자 권한으로 실행된다).
	// 그래서 연 파일의 실제 위치가 Windows가 알려 주는 "실행 중인 exe의 지금 위치"와 같은지 확인한다.
	// 삭제 공유 없이 열어 두므로 확인한 뒤에는 복사가 끝날 때까지 이름을 바꿀 수 없다.
	// 두 값 모두 디스크에 적힌 대소문자 그대로 나오므로 대소문자까지 같아야 한다
	// (대소문자를 구분하는 폴더에서는 대소문자만 다른 이름이 서로 다른 파일이다).
	private static FileStream OpenRunningImage(string path)
	{
		FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		try
		{
			string opened = NtPath(stream.SafeFileHandle);
			StringBuilder mapped = new StringBuilder(1024);
			uint length = GetMappedFileName(GetCurrentProcess(), GetModuleHandle(null), mapped, (uint)mapped.Capacity);
			if (opened == null || length == 0 || length >= mapped.Capacity || !string.Equals(opened, mapped.ToString(), StringComparison.Ordinal))
			{
				throw new IOException("실행 중인 GhostFix 파일이 그 자리에 없거나 바뀌어 있어 복사하지 않습니다. GhostFix.exe를 다시 받아 실행해 주세요.");
			}
			return stream;
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}

	// 열린 파일의 위치를 \Device\HarddiskVolume3\... 형식으로(GetMappedFileName 과 같은 형식)
	private static string NtPath(SafeFileHandle handle)
	{
		StringBuilder stringBuilder = new StringBuilder(1024);
		uint length = GetFinalPathNameByHandle(handle, stringBuilder, (uint)stringBuilder.Capacity, VOLUME_NAME_NT);
		if (length >= stringBuilder.Capacity)
		{
			stringBuilder = new StringBuilder((int)length + 1);
			length = GetFinalPathNameByHandle(handle, stringBuilder, (uint)stringBuilder.Capacity, VOLUME_NAME_NT);
		}
		return (length == 0 || length >= stringBuilder.Capacity) ? null : stringBuilder.ToString();
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
