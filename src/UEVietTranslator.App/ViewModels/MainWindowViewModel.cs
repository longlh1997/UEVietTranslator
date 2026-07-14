using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.CsvSchema;
using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.LocalizationDiscovery;
using UEVietTranslator.Core.Repacking;
using UEVietTranslator.Core.Translation;
using UEVietTranslator.Core.Unpacking;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// Wizard Pha 6 bao trọn toàn bộ pipeline (xem CLAUDE.md §1):
/// Setup/Scan → ConfirmFiles → ExtractAndExportCsv → Translate → Review →
/// Repack. Vẫn là 1 ViewModel/1 DataContext duy nhất (không tách nhiều
/// ViewModel riêng — xem docs/DECISIONS.md ADR mới nhất) nhưng được tách
/// thành nhiều file `partial class` theo tên bước để dễ review:
/// <c>MainWindowViewModel.Setup.cs</c>, <c>.ConfirmFiles.cs</c>,
/// <c>.ExtractCsv.cs</c>, <c>.Translate.cs</c>, <c>.Review.cs</c>,
/// <c>.Repack.cs</c>. File này chỉ giữ field DI, điều hướng bước
/// (<see cref="CurrentStep"/>/<see cref="NextCommand"/>/<see cref="BackCommand"/>),
/// và state cần dùng chung giữa nhiều bước.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IGameProfileDetector _detector;
    private readonly IUnpackProvider _primaryUnpackProvider;
    private readonly IUnpackProvider _fallbackUnpackProvider;
    private readonly IGameProfileStore _profileStore;
    private readonly ILocalizationDiscoveryService _discoveryService;
    private readonly IAssetReaderWriter _assetReaderWriter;
    private readonly ICsvSchemaConverter _csvSchemaConverter;
    private readonly IGeminiSettingsStore _geminiSettingsStore;
    private readonly ITranslationService _translationService;
    private readonly IRepackService _repackService;

    // Profile của lần Quét gần nhất — mọi bước sau dùng lại, KHÔNG suy ra lại
    // từ GameDirectory (người dùng có thể đã gõ sửa ô đường dẫn sau khi Quét).
    private GameProfile? _lastDetectedProfile;

    // Provider đã dùng ở bước Setup (primary hoặc fmodel-fallback) — bước
    // ExtractCsv phải dùng ĐÚNG provider này, không phải luôn luôn primary.
    private IUnpackProvider? _lastUsedUnpackProvider;

    // AES key đã dùng thành công ở bước Setup (gõ tay hoặc lấy từ profile đã
    // lưu) — bước ExtractCsv cần key này để mount lại đúng 1 lần nữa khi gọi
    // ExtractFilesAsync (null nếu game không mã hoá hoặc dùng fallback).
    private string? _lastAesKeyHex;

    // VirtualPath -> (đường dẫn file thật đã extract, kind) — ghi ở bước
    // ExtractCsv, đọc lại ở bước Repack để biết ghi bản dịch vào file nào
    // (IAssetReaderWriter.WriteAsync cần đường dẫn thật, CsvRow.SourceFile
    // chỉ lưu virtual path).
    private readonly Dictionary<string, (string ExtractedFilePath, LocalizationFileKind Kind)> _extractedFileLookup = new();

    // Thư mục đã extract file ra ở bước ExtractAndExportCsv — bước Repack
    // dùng lại CHÍNH thư mục này làm modifiedAssetsDirectory (IAssetReaderWriter.WriteAsync
    // ghi đè trực tiếp lên file trong thư mục này, xem comment trong
    // IAssetReaderWriter.WriteAsync).
    private string? _lastExtractDirectory;

    public enum WizardStep
    {
        Setup,
        ConfirmFiles,
        ExtractAndExportCsv,
        Translate,
        Review,
        Repack,
    }

    [ObservableProperty]
    private WizardStep _currentStep = WizardStep.Setup;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _isBusy;

    public bool IsSetupStep => CurrentStep == WizardStep.Setup;
    public bool IsConfirmFilesStep => CurrentStep == WizardStep.ConfirmFiles;
    public bool IsExtractAndExportCsvStep => CurrentStep == WizardStep.ExtractAndExportCsv;
    public bool IsTranslateStep => CurrentStep == WizardStep.Translate;
    public bool IsReviewStep => CurrentStep == WizardStep.Review;
    public bool IsRepackStep => CurrentStep == WizardStep.Repack;

    public int StepNumber => (int)CurrentStep + 1;
    public int TotalSteps => Enum.GetValues<WizardStep>().Length;

    public string StepTitle => CurrentStep switch
    {
        WizardStep.Setup => "Thiết lập & Quét",
        WizardStep.ConfirmFiles => "Xác nhận file ngôn ngữ",
        WizardStep.ExtractAndExportCsv => "Extract & Xuất CSV",
        WizardStep.Translate => "Dịch tự động",
        WizardStep.Review => "Review bản dịch",
        WizardStep.Repack => "Ghi asset & Repack",
        _ => string.Empty,
    };

    partial void OnCurrentStepChanged(WizardStep value)
    {
        OnPropertyChanged(nameof(IsSetupStep));
        OnPropertyChanged(nameof(IsConfirmFilesStep));
        OnPropertyChanged(nameof(IsExtractAndExportCsvStep));
        OnPropertyChanged(nameof(IsTranslateStep));
        OnPropertyChanged(nameof(IsReviewStep));
        OnPropertyChanged(nameof(IsRepackStep));
        OnPropertyChanged(nameof(StepNumber));
        OnPropertyChanged(nameof(StepTitle));
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    public MainWindowViewModel(
        IGameProfileDetector detector,
        [FromKeyedServices("primary")] IUnpackProvider primaryUnpackProvider,
        [FromKeyedServices("fmodel-fallback")] IUnpackProvider fallbackUnpackProvider,
        IGameProfileStore profileStore,
        ILocalizationDiscoveryService discoveryService,
        IAssetReaderWriter assetReaderWriter,
        ICsvSchemaConverter csvSchemaConverter,
        IGeminiSettingsStore geminiSettingsStore,
        ITranslationService translationService,
        IRepackService repackService)
    {
        _detector = detector;
        _primaryUnpackProvider = primaryUnpackProvider;
        _fallbackUnpackProvider = fallbackUnpackProvider;
        _profileStore = profileStore;
        _discoveryService = discoveryService;
        _assetReaderWriter = assetReaderWriter;
        _csvSchemaConverter = csvSchemaConverter;
        _geminiSettingsStore = geminiSettingsStore;
        _translationService = translationService;
        _repackService = repackService;
    }

    // Next chỉ tiến được khi bước hiện tại đã có kết quả hợp lệ — mỗi bước
    // định nghĩa điều kiện riêng trong file partial tương ứng
    // (CanScan/CanConfirm/CanExtractAndExportCsv/...), gộp lại ở đây.
    private bool CanGoNext() => !IsBusy && CurrentStep switch
    {
        WizardStep.Setup => _lastDetectedProfile is not null && Candidates.Count > 0,
        WizardStep.ConfirmFiles => Candidates.Any(c => c.IsConfirmed),
        WizardStep.ExtractAndExportCsv => _extractedFileLookup.Count > 0,
        WizardStep.Translate => CsvRows.Count > 0,
        WizardStep.Review => CsvRows.Count > 0,
        WizardStep.Repack => false, // bước cuối, không có Next
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (CurrentStep < WizardStep.Repack)
            CurrentStep++;
    }

    private bool CanGoBack() => !IsBusy && CurrentStep > WizardStep.Setup;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentStep > WizardStep.Setup)
            CurrentStep--;
    }
}
