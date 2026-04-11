using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

public class Header
{
    [XmlElement("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("category")]
    public string Category { get; set; } = string.Empty;

    [XmlElement("version")]
    public string Version { get; set; } = string.Empty;

    [XmlElement("date")]
    public string Date { get; set; } = string.Empty;

    [XmlElement("author")]
    public string Author { get; set; } = string.Empty;

    [XmlElement("email")]
    public string Email { get; set; } = string.Empty;

    [XmlElement("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [XmlElement("url")]
    public string Url { get; set; } = string.Empty;

    [XmlElement("clrmamepro")]
    public ClrMameProHeader? ClrMamePro { get; set; }

    [XmlElement("comment")]
    public string? Comment { get; set; }
}
