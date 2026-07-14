using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.Unpacking;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Bước 1 (Setup): nhập đường dẫn game -> Quét (detect + unpack + scan file
/// ngôn ngữ bằng <see cref="Core.LocalizationDiscovery.ILocalizationDiscoveryService"/>).
/// Pha 5 thêm chế độ FModel fallback (<see cref="UseFModelFallback"/>) — khi
/// CUE4Parse fail (ADR-002), người dùng tự export bằng FModel rồi trỏ vào
/// đây. Xem docs/DECISIONS.md#adr-013: ở chế độ này, ô "Thư mục game" thực
/// chất là thư mục FModel đã export, KHÔNG chạy qua
/// <see cref="IGameProfileDetector"/>.
/// </summary>
public partial class MainWindowViewModel
{
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

    private bool CanScan() =>
        !IsBusy && !string.IsNullOrWhiteSpace(GameDirectory) &&
        (!UseFModelFallback || !string.IsNullOrWhiteSpace(FallbackEngineVersion));

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Candidates.Clear();
        _lastDetectedProfile = null;
        _lastUsedUnpackProvider = null;

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
            _lastUsedUnpackProvider = unpackProvider;
            _lastAesKeyHex = aesKeyHex;
            StatusMessage = Candidates.Count > 0
                ? $"Tìm được {Candidates.Count} candidate. Bấm Tiếp tục để sang bước xác nhận."
                : "Không tìm được candidate nào.";
        }
        finally
        {
            IsBusy = false;
            NextCommand.NotifyCanExecuteChanged();
        }
    }
}
