using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GhostFix;

public static class PowerInfo
{
	private const uint ES_CONTINUOUS = 2147483648u;

	private const uint ES_SYSTEM_REQUIRED = 1u;

	private static bool awake;

	private static readonly string[][] NetApps = new string[18][]
	{
		new string[2] { "expressvpn", "ExpressVPN" },
		new string[2] { "nordvpn", "NordVPN" },
		new string[2] { "surfshark", "Surfshark" },
		new string[2] { "protonvpn", "ProtonVPN" },
		new string[2] { "openvpn", "OpenVPN" },
		new string[2] { "wireguard", "WireGuard" },
		new string[2] { "cyberghost", "CyberGhost" },
		new string[2] { "exitlag", "ExitLag" },
		new string[2] { "wtfast", "WTFast" },
		new string[2] { "mudfish", "Mudfish" },
		new string[2] { "gearup", "GearUP" },
		new string[2] { "killer", "Killer 네트워크" },
		new string[2] { "zonealarm", "ZoneAlarm" },
		new string[2] { "glasswire", "GlassWire" },
		new string[2] { "netlimiter", "NetLimiter" },
		new string[2] { "v3lite", "V3 Lite" },
		new string[2] { "asdsvc", "V3" },
		new string[2] { "alyac", "알약" }
	};

	[DllImport("kernel32.dll")]
	private static extern uint SetThreadExecutionState(uint esFlags);

	public static void KeepAwake(bool on)
	{
		if (on == awake)
		{
			return;
		}
		try
		{
			SetThreadExecutionState(on ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) : ES_CONTINUOUS);
			awake = on;
		}
		catch
		{
		}
	}

	public static string SleepTimeouts()
	{
		try
		{
			string text;
			using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes"))
			{
				text = ((registryKey == null) ? null : (registryKey.GetValue("ActivePowerScheme") as string));
			}
			if (string.IsNullOrEmpty(text))
			{
				return "절전 설정 읽기 실패 (활성 전원 구성 없음)";
			}
			string name = "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes\\" + text + "\\238c9fa8-0aad-41ed-83f4-97be242c8f20\\29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
			using RegistryKey registryKey2 = Registry.LocalMachine.OpenSubKey(name);
			if (registryKey2 == null)
			{
				return "절전 진입: 기본값(구성에 항목 없음)";
			}
			int num = ((registryKey2.GetValue("ACSettingIndex") is int) ? ((int)registryKey2.GetValue("ACSettingIndex")) : (-1));
			int num2 = ((registryKey2.GetValue("DCSettingIndex") is int) ? ((int)registryKey2.GetValue("DCSettingIndex")) : (-1));
			return string.Format("절전 진입: 전원 연결 {0}, 배터리 {1}", (num < 0) ? "기본값" : Fmt(num), (num2 < 0) ? "기본값" : Fmt(num2));
		}
		catch (Exception ex)
		{
			return "절전 설정 읽기 실패: " + ex.Message;
		}
	}

	private static string Fmt(int sec)
	{
		if (sec != 0)
		{
			if (sec % 60 != 0)
			{
				return sec + "초 후";
			}
			return sec / 60 + "분 후";
		}
		return "안 함";
	}

	public static string VpnProcesses()
	{
		try
		{
			List<string> names = Process.GetProcesses().Select(delegate(Process p)
			{
				try
				{
					return p.ProcessName.ToLowerInvariant();
				}
				catch
				{
					return "";
				}
			}).Distinct()
				.ToList();
			List<string> list = (from a in NetApps
				where names.Any((string n) => n.Contains(a[0]))
				select a[1]).Distinct().ToList();
			return (list.Count == 0) ? "VPN/네트워크 상주 프로그램: 없음" : ("VPN/네트워크 상주 프로그램 실행 중: " + string.Join(", ", list) + "  ← 끊김 원인일 수 있음, 종료 후 테스트 권장");
		}
		catch (Exception ex)
		{
			return "프로세스 조회 실패: " + ex.Message;
		}
	}

	public static List<string> NicPowerSaving()
	{
		List<string> list = new List<string>();
		try
		{
			Dictionary<string, bool> dictionary = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, Enable FROM MSPower_DeviceEnable"))
			{
				foreach (ManagementObject item in managementObjectSearcher.Get())
				{
					dictionary[((string)item["InstanceName"]).TrimEnd('_', '0')] = (bool)item["Enable"];
				}
			}
			using ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, PNPDeviceID, NetEnabled, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");
			foreach (ManagementObject item2 in managementObjectSearcher2.Get())
			{
				string pnp = item2["PNPDeviceID"] as string;
				if (!string.IsNullOrEmpty(pnp))
				{
					string text = dictionary.Keys.FirstOrDefault((string k) => k.StartsWith(pnp, StringComparison.OrdinalIgnoreCase) || pnp.StartsWith(k, StringComparison.OrdinalIgnoreCase));
					if (text != null)
					{
						bool flag = dictionary[text];
						bool flag2 = item2["NetEnabled"] is bool && (bool)item2["NetEnabled"];
						list.Add(string.Format("  {0}{1}: 장치 절전 {2}", item2["Name"], flag2 ? " (사용 중)" : "", flag ? "허용됨 ← 끄기 권장" : "꺼짐"));
					}
				}
			}
		}
		catch (Exception ex)
		{
			list.Add("  읽기 실패: " + ex.Message);
		}
		return list;
	}
}
