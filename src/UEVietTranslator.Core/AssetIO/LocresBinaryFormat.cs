using System.Text;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace UEVietTranslator.Core.AssetIO;

/// <summary>
/// Đọc/ghi file <c>.locres</c> (Unreal Text Localization Resource).
///
/// ĐỌC: dùng thẳng <see cref="FTextLocalizationResource"/> của CUE4Parse (thư
/// viện đã được cộng đồng kiểm chứng rộng rãi, tự nhận diện đúng version từ
/// magic GUID trong file) — KHÔNG tự parse byte thủ công cho chiều đọc.
///
/// GHI: CUE4Parse là thư viện READ-ONLY, không có API ghi `.locres`, và
/// UAssetAPI cũng không hỗ trợ format này (chỉ StringTable trong `.uasset`) —
/// không có thư viện nào để dùng, PHẢI tự viết binary writer. Đây là phần rủi
/// ro nhất trong toàn bộ AssetIO, xem docs/DECISIONS.md#adr-010 để hiểu rõ
/// đánh đổi đã chọn (ghi theo <see cref="ELocResVersion.Legacy"/>, phiên bản
/// ĐƠN GIẢN NHẤT trong 4 version mà `.locres` hỗ trợ) và rủi ro còn lại CHƯA
/// verify được (SourceStringHash).
/// </summary>
internal static class LocresBinaryFormat
{
    /// <summary>
    /// Đọc toàn bộ entry trong file `.locres` tại <paramref name="filePath"/>.
    /// Trả về (Namespace, Key, LocalizedString) cho MỌI entry — không lọc gì
    /// thêm, người gọi (<see cref="AssetReaderWriter"/>) tự map sang
    /// <see cref="TextEntry"/>.
    /// </summary>
    public static IReadOnlyList<(string Namespace, string Key, string LocalizedString)> Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        // VersionContainer chỉ ảnh hưởng cách CUE4Parse đọc ASSET UE thông
        // thường (property, export...) — FTextLocalizationResource tự đọc
        // version từ magic GUID trong chính file `.locres`, không dùng
        // VersionContainer này, nên default là đủ.
        var archive = new FStreamArchive(Path.GetFileName(filePath), stream, VersionContainer.DEFAULT_VERSION_CONTAINER);
        var resource = new FTextLocalizationResource(archive);

        var result = new List<(string, string, string)>();
        foreach (var (namespaceKey, keys) in resource.Entries)
            foreach (var (entryKey, entry) in keys)
                result.Add((namespaceKey.Str, entryKey.Str, entry.LocalizedString));

        return result;
    }

    /// <summary>
    /// Ghi <paramref name="entries"/> thành 1 file `.locres` mới hoàn toàn tại
    /// <paramref name="filePath"/>, theo <see cref="ELocResVersion.Legacy"/>
    /// (version 0 — KHÔNG phải version mới nhất "Optimized_CityHash64_UTF16"
    /// mà UE5 thường tự sinh ra). Lý do chọn Legacy — xem ADR-010: format này
    /// bỏ qua hoàn toàn phần "namespace/key hash" (StrHash) và bảng string
    /// dùng chung có ref-count (localized string indirection) — 2 phần PHẢI
    /// tính đúng thuật toán hash nội bộ của UE (CRC32/CityHash64 biến thể
    /// riêng) mới ghi đúng được, mà không có tài liệu chính thức để đối chiếu
    /// 100% chắc chắn. Đọc lại code CUE4Parse (decompile
    /// <c>FTextLocalizationResource</c>) xác nhận: engine hiện tại (đến UE5)
    /// vẫn chấp nhận đọc file version 0-3, nên Legacy vẫn là file hợp lệ để
    /// load, chỉ đơn giản/ít field hơn — đánh đổi lấy an toàn.
    /// </summary>
    public static void Write(string filePath, IReadOnlyList<(string Namespace, string Key, string LocalizedString)> entries)
    {
        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);

        // Legacy format: KHÔNG có magic GUID, KHÔNG có version byte — file
        // bắt đầu thẳng bằng NamespaceCount. (Đối chiếu logic đọc của
        // FTextLocalizationResource: nếu 16 byte đầu không khớp magic GUID cố
        // định, CUE4Parse coi cả file là Legacy và đọc lại từ vị trí 0 — nên
        // ta không cần ghi magic/version gì cả, miễn 4 byte đầu là
        // NamespaceCount hợp lệ.)
        var byNamespace = entries.GroupBy(e => e.Namespace).ToList();

        writer.Write((uint)byNamespace.Count);
        foreach (var namespaceGroup in byNamespace)
        {
            WriteFString(writer, namespaceGroup.Key);

            var keyEntries = namespaceGroup.ToList();
            writer.Write((uint)keyEntries.Count);
            foreach (var (_, key, localizedString) in keyEntries)
            {
                WriteFString(writer, key);

                // FEntry.SourceStringHash: theo code UE, đây là CRC32 của
                // text NGUỒN (tiếng Anh) dùng để engine/editor phát hiện bản
                // dịch bị "cũ" so với text nguồn hiện tại (stale translation
                // warning) — KHÔNG dùng để tra cứu Namespace+Key lúc runtime
                // (lookup theo string, xem FTextKey ở Legacy chỉ lưu FString
                // thuần, không có hash). Ta không biết chắc UE dùng bảng
                // CRC32 chuẩn (IEEE 802.3/zlib) hay bảng CRC tự chế riêng của
                // FCrc trong Core — dùng CRC32 chuẩn làm giá trị hợp lý nhất
                // có thể; nếu sai, hệ quả tệ nhất theo hiểu biết hiện tại chỉ
                // là cảnh báo "bản dịch cũ" trong Editor, KHÔNG ảnh hưởng text
                // hiển thị trong game — xem ADR-010, CẦN Hải Long xác nhận
                // thực tế khi có game để test.
                writer.Write(Crc32.Compute(localizedString));

                WriteFString(writer, localizedString);
            }
        }
    }

    /// <summary>
    /// Ghi 1 FString đúng format serialization chuẩn của UE (xem
    /// <c>FArchive.ReadFString</c> trong CUE4Parse — đã decompile để đối
    /// chiếu, xem ADR-010): int32 length prefix rồi tới bytes.
    /// LUÔN dùng nhánh UTF-16 (length ÂM) dù text là thuần ASCII, để không
    /// bao giờ mất dấu tiếng Việt — nhánh ANSI (length dương, 1 byte/ký tự)
    /// chỉ dùng được cho text thuần ASCII nên tránh hẳn cho đơn giản.
    /// </summary>
    private static void WriteFString(BinaryWriter writer, string value)
    {
        if (value.Length == 0)
        {
            writer.Write(0);
            return;
        }

        // +1 cho null terminator UTF-16 (2 byte 0x0000) — bắt buộc, thiếu sẽ
        // khiến FArchive.ReadFString throw "not null terminated" khi đọc lại.
        var charCountWithNull = value.Length + 1;
        writer.Write(-charCountWithNull);
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }
}

/// <summary>
/// CRC32 chuẩn IEEE 802.3 (polynomial 0xEDB88320) — dùng cho
/// <c>FEntry.SourceStringHash</c> khi ghi `.locres`, xem ghi chú "vì sao"
/// trong <see cref="LocresBinaryFormat.Write"/> về mức độ chắc chắn của lựa
/// chọn này.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
