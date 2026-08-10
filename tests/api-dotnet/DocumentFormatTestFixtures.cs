using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace EnterpriseDocumentAssistant.Api.Tests;

internal static class DocumentFormatTestFixtures
{
    public static IFormFile CreateFormFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes, writable: false);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    public static byte[] CreatePdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            if (!string.IsNullOrEmpty(text))
            {
                page.AddText(text, 12, new PdfPoint(36, 760), font);
            }
        }

        return builder.Build();
    }

    public static byte[] CreateDocx(string text, int extraEntryBytes = 0, bool includeDocumentPart = true)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>");

            WriteEntry(
                archive,
                "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");

            if (includeDocumentPart)
            {
                var escaped = System.Security.SecurityElement.Escape(text) ?? string.Empty;
                WriteEntry(
                    archive,
                    "word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:body><w:p><w:r><w:t>" + escaped + "</w:t></w:r></w:p></w:body></w:document>");
            }

            if (extraEntryBytes > 0)
            {
                var entry = archive.CreateEntry("word/media/padding.bin", CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(new byte[extraEntryBytes]);
            }
        }

        return output.ToArray();
    }

    public static async Task<string> WriteTemporaryFileAsync(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"document-format-{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: false);
        writer.Write(content);
    }
}
