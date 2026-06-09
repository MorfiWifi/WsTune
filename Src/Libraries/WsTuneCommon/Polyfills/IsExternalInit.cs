#if !NET5_0_OR_GREATER
// Polyfill so `init` accessors and `record struct` compile on netstandard2.0 / net48.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
