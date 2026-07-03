namespace UEVietTranslator.Core.GameProfile;

/// <summary>
/// Định dạng đóng gói asset của game. Một game có thể dùng cả hai cùng lúc
/// — xem docs/DOMAIN_KNOWLEDGE.md §1. <c>Both</c> nghĩa là cần xử lý cả hai
/// khi unpack, không phải chọn một.
/// </summary>
public enum PakFormat
{
    Unknown,
    LegacyPak,   // .pak
    IoStore,     // .utoc + .ucas
    Both,
}

/// <summary>
/// Thông tin nhận diện 1 bản cài game UE cụ thể, dùng xuyên suốt pipeline
/// (unpack, tìm AES key, phát hiện file ngôn ngữ...). Được lưu lại giữa các
/// lần chạy app dưới dạng file cấu hình riêng cho từng game — xem
/// <c>*.gameprofile.json</c> trong .gitignore (không commit vì có thể chứa
/// AES key của game bản quyền).
/// </summary>
/// <param name="GameDirectory">Thư mục gốc chứa game đã cài (chứa .exe).</param>
/// <param name="ExecutablePath">Đường dẫn file .exe chính, dùng để tìm AES key và tính hash phát hiện update.</param>
/// <param name="ExecutableHash">SHA-256 của .exe tại thời điểm detect — dùng để biết khi nào cần re-scan AES key sau khi game update. Xem docs/DOMAIN_KNOWLEDGE.md §2.</param>
/// <param name="EngineVersion">Version UE phát hiện được (VD: "5.3"), null nếu chưa xác định được.</param>
/// <param name="PakFormat">Định dạng đóng gói asset phát hiện được trong Content/Paks/.</param>
/// <param name="PaksDirectory">Đường dẫn tới thư mục Content/Paks/ (hoặc tương đương).</param>
public sealed record GameProfile(
    string GameDirectory,
    string ExecutablePath,
    string ExecutableHash,
    string? EngineVersion,
    PakFormat PakFormat,
    string PaksDirectory);
