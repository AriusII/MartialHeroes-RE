// netstandard2.0 polyfill: record and init-only members compile against this marker type, which the
// netstandard2.0 reference assemblies do not ship. Internal so it never escapes the analyzer assembly.
// Kept in its own file so the generator source can use a file-scoped namespace.

namespace System.Runtime.CompilerServices;

internal sealed class IsExternalInit
{
}