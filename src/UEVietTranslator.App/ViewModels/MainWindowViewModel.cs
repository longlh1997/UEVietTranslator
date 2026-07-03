using CommunityToolkit.Mvvm.ComponentModel;

namespace UEVietTranslator.App.ViewModels;

// Placeholder Pha 0 — sẽ được thay bằng ViewModel điều phối wizard nhiều
// bước ở Pha 6. Giữ tối giản để verify DI + build pipeline hoạt động trước.
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage =
        "Scaffold Pha 0. Core chưa build/verify thực tế — xem CLAUDE.md §4.";
}
