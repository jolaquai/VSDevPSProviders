namespace VSDevPSProviders.Interop;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseHandle(nint h);
    [LibraryImport("kernel32.dll", SetLastError = true)] public static partial nint CreateToolhelp32Snapshot(uint f, uint pid);
    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool Process32First(nint h, ref PROCESSENTRY32 e);
    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool Process32Next(nint h, ref PROCESSENTRY32 e);
}