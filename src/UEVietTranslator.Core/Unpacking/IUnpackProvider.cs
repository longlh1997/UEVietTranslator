using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;

namespace UEVietTranslator.Core.Unpacking;

/// <summary>
/// 1 asset được liệt kê từ pak/IoStore sau khi mount.
/// </summary>
/// <param name="VirtualPath">Đường dẫn ảo của asset bên trong pak/IoStore (VD:
/// "GameProject/Content/Localization/Game/vi/Game.locres"), dùng để gọi lại
/// <see cref="IUnpackProvider"/> đọc bytes thật khi cần.</param>
/// <param name="ExtractedFilePath">
/// Đường dẫn file thật trên đĩa NẾU asset đã được ghi ra
/// (null nếu chưa). Ở Pha 1, <see cref="CUE4ParseProvider"/> CHỈ liệt kê
/// virtual path, KHÔNG ghi bytes ra đĩa cho mọi asset — 1 game UE5 hiện đại
/// có thể có hàng chục-hàng trăm GB asset trong khi file ngôn ngữ chỉ chiếm
/// phần rất nhỏ, ghi hết ra đĩa ngay từ bước unpack là lãng phí thời gian +
/// dung lượng. Việc đọc bytes thật của 1 asset cụ thể (sau khi
/// LocalizationDiscovery hoặc người dùng đã chọn) sẽ dùng 1 cơ chế đọc theo
/// yêu cầu (on-demand) được thiết kế ở Pha 3/4 — xem docs/ROADMAP.md.
/// </param>
public sealed record UnpackedAssetRef(string VirtualPath, string? ExtractedFilePath);

/// <summary>
/// Thông tin export NHẸ của 1 package (<c>.uasset</c>/<c>.umap</c>) — chỉ
/// tên class của từng export (VD: "StringTable", "Texture2D", "DataTable"),
/// KHÔNG chứa dữ liệu thật (không có pixel, vertex, text...). Dùng để
/// <c>LocalizationDiscoveryService</c> (Pha 3) tự quyết định file nào đáng
/// nghi là StringTable mà không cần <see cref="IUnpackProvider"/> đọc/ghi
/// toàn bộ nội dung asset ra đĩa. Xem docs/DECISIONS.md#adr-005.
/// </summary>
/// <param name="VirtualPath">Đường dẫn ảo của package đã soi.</param>
/// <param name="ExportClassNames">
/// Tên class của từng export trong package. Rỗng nếu package không đọc được
/// (lỗi parse, version không khớp, thiếu mapping...) — đây KHÔNG phải lỗi
/// nghiêm trọng, chỉ đơn giản là package đó không xác định được, người gọi
/// tự quyết định bỏ qua hay báo cho người dùng.
/// </param>
public sealed record PackageExportSummary(string VirtualPath, IReadOnlyList<string> ExportClassNames);

