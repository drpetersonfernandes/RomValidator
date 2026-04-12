# Release Notes - Version 2.4.0

This release marks a significant milestone in the evolution of **ROM Validator**, focusing on architectural stability, performance optimizations, and enhanced support for the No-Intro ecosystem.

### 🔍 Enhanced Validation & DAT Support
- **SHA256 Support**: Full integration of SHA256 hashing for No-Intro XML DATs, including parsing, indexing, and multi-hash verification.
- **Improved Format Detection**: Advanced scanner to proactively identify and reject incompatible formats (ClrMamePro text, MAME XML, HTML, and ZIP) with descriptive user notifications.
- **Smart File Renaming**: New option to automatically rename local files to match DAT entries when hashes match but filenames differ.
- **Internal Archive Renaming**: Added support for renaming files *inside* ZIP archives to ensure full collection compliance.
- **Multi-ROM Support**: Improved handling of game entries containing multiple ROM files.

### 🚀 Performance & Reliability
- **Library Migration**: Replaced `SevenZipSharp` with **SharpCompress**, enabling native support for Zip, 7z, and RAR without requiring external 7z DLL dependencies.
- **Async Architecture**: Fully migrated to `OpenArchive` and `OpenEntryStreamAsync` APIs for non-blocking I/O.
- **UI Responsiveness**: 
    - Implemented background file counting for dynamic, accurate progress bar updates.
    - Added UI virtualization (limiting displayed logs to 500 items) to prevent lag during large scans.
    - Offloaded DAT serialization to background threads.
- **Thread Safety**: Integrated `Interlocked` counters and thread-safe buffers for UI updates.

### 📂 Collection Management
- **Permanent Deletion**: Added a safety-confirmed option to permanently delete failed or unknown files from the disk.
- **Sequential Processing**: Switched to a robust sequential processing model to prevent race conditions during file move/rename operations.
- **ZIP Fallback**: Added a fallback extraction mechanism for ZIP files using incompatible compression methods.

### 💻 System & UX
- **Centralized Logging**: Introduced `LoggerService` for standardized error and activity logging.
- **Automatic Bug Reporting**: Integrated `BugReportService` to automatically capture and report application exceptions for faster troubleshooting.
- **Modern Standards**: Updated codebase to **.NET 10** and **C# 14**, utilizing `GeneratedRegex` and other modern language features.

---
**Note:** This version exclusively supports **No-Intro XML DAT** files. Users attempting to load ClrMamePro or MAME-specific DATs will be notified to download compatible XML files from the No-Intro website.
