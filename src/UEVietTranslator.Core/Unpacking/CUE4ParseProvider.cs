using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.Unpacking;

/// <summary>
/// Cài đặt <see cref="IUnpackProvider"/> dùng CUE4Parse trực tiếp (không qua
/// FModel). Pha 1: mount pak/IoStore và liệt kê virtual path — KHÔNG giải mã
/// AES đa key (đó là Pha 2, xem <c>AesKeyResolver</c>) và KHÔNG ghi asset ra
/// đĩa (xem lý do trong <see cref="UnpackedAssetRef"/>).
/// </summary>
public sealed class CUE4ParseProvider : IUnpackProvider
{
    public async Task<Result<IReadOnlyList<UnpackedAssetRef>>> UnpackAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        string outputDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Chỉ truyền VersionContainer cụ thể khi đoán được EGame từ
        // GameProfile.EngineVersion — nếu không đoán được (game mới CUE4Parse
        // chưa có mapping riêng), để null cho CUE4Parse tự dùng default nội
        // bộ của nó, tốt hơn là áp 1 EGame sai gây lỗi parse asset khó hiểu.
        var versions = TryResolveEGame(gameProfile.EngineVersion, out var game)
            ? new VersionContainer(game)
            : null;

        using var provider = new DefaultFileProvider(
            gameProfile.GameDirectory,
            SearchOption.AllDirectories,
            versions,
            StringComparer.OrdinalIgnoreCase);

        progress?.Report(new ProgressInfo(-1, "Đang quét thư mục game tìm pak/IoStore..."));
        try
        {
            provider.Initialize();
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<UnpackedAssetRef>>.Failure(
                $"Quét thư mục game thất bại: {ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Pha 1 = "unpack thử không key" theo docs/ROADMAP.md. Nếu người gọi
        // đã có sẵn 1 key (VD: đã được AesKeyResolver ở Pha 2 xác nhận, hoặc
        // người dùng tự nhập tay để thử), submit key đó cho MỌI guid đang
        // được provider.RequiredKeys báo là cần — provider.Initialize() ở
        // trên đã đọc xong header của từng archive nên đã biết chính xác
        // guid nào cần key, KHÔNG cần đoán guid rỗng (đã sửa lỗi giả định sai
        // ở bản trước — xem docs/DECISIONS.md#adr-006). Đây vẫn là single-key
        // (1 giá trị hex áp cho mọi guid); multi-key thật sự (nhiều giá trị
        // hex khác nhau cho các guid khác nhau) là việc của AesKeyResolver.
        if (!string.IsNullOrWhiteSpace(aesKeyHex))
        {
            try
            {
                var aesKey = new FAesKey(aesKeyHex);
                await provider.SubmitKeysAsync(
                    provider.RequiredKeys.Select(guid => new KeyValuePair<FGuid, FAesKey>(guid, aesKey))
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Result<IReadOnlyList<UnpackedAssetRef>>.Failure(
                    $"AES key không hợp lệ: {ex.Message}");
            }
        }

        progress?.Report(new ProgressInfo(-1, "Đang mount pak/IoStore..."));

        int mountedCount;
        try
        {
            // CUE4Parse không nhận CancellationToken cho Mount() — đây là
            // giới hạn của thư viện, không huỷ giữa chừng được ở bước này.
            mountedCount = await provider.MountAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<UnpackedAssetRef>>.Failure(
                $"Mount pak/IoStore thất bại: {ex.Message}");
        }

        if (mountedCount == 0 && provider.Files.Count == 0)
        {
            var requiredKeyCount = provider.RequiredKeys.Count;
            return Result<IReadOnlyList<UnpackedAssetRef>>.Failure(
                requiredKeyCount > 0
                    ? $"Không mount được archive nào — {requiredKeyCount} archive yêu cầu AES key mà chưa có/chưa đúng. Xem docs/ROADMAP.md Pha 2."
                    : "Không mount được archive nào, và cũng không có archive nào báo cần key — kiểm tra lại thư mục Content/Paks/ hoặc file pak/IoStore có bị hỏng không.");
        }

        var total = provider.Files.Count;
        var assets = new List<UnpackedAssetRef>(total);
        var done = 0;

        foreach (var file in provider.Files.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Pha 1: chỉ liệt kê virtual path, chưa ghi bytes ra đĩa — xem
            // comment trong UnpackedAssetRef.
            assets.Add(new UnpackedAssetRef(file.Path, ExtractedFilePath: null));

            done++;
            if (done % 500 == 0 || done == total)
                progress?.Report(new ProgressInfo(
                    total == 0 ? 100 : done * 100.0 / total,
                    $"Đã liệt kê {done}/{total} asset"));
        }

        return Result<IReadOnlyList<UnpackedAssetRef>>.Success(assets);
    }

    public Task<Result<IReadOnlyList<PackageExportSummary>>> InspectPackagesAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<string> virtualPaths,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "InspectPackagesAsync chưa implement — xem docs/ROADMAP.md Pha 3 và docs/DECISIONS.md#adr-005.");

    // UE dùng tên enum dạng "GAME_UE{major}_{minor}" (VD: GAME_UE5_3) hoặc
    // "GAME_UE{major}_LATEST" khi CUE4Parse chưa có mapping riêng cho đúng
    // minor version đó. GameProfile.EngineVersion hiện chỉ có dạng
    // "{major}.{minor}" (xem GameProfileDetector.DetectEngineVersionAsync),
    // không phân biệt được game nào cần EGame riêng (VD: EGame.GAME_Valorant)
    // — đây là hạn chế đã biết, chấp nhận được vì mục tiêu Pha 1 chỉ là mount
    // thử, không phải xử lý đúng 100% mọi game đặc thù.
    private static bool TryResolveEGame(string? engineVersion, out EGame game)
    {
        game = default;

        if (string.IsNullOrWhiteSpace(engineVersion))
            return false;

        var parts = engineVersion.Split('.');
        if (parts.Length < 2
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor))
            return false;

        if (Enum.TryParse($"GAME_UE{major}_{minor}", out EGame exact))
        {
            game = exact;
            return true;
        }

        if (Enum.TryParse($"GAME_UE{major}_LATEST", out EGame latest))
        {
            game = latest;
            return true;
        }

        return false;
    }
}
