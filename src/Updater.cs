using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GhostFix;

// 새 버전 알림.
// 자동 확인은 사용자가 허락했을 때만, "지금 확인" 버튼은 누를 때만 GitHub에 최신 배포 번호를 묻는다.
// 보내는 것은 요청 자체뿐이고 이 PC의 정보나 기록은 보내지 않는다.
public static class Updater
{
	private const string Repo = "gwihongg/gwihon_ghostfix";

	private const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

	// 최신 배포 페이지. 응답에 든 주소는 쓰지 않고 이 고정 주소만 연다.
	public const string LatestPage = "https://github.com/" + Repo + "/releases/latest";

	private const int MaxResponse = 1 << 20;

	private static string SettingPath => Path.Combine(Watch.Dir, "update-check.txt");

	// 자동 확인을 허락했는가. 아직 묻지 않았으면 null.
	public static bool? Allowed
	{
		get
		{
			try
			{
				if (!File.Exists(SettingPath))
				{
					return null;
				}
				string text = File.ReadAllText(SettingPath).Trim();
				if (text == "on")
				{
					return true;
				}
				if (text == "off")
				{
					return false;
				}
				return null;
			}
			catch
			{
				return null;
			}
		}
	}

	public static void SetAllowed(bool on)
	{
		Directory.CreateDirectory(Watch.Dir);
		File.WriteAllText(SettingPath, on ? "on" : "off");
	}

	public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version);

	// 최신 배포 버전. 확인하지 못하면 null.
	public static Task<Version> LatestAsync()
	{
		return Task.Run(() => Latest());
	}

	private static Version Latest()
	{
		try
		{
			// TLS 버전은 Windows 기본 선택에 맡긴다(.NET 4.8 + Windows 8.1 이상은 TLS 1.2 이상을 쓴다).
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestApi);
			request.UserAgent = "GhostFix-update-check";
			request.Accept = "application/vnd.github+json";
			request.Timeout = 10000;
			request.ReadWriteTimeout = 10000;
			// 다른 곳으로 넘기는 응답은 따라가지 않는다(다른 서버나 암호화 안 된 주소의 답을 믿지 않게).
			request.AllowAutoRedirect = false;
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			if (response.StatusCode != HttpStatusCode.OK || !string.Equals(response.ResponseUri.AbsoluteUri, LatestApi, StringComparison.Ordinal))
			{
				return null;
			}
			using Stream stream = response.GetResponseStream();
			using MemoryStream buffer = new MemoryStream();
			byte[] chunk = new byte[8192];
			int read;
			while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
			{
				buffer.Write(chunk, 0, read);
				if (buffer.Length > MaxResponse)
				{
					return null;
				}
			}
			JavaScriptSerializer serializer = new JavaScriptSerializer
			{
				MaxJsonLength = MaxResponse
			};
			return serializer.DeserializeObject(Encoding.UTF8.GetString(buffer.ToArray())) is Dictionary<string, object> release
				&& release.TryGetValue("tag_name", out object tag)
				? ParseTag(tag as string)
				: null;
		}
		catch
		{
			return null;
		}
	}

	// v1.2.3 형식의 태그만 받는다.
	public static Version ParseTag(string tag)
	{
		if (tag == null)
		{
			return null;
		}
		Match match = Regex.Match(tag, "^v?(\\d{1,9})(?:\\.(\\d{1,9}))?(?:\\.(\\d{1,9}))?(?:\\.(\\d{1,9}))?\\z");
		if (!match.Success)
		{
			return null;
		}
		int[] parts = new int[4];
		for (int i = 0; i < 4; i++)
		{
			parts[i] = match.Groups[i + 1].Success ? int.Parse(match.Groups[i + 1].Value) : 0;
		}
		return new Version(parts[0], parts[1], parts[2], parts[3]);
	}

	private static Version Normalize(Version v)
	{
		return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
	}

	// 화면에 보일 번호(1.2.1.0 → 1.2.1)
	public static string Show(Version v)
	{
		return v.Revision == 0 ? v.ToString(3) : v.ToString();
	}

	// 브라우저로 주소를 연다. 관리자 권한 GhostFix가 바로 열면 브라우저도 관리자 권한으로 뜨므로,
	// 이미 떠 있는 탐색기에 넘겨 보통 권한으로 열게 한다. 탐색기로 못 열면 예외를 내서 주소를 직접 열라고 안내하게 한다
	// (관리자 권한으로 브라우저를 열지 않는다).
	public static void OpenUrl(string url)
	{
		if (Watch.IsAdmin())
		{
			string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
			using (Process.Start(new ProcessStartInfo(explorer, "\"" + url + "\"")
			{
				UseShellExecute = false
			}))
			{
			}
			return;
		}
		using (Process.Start(new ProcessStartInfo(url)
		{
			UseShellExecute = true
		}))
		{
		}
	}
}
