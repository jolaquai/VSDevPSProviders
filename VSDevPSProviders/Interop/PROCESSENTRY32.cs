namespace VSDevPSProviders.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESSENTRY32
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public nuint th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int pcPriClassBase;
    public uint dwFlags;
    public SzExeFile szExeFile;

    // WCHAR[MAX_PATH]: ushort avoids char marshaling ambiguity; blittable by definition
    [InlineArray(260)]
    public struct SzExeFile { private ushort _; }
}
