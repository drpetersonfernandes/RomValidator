using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

/// <summary>
/// Represents the root element of a No-Intro DAT file.
/// Contains the header and list of games for ROM validation.
/// </summary>
[XmlRoot("datafile", Namespace = "")]
public class Datafile
{
    // XML Schema Instance namespace for schema location
    /// <summary>Gets or sets the XML namespace declarations for the datafile.</summary>
    [XmlNamespaceDeclarations]
    public XmlSerializerNamespaces? Xmlns { get; set; }

    // Schema location attribute
    /// <summary>Gets or sets the XML schema location for validation.</summary>
    [XmlAttribute("schemaLocation", Namespace = "http://www.w3.org/2001/XMLSchema-instance")]
    public string SchemaLocation { get; set; } = "https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd";

    /// <summary>Gets or sets the header information for the DAT file.</summary>
    [XmlElement("header")]
    public Header? Header { get; set; }

    /// <summary>Gets or sets the list of games in the DAT file.</summary>
    [XmlElement("game")]
    public List<Game> Games { get; set; } = new();
}
