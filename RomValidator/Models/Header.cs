using System.Xml.Serialization;

namespace RomValidator.Models;

public class Header
{
    [XmlElement("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("version")]
    public string Version { get; set; } = string.Empty;

    // ADD THESE NEW PROPERTIES
    [XmlElement("author")]
    public string Author { get; set; } = string.Empty;

    [XmlElement("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [XmlElement("url")]
    public string Url { get; set; } = string.Empty;
}