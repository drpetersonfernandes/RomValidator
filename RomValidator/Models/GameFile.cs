namespace RomValidator.Models;

public class GameFile
{
    public string FileName { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Crc32 { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public string Sha1 { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}