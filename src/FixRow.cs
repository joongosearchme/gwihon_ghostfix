using System;
using System.Windows.Forms;

namespace GhostFix;

internal class FixRow
{
	public string Title;

	public string Detail;

	public bool DefaultOn;

	public Func<string, bool> IsApplied;

	public Action<string> Apply;

	public Action<string> Revert;

	public CheckBox Check;

	public Label Status;
}
