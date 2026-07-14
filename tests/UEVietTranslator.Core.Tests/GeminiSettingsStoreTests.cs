using UEVietTranslator.Core.Translation;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class GeminiSettingsStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public GeminiSettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "uevt-gemini-test-" + Guid.NewGuid());
        _filePath = Path.Combine(_tempDir, "gemini-settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ChuaTungSave_TraVeFailureRoRang()
    {
        var store = new GeminiSettingsStore(_filePath);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Chưa cấu hình", result.Error);
    }

    [Fact]
    public async Task SaveRoiLoad_TraVeDungDuLieu()
    {
        var store = new GeminiSettingsStore(_filePath);
        var settings = new GeminiSettings("test-key-123", "gemini-2.0-flash");

        var saveResult = await store.SaveAsync(settings, CancellationToken.None);
        Assert.True(saveResult.IsSuccess, saveResult.Error);

        var loadResult = await store.LoadAsync(CancellationToken.None);
        Assert.True(loadResult.IsSuccess, loadResult.Error);
        Assert.Equal(settings, loadResult.Value);
    }
}
