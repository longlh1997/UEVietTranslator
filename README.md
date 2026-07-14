# UEVietTranslator

Công cụ desktop hỗ trợ Việt hóa game chạy trên Unreal Engine 4/5: unpack →
tìm AES key (nếu cần) → phát hiện file ngôn ngữ → extract ra CSV → dịch
(AI-assisted, Gemini) → review → ghi lại asset → repack.

**Trước khi code, đọc [`CLAUDE.md`](./CLAUDE.md)** — file này chứa toàn bộ
ngữ cảnh, quy tắc, và trạng thái dự án. Dành cho cả người lẫn Claude Code.

## Cấu trúc

```
src/
  UEVietTranslator.Core/     - Logic lõi (không phụ thuộc UI): GameProfile,
                                AesKeyResolver, Unpacking, LocalizationDiscovery,
                                AssetIO, CsvSchema, Translation, Repacking
  UEVietTranslator.App/      - UI Avalonia (desktop) — wizard 6 bước, xem dưới
  UEVietTranslator.Cli/      - CLI mỏng để chạy/test từng bước Core độc lập,
                                không cần UI
tests/
  UEVietTranslator.Core.Tests/
scripts/
  publish-windows.ps1        - Publish App thành 1 file .exe self-contained
docs/
  DOMAIN_KNOWLEDGE.md        - Kiến thức nền UE pak/IoStore/AES/locres
  DECISIONS.md               - Nhật ký quyết định kiến trúc (ADR)
  ROADMAP.md                 - Lộ trình & trạng thái hiện tại
  CODING_STANDARDS.md        - Quy ước code cụ thể
```

## Trạng thái

Xem [`docs/ROADMAP.md`](./docs/ROADMAP.md) để biết chi tiết đầy đủ. Tóm tắt:
Pha 0-6 đã code xong toàn bộ pipeline (build sạch, test tự động pass trên máy
thật, .NET 8 SDK 8.0.422). Phần **CHƯA verify** — tất cả đều cần Hải Long tự
chạy vì môi trường code (Claude Code) không có máy Windows/game thật:

- Chạy trọn wizard/CLI với 1 game UE thật (VD RuneScape: Dragonwilds) từ đầu
  tới cuối, xác nhận text tiếng Việt hiển thị đúng trong game.
- File `.locres` do tool tự ghi (binary writer tự viết, xem ADR-010) — chưa
  load thử trong game.
- 2 CLI ngoài `repak`/`retoc` dùng ở bước Repack (ADR-012) — chưa chạy thử
  binary thật.
- Gemini API — chưa gọi thật với API key thật.
- File `.exe` publish ra (ADR-015) — chưa chạy thử trên Windows thật.
- Wizard UI (`UEVietTranslator.App`) — chưa xem bằng mắt (sandbox code
  không có quyền Accessibility để chụp screenshot).

## Build

