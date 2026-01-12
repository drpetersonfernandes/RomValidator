using System.Xml.Serialization;

namespace RomValidator.Models;

public class Game
{
    // No-Intro attributes
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute("cloneofid")]
    public string? CloneOfId { get; set; }

    // No-Intro elements - Order is important for XmlSerializer
    [XmlElement("category")]
    public string? Category { get; set; }

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("rom")]
    public List<Rom> Roms { get; set; } = new();
}