# ROM Validator

A Windows desktop application for validating ROM files against DAT files and generating new No-Intro compliant DAT files from your collection.

## Features

- **ROM Validation**: Validate your ROM collection against No-Intro DAT files
- **DAT File Generation**: Generate new No-Intro compliant DAT files from your ROM collection
- **Duplicate Detection**: Identify duplicate ROM files in your collection
- **Multiple Hash Support**: Supports CRC32, MD5, SHA-1, and SHA-256 hash verification
- **Archive Support**: Can read ROMs from 7z archives using SharpSevenZip
- **Bug Reporting**: Built-in bug reporting system for error tracking
- **Version Checking**: Automatic GitHub version checking for updates
- **Usage Statistics**: Anonymous usage statistics tracking

## Requirements

- **.NET 10.0** or higher
- **Windows 10/11** (WPF application)
- **7z native libraries** (included via SharpSevenZip package)

## Installation

### From Source
1. Clone the repository:
   ```bash
   git clone https://github.com/drpetersonfernandes/RomValidator.git
   ```
2. Open the solution in Visual Studio 2022 or later:
   ```
   CSharp_RomValidator.sln
   ```
3. Build the solution (Ctrl+Shift+B)
4. Run the application (F5)

### Pre-built Releases
Check the [GitHub Releases](https://github.com/drpetersonfernandes/RomValidator/releases) page for pre-built binaries.

## Usage

### Validating ROMs
1. Launch the application
2. Navigate to the "Validate" tab
3. Load a DAT file (No-Intro format)
4. Select your ROM directory or archive
5. Click "Validate" to start the validation process
6. View results showing which ROMs are valid, missing, or have incorrect hashes

### Generating DAT Files
1. Navigate to the "Generate DAT" tab
2. Select your ROM directory
3. Configure output settings (name, description, version)
4. Click "Generate" to create a new DAT file
5. Save the generated DAT file for use with other ROM management tools

## Project Structure

```
CSharp_RomValidator/
├── RomValidator/                    # Main WPF application
│   ├── Models/                     # Data models
│   │   ├── NoIntro/               # No-Intro DAT file models (Datafile, Game, Rom, Header)
│   │   ├── BugReportPayload.cs    # Bug reporting model
│   │   ├── GitHubRelease.cs       # GitHub release model
│   │   ├── GitHubAsset.cs         # GitHub release asset model
│   │   └── GameFile.cs            # Game file model with hash values
│   ├── Services/                  # Business logic services
│   │   ├── HashCalculator.cs      # Hash calculation service (CRC32, MD5, SHA1, SHA256)
│   │   ├── Crc32Algorithm.cs      # CRC32 hash algorithm implementation
│   │   ├── LoggerService.cs       # Logging and error tracking
│   │   ├── BugReportService.cs    # Bug reporting service
│   │   ├── GitHubVersionChecker.cs # Version checking
│   │   ├── ApplicationStatsService.cs # Usage statistics tracking
│   │   └── TempDirectoryHelper.cs # Temporary directory management
│   ├── Pages/                     # Application pages
│   │   ├── ValidatePage.xaml      # ROM validation UI
│   │   └── GenerateDatPage.xaml   # DAT generation UI
│   ├── MainWindow.xaml            # Main application window
│   ├── AboutWindow.xaml           # About dialog
│   ├── DuplicateFilesWindow.xaml  # Duplicate files dialog
│   └── App.xaml.cs                # Application entry point with global exception handling
├── RomValidator.Tests/            # Unit tests (xUnit)
└── CSharp_RomValidator.sln        # Visual Studio solution
```

## Development

### Building
```bash
dotnet build
```

### Running Tests
```bash
dotnet test
```

### Dependencies
- **SharpSevenZip** (2.0.36): For 7z archive support
- **WPF**: Windows Presentation Foundation for UI
- **xUnit** (2.9.3): Unit testing framework
- **Microsoft.NET.Test.Sdk** (18.4.0): Test SDK
- **coverlet.collector** (10.0.0): Test coverage

### Code Style
- C# 14 language features
- Nullable reference types enabled
- Implicit usings enabled
- Async/await pattern for I/O operations

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add or update tests as needed
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

- **Website**: [http://www.purelogiccode.com](http://www.purelogiccode.com)
- **GitHub Issues**: [Report bugs or request features](https://github.com/drpetersonfernandes/RomValidator/issues)
- **Bug Reports**: Built-in bug reporting system in the application

## Version History

- **v2.6.0**: Current version (see AssemblyVersion in csproj)
- Check GitHub Releases for detailed changelog

## Acknowledgments

- No-Intro for the DAT file format specification
- SharpSevenZip for archive support
- All contributors and testers