/// <summary>
/// Trừu tượng hoá nguồn asset đã unpack — có 2 cài đặt:
/// <see cref="CUE4ParseProvider"/> (luồng chính, tự động) và
/// <see cref="FModelFallbackAdapter"/> (đọc thư mục người dùng đã tự export
/// bằng FModel — xem docs/DECISIONS.md#adr-002, đây là thao tác THỦ CÔNG,
/// KHÔNG tự động hoá việc gọi FModel).
/// </summary>
public interface IUnpackProvider
{
    /// <param name="outputDirectory">
    /// Thư mục dự phòng cho asset được ghi ra đĩa. Pha 1 chưa dùng tham số
    /// này (xem <see cref="UnpackedAssetRef"/>) — giữ lại trong signature vì
    /// bước extract-theo-yêu-cầu ở Pha 3/4 sẽ cần đến.
    /// </param>
    Task<Result<IReadOnlyList<UnpackedAssetRef>>> UnpackAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        string outputDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mount pak/IoStore ĐÚNG 1 LẦN cho cả lô <paramref name="virtualPaths"/>,
    /// rồi đọc export table (KHÔNG đọc toàn bộ nội dung/pixel/vertex) của
    /// từng package trong danh sách đó — dùng bởi <c>LocalizationDiscoveryService</c>
    /// (Pha 3) để quét hàng loạt <c>.uasset</c> tìm StringTable mà không phải
    /// remount lại pak/IoStore cho từng file (chi phí mount có thể vài giây
    /// đến vài chục giây, remount theo từng file trong hàng nghìn file sẽ
    /// thành hàng giờ — xem docs/DECISIONS.md#adr-005). KHÔNG ghi gì ra đĩa.
    /// </summary>
    Task<Result<IReadOnlyList<PackageExportSummary>>> InspectPackagesAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<string> virtualPaths,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mount pak/IoStore ĐÚNG 1 LẦN, rồi ghi bytes thật ra đĩa CHỈ cho các
    /// file trong <paramref name="virtualPaths"/> (khác <see cref="UnpackAsync"/>
    /// — không ghi toàn bộ asset của game). Đây là cơ chế "đọc theo yêu cầu"
    /// đã được dự trù từ Pha 1/3 (xem comment cũ trong <see cref="UnpackedAssetRef"/>),
    /// implement thật ở Pha 4 để <c>AssetIO</c> có file thật trên đĩa mà đọc
    /// qua UAssetAPI/parse thủ công — xem docs/DECISIONS.md#adr-010.
    /// </summary>
    /// <param name="virtualPaths">
    /// Danh sách file người dùng đã xác nhận (VD: từ
    /// <c>ConfirmedLocalizationFile</c>) — thường chỉ vài file trong số hàng
    /// chục nghìn asset của game.
    /// </param>
    /// <param name="outputDirectory">Thư mục gốc ghi file ra — cấu trúc thư mục con giữ nguyên theo virtual path để tránh trùng tên giữa các file khác thư mục.</param>
    Task<Result<IReadOnlyList<UnpackedAssetRef>>> ExtractFilesAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<string> virtualPaths,
        string outputDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Đọc thư mục người dùng đã TỰ export thủ công bằng FModel (ADR-002) —
/// KHÔNG mount pak/IoStore, KHÔNG cần AES key (FModel đã giải mã lúc export).
/// Xem docs/DECISIONS.md#adr-013 cho quyết định thiết kế quan trọng nhất:
/// <see cref="GameProfile.GameProfile.GameDirectory"/> được TÁI DIỄN GIẢI
/// thành "thư mục gốc FModel đã export ra" (không phải thư mục cài game thật)
/// khi dùng qua adapter này — cùng 1 field, ý nghĩa khác theo provider nào
/// đang xử lý, để giữ nguyên chữ ký <see cref="IUnpackProvider"/> dùng chung
/// cho cả 2 luồng chính/fallback (ADR-008 đã đặt tiền lệ này).
/// </summary>
public sealed class FModelFallbackAdapter : IUnpackProvider
{
    public Task<Result<IReadOnlyList<UnpackedAssetRef>>> UnpackAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        string outputDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(gameProfile.GameDirectory))
            return Task.FromResult(Result<IReadOnlyList<UnpackedAssetRef>>.Failure(
                $"Thư mục FModel export không tồn tại: {gameProfile.GameDirectory}"));

        // Khác CUE4ParseProvider (Pha 1 chỉ liệt kê virtual path, KHÔNG ghi
        // bytes — xem comment cũ trong UnpackedAssetRef): ở đây file đã có
        // sẵn trên đĩa (người dùng tự export bằng FModel), nên trả luôn
        // ExtractedFilePath — không cần bước ExtractFilesAsync riêng để có
        // bytes thật, tiết kiệm 1 lượt I/O so với luồng CUE4Parse chính.
        var files = Directory.EnumerateFiles(gameProfile.GameDirectory, "*", SearchOption.AllDirectories);
        var assets = files
            .Select(filePath => new UnpackedAssetRef(ToVirtualPath(gameProfile.GameDirectory, filePath), filePath))
            .ToList();

