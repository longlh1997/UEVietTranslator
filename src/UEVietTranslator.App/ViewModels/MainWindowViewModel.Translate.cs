using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEVietTranslator.Core.Translation;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Bước 4 (Translate): cấu hình Gemini API key/model (lưu qua
/// <see cref="IGeminiSettingsStore"/>, xem docs/DECISIONS.md#adr-011), rồi
/// đọc CSV vừa xuất ở bước trước, dịch các dòng còn
/// <see cref="Core.CsvSchema.TranslationStatus.Untranslated"/> qua
/// <see cref="ITranslationService"/>, ghi kết quả đè lại CSV và nạp vào danh
/// sách <see cref="CsvRows"/> cho bước Review.
/// </summary>
public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string? _geminiApiKey;

    [ObservableProperty]
    private string _geminiModel = "gemini-2.0-flash";

    private bool CanSaveGeminiKey() => !IsBusy && !string.IsNullOrWhiteSpace(GeminiApiKey);

    [RelayCommand(CanExecute = nameof(CanSaveGeminiKey))]
    private async Task SaveGeminiKeyAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await _geminiSettingsStore.SaveAsync(
                new GeminiSettings(GeminiApiKey!, GeminiModel), cancellationToken);

            StatusMessage = result.IsSuccess
                ? $"Đã lưu Gemini API key, model = {GeminiModel}."
                : $"Lưu Gemini key thất bại: {result.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanTranslate() => !IsBusy && !string.IsNullOrWhiteSpace(CsvPath);

    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private async Task TranslateAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Đang đọc CSV...";
            var importResult = await _csvSchemaConverter.ImportAsync(CsvPath, cancellationToken);
            if (!importResult.IsSuccess)
            {
                StatusMessage = $"Đọc CSV thất bại: {importResult.Error}";
                return;
            }

            var progress = new Progress<Core.Common.ProgressInfo>(p => StatusMessage = p.Message);

            StatusMessage = "Đang dịch qua Gemini...";
            var translateResult = await _translationService.TranslateBatchAsync(importResult.Value!, progress, cancellationToken);
            if (!translateResult.IsSuccess)
            {
                StatusMessage = $"Dịch thất bại: {translateResult.Error}";
                return;
            }

            var writeResult = await _csvSchemaConverter.WriteRowsAsync(translateResult.Value!, CsvPath, cancellationToken);
            if (!writeResult.IsSuccess)
            {
                StatusMessage = $"Ghi lại CSV sau khi dịch thất bại: {writeResult.Error}";
                return;
            }

            LoadCsvRowsIntoReview(translateResult.Value!);
            StatusMessage = $"Đã dịch xong {translateResult.Value!.Count} dòng. Bấm Tiếp tục để sang bước Review.";
        }
        finally
        {
            IsBusy = false;
            NextCommand.NotifyCanExecuteChanged();
        }
    }
}
