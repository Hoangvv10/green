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

## Lưu ý bảo mật
Token nằm dạng plain text trong `config.json`, nên chỉ cấp quyền cho đúng các repo cần push.
