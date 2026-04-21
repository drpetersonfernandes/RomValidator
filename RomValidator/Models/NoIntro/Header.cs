using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

/// <summary>
/// Represents the header section of a No-Intro DAT file.
/// Contains metadata about the DAT file including creator, version, and description.
/// </summary>
public class Header
{
    // Optional header id
    /// <summary>Gets or sets the optional identifier for the DAT file.</summary>
    [XmlElement("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the name of the DAT file.</summary>
    [XmlElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the description of the DAT file contents.</summary>
    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the version of the DAT file.</summary>
    [XmlElement("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the author or creator of the DAT file.</summary>
    [XmlElement("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>Gets or sets the homepage URL for the DAT file creator.</summary>
    [XmlElement("homepage")]
    public string Homepage { get; set; } = "No-Intro";

    /// <summary>Gets or sets the URL for the DAT file source.</summary>
    [XmlElement("url")]
    public string Url { get; set; } = "https://www.no-intro.org";

    // Optional fields for full No-Intro compatibility
    /// <summary>Gets or sets the creation date of the DAT file.</summary>
    [XmlElement("date")]
    public string? Date { get; set; }

    /// <summary>Gets or sets the retool information for the DAT file.</summary>
    [XmlElement("retool")]
    public string? Retool { get; set; }

    /// <summary>Gets or sets the contact email for the DAT file author.</summary>
    [XmlElement("email")]
    public string? Email { get; set; }

    /// <summary>Gets or sets additional comments about the DAT file.</summary>
    [XmlElement("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets the category of the DAT file contents.</summary>
    [XmlElement("category")]
    public string? Category { get; set; }

    // ClrMamePro settings element (forcenodump attribute)
    /// <summary>Gets or sets the ClrMamePro settings for the DAT file.</summary>
    [XmlElement("clrmamepro")]
    public ClrMameProSettings? ClrMamePro { get; set; }
}

/// <summary>
/// ClrMamePro settings for No-Intro DAT header
/// </summary>
public class ClrMameProSettings
{
    /// <summary>Gets or sets the force no-dump setting for ClrMamePro compatibility.</summary>
    [XmlAttribute("forcenodump")]
    public string ForceNoDump { get; set; } = "required";
}
