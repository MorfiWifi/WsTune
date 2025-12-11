namespace WsTuneCommon.ConsoleHelpers;

using System;
using System.Runtime.InteropServices;

public class ConsoleHelper
{
    private const uint ENABLE_QUICK_EDIT = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_INPUT_HANDLE = -10;

    public static void DisableQuickEditMode()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        
        IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
        if (GetConsoleMode(handle, out uint mode))
        {
            mode &= ~ENABLE_QUICK_EDIT; // Disable Quick Edit
            mode |= ENABLE_EXTENDED_FLAGS; // Ensure extended flags are enabled
            SetConsoleMode(handle, mode);
        }
    }
}