        progress?.Report(new ProgressInfo(100, $"Đã liệt kê {assets.Count} file từ thư mục FModel export"));
        return Task.FromResult(Result<IReadOnlyList<UnpackedAssetRef>>.Success((IReadOnlyList<UnpackedAssetRef>)assets));
    }

    public Task<Result<IReadOnlyList<PackageExportSummary>>> InspectPackagesAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<string> virtualPaths,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Không có cách nào đọc RIÊNG export table (rẻ) mà không mở cả file
        // qua UAssetAPI khi không mount pak — chấp nhận mở full package cho
        // từng file thay vì chỉ đọc header, vì luồng fallback theo ADR-002
        // vốn đã chấp nhận đánh đổi hiệu năng lấy đơn giản (trường hợp hiếm
        // gặp, không phải luồng chính).
        var summaries = new List<PackageExportSummary>(virtualPaths.Count);
        foreach (var virtualPath in virtualPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classNames = new List<string>();
            var filePath = Path.Combine(gameProfile.GameDirectory, virtualPath.Replace('/', Path.DirectorySeparatorChar));
            // Đọc trực tiếp bằng UAssetAPI ở ĐÂY (không gọi qua module AssetIO)
            // — CLAUDE.md §5.6 cấm module gọi thẳng class cụ thể của module
            // khác, nên chấp nhận lặp lại vài dòng mở UAsset thay vì tái dùng
            // logic tương tự trong AssetReaderWriter.
            if (File.Exists(filePath) && TryResolveEngineVersion(gameProfile.EngineVersion, out var engineVersion))
            {
                try
                {
                    var asset = new UAsset(filePath, engineVersion);
                    classNames.AddRange(asset.Exports.Select(e => e.GetExportClassType().ToString()));
                }
                catch (Exception)
                {
                    // Package không đọc được — KHÔNG phải lỗi nghiêm trọng,
                    // giống hệt triết lý ở CUE4ParseProvider.InspectPackagesAsync.
                    classNames.Clear();
                }
            }

            summaries.Add(new PackageExportSummary(virtualPath, classNames));
        }

        progress?.Report(new ProgressInfo(100, $"Đã soi {summaries.Count} package"));
        return Task.FromResult(Result<IReadOnlyList<PackageExportSummary>>.Success((IReadOnlyList<PackageExportSummary>)summaries));
    }

    public Task<Result<IReadOnlyList<UnpackedAssetRef>>> ExtractFilesAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<string> virtualPaths,
        string outputDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // File đã nằm sẵn trên đĩa (thư mục FModel export) — "extract" ở đây
        // chỉ là copy sang outputDirectory để đồng nhất hành vi/kết quả với
        // CUE4ParseProvider.ExtractFilesAsync (người gọi không cần biết đang
        // dùng provider nào). Tự copy kèm .uexp/.ubulk cùng tên nếu có, giống
        // lý do trong CUE4ParseProvider (UAssetAPI cần các file này cạnh
        // .uasset để đọc đủ export data).
        var companionExtensions = new[] { ".uexp", ".ubulk" };
        var pathsToCopy = new List<string>(virtualPaths);
        foreach (var virtualPath in virtualPaths)
        {
            if (!virtualPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                continue;

            var basePath = virtualPath[..^".uasset".Length];
            foreach (var ext in companionExtensions)
            {
                var companionSourcePath = Path.Combine(gameProfile.GameDirectory, (basePath + ext).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(companionSourcePath))
                    pathsToCopy.Add(basePath + ext);
            }
        }

        var results = new List<UnpackedAssetRef>(virtualPaths.Count);
        foreach (var virtualPath in pathsToCopy)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = Path.Combine(gameProfile.GameDirectory, virtualPath.Replace('/', Path.DirectorySeparatorChar));
            string? destPath = null;
            if (File.Exists(sourcePath))
            {
                destPath = Path.Combine(outputDirectory, virtualPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(sourcePath, destPath, overwrite: true);
            }

            if (virtualPaths.Contains(virtualPath))
                results.Add(new UnpackedAssetRef(virtualPath, destPath));
        }

        progress?.Report(new ProgressInfo(100, $"Đã copy {results.Count} file"));
        return Task.FromResult(Result<IReadOnlyList<UnpackedAssetRef>>.Success((IReadOnlyList<UnpackedAssetRef>)results));
    }

    // Tên enum UAssetAPI dạng "VER_UE{major}_{minor}" — cùng cách suy ra như
    // AssetReaderWriter.ResolveEngineVersion, nhưng KHÔNG gọi thẳng qua đó
    // (module khác) nên lặp lại logic nhỏ này, xem lý do ở trên.
    private static bool TryResolveEngineVersion(string? engineVersionHint, out EngineVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(engineVersionHint))
            return false;

        var parts = engineVersionHint.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;

        return Enum.TryParse($"VER_UE{major}_{minor}", out version);
    }

    // Đường dẫn tương đối so với gameProfile.GameDirectory, chuẩn hoá dấu "/"
    // để KHỚP đúng convention virtual path của CUE4Parse (dùng "/" trên mọi
    // hệ điều hành) — nếu không, ScanAsync/LocalizationDiscoveryService (vốn
    // check ".locres"/"Content/Localization/" bằng "/") sẽ nhận nhầm không
    // khớp trên Windows (dùng "\" mặc định của Path).
    private static string ToVirtualPath(string rootDirectory, string filePath) =>
        Path.GetRelativePath(rootDirectory, filePath).Replace(Path.DirectorySeparatorChar, '/');
}
