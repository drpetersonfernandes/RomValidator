using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

/// <summary>
/// Represents a ROM entry within a game in a No-Intro DAT file.
/// Contains file metadata including name, size, and hash values for validation.
/// </summary>
public class Rom
{
    /// <summary>Gets or sets the name of the ROM file.</summary>
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the size of the ROM file in bytes.</summary>
    [XmlIgnore]
    public long Size { get; set; }

    /// <summary>Gets or sets the size of the ROM file as a string for XML serialization.</summary>
    [XmlAttribute("size")]
    public string SizeString
    {
        get => Size.ToString();
        set
        {
            if (long.TryParse(value, out var result))
            {
                Size = result;
            }
            else
            {
                Size = 0;
            }
        }
    }

    /// <summary>Gets or sets the CRC32 hash value of the ROM.</summary>
    [XmlAttribute("crc")]
    public string Crc { get; set; } = string.Empty;

    /// <summary>Gets or sets the MD5 hash value of the ROM.</summary>
    [XmlAttribute("md5")]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>Gets or sets the SHA1 hash value of the ROM.</summary>
    [XmlAttribute("sha1")]
    public string Sha1 { get; set; } = string.Empty;

    /// <summary>Gets or sets the SHA256 hash value of the ROM.</summary>
    [XmlAttribute("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    // Optional attributes for No-Intro compatibility
    /// <summary>Gets or sets the status of the ROM (e.g., "verified", "bad").</summary>
    [XmlAttribute("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the serial number of the ROM, if applicable.</summary>
    [XmlAttribute("serial")]
    public string? Serial { get; set; }
}
