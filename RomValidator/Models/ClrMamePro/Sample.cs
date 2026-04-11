using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

public class Sample
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;
}
