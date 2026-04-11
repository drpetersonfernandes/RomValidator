using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

public class ClrMamePro
{
    [XmlAttribute("forcenodump")]
    public string ForceNoDump { get; set; } = "required";
}
