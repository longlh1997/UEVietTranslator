using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.LocalizationDiscovery;

namespace UEVietTranslator.Core.AssetIO;

/// <summary>
/// Cài đặt <see cref="IAssetReaderWriter"/>: `.locres` qua
/// <see cref="LocresBinaryFormat"/> (đọc bằng CUE4Parse, ghi tự viết — xem
/// docs/DECISIONS.md#adr-010 về rủi ro), StringTable trong `.uasset` qua
/// UAssetAPI (quyết định đã chốt ở CLAUDE.md §3, thư viện có sẵn cả read/write
/// nên rủi ro thấp hơn nhiều so với `.locres`).
/// </summary>
public sealed class AssetReaderWriter : IAssetReaderWriter
{
    public Task<Result<IReadOnlyList<TextEntry>>> ReadAsync(
        string filePath, LocalizationFileKind kind, string? engineVersionHint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return kind switch
        {
            LocalizationFileKind.Locres => Task.FromResult(ReadLocres(filePath)),
            LocalizationFileKind.StringTableAsset => Task.FromResult(ReadStringTable(filePath, engineVersionHint)),
            _ => Task.FromResult(Result<IReadOnlyList<TextEntry>>.Failure(
                $"AssetReaderWriter không hỗ trợ đọc loại file '{kind}'.")),
        };
    }

    public Task<Result> WriteAsync(
        string filePath, LocalizationFileKind kind, string? engineVersionHint,
        IReadOnlyList<TextEntry> translatedEntries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return kind switch
        {
            LocalizationFileKind.Locres => Task.FromResult(WriteLocres(filePath, translatedEntries)),
            LocalizationFileKind.StringTableAsset => Task.FromResult(WriteStringTable(filePath, engineVersionHint, translatedEntries)),
            _ => Task.FromResult(Result.Failure(
                $"AssetReaderWriter không hỗ trợ ghi loại file '{kind}'.")),
        };
    }

    private static Result<IReadOnlyList<TextEntry>> ReadLocres(string filePath)
    {
        try
        {
            var entries = LocresBinaryFormat.Read(filePath)
                .Select(e => new TextEntry(e.Namespace, e.Key, e.LocalizedString))
                .ToList();
            return Result<IReadOnlyList<TextEntry>>.Success(entries);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<TextEntry>>.Failure($"Đọc file .locres thất bại: {filePath} — {ex.Message}");
        }
    }

    private static Result WriteLocres(string filePath, IReadOnlyList<TextEntry> translatedEntries)
    {
        try
        {
            var entries = translatedEntries
                .Select(e => (e.Namespace, e.Key, LocalizedString: e.SourceText))
                .ToList();
            LocresBinaryFormat.Write(filePath, entries);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Ghi file .locres thất bại: {filePath} — {ex.Message}");
        }
    }

    private static Result<IReadOnlyList<TextEntry>> ReadStringTable(string filePath, string? engineVersionHint)
    {
        var versionResult = ResolveEngineVersion(engineVersionHint);
        if (!versionResult.IsSuccess)
            return Result<IReadOnlyList<TextEntry>>.Failure(versionResult.Error!);

        try
        {
            var asset = new UAsset(filePath, versionResult.Value);
            var stringTableExport = asset.Exports.OfType<StringTableExport>().FirstOrDefault();
            if (stringTableExport is null)
                return Result<IReadOnlyList<TextEntry>>.Failure(
                    $"Không tìm thấy export StringTable nào trong file: {filePath}");

            // FStringTable kế thừa TMap<FString, FString> — Namespace của
            // TextEntry để rỗng vì StringTable (khác .locres) không có khái
            // niệm namespace, chỉ có 1 bảng Key -> Text cho cả file.
            var entries = stringTableExport.Table
                .Select(kv => new TextEntry(string.Empty, kv.Key.Value ?? string.Empty, kv.Value.Value ?? string.Empty))
                .ToList();
            return Result<IReadOnlyList<TextEntry>>.Success(entries);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<TextEntry>>.Failure($"Đọc StringTable thất bại: {filePath} — {ex.Message}");
        }
    }

    private static Result WriteStringTable(string filePath, string? engineVersionHint, IReadOnlyList<TextEntry> translatedEntries)
    {
        var versionResult = ResolveEngineVersion(engineVersionHint);
        if (!versionResult.IsSuccess)
            return Result.Failure(versionResult.Error!);

        try
        {
            var asset = new UAsset(filePath, versionResult.Value);
            var stringTableExport = asset.Exports.OfType<StringTableExport>().FirstOrDefault();
            if (stringTableExport is null)
                return Result.Failure($"Không tìm thấy export StringTable nào trong file: {filePath}");

            foreach (var entry in translatedEntries)
            {
                var key = (FString)entry.Key;
                // ContainsKey trước khi ghi đè để tránh vô tình THÊM key mới
                // không có trong asset gốc — đúng yêu cầu interface: entry
                // không khớp Key gốc thì bỏ qua, không fail cả file.
                if (!stringTableExport.Table.ContainsKey(key))
                    continue;

                stringTableExport.Table[key] = (FString)entry.SourceText;
            }

            asset.Write(filePath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Ghi StringTable thất bại: {filePath} — {ex.Message}");
        }
    }

    // UAssetAPI dùng tên enum "VER_UE{major}_{minor}" (VD: VER_UE5_3), KHÔNG
    // có alias "LATEST" như EGame của CUE4Parse (xem TryResolveEGame trong
    // CUE4ParseProvider) — nếu không khớp chính xác, FAIL rõ ràng thay vì
    // đoán 1 version gần đúng: sai EngineVersion khi GHI có thể làm hỏng
    // layout binary của asset (khác với đọc, chỉ risk là parse lỗi warning).
    private static Result<EngineVersion> ResolveEngineVersion(string? engineVersionHint)
    {
        if (string.IsNullOrWhiteSpace(engineVersionHint))
            return Result<EngineVersion>.Failure(
                "Cần biết UE engine version (VD \"5.3\") để đọc/ghi StringTable qua UAssetAPI — GameProfile.EngineVersion đang trống.");

        var parts = engineVersionHint.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return Result<EngineVersion>.Failure($"Không parse được engine version: '{engineVersionHint}'.");

        if (Enum.TryParse($"VER_UE{major}_{minor}", out EngineVersion version))
            return Result<EngineVersion>.Success(version);

        return Result<EngineVersion>.Failure(
            $"UAssetAPI không có mapping cho UE {major}.{minor} (tên enum kỳ vọng: VER_UE{major}_{minor}) — kiểm tra lại version UAssetAPI đang dùng có hỗ trợ version này chưa.");
    }
}
