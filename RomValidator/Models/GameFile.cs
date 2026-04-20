namespace RomValidator.Models;

/// <summary>
/// Represents a game file with its calculated hash values and metadata.
/// </summary>
public class GameFile
{
    /// <summary>Gets or sets the file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the game name derived from the file.</summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>Gets or sets the file size in bytes.</summary>
    public long FileSize { get; set; }

    /// <summary>Gets or sets the CRC32 hash value.</summary>
    public string Crc32 { get; set; } = string.Empty;

    /// <summary>Gets or sets the MD5 hash value.</summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>Gets or sets the SHA1 hash value.</summary>
    public string Sha1 { get; set; } = string.Empty;

    /// <summary>Gets or sets the SHA256 hash value.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Gets or sets the error message if hashing failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the original archive file name if this file was extracted from an archive.
    /// This is used to properly track the container filename vs the internal ROM filename.
    /// </summary>
    public string? ArchiveFileName { get; set; }
}