# AutoPush

`AutoPush.exe` chạy ngầm (không hiện cửa sổ) mỗi lần đăng nhập Windows, quét các thư mục code trong `config.json` và push lên GitHub **qua REST API**. Máy không cần cài hay config git.

## Cài đặt
1. Build (chỉ cần chạy một lần, dùng `csc.exe` có sẵn của Windows):
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```
2. Tạo token: GitHub → Settings → Developer settings → **Fine-grained tokens** → chọn các repo cần push → quyền **Contents: Read and write**.
3. Sửa `dist\config.json`: điền `token` và danh sách `projects` (`path` local, `repo` dạng `user/repo`, `branch`).
4. Đăng ký chạy khi khởi động (ghi vào `HKCU\...\Run`, không cần quyền admin):
   ```powershell
   .\dist\AutoPush.exe --install
   ```

## Lệnh
| Lệnh | Tác dụng |
|---|---|
| `AutoPush.exe` | Chạy như lúc khởi động (đợi `startupDelaySeconds` rồi sync) |
| `AutoPush.exe --now` | Sync ngay |
| `AutoPush.exe --install` / `--uninstall` | Bật / tắt chạy khi khởi động |

Log ghi vào `dist\autopush.log`.

## Cơ chế
- Tính SHA blob kiểu git cho từng file local rồi so với tree trên GitHub. Không khác gì thì không commit.
- Có thay đổi thì upload blob mới → tạo tree → commit → cập nhật branch. File xoá ở local cũng bị xoá trên remote.
- **Local là nguồn chuẩn**: nếu bạn sửa file trực tiếp trên GitHub, lần push sau sẽ ghi đè (bản cũ vẫn còn trong history).
- Bỏ qua: `.git`, các mục trong `exclude`, `.gitignore` ở thư mục gốc (không hỗ trợ `!`), file lớn hơn `maxFileSizeMB`, symlink/junction.
- Repo trống được tự khởi tạo. Branch chưa có sẽ được tạo mới.
- Nếu thư mục local trống thì bỏ qua để tránh xoá sạch repo.

## Đóng gói 1 file để mang sang máy khác (Bootstrap.exe)
Gộp toàn bộ source + token vào **một file exe tự giải nén**. Mang sang máy phụ, chạy là nó tự bung source ra đĩa, bật autostart và push.

Trên máy chính:
```powershell
# dist\config.json phải đã có token hợp lệ trước
.\build.ps1 -Pack "C:\duong-dan\toi\source-cua-ban" -Repo user/repo -Branch main
```
Tùy chọn `-Setup script.ps1` (hoặc `.cmd`): script này được chạy trên máy phụ ngay sau khi giải nén, trước khi push — dùng để cài dependencies, v.v.

Kết quả: `dist\Bootstrap.exe`. Trên máy phụ chỉ cần chạy file này một lần. Nó sẽ:
1. Giải nén source + AutoPush.exe + config.json vào `%LOCALAPPDATA%\GitHubAutoPush\` (chạy ngầm, không cửa sổ).
2. Chạy `autosetup.*` nếu có.
3. Đăng ký AutoPush chạy mỗi lần đăng nhập, rồi push ngay.

Log trên máy phụ: `%LOCALAPPDATA%\GitHubAutoPush\bootstrap.log` và `autopush.log`.

`%SELF%` trong `path` của config = thư mục chứa AutoPush.exe, nên config chạy được trên mọi máy dù ổ đĩa khác nhau.

## Chạy kèm script mỗi lần push (runWith)
Trong `config.json`, mảng `runWith` chứa các lệnh được chạy ngầm mỗi khi AutoPush chạy (trước khi push). Ví dụ đã cấu hình sẵn `fun.py`:
```json
"runWith": ["py \"%SELF%\\source\\fun.py\" || python \"%SELF%\\source\\fun.py\""]
```
`fun.py` in ra một câu đùa lập trình ngẫu nhiên và ghi vào `fun-log.txt` cạnh nó. **Cần cài Python** (python.org) thì script mới chạy; nếu không có, AutoPush vẫn push bình thường, chỉ bỏ qua bước này.

## Lưu ý bảo mật
- Token nằm dạng plain text trong `config.json`, nên chỉ cấp quyền cho đúng các repo cần push.
- **Bootstrap.exe có nhúng token.** Ai giải nén file này cũng lấy được token của bạn → chỉ dùng trên các máy của chính bạn, đừng chia sẻ ra ngoài.
