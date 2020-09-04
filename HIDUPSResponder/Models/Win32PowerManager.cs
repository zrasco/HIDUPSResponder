// Source: https://www.codeproject.com/Articles/292725/Using-WM-POWER-events-to-monitor-a-UPS
// Credit: klinkenbecker

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

public enum ACLineStatus : byte
{
	Offline = 0,
	Online = 1,
	Unknown = 255,
}

public enum BatteryFlag : byte
{
	High = 1,
	Low = 2,
	Critical = 4,
	Charging = 9,
	NoSystemBattery = 128,
	Unknown = 255
}

// mirrors the unmanaged counterpart
[StructLayout(LayoutKind.Sequential)]
public class SystemPowerStatus
{
	public ACLineStatus ACLineStatus;
	public BatteryFlag BatteryFlag;
	public Byte BatteryLifePercent;
	public Byte Reserved1;
	public Int32 BatteryLifeTime;
	public Int32 BatteryFullLifeTime;
}

class Win32PowerManager
{
	[DllImport("Kernel32")]
	private static extern Boolean GetSystemPowerStatus(SystemPowerStatus sps);

	public static SystemPowerStatus GetSystemPowerStatus()
	{
		SystemPowerStatus sps = new SystemPowerStatus();
		GetSystemPowerStatus(sps);
		return sps;
	}

	[Flags]
	public enum ExitWindowsBits : uint
	{
		// ONE of the following five:
		LogOff = 0x00,
		ShutDown = 0x01,
		Reboot = 0x02,
		PowerOff = 0x08,
		RestartApps = 0x40,
		// plus AT MOST ONE of the following two:
		Force = 0x04,
		ForceIfHung = 0x10,
	}

	[Flags]
	public enum ShutdownReasonBits : uint
	{
		MajorApplication = 0x00040000,
		MajorHardware = 0x00010000,
		MajorLegacyApi = 0x00070000,
		MajorOperatingSystem = 0x00020000,
		MajorOther = 0x00000000,
		MajorPower = 0x00060000,
		MajorSoftware = 0x00030000,
		MajorSystem = 0x00050000,

		MinorBlueScreen = 0x0000000F,
		MinorCordUnplugged = 0x0000000b,
		MinorDisk = 0x00000007,
		MinorEnvironment = 0x0000000c,
		MinorHardwareDriver = 0x0000000d,
		MinorHotfix = 0x00000011,
		MinorHung = 0x00000005,
		MinorInstallation = 0x00000002,
		MinorMaintenance = 0x00000001,
		MinorMMC = 0x00000019,
		MinorNetworkConnectivity = 0x00000014,
		MinorNetworkCard = 0x00000009,
		MinorOther = 0x00000000,
		MinorOtherDriver = 0x0000000e,
		MinorPowerSupply = 0x0000000a,
		MinorProcessor = 0x00000008,
		MinorReconfig = 0x00000004,
		MinorSecurity = 0x00000013,
		MinorSecurityFix = 0x00000012,
		MinorSecurityFixUninstall = 0x00000018,
		MinorServicePack = 0x00000010,
		MinorServicePackUninstall = 0x00000016,
		MinorTermSrv = 0x00000020,
		MinorUnstable = 0x00000006,
		MinorUpgrade = 0x00000003,
		MinorWMI = 0x00000015,

		FlagUserDefined = 0x40000000,
		FlagPlanned = 0x80000000
	}

	[DllImport("user32.dll")]
	static extern bool ExitWindowsEx(ExitWindowsBits uFlags, ShutdownReasonBits dwReason);

	[STAThread]
	public static bool ShutDown()
	{
		return ExitWindowsEx(ExitWindowsBits.PowerOff | ExitWindowsBits.Force,
						ShutdownReasonBits.MajorPower | ShutdownReasonBits.MinorCordUnplugged);
	}
}
