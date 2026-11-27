using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GhostFix;

internal class MainForm : Form
{
	private const string DefaultExe = "C:\\MGAME\\귀혼\\game.exe";

	private const string OpenChatUrl = "https://open.kakao.com/o/sj9j7gJi";

	private static readonly Color Green = Color.FromArgb(34, 139, 74);

	private static readonly Color Gray = Color.FromArgb(140, 140, 140);

	private static readonly Color Red = Color.FromArgb(200, 50, 50);

	private TextBox txtPath;

	private Label lblFile;

	private Label lblStatus;

	private Button btnApply;

	private Button btnRevert;

	private Button btnLog;

	private Button btnTray;

	private NotifyIcon tray;

	private System.Windows.Forms.Timer watchTimer;

	private System.Windows.Forms.Timer ipcTimer;

	private bool watching;

	private bool weDisabled;

	private readonly EventWaitHandle showSignal;

	private readonly List<FixRow> rows = new List<FixRow>();

	private TableLayoutPanel root;

	private TableLayoutPanel grid;

	private readonly List<Label> gridWrapLabels = new List<Label>();

	private Label note;

	private readonly bool trayMode;

	private Process gameProc;

	private DateTime gameStart;

	private DateTime lastSampleAt;

	private string lastSample;

	private string lastTcp;

	private int pollCount;

	private long peakPriv;

	private long peakVirt;

	private uint peakGdi;

	private DateTime lastWarn = DateTime.MinValue;

	private bool gameLaa;

	private string ExePath => txtPath.Text.Trim();

