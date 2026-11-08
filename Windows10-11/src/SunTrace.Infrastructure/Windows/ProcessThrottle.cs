using System.Runtime.InteropServices;

namespace SunTrace.Infrastructure.Windows;

public static class ProcessThrottle
{
    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 0x1;

    public static void SetBackground(bool enabled)
    {
        try
        {
            var state = new PowerThrottlingState
            {
                Version = 1,
                ControlMask = ExecutionSpeed,
                StateMask = enabled ? ExecutionSpeed : 0
            };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        }
        catch (Exception)
        {
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }
}
