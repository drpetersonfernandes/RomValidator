using System.Xml.Serialization;

namespace RomValidator.Models.ClrMamePro;

public class Machine
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("sourcefile")]
    public string? SourceFile { get; set; }

    [XmlAttribute("cloneof")]
    public string? CloneOf { get; set; }

    [XmlAttribute("romof")]
    public string? RomOf { get; set; }

    [XmlAttribute("sampleof")]
    public string? SampleOf { get; set; }

    [XmlAttribute("isdevice")]
    public string? IsDevice { get; set; }

    [XmlAttribute("runnable")]
    public string? Runnable { get; set; }

    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("year")]
    public string? Year { get; set; }

    [XmlElement("manufacturer")]
    public string? Manufacturer { get; set; }

    [XmlElement("biosset")]
    public List<BiosSet> BiosSets { get; set; } = new();

    [XmlElement("rom")]
    public List<Rom> Roms { get; set; } = new();

    [XmlElement("device_ref")]
    public List<DeviceRef> DeviceRefs { get; set; } = new();

    [XmlElement("sample")]
    public List<Sample> Samples { get; set; } = new();

    [XmlElement("driver")]
    public Driver? Driver { get; set; }
}