	public MainForm(EventWaitHandle showSignal, bool trayMode)
	{
		MainForm mainForm = this;
		this.showSignal = showSignal;
		this.trayMode = trayMode;
		Text = "GhostFix — 귀혼 튕김 완화";
		Font = new Font("Malgun Gothic", 10f);
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.ClientSize = new Size(760, 600);
		try
		{
			base.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch
		{
		}
		rows.Add(new FixRow
		{
			Title = "LAA (4GB 메모리)",
			Detail = "32비트 게임이 2GB 대신 4GB까지 쓰게 합니다. 원본은 game.exe.laa-backup 으로 백업.",
			DefaultOn = true,
			IsApplied = (string p) => File.Exists(p) && Pe.Read(p).Laa,
			Apply = delegate(string p)
			{
				Pe.SetLaa(p);
			},
			Revert = delegate(string p)
			{
				Pe.RestoreLaa(p);
			}
		});
		rows.Add(new FixRow
		{
			Title = "호환성 설정",
			Detail = "높은 DPI 재정의 · 전체 화면 최적화 끄기 · 관리자 권한 실행",
			DefaultOn = true,
			IsApplied = (string p) => Compat.Has(p, Compat.CompatTokens),
			Apply = delegate(string p)
			{
				Compat.Add(p, Compat.CompatTokens);
			},
			Revert = delegate(string p)
			{
				Compat.Remove(p, Compat.CompatTokens);
			}
		});
		rows.Add(new FixRow
		{
			Title = "DEP 예외 (선택)",
			Detail = "데이터 실행 방지에서 game.exe만 제외. ntdll / c0000005 튕김에 가끔 효과.",
			DefaultOn = false,
			IsApplied = (string p) => Compat.Has(p, Compat.DepTokens),
			Apply = delegate(string p)
			{
				Compat.Add(p, Compat.DepTokens);
			},
			Revert = delegate(string p)
			{
				Compat.Remove(p, Compat.DepTokens);
			}
		});
		rows.Add(new FixRow
		{
			Title = "자동 유지 관리 시간",
			Detail = "Windows 자동 유지 관리를 새벽 3시 → 낮 14시로 옮기고 절전 해제를 끕니다. (켜 두고 사냥할 때)",
			DefaultOn = true,
			IsApplied = (string p) => Maint.IsMoved(),
			Apply = delegate
			{
				Maint.Set(on: true);
			},
			Revert = delegate
			{
				Maint.Set(on: false);
			}
		});
		rows.Add(new FixRow
		{
			Title = "게임 중 절전 방지 + 연결·세션 기록 (선택)",
			Detail = "로그인 때 GhostFix가 트레이로 자동 시작되어 game.exe가 떠 있는 동안 PC 절전을 막고 메모리·서버 연결을 기록합니다. 관리자 권한일 때는 자동 유지 관리도 끕니다. 게임을 끄면 원래대로.",
			DefaultOn = false,
			IsApplied = (string p) => Watch.IsRegistered(),
			Apply = delegate(string p)
			{
				Watch.Register(p);
			},
			Revert = delegate
			{
				Watch.Unregister();
			}
		});
		BuildLayout();
		string text = Watch.SavedGameExe();
		if (!string.IsNullOrEmpty(text) && File.Exists(text))
		{
			txtPath.Text = text;
		}
		RefreshStatus();
		if (trayMode)
		{
			SetupTray();
			StartWatching();
			return;
		}
		System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
		timer.Interval = 700;
		timer.Tick += delegate
		{
			if (showSignal != null && showSignal.WaitOne(0))
			{
				mainForm.ShowFromTray();
			}
		};
		timer.Start();
	}

	private void SetupTray()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		contextMenuStrip.Items.Add("설정 창 열기", null, delegate
		{
			OpenGui();
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("감시 종료", null, delegate
		{
			Close();
		});
		tray = new NotifyIcon
		{
			Icon = (base.Icon ?? SystemIcons.Application),
			Text = "GhostFix — 귀혼 감시 중",
			Visible = true,
			ContextMenuStrip = contextMenuStrip
		};
		tray.DoubleClick += delegate
		{
			OpenGui();
		};
		ipcTimer = new System.Windows.Forms.Timer
		{
			Interval = 700
		};
		ipcTimer.Tick += delegate
		{
			try
			{
				using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("GhostFix-Quit");
				if (eventWaitHandle.WaitOne(0))
				{
					Close();
				}
			}
			catch
			{
			}
		};
		ipcTimer.Start();
	}

	private void OpenGui()
	{
		try
		{
			using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("GhostFix-Show");
			eventWaitHandle.Set();
			return;
		}
		catch
		{
		}
		try
		{
			Process.Start(new ProcessStartInfo(Application.ExecutablePath)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show("설정 창을 열지 못했습니다.\n" + ex.Message, "GhostFix");
		}
	}

	public void HideToTray()
	{
		if (trayMode)
		{
			return;
		}
		if (tray == null)
		{
			ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
			contextMenuStrip.Items.Add("열기", null, delegate
			{
				ShowFromTray();
			});
			contextMenuStrip.Items.Add("종료", null, delegate
			{
				Close();
			});
			tray = new NotifyIcon
			{
				Icon = (base.Icon ?? SystemIcons.Application),
				Text = "GhostFix — 설정 창 (숨김)",
				ContextMenuStrip = contextMenuStrip
			};
			tray.DoubleClick += delegate
			{
				ShowFromTray();
			};
		}
		tray.Visible = true;
		Hide();
		base.WindowState = FormWindowState.Normal;
	}

	public void ShowFromTray()
	{
		if (tray != null && !trayMode)
		{
			tray.Visible = false;
		}
		Show();
		base.WindowState = FormWindowState.Normal;
		Activate();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (base.WindowState == FormWindowState.Minimized)
		{
			HideToTray();
		}
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		StopWatching();
		if (tray != null)
		{
			tray.Visible = false;
			tray.Dispose();
		}
		base.OnFormClosing(e);
		Application.Exit();
	}

	private void StartWatching()
	{
		if (watching)
		{
			return;
		}
		watching = true;
		weDisabled = false;
		if (File.Exists(Watch.FlagPath) && !IsGameRunning())
		{
			RestoreMaint();
		}
		if (watchTimer == null)
		{
			watchTimer = new System.Windows.Forms.Timer
			{
				Interval = 10000
			};
			watchTimer.Tick += delegate
			{
				PollGame();
			};
		}
		PollGame();
		watchTimer.Start();
	}

	private void StopWatching()
	{
		if (watchTimer != null)
		{
			watchTimer.Stop();
		}
		watching = false;
		PowerInfo.KeepAwake(on: false);
		if (weDisabled || File.Exists(Watch.FlagPath))
		{
			RestoreMaint();
		}
	}

	private void PollGame()
	{
		try
		{
			bool flag = IsGameRunning();
			if (flag && !weDisabled && Watch.IsAdmin())
			{
				try
				{
					Maint.SetDisabled(on: true);
					Directory.CreateDirectory(Watch.Dir);
					File.WriteAllText(Watch.FlagPath, DateTime.Now.ToString("s"));
					weDisabled = true;
				}
				catch
				{
				}
			}
			if (tray != null)
			{
				tray.Text = (flag ? ("GhostFix — 게임 실행 중: 절전 방지" + (weDisabled ? " · 자동 유지 관리 꺼짐" : "")) : "GhostFix — 귀혼 감시 중");
			}
			else if (!flag && weDisabled)
			{
				RestoreMaint();
			}
			PowerInfo.KeepAwake(flag);
			SampleGame(flag);
		}
		catch
		{
		}
	}

	private void SampleGame(bool running)
	{
		bool flag = false;
		if (gameProc != null)
		{
			try
			{
				flag = gameProc.HasExited;
			}
			catch
			{
				try
				{
					Process.GetProcessById(gameProc.Id);
				}
				catch
				{
					flag = true;
				}
			}
		}
		if (running && (gameProc == null || flag))
		{
			Process[] processesByName = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExePath));
			if (processesByName.Length == 0)
			{
				return;
			}
			gameProc = processesByName[0];
			try
			{
				gameStart = gameProc.StartTime;
			}
			catch
			{
				gameStart = DateTime.Now;
			}
			peakPriv = (peakVirt = 0L);
			peakGdi = 0u;
			pollCount = 0;
			lastWarn = DateTime.MinValue;
			try
			{
				gameLaa = Pe.Read(ExePath).Laa;
			}
			catch
			{
				gameLaa = true;
			}
			SessionLog.Write("START pid=" + gameProc.Id + " LAA=" + (gameLaa ? "O" : "X"));
		}
		if (gameProc == null)
		{
			return;
		}
		if (running)
		{
			long num = 0L;
			long num2 = 0L;
			try
			{
				gameProc.Refresh();
				num = gameProc.PrivateMemorySize64 / 1048576;
				num2 = gameProc.VirtualMemorySize64 / 1048576;
			}
			catch
			{
			}
			uint num3 = SessionLog.Gdi(gameProc);
			uint num4 = SessionLog.User(gameProc);
			int num5 = 0;
			int num6 = 0;
			try
			{
				num5 = gameProc.HandleCount;
			}
			catch
			{
			}
			try
			{
				num6 = gameProc.Threads.Count;
			}
			catch
			{
			}
			if (num > peakPriv)
			{
				peakPriv = num;
			}
			if (num2 > peakVirt)
			{
				peakVirt = num2;
			}
			if (num3 > peakGdi)
			{
				peakGdi = num3;
			}
			WarnIfNearLimit(num2, num3);
			string text = TcpInfo.Summary(gameProc.Id);
			lastSample = $"up={Fmt(DateTime.Now - gameStart)}  priv={num}MB  virt={num2}MB  gdi={num3}  user={num4}  handles={num5}  threads={num6}  tcp: {text}";
			lastSampleAt = DateTime.Now;
			if (pollCount++ % 6 == 0)
			{
				SessionLog.Write(lastSample);
			}
			else if (lastTcp != null && lastTcp != text)
			{
				SessionLog.Write("TCP 변화: " + lastSample);
			}
			lastTcp = text;
			return;
		}
		string text2 = "?";
		try
		{
			if (gameProc.HasExited)
			{
				text2 = "0x" + gameProc.ExitCode.ToString("X8");
			}
		}
		catch
		{
		}
		string text3 = ((text2 == "0x00000000") ? "정상 종료" : (text2.StartsWith("0xC0") ? "오류로 종료(예외 코드)" : ((!(text2 == "?")) ? "게임 자체 종료(132 등)" : (Watch.IsAdmin() ? "" : "(일반 권한이라 종료 코드 읽기 불가)"))));
		if (lastSample != null)
		{
			SessionLog.Write($"마지막 표본({lastSampleAt:HH:mm:ss}): {lastSample}");
		}
		SessionLog.Write($"EXIT code={text2} {text3}  up={Fmt(DateTime.Now - gameStart)}  peakPriv={peakPriv}MB  peakVirt={peakVirt}MB  peakGdi={peakGdi}");
		lastSample = null;
		lastTcp = null;
		gameProc.Dispose();
		gameProc = null;
	}

	private static string Fmt(TimeSpan t)
	{
		return $"{(int)t.TotalHours}h{t.Minutes:00}m";
	}

	private void WarnIfNearLimit(long virtMB, uint gdi)
	{
		long num = (gameLaa ? 3400 : 1700);
		if ((virtMB < num && gdi < 9000) || (DateTime.Now - lastWarn).TotalMinutes < 30.0)
		{
			return;
		}
		lastWarn = DateTime.Now;
		string text = ((virtMB >= num) ? $"메모리 주소 공간 {virtMB}MB / 한계 약 {(gameLaa ? 4096 : 2048)}MB" : $"GDI 개체 {gdi}개 / 한계 10,000개");
		SessionLog.Write("WARN " + text);
		if (tray == null)
		{
			return;
		}
		tray.ShowBalloonTip(15000, "GhostFix — 곧 튕길 수 있습니다", text + "\n안전한 곳에서 재접속하면 초기화됩니다.", ToolTipIcon.Warning);
		try
		{
			SystemSounds.Exclamation.Play();
		}
		catch
		{
		}
	}

	private string ExtraReport(List<CrashEntry> crashes)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string fileName = Path.GetFileName(ExePath);
		foreach (CrashEntry item in crashes.Take(3))
		{
			List<string> list = Nearby.Events(item.Time, 3, fileName);
			stringBuilder.AppendLine($"-- {item.Time:MM-dd HH:mm} 튕김 전후 3분의 다른 이벤트 ({list.Count}건) --");
			foreach (string item2 in list.Take(12))
			{
				stringBuilder.AppendLine(item2);
			}
			if (list.Count > 12)
			{
				stringBuilder.AppendLine("  … 외 " + (list.Count - 12) + "건");
			}
		}
		stringBuilder.AppendLine("-- 전원/네트워크 설정 --");
		stringBuilder.AppendLine("  " + Watch.ServicesSummary());
		stringBuilder.AppendLine("  " + PowerInfo.SleepTimeouts());
		foreach (string item3 in PowerInfo.NicPowerSaving())
		{
			stringBuilder.AppendLine(item3);
		}
		stringBuilder.AppendLine("  " + PowerInfo.VpnProcesses());
		List<string> list2 = Nearby.GameGuardLogs(Path.GetDirectoryName(ExePath));
		if (list2.Count > 0)
		{
			stringBuilder.AppendLine("-- 게임가드 오류 로그 (GameGuard\\*.erl, 최근 순) --");
			foreach (string item4 in list2)
			{
				stringBuilder.AppendLine(item4);
			}
		}
		List<string> list3 = Nearby.Defender(8);
		if (list3.Count > 0)
		{
			stringBuilder.AppendLine("-- Windows Defender 탐지/조치/설정 변경 (최근 순) --");
			foreach (string item5 in list3)
			{
				stringBuilder.AppendLine(item5);
			}
		}
		string value = SessionLog.Tail(5);
		if (!string.IsNullOrEmpty(value))
		{
			stringBuilder.AppendLine("-- 마지막 게임 세션 (GhostFix 감시 로그) --");
			stringBuilder.AppendLine(value);
		}
		WerReport werReport = null;
		try
		{
			werReport = Wer.GetLatest(Path.GetFileName(ExePath), Path.GetDirectoryName(ExePath));
		}
		catch
		{
		}
		if (werReport != null)
		{
			stringBuilder.AppendLine($"-- 마지막 WER 보고서 {werReport.Time:yyyy-MM-dd HH:mm} · 로드된 DLL {werReport.Modules.Count}개, 외부 DLL {werReport.Foreign.Count}개 --");
			foreach (string item6 in werReport.Foreign.Take(20))
			{
				string text = Wer.Describe(item6);
				stringBuilder.AppendLine("  " + Path.GetFileName(item6) + ((text.Length > 0) ? ("  ← " + text) : "") + "   (" + Path.GetDirectoryName(item6) + ")");
			}
			if (werReport.Foreign.Count > 20)
			{
				stringBuilder.AppendLine("  … 외 " + (werReport.Foreign.Count - 20) + "개");
			}
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private void RestoreMaint()
	{
		try
		{
			Maint.SetDisabled(on: false);
		}
		catch
		{
		}
		try
		{
			if (File.Exists(Watch.FlagPath))
			{
				File.Delete(Watch.FlagPath);
			}
		}
		catch
		{
		}
		weDisabled = false;
		if (tray != null)
		{
			tray.Text = "GhostFix — 귀혼 튕김 완화";
		}
	}

	private void BuildLayout()
	{
		root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			Padding = new Padding(16, 14, 16, 12)
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		base.Controls.Add(root);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 3,
			Margin = new Padding(0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "게임 파일",
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 6, 8, 0)
		}, 0, 0);
		txtPath = new TextBox
		{
			Text = "C:\\MGAME\\귀혼\\game.exe",
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 3, 6, 0)
		};
		txtPath.TextChanged += delegate
		{
			RefreshStatus();
		};
		tableLayoutPanel.Controls.Add(txtPath, 1, 0);
		Button button = new Button
		{
			Text = "찾기...",
			AutoSize = true,
			Margin = new Padding(0)
		};
		button.Click += delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "게임 실행 파일 (*.exe)|*.exe",
				FileName = Path.GetFileName(txtPath.Text)
			};
			try
			{
				openFileDialog.InitialDirectory = Path.GetDirectoryName(txtPath.Text);
			}
			catch
			{
			}
			if (openFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				txtPath.Text = openFileDialog.FileName;
			}
		};
		tableLayoutPanel.Controls.Add(button, 2, 0);
		root.Controls.Add(tableLayoutPanel, 0, 0);
		lblFile = new Label
		{
			AutoSize = true,
			ForeColor = Gray,
			Margin = new Padding(0, 2, 0, 10)
		};
		root.Controls.Add(lblFile, 0, 1);
		grid = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 3,
			Margin = new Padding(0),
			CellBorderStyle = TableLayoutPanelCellBorderStyle.None
		};
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		int num = 0;
		foreach (FixRow row in rows)
		{
			grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			row.Check = new CheckBox
			{
				Checked = row.DefaultOn,
				AutoSize = true,
				Margin = new Padding(2, 6, 6, 0)
			};
			grid.Controls.Add(row.Check, 0, num);
			grid.Controls.Add(TitleAndDetail(row.Title, row.Detail), 1, num);
			row.Status = new Label
			{
				AutoSize = true,
				Anchor = AnchorStyles.Right,
				Margin = new Padding(8, 6, 0, 0),
				TextAlign = ContentAlignment.MiddleRight
			};
			grid.Controls.Add(row.Status, 2, num);
			num++;
		}
		grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		grid.Controls.Add(new Label
		{
			Text = "",
			AutoSize = true,
			Margin = new Padding(2, 6, 6, 0)
		}, 0, num);
		grid.Controls.Add(TitleAndDetail("튕김 제보 · 문의", "그래도 튕기면 [튕김 기록] → [기록 복사] 후 오픈채팅에 붙여넣어 주세요. 제작자가 보고 답합니다."), 1, num);
		Button button2 = new Button
		{
			Text = "오픈채팅 열기",
			AutoSize = true,
			Anchor = AnchorStyles.Right,
			Margin = new Padding(8, 4, 0, 0),
			Padding = new Padding(6, 1, 6, 1)
		};
		button2.Click += delegate
		{
			OpenChat();
		};
		grid.Controls.Add(button2, 2, num);
		grid.Resize += delegate
		{
			FitWrapLabels();
		};
		root.Controls.Add(grid, 0, 2);
		note = new Label
		{
			Text = "엔비디아 앱을 쓰면 설정 → 기능 → 'NVIDIA 오버레이'를 꺼 두세요. 옛날 게임 힙 손상(c0000374)의 흔한 원인입니다.\n게임 업데이트로 game.exe가 바뀌면 LAA가 풀리니 다시 적용하세요.",
			AutoSize = true,
			ForeColor = Color.FromArgb(90, 90, 90),
			Margin = new Padding(0, 4, 0, 10)
		};
		root.Controls.Add(note, 0, 3);
		root.Resize += delegate
		{
			FitWrapLabels();
		};
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 5,
			Margin = new Padding(0)
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		lblStatus = new Label
		{
			AutoSize = false,
			Dock = DockStyle.Fill,
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Gray,
			Margin = new Padding(0, 0, 8, 0)
		};
		tableLayoutPanel2.Controls.Add(lblStatus, 0, 0);
		btnTray = new Button
		{
			Text = "트레이로",
			AutoSize = true,
			Padding = new Padding(6, 2, 6, 2),
			Margin = new Padding(4, 0, 0, 0)
		};
		btnTray.Click += delegate
		{
			HideToTray();
		};
		tableLayoutPanel2.Controls.Add(btnTray, 1, 0);
		btnLog = new Button
		{
			Text = "튕김 기록",
			AutoSize = true,
			Padding = new Padding(6, 2, 6, 2),
			Margin = new Padding(4, 0, 0, 0)
		};
		btnLog.Click += delegate
		{
			ShowCrashes();
		};
		tableLayoutPanel2.Controls.Add(btnLog, 2, 0);
		btnRevert = new Button
		{
			Text = "원래대로",
			AutoSize = true,
			Padding = new Padding(6, 2, 6, 2),
			Margin = new Padding(4, 0, 0, 0)
		};
		btnRevert.Click += delegate
		{
			RevertAll();
		};
		tableLayoutPanel2.Controls.Add(btnRevert, 3, 0);
		btnApply = new Button
		{
			Text = "적용",
			AutoSize = true,
			Padding = new Padding(18, 2, 18, 2),
			Margin = new Padding(4, 0, 0, 0),
			Font = new Font(Font, FontStyle.Bold)
		};
		btnApply.Click += delegate
		{
			ApplyChecked();
		};
		tableLayoutPanel2.Controls.Add(btnApply, 4, 0);
		root.Controls.Add(tableLayoutPanel2, 0, 4);
		base.AcceptButton = btnApply;
	}

	private Control TitleAndDetail(string title, string detail)
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			AutoSize = true,
			ColumnCount = 1,
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 2, 0, 8),
			Controls = { (Control)new Label
			{
				Text = title,
				AutoSize = true,
				Font = new Font(Font, FontStyle.Bold),
				Margin = new Padding(0)
			} }
		};
		Label label = new Label
		{
			Text = detail,
			AutoSize = true,
			ForeColor = Color.FromArgb(90, 90, 90),
			Margin = new Padding(0, 1, 0, 0),
			MaximumSize = new Size(Math.Max(100, base.ClientSize.Width - Scale(218)), 0)
		};
		gridWrapLabels.Add(label);
		obj.Controls.Add(label);
		return obj;
	}

	private int Scale(int v)
	{
		return (int)Math.Round((float)(v * ((Control)this).DeviceDpi) / 96f);
	}

	private void FitWrapLabels()
	{
		if (note == null)
		{
			return;
		}
		int num = base.ClientSize.Width - Scale(218);
		if (num > 40)
		{
			foreach (Label gridWrapLabel in gridWrapLabels)
			{
				gridWrapLabel.MaximumSize = new Size(num, 0);
			}
		}
		int num2 = base.ClientSize.Width - Scale(36);
		if (num2 > 40)
		{
			note.MaximumSize = new Size(num2, 0);
		}
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		FitToContent();
	}

	private void FitToContent()
	{
		FitWrapLabels();
		root.PerformLayout();
		int num = root.Padding.Vertical;
		int[] rowHeights = root.GetRowHeights();
		foreach (int num2 in rowHeights)
		{
			num += num2;
		}
		num += Scale(6);
		if (num > 0 && num != base.ClientSize.Height)
		{
			base.ClientSize = new Size(base.ClientSize.Width, num);
		}
	}

	private bool IsGameRunning()
	{
		try
		{
			return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExePath)).Length != 0;
		}
		catch
		{
			return false;
		}
	}

	private void RefreshStatus()
	{
		string exePath = ExePath;
		bool flag = false;
		if (!File.Exists(exePath))
		{
			lblFile.Text = "파일이 없습니다. 경로를 확인하세요.";
			lblFile.ForeColor = Red;
		}
		else
		{
			try
			{
				PeInfo peInfo = Pe.Read(exePath);
				if (!peInfo.Valid)
				{
					lblFile.Text = "exe 파일이 아닙니다.";
					lblFile.ForeColor = Red;
				}
				else if (!peInfo.Is32)
				{
					lblFile.Text = "64비트 exe 입니다. LAA는 32비트 전용이라 적용할 수 없습니다.";
					lblFile.ForeColor = Red;
				}
				else
				{
					lblFile.Text = string.Format("{0:N1} MB · 32비트{1}", (double)new FileInfo(exePath).Length / 1048576.0, File.Exists(Pe.BackupPath(exePath)) ? " · 백업 있음" : "");
					lblFile.ForeColor = Gray;
					flag = true;
					string path = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "game.exe");
					if (!string.Equals(Path.GetFileName(exePath), "game.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
					{
						lblFile.Text += "  ·  같은 폴더에 game.exe가 있습니다. 실제 게임은 game.exe이니 그쪽을 고르세요 (이 파일은 런처일 수 있음).";
						lblFile.ForeColor = Color.FromArgb(190, 120, 0);
					}
				}
			}
			catch (Exception ex)
			{
				lblFile.Text = "읽기 실패: " + ex.Message;
				lblFile.ForeColor = Red;
			}
		}
		foreach (FixRow row in rows)
		{
			bool flag2 = false;
			try
			{
				flag2 = flag && row.IsApplied(exePath);
			}
			catch
			{
			}
			row.Status.Text = (flag2 ? "●  적용됨" : "○  미적용");
			row.Status.ForeColor = (flag2 ? Green : Gray);
		}
		btnApply.Enabled = flag;
		btnRevert.Enabled = flag;
	}

	private void ApplyChecked()
	{
		if (IsGameRunning())
		{
			MessageBox.Show(this, "귀혼이 실행 중입니다. 게임을 끄고 다시 적용하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> list3 = new List<string>();
		foreach (FixRow item in rows.Where((FixRow x) => x.Check.Checked))
		{
			try
			{
				if (item.IsApplied(ExePath))
				{
					list2.Add(item.Title.Replace(" (선택)", ""));
					continue;
				}
				item.Apply(ExePath);
				list.Add(item.Title.Replace(" (선택)", ""));
			}
			catch (Exception ex)
			{
				list3.Add(item.Title + ": " + ex.Message);
			}
		}
		RefreshStatus();
		string text = ((list.Count == 0 && list3.Count == 0) ? "이미 모두 적용되어 있습니다." : ((list.Count <= 0) ? "적용 실패" : ("적용 완료: " + string.Join(", ", list) + ((list2.Count > 0) ? ("  (이미 적용됨: " + string.Join(", ", list2) + ")") : ""))));
		lblStatus.Text = text;
		lblStatus.ForeColor = ((list3.Count > 0) ? Red : Green);
		if (list3.Count > 0)
		{
			MessageBox.Show(this, string.Join("\n", list3), "적용 실패", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void RevertAll()
	{
		if (MessageBox.Show(this, "LAA 백업 복원, 호환성/DEP 설정 삭제, 자동 유지 관리 시간 복구를 모두 되돌립니다.\n계속할까요?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
		if (IsGameRunning())
		{
			MessageBox.Show(this, "귀혼이 실행 중입니다. 게임을 끄고 다시 시도하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		List<string> list = new List<string>();
		foreach (FixRow row in rows)
		{
			try
			{
				row.Revert(ExePath);
			}
			catch (Exception ex)
			{
				list.Add(row.Title + ": " + ex.Message);
			}
		}
		RefreshStatus();
		lblStatus.Text = ((list.Count == 0) ? "원래대로 되돌렸습니다." : "일부 복원 실패");
		lblStatus.ForeColor = ((list.Count == 0) ? Green : Red);
		if (list.Count > 0)
		{
			MessageBox.Show(this, string.Join("\n", list), "복원 실패", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void ShowCrashes()
	{
		List<CrashEntry> list;
		try
		{
			Cursor = Cursors.WaitCursor;
			list = Crashes.Get(Path.GetFileName(ExePath), 15);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "이벤트 로그를 읽지 못했습니다.\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return;
		}
		finally
		{
			Cursor = Cursors.Default;
		}
		using Form form = new Form
		{
			Text = "튕김 기록 — " + Path.GetFileName(ExePath),
			Font = Font,
			StartPosition = FormStartPosition.CenterParent,
			AutoScaleDimensions = new SizeF(96f, 96f),
			AutoScaleMode = AutoScaleMode.Dpi,
			ClientSize = new Size(860, 520),
			MinimizeBox = false,
			ShowInTaskbar = false
		};
		string extra = "";
		try
		{
			extra = ExtraReport(list);
		}
		catch (Exception ex2)
		{
			extra = "추가 정보 수집 실패: " + ex2.Message;
		}
		ListView lv = new ListView
		{
			Dock = DockStyle.Fill,
			View = View.Details,
			FullRowSelect = true,
			GridLines = false,
			HeaderStyle = ColumnHeaderStyle.Nonclickable
		};
		lv.Columns.Add("시각", 140);
		lv.Columns.Add("모듈", 140);
		lv.Columns.Add("예외 코드", 100);
		lv.Columns.Add("해석", 420);
		lv.Resize += delegate
		{
			int num = lv.ClientSize.Width - 380;
			if (num > 100)
			{
				lv.Columns[3].Width = num;
			}
		};
		foreach (CrashEntry item in list)
		{
			lv.Items.Add(new ListViewItem(new string[4]
			{
				item.Time.ToString("yyyy-MM-dd HH:mm"),
				item.Module,
				item.Code,
				item.Hint
			}));
		}
		if (list.Count == 0)
		{
			lv.Items.Add(new ListViewItem(new string[4] { "", "", "", "기록이 없습니다. (게임가드가 끈 경우나 '응답 없음'은 여기 남지 않습니다.)" }));
		}
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Bottom,
			AutoSize = true,
			ColumnCount = 4,
			Padding = new Padding(10, 6, 10, 8)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Label info = new Label
		{
			Text = "[기록 복사] 후 오픈채팅에 붙여넣어 제보하세요.",
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = Gray,
			Margin = new Padding(0, 8, 8, 0)
		};
		tableLayoutPanel.Controls.Add(info, 0, 0);
		Button button = new Button
		{
			Text = "기록 복사",
			AutoSize = true,
			Padding = new Padding(8, 2, 8, 2),
			Margin = new Padding(4, 0, 0, 0),
			Font = new Font(Font, FontStyle.Bold)
		};
		button.Click += delegate
		{
			try
			{
				string text = Crashes.FormatForReport(Path.GetFileName(ExePath), list, StatusLine());
				if (extra.Length > 0)
				{
					text = text + "\r\n" + extra;
				}
				Clipboard.SetText(text);
				info.Text = "복사됨 — 오픈채팅에 붙여넣으세요.";
				info.ForeColor = Green;
			}
			catch (Exception ex3)
			{
				info.Text = "복사 실패: " + ex3.Message;
				info.ForeColor = Red;
			}
		};
		tableLayoutPanel.Controls.Add(button, 1, 0);
		Button button2 = new Button
		{
			Text = "오픈채팅 열기",
			AutoSize = true,
			Padding = new Padding(8, 2, 8, 2),
			Margin = new Padding(4, 0, 0, 0)
		};
		button2.Click += delegate
		{
			OpenChat();
		};
		tableLayoutPanel.Controls.Add(button2, 2, 0);
		Button button3 = new Button
		{
			Text = "닫기",
			AutoSize = true,
			Padding = new Padding(8, 2, 8, 2),
			Margin = new Padding(4, 0, 0, 0),
			DialogResult = DialogResult.Cancel
		};
		tableLayoutPanel.Controls.Add(button3, 3, 0);
		form.CancelButton = button3;
		TextBox value = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Dock = DockStyle.Bottom,
			Height = Scale(170),
			Font = new Font("Consolas", 9f),
			BackColor = Color.FromArgb(248, 248, 248),
			Text = ((extra.Length > 0) ? extra : "추가 정보 없음 (감시 모드를 켜 두면 게임 세션 로그가, 튕김 후에는 WER 보고서의 DLL 목록이 여기 나옵니다.)")
		};
		form.Controls.Add(lv);
		form.Controls.Add(value);
		form.Controls.Add(tableLayoutPanel);
		form.ShowDialog(this);
	}

	private string StatusLine()
	{
		List<string> list = new List<string>();
		foreach (FixRow row in rows)
		{
			bool flag = false;
			try
			{
				flag = row.IsApplied(ExePath);
			}
			catch
			{
			}
			list.Add(row.Title.Replace(" (선택)", "") + (flag ? " O" : " X"));
		}
		return string.Join(" / ", list);
	}

	private void OpenChat()
	{
		try
		{
			Process.Start(new ProcessStartInfo("https://open.kakao.com/o/sj9j7gJi")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "브라우저를 열지 못했습니다. 주소를 직접 여세요:\nhttps://open.kakao.com/o/sj9j7gJi\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}
}
