using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

/// <summary>
/// Represents a game entry in a No-Intro DAT file.
/// Contains metadata about a game including its name, ID, categories, description, and ROMs.
/// </summary>
public class Game
{
    // No-Intro attributes
    /// <summary>Gets or sets the name of the game.</summary>
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the unique identifier for the game.</summary>
    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the ID of the game this is a clone of, if applicable.</summary>
    [XmlAttribute("cloneofid")]
    public string? CloneOfId { get; set; }

    // No-Intro elements - Order is important for XmlSerializer
    /// <summary>Gets or sets the list of categories for the game.</summary>
    [XmlElement("category")]
    public List<string> Categories { get; set; } = new();

    /// <summary>Gets or sets the description of the game.</summary>
    [XmlElement("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of ROMs associated with the game.</summary>
    [XmlElement("rom")]
    public List<Rom> Roms { get; set; } = new();
}
