// SPDX-License-Identifier: MIT
// Polyfill for C# 9 records on .NET Framework 4.8.1 (where the runtime lacks
// System.Runtime.CompilerServices.IsExternalInit). Compiler picks up any
// declaration of this type with the right name and namespace.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
