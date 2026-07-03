# UEVietTranslator

Công cụ desktop hỗ trợ Việt hóa game chạy trên Unreal Engine 4/5: unpack →
tìm AES key (nếu cần) → phát hiện file ngôn ngữ → extract ra CSV → dịch
(AI-assisted) → repack.

**Trước khi code, đọc [`CLAUDE.md`](./CLAUDE.md)** — file này chứa toàn bộ
ngữ cảnh, quy tắc, và trạng thái dự án. Dành cho cả người lẫn Claude Code.

## Cấu trúc

```
src/
  UEVietTranslator.Core/     - Logic lõi (không phụ thuộc UI): unpack, AES key,
                                phát hiện file ngôn ngữ, đọc/ghi asset, CSV, dịch, repack
  UEVietTranslator.App/      - UI Avalonia (desktop)
  UEVietTranslator.Cli/      - CLI mỏng để test Core độc lập, không cần UI
tests/
  UEVietTranslator.Core.Tests/
docs/
  DOMAIN_KNOWLEDGE.md        - Kiến thức nền UE pak/IoStore/AES/locres
  DECISIONS.md               - Nhật ký quyết định kiến trúc (ADR)
  ROADMAP.md                 - Lộ trình & trạng thái hiện tại
  CODING_STANDARDS.md        - Quy ước code cụ thể
```

## Trạng thái

Xem [`docs/ROADMAP.md`](./docs/ROADMAP.md) để biết pha hiện tại. Pha 0-2 đã
build/test thành công (.NET 8 SDK 8.0.422) — CUE4Parse `1.2.2`, UAssetAPI
`1.1.0` (xem `docs/DECISIONS.md` ADR-003 lý do chọn version). Pha 1-2 (detect,
unpack thử, dò AES key) đã code xong nhưng **CHƯA test với game thật** — cần
chạy trên máy có cài game, xem mục "Test với game thật" dưới đây.

## Build

Cần .NET 8 SDK ([tải tại đây](https://dotnet.microsoft.com/download/dotnet/8.0)
nếu máy chưa có).

```bash
dotnet restore
dotnet build
dotnet test
```

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

# 3. Detect: xác nhận đọc được UE version + định dạng pak/IoStore
dotnet run --project src/UEVietTranslator.Cli -- detect "D:\đường dẫn tới thư mục cài game"

# 4. Unpack thử KHÔNG key — xem game này có mã hoá AES hay không
dotnet run --project src/UEVietTranslator.Cli -- unpack "D:\đường dẫn tới thư mục cài game"

# 5. CHỈ chạy bước này nếu bước 4 báo lỗi kiểu "cần AES key" —
#    có thể mất vài phút (xem docs/DOMAIN_KNOWLEDGE.md mục benchmark)
dotnet run --project src/UEVietTranslator.Cli -- resolve-key "D:\đường dẫn tới thư mục cài game"
```

Copy lại toàn bộ output của cả 3 lệnh (kể cả lỗi nếu có) để báo lại — đó là
dữ liệu thực tế đầu tiên về Dragonwilds, cần điền vào mục 5 trong
`docs/DOMAIN_KNOWLEDGE.md`.

Game khởi điểm: **RuneScape: Dragonwilds** (Jagex, Unreal Engine 5). Mục tiêu
sản phẩm là dùng chung được cho game UE4/UE5 bất kỳ, không hardcode riêng cho
game này — xem CLAUDE.md §1.
