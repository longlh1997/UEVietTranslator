using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using UEVietTranslator.Core.CsvSchema;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Bước 5 (Review): danh sách sửa được ngay trong app (quyết định UX đã chốt
/// với Hải Long — KHÔNG bắt buộc sửa ngoài Excel, xem
/// docs/ROADMAP.md Pha 6). "Lưu CSV" ghi lại đúng trạng thái hiện có trong
/// <see cref="CsvRows"/> (kể cả sửa tay chưa qua Gemini); "Tải lại từ CSV"
/// đọc lại từ đĩa — dùng khi Hải Long cũng sửa tay file CSV song song bằng
/// Excel bên ngoài app.
/// </summary>
public partial class MainWindowViewModel
{
    public ObservableCollection<CsvRowItemViewModel> CsvRows { get; } = new();

    private void LoadCsvRowsIntoReview(IReadOnlyList<CsvRow> rows)
    {
        CsvRows.Clear();
        foreach (var row in rows)
            CsvRows.Add(new CsvRowItemViewModel(row));
    }

    private bool CanSaveCsvRows() => !IsBusy && CsvRows.Count > 0 && !string.IsNullOrWhiteSpace(CsvPath);

    [RelayCommand(CanExecute = nameof(CanSaveCsvRows))]
    private async Task SaveCsvRowsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var rows = CsvRows.Select(item => item.ToCsvRow()).ToList();
            var result = await _csvSchemaConverter.WriteRowsAsync(rows, CsvPath, cancellationToken);

            StatusMessage = result.IsSuccess
                ? $"Đã lưu {rows.Count} dòng vào {CsvPath}."
                : $"Lưu CSV thất bại: {result.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanReloadCsvRows() => !IsBusy && !string.IsNullOrWhiteSpace(CsvPath);

    [RelayCommand(CanExecute = nameof(CanReloadCsvRows))]
    private async Task ReloadCsvRowsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await _csvSchemaConverter.ImportAsync(CsvPath, cancellationToken);
            if (!result.IsSuccess)
            {
                StatusMessage = $"Tải lại CSV thất bại: {result.Error}";
                return;
            }

            LoadCsvRowsIntoReview(result.Value!);
            StatusMessage = $"Đã tải lại {result.Value!.Count} dòng từ {CsvPath}. Bấm Tiếp tục để sang bước Repack.";
        }
        finally
        {
            IsBusy = false;
            NextCommand.NotifyCanExecuteChanged();
        }
    }
}
