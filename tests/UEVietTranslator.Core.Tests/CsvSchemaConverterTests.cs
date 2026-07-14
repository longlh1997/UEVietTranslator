using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.CsvSchema;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class CsvSchemaConverterTests : IDisposable
{
    private readonly string _tempDir;

    public CsvSchemaConverterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "uevt-csvschema-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ExportRoiImport_TraVeDungDuLieuVoiTiengViet()
    {
        var converter = new CsvSchemaConverter();
        var csvPath = Path.Combine(_tempDir, "strings.csv");

        var entriesBySourceFile = new Dictionary<string, IReadOnlyList<TextEntry>>
        {
            ["Game/vi/Game.locres"] = new List<TextEntry>
            {
                new("UI", "Btn_Start", "Bắt đầu, chào mừng \"bạn\"! Có dấu phẩy, xuống dòng\nvà ký tự đặc biệt."),
                new("UI", "Btn_Quit", "Thoát"),
            },
        };

        var exportResult = await converter.ExportAsync(entriesBySourceFile, csvPath, CancellationToken.None);
        Assert.True(exportResult.IsSuccess, exportResult.Error);
        Assert.True(File.Exists(csvPath));

        var importResult = await converter.ImportAsync(csvPath, CancellationToken.None);
        Assert.True(importResult.IsSuccess, importResult.Error);

        var rows = importResult.Value!;
        Assert.Equal(2, rows.Count);

        var startRow = rows.Single(r => r.Key == "Btn_Start");
        Assert.Equal("Game/vi/Game.locres", startRow.SourceFile);
        Assert.Equal("UI", startRow.Namespace);
        Assert.Equal("Bắt đầu, chào mừng \"bạn\"! Có dấu phẩy, xuống dòng\nvà ký tự đặc biệt.", startRow.SourceText);
        Assert.Equal(string.Empty, startRow.TranslatedText);
        Assert.Equal(TranslationStatus.Untranslated, startRow.Status);
    }

    [Fact]
    public async Task Import_DongStatusGoSaiTay_FallbackVeUntranslatedKhongFailCaFile()
    {
        var converter = new CsvSchemaConverter();
        var csvPath = Path.Combine(_tempDir, "badstatus.csv");
        await File.WriteAllTextAsync(csvPath,
            "SourceFile,Namespace,Key,SourceText,Context,TranslatedText,Status\n" +
            "f.locres,NS,K1,Hello,,Xin chào,typo_status\n");

        var importResult = await converter.ImportAsync(csvPath, CancellationToken.None);

        Assert.True(importResult.IsSuccess, importResult.Error);
        Assert.Equal(TranslationStatus.Untranslated, importResult.Value!.Single().Status);
    }

    [Fact]
    public async Task Import_FileKhongTonTai_TraVeFailureRoRang()
    {
        var converter = new CsvSchemaConverter();

        var result = await converter.ImportAsync(Path.Combine(_tempDir, "khong-ton-tai.csv"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("không tồn tại", result.Error);
    }
}
