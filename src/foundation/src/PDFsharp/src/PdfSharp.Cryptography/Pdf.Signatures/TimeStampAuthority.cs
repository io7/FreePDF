// FreePDF - MIT, see the LICENSE file in the solution root.

#if NET8_0_OR_GREATER
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Posts RFC 3161 time-stamp requests over HTTP (RFC 3161 3.4): application/timestamp-query in,
    /// application/timestamp-reply out.
    /// </summary>
    static class TimeStampAuthority
    {
        const string QueryContentType = "application/timestamp-query";
        const string ReplyContentType = "application/timestamp-reply";

        // One client for all requests: no new connection per signature, and a bounded wait for the authority.
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>
        /// Sends a DER-encoded TimeStampReq to the authority and returns its DER-encoded TimeStampResp.
        /// </summary>
        internal static async Task<byte[]> PostAsync(Uri authority, byte[] request)
        {
            using var content = new ByteArrayContent(request);
            content.Headers.ContentType = new MediaTypeHeaderValue(QueryContentType);
            using var response = await Http.PostAsync(authority, content).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new CryptographicException(
                    $"There was an error from the timestamp authority. It responded with {response.StatusCode} {(int)response.StatusCode}.");
            }
            if (response.Content.Headers.ContentType?.MediaType != ReplyContentType)
                throw new CryptographicException("The reply from the time stamp server was in an invalid format.");

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
    }
}
#endif
