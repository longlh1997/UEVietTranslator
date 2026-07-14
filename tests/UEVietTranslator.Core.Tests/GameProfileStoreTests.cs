using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.LocalizationDiscovery;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class GameProfileStoreTests : IDisposable
{
    private readonly string _profilesDir;

    public GameProfileStoreTests()
    {
        _profilesDir = Path.Combine(Path.GetTempPath(), "uevt-test-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_profilesDir))
            Directory.Delete(_profilesDir, recursive: true);
    }

    private static GameProfile.GameProfile MakeProfile(string gameDirectory) => new(
        GameDirectory: gameDirectory,
        ExecutablePath: Path.Combine(gameDirectory, "Game.exe"),
        ExecutableHash: "ABCDEF1234567890",
        EngineVersion: "5.3",
        PakFormat: PakFormat.IoStore,
        PaksDirectory: Path.Combine(gameDirectory, "Content", "Paks"));

    [Fact]
    public async Task LoadAsync_ChuaTungSave_TraVeFailureRoRang()
    {
        var store = new GameProfileStore(_profilesDir);

        var result = await store.LoadAsync(@"C:\Games\Chua Ton Tai", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Chưa có profile nào được lưu", result.Error);
    }

    [Fact]
    public async Task SaveRoiLoad_TraVeDungDuLieuDaLuu()
    {
        var store = new GameProfileStore(_profilesDir);
        var profile = MakeProfile(@"D:\Games\RuneScape Dragonwilds");
        var keys = new List<string> { new string('A', 64), new string('B', 64) };

        var saveResult = await store.SaveAsync(profile, keys, CancellationToken.None);
        Assert.True(saveResult.IsSuccess);

        var loadResult = await store.LoadAsync(profile.GameDirectory, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(profile, loadResult.Value!.Profile);
        Assert.Equal(keys, loadResult.Value.ValidatedAesKeys);
    }

    [Fact]
    public async Task SaveAsync_TenThuMucCoKyTuKhongHopLeChoFileName_VanSaveDuocKhongCrash()
    {
        var store = new GameProfileStore(_profilesDir);
        // Tên thư mục có dấu ":" và khoảng trắng — Windows path thật, không
        // phải ký tự hợp lệ trong tên file trên mọi hệ điều hành.
        var profile = MakeProfile(@"D:\Games\Some: Weird / Name?");

        var saveResult = await store.SaveAsync(profile, [], CancellationToken.None);
        Assert.True(saveResult.IsSuccess);

        var loadResult = await store.LoadAsync(profile.GameDirectory, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);
    }

    [Fact]
    public async Task SaveAsync_LuuFileDangJsonDocDuocPakFormatDangChuoi()
    {
        // Xác nhận PakFormat serialize dạng chuỗi ("IoStore") chứ không phải
        // số nguyên — dễ đọc khi Hải Long mở file profiles/*.json thủ công.
        var store = new GameProfileStore(_profilesDir);
        var profile = MakeProfile(@"D:\Games\RuneScape Dragonwilds");

        await store.SaveAsync(profile, [], CancellationToken.None);

        var files = Directory.GetFiles(_profilesDir, "*.gameprofile.json");
        Assert.Single(files);
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.Contains("\"IoStore\"", content);
        Assert.Contains("RuneScape_Dragonwilds", Path.GetFileName(files[0]));
    }

    [Fact]
    public async Task SaveConfirmedLocalizationFilesAsync_ChuaCoProfile_TraVeFailureRoRang()
    {
        var store = new GameProfileStore(_profilesDir);
        var confirmed = new List<ConfirmedLocalizationFile>
        {
            new("Content/Localization/Game/vi/Game.locres", LocalizationFileKind.Locres),
        };

        var result = await store.SaveConfirmedLocalizationFilesAsync(
            @"C:\Games\Chua Ton Tai", confirmed, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Chưa có profile nào lưu", result.Error);
    }

    [Fact]
    public async Task SaveConfirmedLocalizationFilesAsync_DaCoProfile_LuuDuocMaKhongDungToiProfileVaKey()
    {
        var store = new GameProfileStore(_profilesDir);
        var profile = MakeProfile(@"D:\Games\RuneScape Dragonwilds");
        var keys = new List<string> { new string('A', 64) };
        await store.SaveAsync(profile, keys, CancellationToken.None);

        var confirmed = new List<ConfirmedLocalizationFile>
        {
            new("Content/Localization/Game/vi/Game.locres", LocalizationFileKind.Locres),
            new("Content/Game/UI/DA_Strings.uasset", LocalizationFileKind.StringTableAsset),
        };
        var saveResult = await store.SaveConfirmedLocalizationFilesAsync(
            profile.GameDirectory, confirmed, CancellationToken.None);
        Assert.True(saveResult.IsSuccess);

        var loadResult = await store.LoadAsync(profile.GameDirectory, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(profile, loadResult.Value!.Profile);
        Assert.Equal(keys, loadResult.Value.ValidatedAesKeys);
        Assert.Equal(confirmed, loadResult.Value.ConfirmedLocalizationFiles);
    }

    [Fact]
    public async Task SaveAsync_GoiLaiSauKhiDaXacNhanFileNgonNgu_KhongXoaMatLuaChonCu()
    {
        // SaveAsync có thể được gọi lại sau khi game update (re-detect +
        // re-resolve key) — không được xoá mất lựa chọn file ngôn ngữ người
        // dùng đã xác nhận thủ công ở bước khác. Xem docs/DECISIONS.md#adr-009.
        var store = new GameProfileStore(_profilesDir);
        var profile = MakeProfile(@"D:\Games\RuneScape Dragonwilds");
        await store.SaveAsync(profile, [new string('A', 64)], CancellationToken.None);

        var confirmed = new List<ConfirmedLocalizationFile>
        {
            new("Content/Localization/Game/vi/Game.locres", LocalizationFileKind.Locres),
        };
        await store.SaveConfirmedLocalizationFilesAsync(profile.GameDirectory, confirmed, CancellationToken.None);

        // Game update: resolve-key chạy lại, key mới khác key cũ.
        var updatedProfile = profile with { ExecutableHash = "NEWHASH1234567890" };
        await store.SaveAsync(updatedProfile, [new string('B', 64)], CancellationToken.None);

        var loadResult = await store.LoadAsync(profile.GameDirectory, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(updatedProfile, loadResult.Value!.Profile);
        Assert.Equal(confirmed, loadResult.Value.ConfirmedLocalizationFiles);
    }
}
