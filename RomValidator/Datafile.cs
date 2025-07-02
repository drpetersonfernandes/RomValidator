using System.Xml.Serialization;

namespace RomValidator;

// These classes are designed to match the No-Intro DAT file XML structure.
// Using XmlSerializer attributes to map XML elements and attributes to C# properties.

[XmlRoot("datafile")]
public class Datafile
{
    [XmlElement("header")]
    public Header? Header { get; set; }

    [XmlElement("game")]
    public Game[] Games { get; set; } = [];
}

public class Header
{
    [XmlElement("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("version")]
    public string Version { get; set; } = string.Empty;
}

public class Game
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("rom")]
    public Rom Rom { get; set; } = new();
}

public class Rom
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("size")]
    public long Size { get; set; }

    [XmlAttribute("crc")]
    public string Crc { get; set; } = string.Empty;

    [XmlAttribute("md5")]
    public string Md5 { get; set; } = string.Empty;

    [XmlAttribute("sha1")]
    public string Sha1 { get; set; } = string.Empty;
}