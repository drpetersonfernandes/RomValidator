using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using RomValidator.Models;
using RomValidator.Services;
using ClrMameProModels = RomValidator.Models.ClrMamePro;

namespace RomValidator.Services.ClrMamePro;

public class ClrMameProValidator
{
    private readonly BugReportService _bugReportService;
    private readonly Dictionary<string, List<ClrMameProModels.Rom>> _romDatabase = new();
    private readonly Dictionary<string, ClrMameProModels.Rom> _romDatabaseBySha1 = new();
    private readonly Dictionary<string, ClrMameProModels.Rom> _romDatabaseByMd5 = new();
    private readonly Dictionary<string, ClrMameProModels.Rom> _romDatabaseByCrc = new();
    private readonly Dictionary<string, ClrMameProModels.Machine> _machineDatabase = new();

    public ClrMameProModels.Datafile? LoadedDatafile { get; private set; }
    public int TotalRoms => _romDatabase.Count;
    public int TotalMachines => _machineDatabase.Count;

    public ClrMameProValidator(BugReportService bugReportService)
    {
        _bugReportService = bugReportService;
    }

    public async Task<bool> LoadDatFileAsync(ClrMameProModels.Datafile datafile)
    {
        return await Task.Run(() =>
        {
            try
            {
                LoadedDatafile = datafile;
                _romDatabase.Clear();
                _romDatabaseBySha1.Clear();
                _romDatabaseByMd5.Clear();
                _romDatabaseByCrc.Clear();
                _machineDatabase.Clear();

                // Build ROM databases from all machines
                foreach (var machine in datafile.Machines)
                {
                    _machineDatabase[machine.Name] = machine;

                    foreach (var rom in machine.Roms)
                    {
                        // Index by ROM name (including machine name for uniqueness)
                        var uniqueRomName = $"{machine.Name}/{rom.Name}";
                        
                        if (!_romDatabase.ContainsKey(rom.Name))
                        {
                            _romDatabase[rom.Name] = new List<ClrMameProModels.Rom>();
                        }
                        _romDatabase[rom.Name].Add(rom);

                        // Index by hashes for lookup
                        if (!string.IsNullOrEmpty(rom.Sha1))
                        {
                            _romDatabaseBySha1[rom.Sha1.ToLowerInvariant()] = rom;
                        }
                        if (!string.IsNullOrEmpty(rom.Md5))
                        {
                            _romDatabaseByMd5[rom.Md5.ToLowerInvariant()] = rom;
                        }
                        if (!string.IsNullOrEmpty(rom.Crc))
                        {
                            _romDatabaseByCrc[rom.Crc.ToLowerInvariant()] = rom;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _ = _bugReportService.SendBugReportAsync("Error building ClrMamePro ROM database", ex);
                return false;
            }
        });
    }

    public async Task<ClrMameProValidationResult> ValidateFileAsync(
        string filePath,
        bool moveSuccess,
        bool moveFailed,
        bool deleteFailed,
        bool renameMatched,
        string successPath,
        string failPath,
        CancellationToken cancellationToken)
    {
        var result = new ClrMameProValidationResult
        {
            FileName = Path.GetFileName(filePath),
            MachineName = null,
            RomName = null,
            Status = ValidationStatus.Unknown
        };

        var fileName = Path.GetFileName(filePath);

        try
        {
            // Calculate hashes for the file
            var gameFiles = await HashCalculator.CalculateHashesAsync(filePath, cancellationToken, _bugReportService);

            if (gameFiles.Count == 0 || !string.IsNullOrEmpty(gameFiles[0].ErrorMessage))
            {
                result.Status = ValidationStatus.Error;
                result.ErrorMessage = gameFiles.Count > 0 ? gameFiles[0].ErrorMessage : "Failed to calculate hashes";
                return result;
            }

            var gameFile = gameFiles[0];

            // Try to find ROM by hash
            var matchedRom = FindRomByHash(gameFile);

            if (matchedRom != null)
            {
                // Find which machine this ROM belongs to
                var machine = FindMachineForRom(matchedRom);
                
                result.MachineName = machine?.Name;
                result.RomName = matchedRom.Name;
                result.MatchedHash = $"SHA1: {gameFile.Sha1}";
                
                // Check if filename matches (allowing for archive extensions)
                var expectedName = matchedRom.Name;
                var actualName = gameFile.FileName;

                if (string.Equals(expectedName, actualName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = ValidationStatus.Success;
                }
                else
                {
                    result.Status = ValidationStatus.HashMatchWrongName;
                    result.ExpectedName = expectedName;
                    result.ActualName = actualName;
                }

                // Handle file operations
                if (result.Status == ValidationStatus.Success && moveSuccess)
                {
                    await MoveFileAsync(filePath, Path.Combine(successPath, fileName));
                }
                else if ((result.Status == ValidationStatus.HashMatchWrongName || result.Status == ValidationStatus.Unknown) && renameMatched && !string.IsNullOrEmpty(result.ExpectedName))
                {
                    // Rename the file to match the DAT entry
                    var directory = Path.GetDirectoryName(filePath);
                    var extension = Path.GetExtension(filePath);
                    var newFilePath = Path.Combine(directory ?? string.Empty, result.ExpectedName + extension);
                    await RenameFileAsync(filePath, newFilePath);
                    result.WasRenamed = true;
                    
                    if (moveSuccess)
                    {
                        await MoveFileAsync(newFilePath, Path.Combine(successPath, result.ExpectedName + extension));
                    }
                }
                else if (result.Status != ValidationStatus.Success && moveFailed)
                {
                    await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
                }
                else if (result.Status != ValidationStatus.Success && deleteFailed)
                {
                    await DeleteFileAsync(filePath);
                    result.WasDeleted = true;
                }
            }
            else
            {
                result.Status = ValidationStatus.Unknown;
                result.ErrorMessage = "ROM not found in DAT file";

                // Handle unknown files
                if (moveFailed)
                {
                    await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
                }
                else if (deleteFailed)
                {
                    await DeleteFileAsync(filePath);
                    result.WasDeleted = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Status = ValidationStatus.Error;
            result.ErrorMessage = ex.Message;
            _ = _bugReportService.SendBugReportAsync($"Error validating file: {fileName}", ex);
        }

        return result;
    }

    private ClrMameProModels.Rom? FindRomByHash(GameFile gameFile)
    {
        // Try SHA1 first (most reliable)
        if (!string.IsNullOrEmpty(gameFile.Sha1) && _romDatabaseBySha1.TryGetValue(gameFile.Sha1.ToLowerInvariant(), out var romBySha1))
        {
            if (romBySha1.Size == gameFile.FileSize)
            {
                return romBySha1;
            }
        }

        // Try MD5
        if (!string.IsNullOrEmpty(gameFile.Md5) && _romDatabaseByMd5.TryGetValue(gameFile.Md5.ToLowerInvariant(), out var romByMd5))
        {
            if (romByMd5.Size == gameFile.FileSize)
            {
                return romByMd5;
            }
        }

        // Try CRC32
        if (!string.IsNullOrEmpty(gameFile.Crc32) && _romDatabaseByCrc.TryGetValue(gameFile.Crc32.ToLowerInvariant(), out var romByCrc))
        {
            if (romByCrc.Size == gameFile.FileSize)
            {
                return romByCrc;
            }
        }

        return null;
    }

    private ClrMameProModels.Machine? FindMachineForRom(ClrMameProModels.Rom rom)
    {
        foreach (var machine in LoadedDatafile?.Machines ?? new List<ClrMameProModels.Machine>())
        {
            if (machine.Roms.Any(r => r == rom || 
                (r.Name == rom.Name && r.Sha1 == rom.Sha1)))
            {
                return machine;
            }
        }
        return null;
    }

    private static async Task MoveFileAsync(string sourcePath, string destPath)
    {
        const int maxRetries = 10;
        const int delayMs = 200;

        Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? string.Empty);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Run(() => File.Move(sourcePath, destPath));
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(delayMs * attempt);
            }
        }
    }

    private static async Task RenameFileAsync(string sourcePath, string destPath)
    {
        const int maxRetries = 10;
        const int delayMs = 200;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Run(() => File.Move(sourcePath, destPath));
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(delayMs * attempt);
            }
        }
    }

    private static async Task DeleteFileAsync(string filePath)
    {
        const int maxRetries = 10;
        const int delayMs = 200;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Run(() => File.Delete(filePath));
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(delayMs * attempt);
            }
        }
    }
}

public enum ValidationStatus
{
    Success,
    HashMatchWrongName,
    Unknown,
    Error
}

public class ClrMameProValidationResult
{
    public string FileName { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public string? RomName { get; set; }
    public ValidationStatus Status { get; set; }
    public string? MatchedHash { get; set; }
    public string? ExpectedName { get; set; }
    public string? ActualName { get; set; }
    public string? ErrorMessage { get; set; }
    public bool WasRenamed { get; set; }
    public bool WasDeleted { get; set; }
}
