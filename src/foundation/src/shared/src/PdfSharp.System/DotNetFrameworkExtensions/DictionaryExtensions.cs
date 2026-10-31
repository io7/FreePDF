// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

#if !NET8_0_OR_GREATER 

using System.Runtime.CompilerServices;

#pragma warning disable IDE0130

// Extensions for .NET functionality missing in .NET Standard/Framework.

namespace PdfSharp.DotNetFrameworkExtensions
{
    /// <summary>
    /// Extensions for Dictionary.
    /// </summary>
    public static class DictionaryExtensions
    {
        extension<TKey, TValue>(Dictionary<TKey, TValue> dict) where TKey : notnull
        {
#nullable disable // Preserve the published method metadata without altering the extension marker.
            /// <summary>
            /// Implementation of TryAdd. Retains the published MIT v7 generic contract.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryAdd(TKey key, TValue value)
            {
                if (!dict.ContainsKey(key))
                {
                    dict.Add(key, value);
                    return true;
                }
                return false;
            }
#nullable restore
        }
    }
}
#endif
