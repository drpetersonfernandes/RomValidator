using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

[XmlRoot("datafile", Namespace = "")]
public class Datafile
{
    // XML Schema Instance namespace for schema location
    [XmlAttribute("xmlns:xsi", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string XmlnsXsi { get; set; } = "http://www.w3.org/2001/XMLSchema-instance";

    // Schema location attribute
    [XmlAttribute("schemaLocation", Namespace = "http://www.w3.org/2001/XMLSchema-instance")]
    public string SchemaLocation { get; set; } = "https://datomatic.no-intro.org/stuff https://datomatic.no-intro.org/stuff/schema_nointro_datfile_v3.xsd";

    [XmlElement("header")]
    public Header? Header { get; set; }

    [XmlElement("game")]
    public List<Game> Games { get; set; } = new();
}
