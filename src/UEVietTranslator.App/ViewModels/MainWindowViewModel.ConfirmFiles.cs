using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using UEVietTranslator.Core.LocalizationDiscovery;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Bước 2 (ConfirmFiles): tick chọn/sửa loại file trong danh sách candidate
/// tìm được ở bước Setup, bấm Lưu để persist qua
/// <see cref="Core.GameProfile.IGameProfileStore.SaveConfirmedLocalizationFilesAsync"/>.
/// </summary>
public partial class MainWindowViewModel
{
    public ObservableCollection<LocalizationCandidateItemViewModel> Candidates { get; } = new();

    private bool CanSaveConfirmed() => !IsBusy && _lastDetectedProfile is not null && Candidates.Any(c => c.IsConfirmed);

    [RelayCommand(CanExecute = nameof(CanSaveConfirmed))]
    private async Task SaveConfirmedAsync(CancellationToken cancellationToken)
    {
        if (_lastDetectedProfile is null)
            return;

        IsBusy = true;
        try
        {
            var confirmed = Candidates
                .Where(c => c.IsConfirmed)
                .Select(c => new ConfirmedLocalizationFile(c.Path, c.Kind))
                .ToList();

            var result = await _profileStore.SaveConfirmedLocalizationFilesAsync(
                _lastDetectedProfile.GameDirectory, confirmed, cancellationToken);

            StatusMessage = result.IsSuccess
                ? $"Đã lưu {confirmed.Count} file ngôn ngữ đã xác nhận. Bấm Tiếp tục để sang bước Extract & Xuất CSV."
                : $"Lưu thất bại: {result.Error}";
        }
        finally
        {
            IsBusy = false;
            NextCommand.NotifyCanExecuteChanged();
        }
    }
}
