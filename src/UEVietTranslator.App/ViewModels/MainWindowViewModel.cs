using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.LocalizationDiscovery;
using UEVietTranslator.Core.Unpacking;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Màn hình đơn Pha 3: nhập đường dẫn game -> Quét (detect + unpack + scan
/// file ngôn ngữ bằng <see cref="ILocalizationDiscoveryService"/>) -> tick
/// chọn/sửa loại file -> Lưu (persist qua
/// <see cref="IGameProfileStore.SaveConfirmedLocalizationFilesAsync"/>). Đây
/// là nền tảng sẽ được tách thành 1 bước trong wizard nhiều bước ở Pha 6, xem
/// docs/ROADMAP.md.
///
/// Pha 5 thêm chế độ FModel fallback (<see cref="UseFModelFallback"/>) — khi
/// CUE4Parse fail (ADR-002), người dùng tự export bằng FModel rồi trỏ vào
/// đây. Xem docs/DECISIONS.md#adr-013: ở chế độ này, ô "Thư mục game" thực
/// chất là thư mục FModel đã export ra, KHÔNG chạy qua
/// <see cref="IGameProfileDetector"/> (sẽ fail vì không có .exe/pak để quét).
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IGameProfileDetector _detector;
    private readonly IUnpackProvider _primaryUnpackProvider;
    private readonly IUnpackProvider _fallbackUnpackProvider;
    private readonly IGameProfileStore _profileStore;
    private readonly ILocalizationDiscoveryService _discoveryService;

    // Profile của lần Quét gần nhất — Lưu dùng lại để biết ghi vào file
    // profile nào, KHÔNG suy ra lại từ GameDirectory lúc bấm Lưu (người dùng
    // có thể đã gõ sửa ô đường dẫn sau khi Quét xong).
    private GameProfile? _lastDetectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _gameDirectory = string.Empty;

    [ObservableProperty]
    private string? _aesKeyHex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _useFModelFallback;

    // Chỉ cần khi UseFModelFallback — thư mục export không có .exe để tự
    // detect UE version, người dùng gõ tay (FModel hiển thị version lúc
    // export). Xem docs/DECISIONS.md#adr-013.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string? _fallbackEngineVersion;

    [ObservableProperty]
    private string _statusMessage = "Nhập đường dẫn thư mục cài game rồi bấm Quét.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    public ObservableCollection<LocalizationCandidateItemViewModel> Candidates { get; } = new();

    public MainWindowViewModel(
        IGameProfileDetector detector,
        [FromKeyedServices("primary")] IUnpackProvider primaryUnpackProvider,
        [FromKeyedServices("fmodel-fallback")] IUnpackProvider fallbackUnpackProvider,
        IGameProfileStore profileStore,
        ILocalizationDiscoveryService discoveryService)
    {
        _detector = detector;
        _primaryUnpackProvider = primaryUnpackProvider;
        _fallbackUnpackProvider = fallbackUnpackProvider;
        _profileStore = profileStore;
        _discoveryService = discoveryService;
    }

    private bool CanScan() =>
        !IsBusy && !string.IsNullOrWhiteSpace(GameDirectory) &&
        (!UseFModelFallback || !string.IsNullOrWhiteSpace(FallbackEngineVersion));

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Candidates.Clear();
        _lastDetectedProfile = null;

        try
        {
            var unpackProvider = UseFModelFallback ? _fallbackUnpackProvider : _primaryUnpackProvider;
            string? aesKeyHex = null;
            GameProfile profile;

            if (UseFModelFallback)
            {
                // Không detect thật — GameDirectory ở đây là thư mục FModel
                // đã export, không phải thư mục cài game (ADR-013).
                profile = new GameProfile(
                    GameDirectory: GameDirectory,
                    ExecutablePath: string.Empty,
                    ExecutableHash: string.Empty,
                    EngineVersion: FallbackEngineVersion,
                    PakFormat: PakFormat.Unknown,
                    PaksDirectory: string.Empty);

                // Lưu ngay để SaveAsync (bước xác nhận file ngôn ngữ) dùng
                // được sau này — luồng fallback không có bước resolve-key
                // nên không AES key nào để lưu, xem CLI discover-fallback.
                var saveProfileResult = await _profileStore.SaveAsync(profile, validatedAesKeys: [], cancellationToken);
                if (!saveProfileResult.IsSuccess)
                {
                    StatusMessage = $"Lưu profile fallback thất bại: {saveProfileResult.Error}";
                    return;
                }
            }
            else
            {
                StatusMessage = "Đang detect game...";
                var detectResult = await _detector.DetectAsync(GameDirectory, cancellationToken);
                if (!detectResult.IsSuccess)
                {
                    StatusMessage = $"Detect thất bại: {detectResult.Error}";
                    return;
                }

                profile = detectResult.Value!;

                // Chưa gõ key thủ công thì thử lấy key đã lưu từ lần
                // resolve-key trước (giống CLI `discover` — xem
                // docs/DECISIONS.md#adr-007/009).
                aesKeyHex = AesKeyHex;
                if (string.IsNullOrWhiteSpace(aesKeyHex))
                {
                    var loadResult = await _profileStore.LoadAsync(GameDirectory, cancellationToken);
                    if (loadResult.IsSuccess && loadResult.Value!.ValidatedAesKeys.Count > 0)
                        aesKeyHex = loadResult.Value.ValidatedAesKeys[0];
                }
            }

            var progress = new Progress<ProgressInfo>(p => StatusMessage = p.Message);

            StatusMessage = UseFModelFallback ? "Đang liệt kê thư mục export..." : "Đang unpack...";
            var outputDirectory = Path.Combine(Path.GetTempPath(), "uevt-unpack");
            var unpackResult = await unpackProvider.UnpackAsync(
                profile, aesKeyHex, outputDirectory, progress, cancellationToken);
            if (!unpackResult.IsSuccess)
            {
                StatusMessage = $"Unpack thất bại: {unpackResult.Error}";
                return;
            }

            StatusMessage = "Đang quét file ngôn ngữ...";
            var scanResult = await _discoveryService.ScanAsync(
                unpackProvider, profile, aesKeyHex, unpackResult.Value!, progress, cancellationToken);
            if (!scanResult.IsSuccess)
            {
                StatusMessage = $"Quét thất bại: {scanResult.Error}";
                return;
            }

            foreach (var candidate in scanResult.Value!.OrderByDescending(c => c.Confidence))
                Candidates.Add(new LocalizationCandidateItemViewModel(candidate));

            _lastDetectedProfile = profile;
            StatusMessage = Candidates.Count > 0
                ? $"Tìm được {Candidates.Count} candidate. Tick chọn file đúng rồi bấm Lưu."
                : "Không tìm được candidate nào.";
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSave() => !IsBusy && _lastDetectedProfile is not null && Candidates.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
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
                ? $"Đã lưu {confirmed.Count} file ngôn ngữ đã xác nhận vào profile."
                : $"Lưu thất bại: {result.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
