using CommunityToolkit.Mvvm.ComponentModel;
using UEVietTranslator.Core.LocalizationDiscovery;

namespace UEVietTranslator.App.ViewModels;

/// <summary>
/// 1 dòng trong danh sách candidate hiển thị cho người dùng tick chọn.
/// <see cref="Kind"/> để dạng có thể sửa (ComboBox) vì heuristic của
/// <see cref="ILocalizationDiscoveryService"/> chỉ là gợi ý — người dùng có
/// thể biết rõ hơn (VD: 1 DataTable cụ thể thực ra không phải text dịch).
/// </summary>
public partial class LocalizationCandidateItemViewModel : ObservableObject
{
    public string Path { get; }
    public double Confidence { get; }

    [ObservableProperty]
    private LocalizationFileKind _kind;

    [ObservableProperty]
    private bool _isConfirmed;

    public LocalizationCandidateItemViewModel(LocalizationFileCandidate candidate)
    {
        Path = candidate.Path;
        Confidence = candidate.Confidence;
        Kind = candidate.Kind;
    }

    // Instance property (không phải static) để ComboBox trong DataTemplate
    // (DataContext là chính item này) bind ItemsSource được qua Binding
    // thường — XAML không dùng x:Static ở đây.
    public LocalizationFileKind[] AllKinds { get; } = Enum.GetValues<LocalizationFileKind>();
}
