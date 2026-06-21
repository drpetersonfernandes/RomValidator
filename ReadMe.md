[![GitHub release](https://img.shields.io/github/v/release/drpetersonfernandes/RomValidator)](https://github.com/drpetersonfernandes/RomValidator/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64%20%7C%20ARM64-blue)](https://github.com/drpetersonfernandes/RomValidator/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE.txt)

# ROM Validator

A Windows desktop application for validating ROM files against DAT files and generating new No-Intro compliant DAT files from your collection.

## 📸 Screenshots

![System Selection](screenshot.png)

![List Of Games in Grid Mode](screenshot2.png)

## Features

- **ROM Validation**: Validate your ROM collection against No-Intro DAT files
- **DAT File Generation**: Generate new No-Intro compliant DAT files from your ROM collection
- **Duplicate Detection**: Identify duplicate ROM files in your collection
- **Multiple Hash Support**: Supports CRC32, MD5, SHA-1, and SHA-256 hash verification
- **Archive Support**: Can read ROMs from 7z archives using SharpSevenZip
- **Version Checking**: Automatic GitHub version checking for updates

## Requirements

- **.NET 10.0** or higher
- **Windows 10/11** (WPF application)
- **7z native libraries** (included via SharpSevenZip package)

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

### Dependencies
- **SharpSevenZip** (2.0.36): For 7z archive support
- **WPF**: Windows Presentation Foundation for UI
- **xUnit** (2.9.3): Unit testing framework
- **Microsoft.NET.Test.Sdk** (18.4.0): Test SDK
- **coverlet.collector** (10.0.0): Test coverage

## Acknowledgments

- No-Intro for the DAT file format specification
- SharpSevenZip for archive support

## Contributing & Support

* **Donate:** If you find this project useful, consider [supporting the developer](https://www.purelogiccode.com/donate).
* **If you like this project, please give us a star on GitHub! ⭐**

## 📜 License

This project is licensed under the GPLv3 License – see the [LICENSE](LICENSE.txt) file for details.