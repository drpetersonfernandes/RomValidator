using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

public class Driver
{
    [XmlAttribute("status")]
    public string Status { get; set; } = string.Empty;

    [XmlAttribute("emulation")]
    public string? Emulation { get; set; }

    [XmlAttribute("color")]
    public string? Color { get; set; }

    [XmlAttribute("sound")]
    public string? Sound { get; set; }

    [XmlAttribute("graphic")]
    public string? Graphic { get; set; }

    [XmlAttribute("cocktail")]
    public string? Cocktail { get; set; }

    [XmlAttribute("protection")]
    public string? Protection { get; set; }

    [XmlAttribute("savestate")]
    public string? SaveState { get; set; }
}
