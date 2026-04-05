using System.Xml.Serialization;

namespace RomValidator.Models;

public class Rom
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlIgnore]
    public long Size { get; set; }

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

    [XmlAttribute("crc")]
    public string Crc { get; set; } = string.Empty;

    [XmlAttribute("md5")]
    public string Md5 { get; set; } = string.Empty;

    [XmlAttribute("sha1")]
    public string Sha1 { get; set; } = string.Empty;

    [XmlAttribute("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    // Optional attributes for No-Intro compatibility
    [XmlAttribute("status")]
    public string? Status { get; set; }

    [XmlAttribute("serial")]
    public string? Serial { get; set; }
}