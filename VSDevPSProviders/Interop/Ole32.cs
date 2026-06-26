using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.Marshalling;

namespace VSDevPSProviders.Interop.Interop;

internal static partial class Ole32
{
    [LibraryImport("ole32.dll")]
    public static partial int CreateBindCtx(uint r, [MarshalUsing(typeof(BindCtxMarshaler))] out IBindCtx p);

    [LibraryImport("ole32.dll")]
    public static partial int GetRunningObjectTable(uint r, [MarshalUsing(typeof(RotMarshaler))] out IRunningObjectTable p);
}

// Both marshalers follow the same pattern: native CreateBindCtx / GetRunningObjectTable
// hand back a pointer with refcount = 1 (caller owns). GetObjectForIUnknown AddRefs (+1),
// so Free must Release the raw pointer to net back to refcount = 1 held by the RCW.

/// <summary>
/// Marshals <see cref="IBindCtx"/> to and from unmanaged code.
/// </summary>
[CustomMarshaller(typeof(IBindCtx), MarshalMode.ManagedToUnmanagedOut, typeof(BindCtxMarshaler))]
internal struct BindCtxMarshaler
{
    private nint _ptr;

    public void FromUnmanaged(nint ptr) => _ptr = ptr;
    public IBindCtx ToManaged() => _ptr == nint.Zero ? null : (IBindCtx)Marshal.GetObjectForIUnknown(_ptr);
    public void Free() { if (_ptr != nint.Zero) Marshal.Release(_ptr); }
}

/// <summary>
/// Marshals <see cref="IRunningObjectTable"/> to and from unmanaged code.
/// </summary>
[CustomMarshaller(typeof(IRunningObjectTable), MarshalMode.ManagedToUnmanagedOut, typeof(RotMarshaler))]
internal struct RotMarshaler
{
    private nint _ptr;

    public void FromUnmanaged(nint ptr) => _ptr = ptr;
    public IRunningObjectTable ToManaged() => _ptr == nint.Zero ? null : (IRunningObjectTable)Marshal.GetObjectForIUnknown(_ptr);
    public void Free() { if (_ptr != nint.Zero) Marshal.Release(_ptr); }
}
