using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Bước 3 (ExtractAndExportCsv): với các file đã tick ở bước ConfirmFiles
/// (dùng thẳng danh sách trong bộ nhớ, KHÔNG bắt buộc đã bấm Lưu — Lưu chỉ để
/// tái sử dụng lựa chọn cho lần chạy sau, xem <see cref="Candidates"/>), gọi
/// <see cref="Core.Unpacking.IUnpackProvider.ExtractFilesAsync"/> 1 lần cho
/// cả lô rồi <see cref="IAssetReaderWriter.ReadAsync"/> từng file, gộp theo
/// virtual path và xuất ra 1 file CSV chuẩn hoá qua
/// <see cref="Core.CsvSchema.ICsvSchemaConverter"/>.
/// </summary>
public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _csvPath = string.Empty;

    private bool CanExtractAndExportCsv() =>
        !IsBusy && _lastDetectedProfile is not null && _lastUsedUnpackProvider is not null &&
        Candidates.Any(c => c.IsConfirmed);

    [RelayCommand(CanExecute = nameof(CanExtractAndExportCsv))]
    private async Task ExtractAndExportCsvAsync(CancellationToken cancellationToken)
    {
        if (_lastDetectedProfile is null || _lastUsedUnpackProvider is null)
            return;

        IsBusy = true;
        _extractedFileLookup.Clear();

        try
        {
            var confirmed = Candidates.Where(c => c.IsConfirmed).ToList();
            var virtualPaths = confirmed.Select(c => c.Path).ToList();
            IProgress<ProgressInfo> progress = new Progress<ProgressInfo>(p => StatusMessage = p.Message);

            var extractDirectory = Path.Combine(Path.GetTempPath(), "uevt-extract", SanitizeForPath(_lastDetectedProfile.GameDirectory));
            _lastExtractDirectory = extractDirectory;

            StatusMessage = "Đang extract file đã xác nhận...";
            var extractResult = await _lastUsedUnpackProvider.ExtractFilesAsync(
                _lastDetectedProfile, _lastAesKeyHex, virtualPaths, extractDirectory, progress, cancellationToken);
            if (!extractResult.IsSuccess)
            {
                StatusMessage = $"Extract thất bại: {extractResult.Error}";
                return;
            }

            var kindByPath = confirmed.ToDictionary(c => c.Path, c => c.Kind);
            var entriesBySourceFile = new Dictionary<string, IReadOnlyList<TextEntry>>();

            var readCount = 0;
            foreach (var extracted in extractResult.Value!)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Không phải mọi file trả về đều là file đã confirm (có thể
                // kèm .uexp/.ubulk companion) — chỉ đọc entry cho đúng các
                // virtual path người dùng đã tick.
                if (extracted.ExtractedFilePath is null || !kindByPath.TryGetValue(extracted.VirtualPath, out var kind))
                    continue;

                _extractedFileLookup[extracted.VirtualPath] = (extracted.ExtractedFilePath, kind);

                var readResult = await _assetReaderWriter.ReadAsync(
                    extracted.ExtractedFilePath, kind, _lastDetectedProfile.EngineVersion, cancellationToken);
                if (!readResult.IsSuccess)
                {
                    StatusMessage = $"Đọc file thất bại (bỏ qua, tiếp tục các file khác): {extracted.VirtualPath} — {readResult.Error}";
                    continue;
                }

                entriesBySourceFile[extracted.VirtualPath] = readResult.Value!;
                readCount++;
                progress.Report(new ProgressInfo(readCount * 100.0 / confirmed.Count, $"Đã đọc {readCount}/{confirmed.Count} file"));
            }

            if (entriesBySourceFile.Count == 0)
            {
                StatusMessage = "Không đọc được entry nào từ các file đã chọn.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CsvPath))
                CsvPath = Path.Combine(extractDirectory, "translations.csv");

            StatusMessage = "Đang xuất CSV...";
            var exportResult = await _csvSchemaConverter.ExportAsync(entriesBySourceFile, CsvPath, cancellationToken);
            if (!exportResult.IsSuccess)
            {
                StatusMessage = $"Xuất CSV thất bại: {exportResult.Error}";
                return;
            }

            var totalEntries = entriesBySourceFile.Sum(kv => kv.Value.Count);
            StatusMessage = $"Đã xuất {totalEntries} dòng text từ {entriesBySourceFile.Count} file ra {CsvPath}. Bấm Tiếp tục để sang bước Dịch.";
        }
        finally
        {
            IsBusy = false;
            NextCommand.NotifyCanExecuteChanged();
        }
    }

    // Thay ký tự không hợp lệ trong đường dẫn (":", "\", "/"...) bằng "_" để
    // dùng GameDirectory làm tên thư mục con dưới temp — tránh lẫn lộn file
    // extract giữa nhiều game khác nhau đã Quét trong cùng phiên làm việc.
    private static string SanitizeForPath(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
