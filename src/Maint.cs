using System;
using Microsoft.Win32;

namespace GhostFix;

public static class Maint
{
	private const string Key = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Schedule\\Maintenance";

	private const string Time = "2000-01-01T14:00:00";

	private static RegistryKey Open(bool writable)
	{
		RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
		if (!writable)
		{
			return registryKey.OpenSubKey(Key);
		}
		return registryKey.CreateSubKey(Key);
	}

	public static bool IsMoved()
	{
		using RegistryKey registryKey = Open(writable: false);
		return registryKey != null && Time.Equals(registryKey.GetValue("Activation Boundary") as string, StringComparison.Ordinal);
	}

	public static void Set(bool on)
	{
		using RegistryKey registryKey = Open(writable: true);
		if (on)
		{
			registryKey.SetValue("Activation Boundary", Time, RegistryValueKind.String);
			registryKey.SetValue("WakeUp", 0, RegistryValueKind.DWord);
		}
		else
		{
			registryKey.DeleteValue("Activation Boundary", throwOnMissingValue: false);
			registryKey.DeleteValue("WakeUp", throwOnMissingValue: false);
		}
	}

	public static void SetDisabled(bool on)
	{
		using RegistryKey registryKey = Open(writable: true);
		if (on)
		{
			registryKey.SetValue("MaintenanceDisabled", 1, RegistryValueKind.DWord);
		}
		else
		{
			registryKey.DeleteValue("MaintenanceDisabled", throwOnMissingValue: false);
		}
	}
}
