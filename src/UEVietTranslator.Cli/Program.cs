using Microsoft.Extensions.DependencyInjection;
using UEVietTranslator.Core;
using UEVietTranslator.Core.AesKeyResolver;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.Unpacking;

namespace UEVietTranslator.Cli;

// CLI mỏng để Hải Long (hoặc Claude Code) test từng module Core độc lập,
// không phải chờ UI Avalonia xong. Xem docs/ROADMAP.md Pha 1.
//
// Ví dụ:
//   dotnet run --project src/UEVietTranslator.Cli -- detect "D:\Games\RuneScape Dragonwilds"
//   dotnet run --project src/UEVietTranslator.Cli -- unpack "D:\Games\RuneScape Dragonwilds"
//   dotnet run --project src/UEVietTranslator.Cli -- resolve-key "D:\Games\RuneScape Dragonwilds"
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
    }
}
