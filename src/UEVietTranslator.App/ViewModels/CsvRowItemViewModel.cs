using CommunityToolkit.Mvvm.ComponentModel;
using UEVietTranslator.Core.CsvSchema;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// 1 dòng CSV hiển thị ở bước Review — cho sửa <see cref="TranslatedText"/>
/// và <see cref="Status"/> ngay trong app (quyết định UX đã chốt với Hải
/// Long: KHÔNG bắt buộc sửa ngoài Excel), các field còn lại (SourceFile,
/// Namespace, Key, SourceText, Context) chỉ đọc — đây là dữ liệu gốc từ
/// asset, sửa nhầm sẽ làm lệch khoá khi ghi lại (xem
/// <see cref="Core.AssetIO.IAssetReaderWriter.WriteAsync"/>).
/// </summary>
public partial class CsvRowItemViewModel : ObservableObject
{
    public string SourceFile { get; }
    public string Namespace { get; }
    public string Key { get; }
    public string SourceText { get; }
    public string? Context { get; }

    [ObservableProperty]
    private string _translatedText;

    [ObservableProperty]
    private TranslationStatus _status;

    public CsvRowItemViewModel(CsvRow row)
    {
        SourceFile = row.SourceFile;
        Namespace = row.Namespace;
        Key = row.Key;
        SourceText = row.SourceText;
        Context = row.Context;
        _translatedText = row.TranslatedText;
        _status = row.Status;
    }

    public CsvRow ToCsvRow() => new(SourceFile, Namespace, Key, SourceText, Context, TranslatedText, Status);

    // Instance property (không static) để ComboBox trong DataTemplate bind
    // ItemsSource được qua Binding thường — cùng lý do với AllKinds trong
    // LocalizationCandidateItemViewModel.
    public TranslationStatus[] AllStatuses { get; } = Enum.GetValues<TranslationStatus>();
}
