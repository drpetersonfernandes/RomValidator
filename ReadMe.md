# ROM Validator

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE.txt)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](#requirements)

**ROM Validator** is a high-performance Windows desktop utility designed for ROM collection management and digital preservation. It provides a streamlined workflow for validating local ROM files against industry-standard No-Intro DAT specifications and generating new, compliant DAT files from existing collections.

![ROM Validator Screenshot](screenshot.png)
![ROM Validator Screenshot](screenshot2.png)

## 🚀 Key Features

### 🔍 Advanced Validation
-   **Multi-Hash Verification**: Validates file integrity using CRC32, MD5, and SHA1/SHA256 checksums.
-   **No-Intro Integration**: Native support for No-Intro XML DAT formats.
-   **Archive Support**: Deep-scans within compressed archives (ZIP, 7z, RAR, etc.) without manual extraction.
-   **Parallel Processing**: High-speed validation utilizing multi-threaded hashing (optimized for modern multi-core CPUs).

### 📂 Collection Management
-   **Automated Organization**: Automatically sorts files into `_success` or `_fail` directories based on validation results.
-   **DAT Generation**: Create No-Intro compliant DAT files from any folder, complete with custom metadata (Author, Version, Description).
-   **Real-time Logging**: Detailed, timestamped logs for every operation, including specific reasons for validation failures.

### 💻 User Experience
-   **Modern WPF Interface**: A clean, responsive UI with progress monitoring and statistical breakdowns.
-   **Update Notifications**: Integrated GitHub version checking to ensure you are always using the latest preservation logic.
-   **Bug Reporting**: Built-in service to report issues directly to the development team.

---

## 🛠 Requirements

-   **Operating System**: Windows 10 (version 1809) or later / Windows 11.
-   **Runtime**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
-   **Architecture**: x64 or ARM64.

---

## 📥 Installation

1.  Navigate to the [Releases](https://github.com/drpetersonfernandes/RomValidator/releases) page.
2.  Download the latest `RomValidator.zip`.
3.  Extract the contents to a permanent folder.
4.  Launch `RomValidator.exe`.

---

## 📖 Usage Guide

### Validating ROMs
1.  **Select Source**: Choose the folder containing your ROM files.
2.  **Load DAT**: Select a compatible No-Intro XML DAT file.
3.  **Configure**: Toggle "Move successful items" or "Parallel Processing" as needed.
4.  **Execute**: Click **Start Validation**. The application will categorize files as ✅ **Success**, ❌ **Failed**, or ❓ **Unknown**.

### Generating DATs
1.  Switch to the **Generate DAT** tab.
2.  **Select Folder**: Choose the directory containing the files you wish to catalog.
3.  **Metadata**: Enter the Name, Description, and Author for the DAT header.
4.  **Process**: Click **Start Hashing**.
5.  **Export**: Once complete, click **Export DAT** to save the XML file.

---

## 🤝 Support & Contribution

If you find this tool helpful for your preservation projects, please consider:

-   **Starring the project** on GitHub to increase visibility.
-   **Reporting Bugs**: Use the built-in reporting tool or open a GitHub Issue.
-   **Donations**: Support continued development at [purelogiccode.com/donate](https://www.purelogiccode.com/donate).

---

## 📜 License

This project is licensed under the **GNU General Public License v3.0**. See the [LICENSE.txt](LICENSE.txt) file for full details.

Developed by **Pure Logic Code**  
[www.purelogiccode.com](http://www.purelogiccode.com)