using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace GhostFix;

internal static class Program
{
	// 프로세스는 하나만 띄운다. 감시(트레이)와 설정 창을 같은 프로세스가 맡는다.
	//   GhostFix.exe          설정 창을 연다. 이미 떠 있으면 그 프로세스에 창을 띄우라고 알리고 끝난다.
	//   GhostFix.exe --tray   로그인 때 작업 스케줄러가 실행. 창 없이 트레이에서 감시만 한다.
	[STAThread]
	private static void Main(string[] args)
	{
		bool startHidden = args.Length != 0 && args[0] == "--tray";
		bool admin = Watch.IsAdmin();
		// --tray 는 1.0.0 이 시작 프로그램(Run)에 등록한 경우 일반 권한으로 올 수 있다. 그때는 그대로 감시만 한다.
		if (!admin && !startHidden)
		{
			// 관리자 권한 GhostFix 가 이미 떠 있으면 권한 확인(UAC) 없이 그 창만 띄운다.
			if (Exists(Watch.ElevatedMutex) && ShowExisting())
			{
				return;
			}
			try
			{
				Process.Start(new ProcessStartInfo(Application.ExecutablePath)
				{
					UseShellExecute = true,
					Verb = "runas"
				});
			}
			catch
			{
			}
			return;
		}
		Mutex instance;
		try
		{
			instance = new Mutex(initiallyOwned: false, Watch.InstanceMutex);
		}
		catch (UnauthorizedAccessException)
		{
			// 관리자 권한 GhostFix 가 이미 떠 있고 이쪽은 일반 권한
			return;
		}
		using (instance)
		{
			if (!BecomePrimary(instance, admin))
			{
				if (!startHidden)
				{
					ShowExisting();
				}
				return;
			}
			// 관리자 권한으로 떠 있다는 표시. 나중에 뜬 관리자 GhostFix 가 이걸 보고 넘겨받을지 정한다.
			Mutex elevated = (admin ? new Mutex(initiallyOwned: false, Watch.ElevatedMutex) : null);
			try
			{
				if (admin)
				{
					// 1.0.0 감시 프로세스가 떠 있으면 끄라고 알리고, 시작 프로그램 등록을 작업 스케줄러로 옮긴다.
					if (Exists(Watch.OldTrayMutex))
					{
						StopOldTray();
					}
					Watch.MigrateLegacy();
				}
				using EventWaitHandle showSignal = CreateSignal(Watch.ShowEvent);
				using EventWaitHandle quitSignal = CreateSignal(Watch.QuitEvent);
				showSignal.Reset();
				quitSignal.Reset();
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				MainForm mainForm = new MainForm(showSignal, quitSignal, startHidden);
				if (!startHidden)
				{
					mainForm.Show();
				}
				Application.Run();
				GC.KeepAlive(mainForm);
			}
			finally
			{
				elevated?.Dispose();
				try
				{
					instance.ReleaseMutex();
				}
				catch
				{
				}
			}
		}
	}

	// 이 프로세스가 감시·창을 맡을 하나가 되는가.
	// 일반 권한으로 떠 있는 GhostFix(1.0.0 방식 자동 시작)를 관리자 권한 GhostFix 가 만나면 그것을 끄고 넘겨받는다.
	private static bool BecomePrimary(Mutex instance, bool admin)
	{
		if (TryTake(instance, 0))
		{
			return true;
		}
		if (!admin || Exists(Watch.ElevatedMutex))
		{
			return false;
		}
		Signal(Watch.QuitEvent);
		return TryTake(instance, 10000);
	}

	// 1.0.0 감시 프로세스를 끈다. 1.0.0 은 창을 띄운 적이 없으면 종료 신호를 받아도 끝나지 않으므로
	// 3초 안에 안 끝나면 강제로 끝낸다. 1.0.0 감시는 일반 권한이라 되돌릴 설정을 바꾼 적이 없다.
	private static void StopOldTray()
	{
		Signal(Watch.QuitEvent);
		for (int i = 0; i < 15 && Exists(Watch.OldTrayMutex); i++)
		{
			Thread.Sleep(200);
		}
		if (!Exists(Watch.OldTrayMutex))
		{
			return;
		}
		int self = Process.GetCurrentProcess().Id;
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'GhostFix.exe'");
			foreach (ManagementBaseObject item in searcher.Get())
			{
				using (item)
				{
					int id = Convert.ToInt32(item["ProcessId"]);
					string commandLine = item["CommandLine"] as string;
					if (id == self || commandLine == null || !commandLine.Contains("--tray"))
					{
						continue;
					}
					try
					{
						using Process process = Process.GetProcessById(id);
						process.Kill();
						process.WaitForExit(3000);
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}

	private static bool TryTake(Mutex mutex, int timeout)
	{
		try
		{
			return mutex.WaitOne(timeout);
		}
		catch (AbandonedMutexException)
		{
			return true;
		}
	}

	private static bool Exists(string mutexName)
	{
		try
		{
			using (Mutex.OpenExisting(mutexName))
			{
				return true;
			}
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
		catch
		{
			return false;
		}
	}

	// 떠 있는 GhostFix 에 창을 띄우라고 알린다. 사용자가 방금 실행한 이쪽이 창을 앞으로 가져와도 된다고 허락해 준다.
	private static bool ShowExisting()
	{
		AllowSetForegroundWindow(-1);
		return Signal(Watch.ShowEvent);
	}

	[DllImport("user32.dll")]
	private static extern bool AllowSetForegroundWindow(int dwProcessId);

	private static bool Signal(string eventName)
	{
		try
		{
			using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting(eventName, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize);
			return eventWaitHandle.Set();
		}
		catch
		{
			return false;
		}
	}

	// 관리자 권한으로 만든 신호는 기본으로 관리자만 열 수 있다.
	// 같은 사용자의 일반 권한 GhostFix 도 신호를 보낼 수 있게 현재 사용자에게 열어 준다.
	private static EventWaitHandle CreateSignal(string name)
	{
		try
		{
			EventWaitHandleSecurity security = new EventWaitHandleSecurity();
			security.AddAccessRule(new EventWaitHandleAccessRule(WindowsIdentity.GetCurrent().User, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
			return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name, out var _, security);
		}
		catch
		{
			return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);
		}
	}
}
