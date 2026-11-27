using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace GhostFix;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		bool flag = args.Length != 0 && args[0] == "--tray";
		if (!flag && !Watch.IsAdmin())
		{
			try
			{
				Process.Start(new ProcessStartInfo(Application.ExecutablePath)
				{
					UseShellExecute = true,
					Verb = "runas"
				});
				return;
			}
			catch
			{
				return;
			}
		}
		bool createdNew;
		using Mutex obj2 = new Mutex(initiallyOwned: true, flag ? "GhostFix-Tray" : "GhostFix-GUI", out createdNew);
		using EventWaitHandle eventWaitHandle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "GhostFix-Show");
		using EventWaitHandle obj3 = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "GhostFix-Quit");
		if (!createdNew)
		{
			if (!flag)
			{
				eventWaitHandle.Set();
			}
			return;
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		MainForm mainForm = new MainForm(eventWaitHandle, flag);
		if (flag)
		{
			Application.Run();
		}
		else
		{
			Application.Run(mainForm);
		}
		GC.KeepAlive(obj2);
		GC.KeepAlive(obj3);
	}
}
