// Compiled for netstandard2.1 only (see Nfty.Ticket.csproj). Compiler-support types the older
// framework lacks — declarations the C# compiler looks for by name, carrying no logic, so the two
// targets still run one code path.

namespace System.Runtime.CompilerServices
{
    /// <summary>Lets records and <c>init</c> accessors compile on netstandard2.1.</summary>
    internal static class IsExternalInit
    {
    }
}
