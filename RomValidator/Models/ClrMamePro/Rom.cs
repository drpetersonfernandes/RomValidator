using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

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

    [XmlAttribute("merge")]
    public string? Merge { get; set; }

    [XmlAttribute("status")]
    public string? Status { get; set; }

    [XmlAttribute("region")]
    public string? Region { get; set; }

    [XmlAttribute("offset")]
    public string? Offset { get; set; }
}
