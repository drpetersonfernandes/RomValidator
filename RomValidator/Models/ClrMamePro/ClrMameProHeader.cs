using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

public class ClrMameProHeader
{
    [XmlAttribute("header")]
    public string? Header { get; set; }

    [XmlAttribute("forcemerging")]
    public string? ForceMerging { get; set; }

    [XmlAttribute("forcenodump")]
    public string? ForceNoDump { get; set; }

    [XmlAttribute("forcepacking")]
    public string? ForcePacking { get; set; }
}
