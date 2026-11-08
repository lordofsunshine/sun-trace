using System.Runtime.InteropServices;

namespace SunTrace.Infrastructure.Windows;

public static class PowerState
{
    private const byte OnBattery = 0;
    private const byte BatterySaverOn = 1;

    public static bool IsConstrained()
    {
        try
        {
            return GetSystemPowerStatus(out var status)
                   && (status.ACLineStatus == OnBattery || status.SystemStatusFlag == BatterySaverOn);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
