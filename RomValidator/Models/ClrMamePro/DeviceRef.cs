using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

public class DeviceRef
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;
}
