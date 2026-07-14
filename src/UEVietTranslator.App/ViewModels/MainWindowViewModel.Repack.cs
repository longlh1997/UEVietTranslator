using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Bước 6 (Repack, bước cuối): ghi bản dịch hiện có trong
/// <see cref="CsvRows"/> đè lên đúng file đã extract ở bước 3 (qua
/// <see cref="_extractedFileLookup"/>), rồi gọi
/// <see cref="Core.Repacking.IRepackService"/> đóng gói lại pak/IoStore.
/// </summary>
public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _outputPakPath = string.Empty;

    // Để trống = tự tìm "repak"/"retoc" qua PATH hệ điều hành, giống hành vi
    // mặc định của CLI `repack` — xem IRepackService.RepackAsync.
    [ObservableProperty]
    private string? _repakExecutablePath;

    [ObservableProperty]
    private string? _retocExecutablePath;

    private bool CanWriteAssetsAndRepack() =>
        !IsBusy && CsvRows.Count > 0 && _lastDetectedProfile is not null &&
        _lastExtractDirectory is not null && !string.IsNullOrWhiteSpace(OutputPakPath);

    [RelayCommand(CanExecute = nameof(CanWriteAssetsAndRepack))]
    private async Task WriteAssetsAndRepackAsync(CancellationToken cancellationToken)
    {
        if (_lastDetectedProfile is null || _lastExtractDirectory is null)
            return;

        IsBusy = true;
        try
        {
            var groups = CsvRows.GroupBy(item => item.SourceFile).ToList();
            var writtenCount = 0;

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_extractedFileLookup.TryGetValue(group.Key, out var extracted))
                {
                    StatusMessage = $"Bỏ qua (không tìm thấy file đã extract): {group.Key}";
                    continue;
                }

                // TextEntry.SourceText được AssetReaderWriter.WriteAsync coi
                // là NỘI DUNG CẦN GHI (đã dịch) — tái dùng record đọc/ghi
                // chung, xem AssetReaderWriter.WriteLocres/WriteStringTable.
                var entries = group
                    .Select(item => new TextEntry(item.Namespace, item.Key, item.TranslatedText))
                    .ToList();

                StatusMessage = $"Đang ghi bản dịch vào {group.Key}...";
                var writeResult = await _assetReaderWriter.WriteAsync(
                    extracted.ExtractedFilePath, extracted.Kind, _lastDetectedProfile.EngineVersion, entries, cancellationToken);
                if (!writeResult.IsSuccess)
                {
                    StatusMessage = $"Ghi thất bại (bỏ qua, tiếp tục các file khác): {group.Key} — {writeResult.Error}";
                    continue;
                }

                writtenCount++;
            }

            if (writtenCount == 0)
            {
                StatusMessage = "Không ghi được file nào — dừng lại, chưa repack.";
                return;
            }

            var progress = new Progress<ProgressInfo>(p => StatusMessage = p.Message);

            StatusMessage = "Đang repack...";
            var repackResult = await _repackService.RepackAsync(
                _lastDetectedProfile, _lastExtractDirectory, OutputPakPath,
                RepakExecutablePath, RetocExecutablePath, progress, cancellationToken);

            StatusMessage = repackResult.IsSuccess
                ? $"Repack thành công. Đã ghi {writtenCount}/{groups.Count} file. Output: {OutputPakPath}"
                : $"Repack thất bại: {repackResult.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
