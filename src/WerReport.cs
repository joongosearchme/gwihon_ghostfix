using System;
using System.Collections.Generic;

namespace GhostFix;

public class WerReport
{
	public DateTime Time;

	public string Folder;

	public List<string> Modules = new List<string>();

	public List<string> Foreign = new List<string>();
}
