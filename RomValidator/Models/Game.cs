using System.Xml.Serialization;

namespace RomValidator.Models;

public class Game
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("rom")]
    public Rom Rom { get; set; } = new();
}