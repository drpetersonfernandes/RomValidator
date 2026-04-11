using System.IO;
using System.Xml;
using System.Xml.Serialization;
using ClrMameProModels = RomValidator.Models.ClrMamePro;

namespace RomValidator.Services.ClrMamePro;

public class ClrMameProDatLoader
{
    private readonly BugReportService _bugReportService;

    public ClrMameProDatLoader(BugReportService bugReportService)
    {
        _bugReportService = bugReportService;
    }

    public async Task<(bool Success, ClrMameProModels.Datafile? Datafile, string ErrorMessage)> LoadDatFileAsync(string datFilePath)
    {
        string? datFilePreview = null;

        try
        {
            // Read a preview of the DAT file for error reporting (first 5000 characters)
            try
            {
                await using var stream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                using var reader = new StreamReader(stream);
                var buffer = new char[5000];
                var charsRead = await reader.ReadBlockAsync(buffer, 0, 5000);
                datFilePreview = new string(buffer, 0, charsRead);

                if (charsRead == 5000)
                {
                    datFilePreview += "\n\n[... FILE TRUNCATED FOR PREVIEW ...]";
                }
            }
            catch
            {
                datFilePreview = "[Could not read file preview]";
            }

            // Validate it's a valid XML first
            await using (var validationStream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            {
                using var validationReader = XmlReader.Create(validationStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

                if (!validationReader.ReadToFollowing("datafile"))
                {
                    return (false, null, "The selected file does not contain a valid <datafile> root element.");
                }
            }

            // Create serializer for ClrMamePro format
            var serializer = new XmlSerializer(typeof(ClrMameProModels.Datafile));

            // Deserialize the DAT file
            await using var deserializeStream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var xmlReader = XmlReader.Create(deserializeStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

            var datafile = await Task.Run(() => (ClrMameProModels.Datafile?)serializer.Deserialize(xmlReader));

            if (datafile?.Machines is null || datafile.Machines.Count == 0)
            {
                var detailedError = $"User attempted to load empty/invalid ClrMamePro DAT file: {Path.GetFileName(datFilePath)}\n\nFile Preview:\n{datFilePreview}";
                _ = _bugReportService.SendBugReportAsync(detailedError);
                return (false, null, "The selected file was parsed but contains no machine entries.");
            }

            return (true, datafile, string.Empty);
        }
        catch (InvalidOperationException ex) when (ex.InnerException != null)
        {
            var innerMsg = ex.InnerException.Message;
            var detailedError = $"XML Serialization error for ClrMamePro DAT file: {Path.GetFileName(datFilePath)}\n\nError: {innerMsg}\n\nFull Exception: {ex}\n\nFile Preview:\n{datFilePreview}";
            _ = _bugReportService.SendBugReportAsync(detailedError, ex);
            return (false, null, $"XML Parsing Error: {innerMsg}");
        }
        catch (XmlException xmlEx)
        {
            var detailedError = $"XML parsing error for ClrMamePro DAT file: {Path.GetFileName(datFilePath)}\n\nError: {xmlEx.Message}\n\nLine: {xmlEx.LineNumber}, Position: {xmlEx.LinePosition}\n\nFile Preview:\n{datFilePreview}";
            _ = _bugReportService.SendBugReportAsync(detailedError, xmlEx);
            return (false, null, $"XML Error: {xmlEx.Message}");
        }
        catch (Exception ex)
        {
            var detailedError = $"Unexpected error loading ClrMamePro DAT file: {Path.GetFileName(datFilePath)}\n\nError: {ex.Message}\n\nException Type: {ex.GetType().Name}\n\nFile Preview:\n{datFilePreview}";
            _ = _bugReportService.SendBugReportAsync(detailedError, ex);
            return (false, null, $"Unexpected error: {ex.Message}");
        }
    }
}
