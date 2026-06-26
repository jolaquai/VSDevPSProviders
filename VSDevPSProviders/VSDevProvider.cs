using System.Collections;

using EnvDTE;

namespace VSDevPSProviders;

[CmdletProvider("VSDev", ProviderCapabilities.None)]
public sealed class VSDevProvider : NavigationCmdletProvider, IContentCmdletProvider
{
    #region Single registration table
    /// <summary>
    /// Encapsulates a single property registration.
    /// </summary>
    /// <param name="Group">The group name (e.g. "Solution"). Acts as a hierarchical container segment.</param>
    /// <param name="Names">Defines the canonical name and zero or more aliases. Each name produces both a hierarchical path (Group\Name) and a flat alias (GroupName).</param>
    /// <param name="Resolve">A delegate that takes a <see cref="VSHelper"/> instance and returns the resolved value for this property.</param>
    /// <param name="ExtraFlat">Zero or more root-level aliases that do not follow the Group+Name scheme. These aliases will resolve to the same value as the canonical name.</param>
    private sealed record Prop(string Group, string[] Names, Func<VSHelper, string> Resolve, string[] ExtraFlat = null);

    private static readonly Prop[] _props =
    [
        new("Solution", ["Dir"],           vs => vs.DTE.Solution?.FullName is string s ? Path.GetDirectoryName(s) : null),
        new("Solution", ["Path", "File"],  vs => vs.DTE.Solution?.FullName),
        new("Solution", ["Name"],          vs => vs.DTE.Solution?.FullName is string s ? Path.GetFileNameWithoutExtension(s) : null),
        new("Solution", ["Ext"],           vs => vs.DTE.Solution?.FullName is string s ? Path.GetExtension(s) : null),

        new("Project",  ["Dir"],           vs => vs.ResolveActiveProjectPath() is string p ? Path.GetDirectoryName(p) : null),
        new("Project",  ["Path", "File"],  vs => vs.ResolveActiveProjectPath()),
        new("Project",  ["Name"],          vs => vs.ResolveActiveProjectPath() is string p ? Path.GetFileNameWithoutExtension(p) : null),
        new("Project",  ["FileName"],      vs => vs.ResolveActiveProjectPath() is string p ? Path.GetFileName(p) : null),
        new("Project",  ["Ext"],           vs => vs.ResolveActiveProjectPath() is string p ? Path.GetExtension(p) : null),
        new("Project",  ["Configuration"], vs => vs.ResolveActiveProject()?.ConfigurationManager?.ActiveConfiguration?.ConfigurationName, ["Configuration"]),
        new("Project",  ["Platform"],      vs => vs.ResolveActiveProject()?.ConfigurationManager?.ActiveConfiguration?.PlatformName,      ["Platform"]),

        new("Target",   ["Dir"],           vs => vs.ResolveOutputDir(), ["OutDir"]),
        new("Target",   ["Path"],          vs => vs.ResolveTargetPath()),
        new("Target",   ["Name"],          vs => vs.ResolveTargetPath() is string t ? Path.GetFileNameWithoutExtension(t) : null),
        new("Target",   ["FileName"],      vs => vs.ResolveTargetPath() is string t ? Path.GetFileName(t) : null),
        new("Target",   ["Ext"],           vs => vs.ResolveTargetPath() is string t ? Path.GetExtension(t) : null),

        new("DevEnv",   ["Dir"],           vs => Path.GetDirectoryName(vs.DTE.FullName)),
    ];
    #endregion

    #region Lookup tables derived once from _props at startup
    // _lookup   : every accessible path string → resolver
    // _children : container path → ordered canonical child names
    // _containers: set of all container path strings
    private static readonly FrozenDictionary<string, Func<VSHelper, string>> _lookup;
    private static readonly FrozenDictionary<string, string[]> _children;
    private static readonly FrozenSet<string> _containers;

