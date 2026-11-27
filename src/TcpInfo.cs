using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace GhostFix;

public static class TcpInfo
{
	private struct MIB_TCPROW_OWNER_PID
	{
		public uint state;

		public uint localAddr;

		public uint localPort;

		public uint remoteAddr;

		public uint remotePort;

		public uint owningPid;
	}

	[DllImport("iphlpapi.dll", SetLastError = true)]
	private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tableClass, uint reserved);

	public static string Summary(int pid)
	{
		try
		{
			int dwOutBufLen = 0;
			GetExtendedTcpTable(IntPtr.Zero, ref dwOutBufLen, sort: false, 2, 5, 0u);
			IntPtr intPtr = Marshal.AllocHGlobal(dwOutBufLen);
			try
			{
				if (GetExtendedTcpTable(intPtr, ref dwOutBufLen, sort: false, 2, 5, 0u) != 0)
				{
					return "?";
				}
				int num = Marshal.ReadInt32(intPtr);
				IntPtr intPtr2 = IntPtr.Add(intPtr, 4);
				int offset = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
				List<string> list = new List<string>();
				int num2 = 0;
				int num3 = 0;
				while (num3 < num)
				{
					MIB_TCPROW_OWNER_PID mIB_TCPROW_OWNER_PID = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(intPtr2, typeof(MIB_TCPROW_OWNER_PID));
					if (mIB_TCPROW_OWNER_PID.owningPid == pid && mIB_TCPROW_OWNER_PID.state != 2)
					{
						if (mIB_TCPROW_OWNER_PID.state == 5)
						{
							IPAddress iPAddress = new IPAddress(mIB_TCPROW_OWNER_PID.remoteAddr);
							int num4 = (int)(((mIB_TCPROW_OWNER_PID.remotePort & 0xFF) << 8) | ((mIB_TCPROW_OWNER_PID.remotePort >> 8) & 0xFF));
							list.Add(iPAddress.ToString() + ":" + num4);
						}
						else
						{
							num2++;
						}
					}
					num3++;
					intPtr2 = IntPtr.Add(intPtr2, offset);
				}
				string text = ((list.Count == 0) ? "연결 없음" : ("ESTABLISHED " + string.Join(", ", list)));
				if (num2 > 0)
				{
					text = text + " (+기타 " + num2 + ")";
				}
				return text;
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
			}
		}
		catch (Exception ex)
		{
			return "? " + ex.Message;
		}
	}
}
