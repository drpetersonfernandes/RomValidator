using System.Security.Cryptography;

namespace RomValidator.Services;

/// <summary>
/// Implements a 32-bit CRC (Cyclic Redundancy Check) hash algorithm.
/// Compatible with System.Security.Cryptography.HashAlgorithm for use in hashing operations.
/// </summary>
public class Crc32Algorithm : HashAlgorithm
{
    private static readonly uint[] ChecksumTable;
    private uint _currentCrc;

    static Crc32Algorithm()
    {
        const uint polynomial = 0xEDB88320;
        ChecksumTable = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var j = 0; j < 8; j++)
            {
                entry = (entry & 1) == 1 ? (entry >> 1) ^ polynomial : entry >> 1;
            }

            ChecksumTable[i] = entry;
        }
    }

    /// <summary>
    /// Initializes a new instance of the Crc32Algorithm class.
    /// Sets the hash size to 32 bits and initializes the algorithm state.
    /// </summary>
    public Crc32Algorithm()
    {
        HashSizeValue = 32;
        Initialize();
    }

    /// <summary>
    /// Initializes the CRC32 algorithm state.
    /// </summary>
    public sealed override void Initialize()
    {
        _currentCrc = 0xFFFFFFFF;
    }

    /// <summary>
    /// Routes data written to the object into the CRC32 hash algorithm for computing the hash.
    /// </summary>
    /// <param name="array">The input to compute the hash code for.</param>
    /// <param name="ibStart">The offset into the byte array from which to begin using data.</param>
    /// <param name="cbSize">The number of bytes in the byte array to use as data.</param>
    protected override void HashCore(byte[] array, int ibStart, int cbSize)
    {
        // Use a countdown loop to avoid potential overflow in ibStart + cbSize
        for (var count = cbSize; count > 0; count--)
        {
            if (ibStart >= array.Length) break;

            _currentCrc = (_currentCrc >> 8) ^ ChecksumTable[array[ibStart] ^ (_currentCrc & 0xFF)];
            ibStart++;
        }
    }

    /// <summary>
    /// Finalizes the CRC32 hash computation after the last data is processed by the cryptographic hash algorithm.
    /// </summary>
    /// <returns>The computed CRC32 hash code.</returns>
    protected override byte[] HashFinal()
    {
        var finalCrc = ~_currentCrc;
        return
        [
            (byte)(finalCrc >> 24),
            (byte)(finalCrc >> 16),
            (byte)(finalCrc >> 8),
            (byte)finalCrc
        ];
    }
}