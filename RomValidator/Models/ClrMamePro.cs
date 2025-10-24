using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace RomValidator.Models;

public class ClrMamePro : IXmlSerializable
{
    [XmlAttribute("forcenodump")]
    public string ForceNoDump { get; set; } = "required";

    public XmlSchema? GetSchema()
    {
        return null;
    }

    public void ReadXml(XmlReader reader)
    {
        // Not needed for reading DATs in this app
    }

    public void WriteXml(XmlWriter writer)
    {
        writer.WriteStartElement("clrmamepro");
        if (!string.IsNullOrEmpty(ForceNoDump))
        {
            writer.WriteAttributeString("forcenodump", ForceNoDump);
        }

        writer.WriteEndElement();
    }
}