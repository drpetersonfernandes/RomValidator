using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

public class BiosSet
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("description")]
    public string Description { get; set; } = string.Empty;

    [XmlAttribute("default")]
    public string? Default { get; set; }
}
