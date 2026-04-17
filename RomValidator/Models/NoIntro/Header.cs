using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

public class Header
{
    // Optional header id
    [XmlElement("id")]
    public string? Id { get; set; }

    [XmlElement("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("version")]
    public string Version { get; set; } = string.Empty;

    [XmlElement("author")]
    public string Author { get; set; } = string.Empty;

    [XmlElement("homepage")]
    public string Homepage { get; set; } = "No-Intro";

    [XmlElement("url")]
    public string Url { get; set; } = "https://www.no-intro.org";

    // Optional fields for full No-Intro compatibility
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

    // ClrMamePro settings element (forcenodump attribute)
    [XmlElement("clrmamepro")]
    public ClrMameProSettings? ClrMamePro { get; set; }
}

/// <summary>
/// ClrMamePro settings for No-Intro DAT header
/// </summary>
public class ClrMameProSettings
{
    [XmlAttribute("forcenodump")]
    public string ForceNoDump { get; set; } = "required";
}
