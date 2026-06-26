namespace VSDevPSProviders;

internal static class Helpers
{
    public static ReadOnlySpan<char> PreparePath(string path) => path.AsSpan().TrimStart('\\');
}