Cần .NET 8 SDK ([tải tại đây](https://dotnet.microsoft.com/download/dotnet/8.0)
nếu máy chưa có).

```bash
dotnet restore
dotnet build
dotnet test
```

## Chạy app (UI wizard)

```bash
dotnet run --project src/UEVietTranslator.App
```

Wizard có 6 bước, đi tuần tự bằng nút "Quay lại"/"Tiếp tục":

1. **Thiết lập & Quét** — nhập thư mục cài game (+ AES key nếu biết, hoặc để
   trống để dùng key đã lưu từ lần trước), hoặc bật "Dùng FModel fallback"
   nếu Quét tự động thất bại (xem hướng dẫn ngay trong app). Bấm Quét.
2. **Xác nhận file ngôn ngữ** — tick chọn/sửa loại file trong danh sách
   candidate tìm được, bấm Lưu để tái dùng lựa chọn ở lần Quét sau.
3. **Extract & Xuất CSV** — extract các file đã chọn, đọc text gốc, xuất ra 1
   file CSV chuẩn hoá.
4. **Dịch tự động** — cấu hình Gemini API key/model (bấm Lưu key 1 lần đầu),
   rồi bấm Dịch tự động.
5. **Review bản dịch** — sửa trực tiếp bản dịch/trạng thái từng dòng ngay
   trong app, Lưu CSV hoặc Tải lại từ CSV (nếu sửa tay bằng Excel song song).
6. **Ghi asset & Repack** — nhập đường dẫn `.pak` output (+ đường dẫn
   `repak`/`retoc` nếu không có sẵn trong PATH), bấm Ghi bản dịch + Repack.

## Publish file .exe (Windows)

```powershell
.\scripts\publish-windows.ps1
```

Ra 1 file self-contained duy nhất `publish\UEVietTranslator.App.exe` (không
cần cài .NET runtime trên máy chạy) — xem ADR-015. Publish target chỉ
`win-x64` (CUE4Parse mang theo 1 thư viện native chỉ chạy Windows).
`repak`/`retoc` (bước Repack) là 2 CLI ngoài, KHÔNG đóng gói kèm — tự tải từ
GitHub Releases của `trumank/repak` và `trumank/retoc`, đặt vào PATH.

## Dùng qua CLI (thay cho UI, hoặc để debug từng bước)

```powershell
dotnet run --project src/UEVietTranslator.Cli -- detect "<thư mục game>"
dotnet run --project src/UEVietTranslator.Cli -- unpack "<thư mục game>" [aesKeyHex]
dotnet run --project src/UEVietTranslator.Cli -- resolve-key "<thư mục game>"
dotnet run --project src/UEVietTranslator.Cli -- discover "<thư mục game>" [aesKeyHex]
dotnet run --project src/UEVietTranslator.Cli -- confirm-locfiles "<thư mục game>" <path>:<kind> ...
dotnet run --project src/UEVietTranslator.Cli -- read-locfile "<thư mục game>" <path>:<kind> [aesKeyHex]
dotnet run --project src/UEVietTranslator.Cli -- set-gemini-key <apiKey> [model]
dotnet run --project src/UEVietTranslator.Cli -- repack "<thư mục game>" <modifiedAssetsDir> <outputPakPath>
dotnet run --project src/UEVietTranslator.Cli -- discover-fallback "<thư mục FModel đã export>" <engineVersion, VD 5.3>
```

CLI không có lệnh export-CSV/dịch/ghi-lại-asset riêng (những bước đó hiện chỉ
có trong wizard `UEVietTranslator.App`) — dùng app cho full pipeline, dùng CLI
khi cần test/debug 1 bước cụ thể độc lập.

## Test với game thật (VD RuneScape: Dragonwilds)

Chạy trên máy Windows đã cài game đó (không cần máy này có network tới GitHub
gì cả, chỉ cần .NET SDK). Copy toàn bộ thư mục repo sang máy đó bằng cách nào
cũng được (USB, cloud, network share...), rồi mở PowerShell tại thư mục repo:

```powershell
# 1. Kiểm tra .NET SDK đã có chưa
dotnet --version    # cần thấy 8.x — nếu chưa có, tải ở link Build phía trên

# 2. Restore + build (lần đầu có thể mất vài phút)
dotnet restore
dotnet build

# 3. Chạy thử nguyên wizard qua UI (khuyến nghị) ...
dotnet run --project src/UEVietTranslator.App

# ... hoặc từng bước qua CLI để debug nếu có bước nào lỗi:
dotnet run --project src/UEVietTranslator.Cli -- detect "D:\đường dẫn tới thư mục cài game"
dotnet run --project src/UEVietTranslator.Cli -- unpack "D:\đường dẫn tới thư mục cài game"
# Chỉ chạy resolve-key nếu unpack báo lỗi kiểu "cần AES key" (có thể mất vài
# phút, xem docs/DOMAIN_KNOWLEDGE.md mục benchmark):
dotnet run --project src/UEVietTranslator.Cli -- resolve-key "D:\đường dẫn tới thư mục cài game"
```

Copy lại output/lỗi (nếu có) để báo lại — đây là dữ liệu thực tế cần điền vào
mục 5 trong `docs/DOMAIN_KNOWLEDGE.md` và các mục "CHƯA verify" trong
`docs/ROADMAP.md`.

Game khởi điểm: **RuneScape: Dragonwilds** (Jagex, Unreal Engine 5). Mục tiêu
sản phẩm là dùng chung được cho game UE4/UE5 bất kỳ, không hardcode riêng cho
game này — xem CLAUDE.md §1.