    // Impossible to initialize inline
#pragma warning disable CA1810 // Initialize reference type static fields inline
    static VSDevProvider()
#pragma warning restore CA1810 // Initialize reference type static fields inline
    {
        var lookup = new Dictionary<string, Func<VSHelper, string>>(StringComparer.OrdinalIgnoreCase);
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) { [""] = [] };
        var containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in _props)
        {
            var g = prop.Group;
            if (containers.Add(g))
            {
                children[""].Add(g);
                children[g] = [];
            }

            children[g].Add(prop.Names[0]); // canonical name only in child listings

            foreach (var name in prop.Names)
            {
                lookup[g + '\\' + name] = prop.Resolve; // hierarchical: Solution\Dir
                lookup[g + name] = prop.Resolve; // flat alias:   SolutionDir
            }

            if (prop.ExtraFlat is not null)
                foreach (var alias in prop.ExtraFlat)
                    lookup[alias] = prop.Resolve;        // extra flat:   OutDir, Configuration
        }

        _lookup = lookup.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _children = children.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        _containers = containers.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
    #endregion

    #region Item
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override bool IsValidPath(string path) => true;

    protected override bool ItemExists(string path)
    {
        var p = Norm(path);
        return _lookup.ContainsKey(p) || _containers.Contains(p);
    }

    protected override void GetItem(string path)
    {
        var p = Norm(path);
        if (_lookup.TryGetValue(p, out var resolve))
            WriteItemObject(resolve(VSHelper.Create()), path, false);
        else if (_containers.Contains(p))
            WriteItemObject(p, path, true);
    }
    #endregion

    #region Container
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override bool HasChildItems(string path) => _children.ContainsKey(Norm(path));

    protected override void GetChildItems(string path, bool recurse)
    {
        var p = Norm(path);
        if (!_children.TryGetValue(p, out var names))
            return;

        var vs = VSHelper.Create();
        foreach (var name in names)
        {
            var childPath = p.Length == 0 ? name : p + '\\' + name;
            var isContainer = _containers.Contains(name);
            var value = isContainer || !_lookup.TryGetValue(childPath, out var resolve) ? (object)name : resolve(vs);
            WriteItemObject(value, childPath, isContainer);
        }

        if (recurse)
            foreach (var name in names.Where(_containers.Contains))
                GetChildItems(name, recurse: false);
    }

    protected override void GetChildNames(string path, ReturnContainers returnContainers)
    {
        var p = Norm(path);
        if (!_children.TryGetValue(p, out var names))
            return;
        foreach (var name in names)
            WriteItemObject(name, name, false);
    }
    #endregion

    #region Navigation
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override bool IsItemContainer(string path) => _containers.Contains(Norm(path));

    protected override string GetParentPath(string path, string root)
    {
        var p = Norm(path);
        var idx = p.LastIndexOf('\\');
        return idx < 0 ? root ?? "" : p[..idx];
    }

    protected override string GetChildName(string path)
    {
        var p = Norm(path);
        var idx = p.LastIndexOf('\\');
        return idx < 0 ? p : p[(idx + 1)..];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override string MakePath(string parent, string child)=> string.IsNullOrEmpty(parent) ? child : parent + '\\' + child;
    #endregion

    #region Content (drives $Dev:Path variable syntax)
    public IContentReader GetContentReader(string path)
    {
        var p = Norm(path);
        return new Reader(_lookup.TryGetValue(p, out var resolve) ? resolve(VSHelper.Create()) : null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public object GetContentReaderDynamicParameters(string path) => null;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public IContentWriter GetContentWriter(string path) => throw new NotSupportedException();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public object GetContentWriterDynamicParameters(string path) => null;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public void ClearContent(string path) => throw new NotSupportedException();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public object ClearContentDynamicParameters(string path) => null;
    #endregion

    #region Helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Norm(string path) => path.AsSpan().TrimStart('\\').ToString();
    #endregion

    #region Reader
    internal sealed class Reader(string value) : IContentReader
    {
        private bool _done;
        public IList Read(long readCount)
        {
            var ret = new List<object>();
            if (!_done)
            { _done = true; ret.Add(value); }
            return ret;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public void Seek(long offset, SeekOrigin origin) => _done = offset != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public void Close() { }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public void Dispose() { }
    }
    #endregion
}
