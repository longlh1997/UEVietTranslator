using Microsoft.Extensions.DependencyInjection;
using UEVietTranslator.Core.AesKeyResolver;
using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.CsvSchema;
using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.LocalizationDiscovery;
using UEVietTranslator.Core.Repacking;
using UEVietTranslator.Core.Translation;
using UEVietTranslator.Core.Unpacking;

namespace UEVietTranslator.Core;

/// <summary>
/// Điểm đăng ký DI duy nhất cho toàn bộ Core, để App (Avalonia) và Cli dùng
/// chung — xem CLAUDE.md §5.6. Khi thêm module Core mới, đăng ký tại đây,
/// KHÔNG rải đăng ký DI ở nhiều nơi.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddUeVietTranslatorCore(this IServiceCollection services)
    {
        services.AddSingleton<IGameProfileDetector, GameProfileDetector>();
        services.AddSingleton<IGameProfileStore, GameProfileStore>();
        services.AddSingleton<IAesKeyResolver, AesKeyResolverService>();

        // 2 implementation của IUnpackProvider được đăng ký có key để nơi
        // gọi chọn tường minh (chính vs fallback), KHÔNG dùng DI để "tự
        // chọn" — quyết định dùng fallback là hành động của người dùng,
        // không phải logic tự động. Xem docs/DECISIONS.md#adr-002.
        services.AddKeyedSingleton<IUnpackProvider, CUE4ParseProvider>("primary");
        services.AddKeyedSingleton<IUnpackProvider, FModelFallbackAdapter>("fmodel-fallback");

        services.AddSingleton<ILocalizationDiscoveryService, LocalizationDiscoveryService>();
        services.AddSingleton<IAssetReaderWriter, AssetReaderWriter>();
        services.AddSingleton<ICsvSchemaConverter, CsvSchemaConverter>();
        services.AddSingleton<IGeminiSettingsStore, GeminiSettingsStore>();
        // HttpClient dùng chung 1 instance (best practice — tránh socket
        // exhaustion nếu new HttpClient() mỗi lần gọi), đủ dùng cho app
        // desktop 1 người dùng gọi tuần tự, không cần IHttpClientFactory.
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ITranslationService, GeminiTranslationService>();
        services.AddSingleton<IRepackService, RepackService>();

        return services;
    }
}
