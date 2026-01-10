using System.Xml.Serialization;

namespace RomValidator.Models;

public class Game
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute("cloneofid")]
    public string? CloneOfId { get; set; }

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("rom")]
    public List<Rom> Roms { get; set; } = new();

    // MAME specific optional attributes
    [XmlAttribute("isbios")]
    public string? IsBios { get; set; }

    [XmlAttribute("isdevice")]
    public string? IsDevice { get; set; }

    [XmlAttribute("runnable")]
    public string? Runnable { get; set; }

    // MAME specific optional elements
    [XmlElement("year")]
    public string? Year { get; set; }

    [XmlElement("manufacturer")]
    public string? Manufacturer { get; set; }
}