using System.Xml.Serialization;

namespace RomValidator.Models;

public class Header
{
    [XmlElement("id")]
    public string Id { get; set; } = string.Empty;

    [XmlElement("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("version")]
    public string Version { get; set; } = string.Empty;

    [XmlElement("author")]
    public string Author { get; set; } = string.Empty;

    [XmlElement("homepage")]
    public string Homepage { get; set; } = "Pure Logic Code";

    [XmlElement("url")]
    public string Url { get; set; } = "https://www.purelogiccode.com";

    [XmlElement("clrmamepro")]
    public ClrMamePro ClrMamePro { get; set; } = new();

    // Optional elements found in various DAT formats
    [XmlElement("date")]
    public string? Date { get; set; }

    [XmlElement("retool")]
    public string? Retool { get; set; }

    [XmlElement("email")]
    public string? Email { get; set; }

    [XmlElement("comment")]
    public string? Comment { get; set; }

    [XmlElement("category")]
    public string? Category { get; set; }
}
