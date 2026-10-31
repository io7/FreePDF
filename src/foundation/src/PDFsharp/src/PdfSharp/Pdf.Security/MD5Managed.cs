// Original FreePDF implementation, copyright 2026 io7. MIT; see LICENSE.
// Algorithm: RFC 1321, sections 3.1-3.5. No reference implementation was copied.
// MD5 is required by legacy PDF encryption and embedded-file checksums.
// This is not a secure signature hash and does not establish FIPS compliance.
using System.Security.Cryptography;

namespace PdfSharp.Pdf.Security;

sealed class MD5Managed : HashAlgorithm
{
    readonly byte[] _pending = new byte[64];
    readonly uint[] _state = new uint[4];
    int _pendingCount;
    ulong _byteCount;

    // Integer part of 2^32 * abs(sin(i)), i = 1..64, radians; RFC 1321 section 3.4.
    static readonly uint[] Constants =
    [
        0xd76aa478u, 0xe8c7b756u, 0x242070dbu, 0xc1bdceeeu, 0xf57c0fafu, 0x4787c62au, 0xa8304613u, 0xfd469501u,
        0x698098d8u, 0x8b44f7afu, 0xffff5bb1u, 0x895cd7beu, 0x6b901122u, 0xfd987193u, 0xa679438eu, 0x49b40821u,
        0xf61e2562u, 0xc040b340u, 0x265e5a51u, 0xe9b6c7aau, 0xd62f105du, 0x02441453u, 0xd8a1e681u, 0xe7d3fbc8u,
        0x21e1cde6u, 0xc33707d6u, 0xf4d50d87u, 0x455a14edu, 0xa9e3e905u, 0xfcefa3f8u, 0x676f02d9u, 0x8d2a4c8au,
        0xfffa3942u, 0x8771f681u, 0x6d9d6122u, 0xfde5380cu, 0xa4beea44u, 0x4bdecfa9u, 0xf6bb4b60u, 0xbebfbc70u,
        0x289b7ec6u, 0xeaa127fau, 0xd4ef3085u, 0x04881d05u, 0xd9d4d039u, 0xe6db99e5u, 0x1fa27cf8u, 0xc4ac5665u,
        0xf4292244u, 0x432aff97u, 0xab9423a7u, 0xfc93a039u, 0x655b59c3u, 0x8f0ccc92u, 0xffeff47du, 0x85845dd1u,
        0x6fa87e4fu, 0xfe2ce6e0u, 0xa3014314u, 0x4e0811a1u, 0xf7537e82u, 0xbd3af235u, 0x2ad7d2bbu, 0xeb86d391u,
    ];
    static readonly int[] Rotations = [7, 12, 17, 22, 5, 9, 14, 20, 4, 11, 16, 23, 6, 10, 15, 21];

    MD5Managed() { HashSizeValue = 128; Initialize(); }
    public new static MD5Managed Create() => new();

    public static string ComputeHashHex(byte[] bytes)
    {
        using var digest = Create();
        return BitConverter.ToString(digest.ComputeHash(bytes)).Replace("-", "");
    }

    public override void Initialize()
    {
        _state[0] = 0x67452301; _state[1] = 0xefcdab89;
        _state[2] = 0x98badcfe; _state[3] = 0x10325476;
        _pendingCount = 0; _byteCount = 0;
        Array.Clear(_pending, 0, _pending.Length);
    }

    protected override void HashCore(byte[] array, int ibStart, int cbSize)
    {
        _byteCount = unchecked(_byteCount + (ulong)cbSize);
        while (cbSize > 0)
        {
            int take = Math.Min(64 - _pendingCount, cbSize);
            Buffer.BlockCopy(array, ibStart, _pending, _pendingCount, take);
            ibStart += take; cbSize -= take; _pendingCount += take;
            if (_pendingCount == 64) { Compress(_pending, 0); _pendingCount = 0; }
        }
    }

    protected override byte[] HashFinal()
    {
        var ending = new byte[_pendingCount < 56 ? 64 : 128];
        Buffer.BlockCopy(_pending, 0, ending, 0, _pendingCount);
        ending[_pendingCount] = 0x80;
        ulong bits = unchecked(_byteCount * 8);
        for (int i = 0; i < 8; i++) ending[ending.Length - 8 + i] = (byte)(bits >> (i * 8));
        for (int offset = 0; offset < ending.Length; offset += 64) Compress(ending, offset);
        var digest = new byte[16];
        for (int i = 0; i < digest.Length; i++) digest[i] = (byte)(_state[i / 4] >> (8 * (i % 4)));
        // Framework's TransformFinalBlock does not reinitialize derived state.
        Initialize();
        return digest;
    }

    void Compress(byte[] block, int offset)
    {
        unchecked
        {
            uint a = _state[0], b = _state[1], c = _state[2], d = _state[3];
            for (int step = 0; step < 64; step++)
            {
                int round = step / 16;
                uint function;
                int word;
                switch (round)
                {
                    case 0: function = (b & c) | (~b & d); word = step; break;
                    case 1: function = (b & d) | (c & ~d); word = (5 * step + 1) % 16; break;
                    case 2: function = b ^ c ^ d; word = (3 * step + 5) % 16; break;
                    default: function = c ^ (b | ~d); word = 7 * step % 16; break;
                }
                int position = offset + 4 * word;
                uint value = (uint)(block[position] | block[position + 1] << 8 | block[position + 2] << 16 | block[position + 3] << 24);
                uint sum = a + function + value + Constants[step];
                int rotation = Rotations[round * 4 + step % 4];
                uint next = b + ((sum << rotation) | (sum >> (32 - rotation)));
                a = d; d = c; c = b; b = next;
            }
            _state[0] += a; _state[1] += b; _state[2] += c; _state[3] += d;
        }
    }

    protected override void Dispose(bool disposing)
    {
        Array.Clear(_pending, 0, _pending.Length);
        Array.Clear(_state, 0, _state.Length);
        _pendingCount = 0; _byteCount = 0;
        base.Dispose(disposing);
    }
}
