using System.Diagnostics;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

using EnvDTE;

using VSDevPSProviders.Interop.Interop;

namespace VSDevPSProviders;

[SupportedOSPlatform("windows")]
internal sealed partial class VSHelper
{
    private static readonly Lazy<VSHelper> _thisProcessCache = new Lazy<VSHelper>(CreateCore, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets the <see cref="DTE2"/> instance that represents the VS instance that owns the current process (i.e. the <c>powershell</c>/<c>pwsh</c> it launched).
    /// </summary>
    public readonly DTE2 DTE;

    private VSHelper(DTE2 dte)
    {
        DTE = dte;
    }

    // Runs on a thread-pool thread; COM marshaling handles cross-apartment DTE calls.
    public static VSHelper Create()
    {
        return _thisProcessCache.Value;
    }
    private static VSHelper CreateCore()
    {
        var devenvs = System.Diagnostics.Process.GetProcessesByName("devenv");
        if (devenvs.Length == 0)
            throw new InvalidOperationException("No devenv.exe is running.");

        var devenvPid = devenvs.Length == 1
            ? devenvs[0].Id
            : FindAncestorDevenv(Environment.ProcessId)
              ?? throw new InvalidOperationException("Could not resolve owning devenv.exe through process ancestry.");

        return new VSHelper(
            GetDTEForPid(devenvPid) ?? throw new InvalidOperationException($"DTE not found in ROT for devenv PID {devenvPid}.")
        );
    }

    // Walks the process tree upward until devenv.exe is found or the chain ends.
    private static int? FindAncestorDevenv(int pid)
    {
        for (var i = 0; i < 20; i++)
        {
            var parent = GetParentPid(pid);
            if (parent <= 0)
                return null;
            try
            {
                if (System.Diagnostics.Process.GetProcessById(parent).ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase))
                    return parent;
            }
            catch (ArgumentException) { return null; } // process already gone
            pid = parent;
        }
        return null;
    }

    // Enumerates the ROT for "!VisualStudio.DTE.<ver>:<pid>" and returns the matching DTE2.
    private static DTE2 GetDTEForPid(int pid)
    {
        Ole32.GetRunningObjectTable(0, out var rot);
        rot.EnumRunning(out var enumMoniker);
        var suffix = $":{pid}";
        var tmp = new IMoniker[1];
        while (enumMoniker.Next(1, tmp, nint.Zero) == 0)
        {
            Ole32.CreateBindCtx(0, out var ctx);
            try
            {
                tmp[0].GetDisplayName(ctx, null, out var name);
                if (name is not null
                    && name.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    rot.GetObject(tmp[0], out var obj);
                    return (DTE2)obj;
                }
            }
            catch (COMException) { }
            finally
            {
                Marshal.ReleaseComObject(ctx);
                if (tmp[0] is not null)
                    Marshal.ReleaseComObject(tmp[0]);
                tmp[0] = null;
            }
        }
        return null;
    }

    private static int GetParentPid(int pid)
    {
        var snap = Kernel32.CreateToolhelp32Snapshot(2u, 0u); // TH32CS_SNAPPROCESS
        if (snap is 0 or -1)
            return -1;
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Kernel32.Process32First(snap, ref e))
                return -1;
            do
            {
                if ((int)e.th32ProcessID == pid)
                    return (int)e.th32ParentProcessID;
            } while (Kernel32.Process32Next(snap, ref e));
        }
        finally { Kernel32.CloseHandle(snap); }
        return -1;
    }

    // Resolution priority: active document → Solution Explorer selection → unique startup project.
    // Solution folder items are excluded (they have empty FullName).
    public Project ResolveActiveProject()
    {
        var dte = DTE;
        if (dte.ActiveDocument?.ProjectItem?.ContainingProject is { } docProj && docProj.FullName?.Length > 0)
            return docProj;

        if (dte.ActiveSolutionProjects is object[] selected)
        {
            foreach (var obj in selected)
                if (obj is Project p && p.FullName?.Length > 0)
                    return p;
        }

        try
        {
            if (dte.Solution?.SolutionBuild?.StartupProjects is { } raw)
            {
                var startups = raw as object[] ?? [raw];
                if (startups.Length == 1 && startups[0] is string startupName)
                {
                    foreach (Project p in dte.Solution.Projects)
                        if (p?.UniqueName == startupName)
                            return p;
                }
            }
        }
        catch { }

        return null;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ResolveActiveProjectPath() => ResolveActiveProject()?.FullName;
    public string ResolveOutputDir()
    {
        var proj = ResolveActiveProject();
        if (proj?.FullName is not string projPath)
            return null;
        var config = proj.ConfigurationManager?.ActiveConfiguration;
        if (config is null)
            return null;
        try
        {
            if (config.Properties?.Item("OutputPath")?.Value is not string outputPath)
                return null;
            // OutputPath is usually relative ("bin\Debug\net10.0\"); GetFullPath handles both cases.
            return Path.GetFullPath(outputPath, Path.GetDirectoryName(projPath));
        }
        catch (ArgumentException) { return null; }
    }
    public string ResolveTargetPath()
    {
        var dte = DTE;
        var proj = ResolveActiveProject();
        if (proj?.FullName is not string projPath)
            return null;
        var outputDir = ResolveOutputDir();
        if (outputDir is null)
            return null;
        try
        {
            var assemblyName = proj.Properties?.Item("AssemblyName")?.Value as string
                ?? Path.GetFileNameWithoutExtension(projPath);
            // OutputType: 0 = Exe, 1 = WinExe, 2 = Library
            var ext = proj.Properties?.Item("OutputType")?.Value switch
            {
                0 or 1 => ".exe",
                _ => ".dll"
            };
            return Path.Combine(outputDir, assemblyName + ext);
        }
        catch (ArgumentException) { return null; }
    }

    ~VSHelper()
    {
        if (DTE is not null)
            Marshal.FinalReleaseComObject(DTE);
    }
}
