using System.Security.Cryptography;

namespace RomValidator.Services;

/// <summary>
/// Implements a 32-bit CRC hash algorithm for compatibility with HashAlgorithm.
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

    public Crc32Algorithm()
    {
        HashSizeValue = 32;
        Initialize();
    }

    public sealed override void Initialize()
    {
        _currentCrc = 0xFFFFFFFF;
    }

    protected override void HashCore(byte[] array, int ibStart, int cbSize)
    {
        for (var i = ibStart; i < ibStart + cbSize; i++)
        {
            _currentCrc = (_currentCrc >> 8) ^ ChecksumTable[array[i] ^ (_currentCrc & 0xFF)];
        }
    }

    protected override byte[] HashFinal()
    {
        var finalCrc = ~_currentCrc;
        return BitConverter.GetBytes(finalCrc);
    }
}