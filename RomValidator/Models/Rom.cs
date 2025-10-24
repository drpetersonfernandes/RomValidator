using System.Xml.Serialization;

namespace RomValidator.Models;

public class Rom
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("size")]
    public long Size { get; set; }

    [XmlAttribute("crc")]
    public string Crc { get; set; } = string.Empty;

    [XmlAttribute("md5")]
    public string Md5 { get; set; } = string.Empty;

    [XmlAttribute("sha1")]
    public string Sha1 { get; set; } = string.Empty;

    [XmlAttribute("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}