using UEVietTranslator.Core.GameProfile;
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
}
