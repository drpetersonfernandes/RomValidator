using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

[XmlRoot("datafile")]
public class Datafile
{
    [XmlElement("header")]
    public Header? Header { get; set; }

    [XmlElement("machine")]
    public List<Machine> Machines { get; set; } = new();
}
