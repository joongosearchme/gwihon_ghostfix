using System;
using System.Linq;
using Microsoft.Win32;

namespace GhostFix;

public static class Compat
{
	private const string LayersKey = "Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers";

	public static readonly string[] CompatTokens = new string[3] { "HIGHDPIAWARE", "DISABLEDXMAXIMIZEDWINDOWEDMODE", "RUNASADMIN" };

	public static readonly string[] DepTokens = new string[1] { "DisableNXShowUI" };

	public static string[] Get(string exe)
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
		string text = ((registryKey == null) ? null : (registryKey.GetValue(exe) as string));
		if (string.IsNullOrWhiteSpace(text))
		{
			return new string[0];
		}
		return (from t in text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
			where t != "~"
			select t).ToArray();
	}

	private static void Set(string exe, string[] tokens)
	{
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
		if (tokens.Length == 0)
		{
			registryKey.DeleteValue(exe, throwOnMissingValue: false);
		}
		else
		{
			registryKey.SetValue(exe, "~ " + string.Join(" ", tokens));
		}
	}

	public static bool Has(string exe, string[] tokens)
	{
		string[] cur = Get(exe);
		return tokens.All((string t) => cur.Contains(t, StringComparer.OrdinalIgnoreCase));
	}

	public static void Add(string exe, string[] tokens)
	{
		Set(exe, Get(exe).Union(tokens, StringComparer.OrdinalIgnoreCase).ToArray());
	}

	public static void Remove(string exe, string[] tokens)
	{
		Set(exe, (from t in Get(exe)
			where !tokens.Contains(t, StringComparer.OrdinalIgnoreCase)
			select t).ToArray());
	}
}
