using Microsoft.Extensions.DependencyInjection;
using UEVietTranslator.Core;
using UEVietTranslator.Core.AesKeyResolver;
using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.LocalizationDiscovery;
using UEVietTranslator.Core.Repacking;
using UEVietTranslator.Core.Translation;
using UEVietTranslator.Core.Unpacking;

namespace UEVietTranslator.Cli;

// CLI mỏng để Hải Long (hoặc Claude Code) test từng module Core độc lập,
// không phải chờ UI Avalonia xong. Xem docs/ROADMAP.md Pha 1.
//
// Ví dụ:
//   dotnet run --project src/UEVietTranslator.Cli -- detect "D:\Games\RuneScape Dragonwilds"
//   dotnet run --project src/UEVietTranslator.Cli -- unpack "D:\Games\RuneScape Dragonwilds"
//   dotnet run --project src/UEVietTranslator.Cli -- resolve-key "D:\Games\RuneScape Dragonwilds"
//   dotnet run --project src/UEVietTranslator.Cli -- discover "D:\Games\RuneScape Dragonwilds" [aesKeyHex]
//   dotnet run --project src/UEVietTranslator.Cli -- confirm-locfiles "D:\Games\RuneScape Dragonwilds" "Content/Localization/Game/vi/Game.locres:Locres" ...
//   dotnet run --project src/UEVietTranslator.Cli -- read-locfile "D:\Games\RuneScape Dragonwilds" "Content/Localization/Game/vi/Game.locres:Locres" [aesKeyHex]
//   dotnet run --project src/UEVietTranslator.Cli -- set-gemini-key <apiKey> [model, mặc định gemini-2.0-flash]
//   dotnet run --project src/UEVietTranslator.Cli -- repack "D:\Games\RuneScape Dragonwilds" <modifiedAssetsDir> <outputPakPath>
//   dotnet run --project src/UEVietTranslator.Cli -- discover-fallback <thư mục FModel đã export> <engineVersion, VD "5.3">
//     (fallback ADR-002 — dùng khi CUE4Parse fail, xem docs/DECISIONS.md#adr-013)
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddUeVietTranslatorCore();
        await using var provider = services.BuildServiceProvider();

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        return args[0] switch
        {
            "detect" when args.Length > 1 => await RunDetectAsync(provider, args[1]),
            "unpack" when args.Length > 1 => await RunUnpackAsync(provider, args[1], args.ElementAtOrDefault(2)),
            "resolve-key" when args.Length > 1 => await RunResolveKeyAsync(provider, args[1]),
            "discover" when args.Length > 1 => await RunDiscoverAsync(provider, args[1], args.ElementAtOrDefault(2)),
            "confirm-locfiles" when args.Length > 2 => await RunConfirmLocFilesAsync(provider, args[1], args.Skip(2).ToArray()),
            "read-locfile" when args.Length > 2 => await RunReadLocFileAsync(provider, args[1], args[2], args.ElementAtOrDefault(3)),
            "set-gemini-key" when args.Length > 1 => await RunSetGeminiKeyAsync(provider, args[1], args.ElementAtOrDefault(2)),
            "repack" when args.Length > 3 => await RunRepackAsync(provider, args[1], args[2], args[3]),
            "discover-fallback" when args.Length > 2 => await RunDiscoverFallbackAsync(provider, args[1], args[2]),
            _ => PrintUsageAndFail(),
        };
    }

    private static async Task<int> RunDetectAsync(ServiceProvider provider, string gameDirectory)
    {
        var detector = provider.GetRequiredService<IGameProfileDetector>();
        var result = await detector.DetectAsync(gameDirectory, CancellationToken.None);

        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Detect thất bại: {result.Error}");
            return 1;
        }

        Console.WriteLine($"UE version: {result.Value!.EngineVersion}");
        Console.WriteLine($"Pak format: {result.Value.PakFormat}");
        return 0;
    }

    private static async Task<int> RunUnpackAsync(ServiceProvider provider, string gameDirectory, string? aesKeyHex)
    {
        var detector = provider.GetRequiredService<IGameProfileDetector>();
        var detectResult = await detector.DetectAsync(gameDirectory, CancellationToken.None);
        if (!detectResult.IsSuccess)
        {
            Console.Error.WriteLine($"Detect thất bại: {detectResult.Error}");
            return 1;
        }

        Console.WriteLine($"UE version: {detectResult.Value!.EngineVersion}");
        Console.WriteLine($"Pak format: {detectResult.Value.PakFormat}");

        var unpackProvider = provider.GetRequiredKeyedService<IUnpackProvider>("primary");
        var progress = new Progress<ProgressInfo>(p =>
            Console.WriteLine(p.PercentComplete < 0
                ? p.Message
                : $"{p.Message} ({p.PercentComplete:0.0}%)"));

        var outputDirectory = Path.Combine(Path.GetTempPath(), "uevt-unpack");
        var unpackResult = await unpackProvider.UnpackAsync(
            detectResult.Value, aesKeyHex, outputDirectory, progress, CancellationToken.None);

        if (!unpackResult.IsSuccess)
        {
            Console.Error.WriteLine($"Unpack thất bại: {unpackResult.Error}");
            return 1;
        }

        var assets = unpackResult.Value!;
        Console.WriteLine($"Tổng số asset liệt kê được: {assets.Count}");

        // In thử vài file trông giống file ngôn ngữ để có cảm nhận nhanh —
        // đây KHÔNG phải LocalizationDiscovery thật (Pha 3), chỉ lọc theo
        // đuôi file để Hải Long xem sơ bộ ngay khi test CLI.
        var localizationLike = assets
            .Where(a => a.VirtualPath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        if (localizationLike.Count > 0)
        {
            Console.WriteLine($"Tìm thấy {localizationLike.Count} file .locres (hiển thị tối đa 20):");
            foreach (var asset in localizationLike)
                Console.WriteLine($"  {asset.VirtualPath}");
        }
        else
        {
            Console.WriteLine("Không thấy file .locres nào (game có thể dùng StringTable trong .uasset — xem docs/ROADMAP.md Pha 3).");
        }

        return 0;
    }

    private static async Task<int> RunResolveKeyAsync(ServiceProvider provider, string gameDirectory)
    {
        var detector = provider.GetRequiredService<IGameProfileDetector>();
        var detectResult = await detector.DetectAsync(gameDirectory, CancellationToken.None);
        if (!detectResult.IsSuccess)
        {
            Console.Error.WriteLine($"Detect thất bại: {detectResult.Error}");
            return 1;
        }

        var resolver = provider.GetRequiredService<IAesKeyResolver>();
        var progress = new Progress<ProgressInfo>(p => Console.WriteLine($"{p.Message} ({p.PercentComplete:0.0}%)"));

        var result = await resolver.ResolveAsync(
            detectResult.Value!.ExecutablePath,
            detectResult.Value.PaksDirectory,
            progress,
            CancellationToken.None);

        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Resolve key thất bại: {result.Error}");
            return 1;
        }

        var candidates = result.Value!;
        var validated = candidates.Where(c => c.Validated).ToList();

        if (validated.Count > 0)
        {
            Console.WriteLine($"Tìm được {validated.Count} key đã xác nhận:");
            foreach (var c in validated)
                Console.WriteLine($"  {c.HexKey}");

            var store = provider.GetRequiredService<IGameProfileStore>();
            var saveResult = await store.SaveAsync(
                detectResult.Value, validated.Select(c => c.HexKey).ToList(), CancellationToken.None);
            Console.WriteLine(saveResult.IsSuccess
                ? "Đã lưu profile + key vào profiles/ để lần sau không cần dò lại."
                : $"Lưu profile thất bại (không ảnh hưởng kết quả dò key ở trên): {saveResult.Error}");
        }
        else
        {
            Console.WriteLine($"Không key nào validate được. {candidates.Count} candidate chưa xác nhận (mẫu):");
            foreach (var c in candidates)
                Console.WriteLine($"  {c.HexKey}");
        }

        return 0;
    }

    private static async Task<int> RunDiscoverAsync(ServiceProvider provider, string gameDirectory, string? aesKeyHex)
    {
        var detector = provider.GetRequiredService<IGameProfileDetector>();
        var detectResult = await detector.DetectAsync(gameDirectory, CancellationToken.None);
        if (!detectResult.IsSuccess)
        {
            Console.Error.WriteLine($"Detect thất bại: {detectResult.Error}");
            return 1;
        }

        // Chưa truyền key trên dòng lệnh thì thử lấy key đã lưu từ lần
        // resolve-key trước — tránh bắt Hải Long gõ lại key thủ công mỗi lần
        // test discover. Xem docs/DECISIONS.md#adr-007 (CHƯA wire tự động
        // cho unpack, nhưng discover là lệnh mới nên wire luôn cho tiện test).
        if (aesKeyHex is null)
        {
            var store = provider.GetRequiredService<IGameProfileStore>();
            var loadResult = await store.LoadAsync(gameDirectory, CancellationToken.None);
            if (loadResult.IsSuccess && loadResult.Value!.ValidatedAesKeys.Count > 0)
                aesKeyHex = loadResult.Value.ValidatedAesKeys[0];
        }

        var unpackProvider = provider.GetRequiredKeyedService<IUnpackProvider>("primary");
        var progress = new Progress<ProgressInfo>(p =>
            Console.WriteLine(p.PercentComplete < 0
                ? p.Message
                : $"{p.Message} ({p.PercentComplete:0.0}%)"));

        var outputDirectory = Path.Combine(Path.GetTempPath(), "uevt-unpack");
        var unpackResult = await unpackProvider.UnpackAsync(
            detectResult.Value!, aesKeyHex, outputDirectory, progress, CancellationToken.None);
        if (!unpackResult.IsSuccess)
        {
            Console.Error.WriteLine($"Unpack thất bại: {unpackResult.Error}");
            return 1;
        }

        var discoveryService = provider.GetRequiredService<ILocalizationDiscoveryService>();
        var scanResult = await discoveryService.ScanAsync(
            unpackProvider, detectResult.Value!, aesKeyHex, unpackResult.Value!, progress, CancellationToken.None);
        if (!scanResult.IsSuccess)
        {
            Console.Error.WriteLine($"Scan thất bại: {scanResult.Error}");
            return 1;
        }

        var candidates = scanResult.Value!.OrderByDescending(c => c.Confidence).ToList();
        Console.WriteLine($"Tìm được {candidates.Count} candidate file ngôn ngữ:");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            Console.WriteLine($"  [{i}] confidence={c.Confidence:0.0} kind={c.Kind} {c.Path}");
        }
        Console.WriteLine();
        Console.WriteLine("Xác nhận lựa chọn bằng lệnh confirm-locfiles, VD:");
        Console.WriteLine($"  dotnet run -- confirm-locfiles \"{gameDirectory}\" \"<path>:<kind>\" ...");

        return 0;
    }

    private static async Task<int> RunConfirmLocFilesAsync(ServiceProvider provider, string gameDirectory, string[] pathKindPairs)
    {
        var confirmed = new List<ConfirmedLocalizationFile>();
        foreach (var pair in pathKindPairs)
        {
            var separatorIndex = pair.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == pair.Length - 1)
            {
                Console.Error.WriteLine($"Sai định dạng '<path>:<kind>': {pair}");
                return 1;
            }

            var path = pair[..separatorIndex];
            var kindText = pair[(separatorIndex + 1)..];
            if (!Enum.TryParse<LocalizationFileKind>(kindText, ignoreCase: true, out var kind))
            {
                Console.Error.WriteLine(
                    $"Kind không hợp lệ '{kindText}' (giá trị hợp lệ: {string.Join(", ", Enum.GetNames<LocalizationFileKind>())}): {pair}");
                return 1;
            }

            confirmed.Add(new ConfirmedLocalizationFile(path, kind));
        }

        var store = provider.GetRequiredService<IGameProfileStore>();
        var saveResult = await store.SaveConfirmedLocalizationFilesAsync(gameDirectory, confirmed, CancellationToken.None);
        if (!saveResult.IsSuccess)
        {
            Console.Error.WriteLine($"Lưu lựa chọn thất bại: {saveResult.Error}");
            return 1;
        }

        Console.WriteLine($"Đã lưu {confirmed.Count} file ngôn ngữ đã xác nhận vào profile.");
        return 0;
    }

    private static async Task<int> RunReadLocFileAsync(
        ServiceProvider provider, string gameDirectory, string pathKindPair, string? aesKeyHex)
    {
        var separatorIndex = pathKindPair.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == pathKindPair.Length - 1)
        {
            Console.Error.WriteLine($"Sai định dạng '<path>:<kind>': {pathKindPair}");
            return 1;
        }

        var virtualPath = pathKindPair[..separatorIndex];
        var kindText = pathKindPair[(separatorIndex + 1)..];
        if (!Enum.TryParse<LocalizationFileKind>(kindText, ignoreCase: true, out var kind))
        {
            Console.Error.WriteLine(
                $"Kind không hợp lệ '{kindText}' (giá trị hợp lệ: {string.Join(", ", Enum.GetNames<LocalizationFileKind>())}): {pathKindPair}");
            return 1;
        }

        var detector = provider.GetRequiredService<IGameProfileDetector>();
        var detectResult = await detector.DetectAsync(gameDirectory, CancellationToken.None);
        if (!detectResult.IsSuccess)
        {
            Console.Error.WriteLine($"Detect thất bại: {detectResult.Error}");
            return 1;
        }

        if (aesKeyHex is null)
        {
            var store = provider.GetRequiredService<IGameProfileStore>();
            var loadResult = await store.LoadAsync(gameDirectory, CancellationToken.None);
            if (loadResult.IsSuccess && loadResult.Value!.ValidatedAesKeys.Count > 0)
                aesKeyHex = loadResult.Value.ValidatedAesKeys[0];
        }

        var unpackProvider = provider.GetRequiredKeyedService<IUnpackProvider>("primary");
        var progress = new Progress<ProgressInfo>(p => Console.WriteLine(p.Message));

        var outputDirectory = Path.Combine(Path.GetTempPath(), "uevt-extract");
        var extractResult = await unpackProvider.ExtractFilesAsync(
            detectResult.Value!, aesKeyHex, [virtualPath], outputDirectory, progress, CancellationToken.None);
        if (!extractResult.IsSuccess)
        {
            Console.Error.WriteLine($"Extract thất bại: {extractResult.Error}");
            return 1;
        }

        var extractedPath = extractResult.Value!.FirstOrDefault()?.ExtractedFilePath;
        if (extractedPath is null)
        {
            Console.Error.WriteLine($"Không extract được file (không tìm thấy virtual path trong pak/IoStore): {virtualPath}");
            return 1;
        }

        Console.WriteLine($"Đã extract ra: {extractedPath}");

        var readerWriter = provider.GetRequiredService<IAssetReaderWriter>();
        var readResult = await readerWriter.ReadAsync(
            extractedPath, kind, detectResult.Value!.EngineVersion, CancellationToken.None);
        if (!readResult.IsSuccess)
        {
            Console.Error.WriteLine($"Đọc thất bại: {readResult.Error}");
            return 1;
        }

        var entries = readResult.Value!;
        Console.WriteLine($"Đọc được {entries.Count} entry (hiển thị tối đa 30):");
        foreach (var entry in entries.Take(30))
            Console.WriteLine($"  [{entry.Namespace}] {entry.Key} = {entry.SourceText}");

        return 0;
    }

    private static async Task<int> RunSetGeminiKeyAsync(ServiceProvider provider, string apiKey, string? model)
    {
        var store = provider.GetRequiredService<IGeminiSettingsStore>();
        var settings = new GeminiSettings(apiKey, model ?? "gemini-2.0-flash");

        var result = await store.SaveAsync(settings, CancellationToken.None);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Lưu cấu hình Gemini thất bại: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Đã lưu Gemini API key, model = {settings.Model}.");
        return 0;
    }

    private static async Task<int> RunRepackAsync(
        ServiceProvider provider, string gameDirectory, string modifiedAssetsDirectory, string outputPakPath)
    {
        var detector = provider.GetRequiredService<IGameProfileDetector>();
        var detectResult = await detector.DetectAsync(gameDirectory, CancellationToken.None);
        if (!detectResult.IsSuccess)
        {
            Console.Error.WriteLine($"Detect thất bại: {detectResult.Error}");
            return 1;
        }

        var repackService = provider.GetRequiredService<IRepackService>();
        var progress = new Progress<ProgressInfo>(p => Console.WriteLine(p.Message));

        var result = await repackService.RepackAsync(
            detectResult.Value!, modifiedAssetsDirectory, outputPakPath,
            repakExecutablePath: null, retocExecutablePath: null, progress, CancellationToken.None);

        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Repack thất bại: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Repack thành công. Kiểm tra thư mục output: {Path.GetDirectoryName(outputPakPath)}");
        return 0;
    }

    private static async Task<int> RunDiscoverFallbackAsync(ServiceProvider provider, string exportedDirectory, string engineVersion)
    {
        if (!Directory.Exists(exportedDirectory))
        {
            Console.Error.WriteLine($"Thư mục không tồn tại: {exportedDirectory}");
            return 1;
        }

        // GameProfile "giả" cho luồng fallback — xem docs/DECISIONS.md#adr-013.
        // KHÔNG chạy qua GameProfileDetector (sẽ fail vì thư mục FModel export
        // không có .exe/pak để quét).
        var fakeProfile = new GameProfile(
            GameDirectory: exportedDirectory,
            ExecutablePath: string.Empty,
            ExecutableHash: string.Empty,
            EngineVersion: engineVersion,
            PakFormat: PakFormat.Unknown,
            PaksDirectory: string.Empty);

        // Lưu profile "giả" ngay để CLI confirm-locfiles (đọc profile đã lưu
        // qua IGameProfileStore.LoadAsync) dùng được sau bước này — luồng
        // fallback không có bước resolve-key nên không AES key nào để lưu.
        var store = provider.GetRequiredService<IGameProfileStore>();
        var saveProfileResult = await store.SaveAsync(fakeProfile, validatedAesKeys: [], CancellationToken.None);
        if (!saveProfileResult.IsSuccess)
        {
            Console.Error.WriteLine($"Lưu profile fallback thất bại: {saveProfileResult.Error}");
            return 1;
        }

        var unpackProvider = provider.GetRequiredKeyedService<IUnpackProvider>("fmodel-fallback");
        var progress = new Progress<ProgressInfo>(p => Console.WriteLine(p.Message));

        var unpackResult = await unpackProvider.UnpackAsync(fakeProfile, aesKeyHex: null, outputDirectory: "unused", progress, CancellationToken.None);
        if (!unpackResult.IsSuccess)
        {
            Console.Error.WriteLine($"Liệt kê thư mục export thất bại: {unpackResult.Error}");
            return 1;
        }

        Console.WriteLine($"Tìm thấy {unpackResult.Value!.Count} file trong thư mục export.");

        var discoveryService = provider.GetRequiredService<ILocalizationDiscoveryService>();
        var scanResult = await discoveryService.ScanAsync(
            unpackProvider, fakeProfile, aesKeyHex: null, unpackResult.Value!, progress, CancellationToken.None);
        if (!scanResult.IsSuccess)
        {
            Console.Error.WriteLine($"Scan thất bại: {scanResult.Error}");
            return 1;
        }

        var candidates = scanResult.Value!.OrderByDescending(c => c.Confidence).ToList();
        Console.WriteLine($"Tìm được {candidates.Count} candidate file ngôn ngữ:");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            Console.WriteLine($"  [{i}] confidence={c.Confidence:0.0} kind={c.Kind} {c.Path}");
        }
        Console.WriteLine();
        Console.WriteLine("Xác nhận lựa chọn bằng lệnh confirm-locfiles, dùng CHÍNH thư mục export làm <đường dẫn thư mục cài game>:");
        Console.WriteLine($"  dotnet run -- confirm-locfiles \"{exportedDirectory}\" \"<path>:<kind>\" ...");

        return 0;
    }

    private static int PrintUsageAndFail()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Cách dùng:");
        Console.WriteLine("  dotnet run -- detect <đường dẫn thư mục cài game>");
        Console.WriteLine("  dotnet run -- unpack <đường dẫn thư mục cài game> [aesKeyHex]");
        Console.WriteLine("  dotnet run -- resolve-key <đường dẫn thư mục cài game>");
        Console.WriteLine("  dotnet run -- discover <đường dẫn thư mục cài game> [aesKeyHex]");
        Console.WriteLine("  dotnet run -- confirm-locfiles <đường dẫn thư mục cài game> <path>:<kind> ...");
        Console.WriteLine("  dotnet run -- read-locfile <đường dẫn thư mục cài game> <path>:<kind> [aesKeyHex]");
        Console.WriteLine("  dotnet run -- set-gemini-key <apiKey> [model]");
        Console.WriteLine("  dotnet run -- repack <đường dẫn thư mục cài game> <modifiedAssetsDir> <outputPakPath>");
        Console.WriteLine("  dotnet run -- discover-fallback <thư mục FModel đã export> <engineVersion, VD 5.3>");
    }
}
