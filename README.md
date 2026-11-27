# Taskbar Widget 1.2.25

Một exe duy nhất trên taskbar Windows, gộp hai phần mềm:

- **Battery Bar 1.3.0** (MIT, cappucinudev) — % pin tai nghe, chuột, bàn phím.
- **TraySense 1.2** (AGPL-3.0, DMTWEAKS) — CPU, RAM, GPU, VRAM, nhiệt độ, ổ cứng.

Giao diện và cách setting lấy theo **Battery Bar**: hai strip trong suốt
trên taskbar (một cụm pin/Bluetooth, một cụm thông số máy tính), bấm chuột
phải vào để mở panel kiểu Windows 11, nút bánh răng mở trang Settings, file setting
`settings.txt`, cài đặt / gỡ cài đặt giống hệt Battery Bar. Không còn menu
chuột phải kiểu TraySense, không còn chế độ tray-icon riêng.

Ví dụ trên taskbar (đề mục hệ thống in đậm, các mục cách nhau bằng `|`):

```
CPU 12% 54° 20W | GPU 4% 44° 120W | RAM 61% 9.8/16.0 GB | VRAM 6% 0.5/8.0 GB | Disk 12% | SSD 38° | NET ↓1.2M ↑320K        [🎧 30%] | [📱 78%] | [🖱 93%]
```

## Có gì mới trong 1.2.25

- **Tô màu thiết bị Bluetooth khi pin dưới 10%:** giống màu
  cảnh báo của số liệu hệ thống, icon và % pin đổi sang vàng hổ
  phách cả trên thanh taskbar lẫn trong panel. Thiết bị đang sạc
  thì không đổi màu (pin đang lên, không sắp hết).

## Có gì mới trong 1.2.24

- **Sửa khoảng cách giữa các mục trên thanh taskbar:** dấu `|`
  ngăn cách bị dính về phía mục bên trái (cách mục trước có 6px
  mà cách mục sau tới 17px) vì vẽ cả chuỗi `" | "` rồi mới cộng
  khoảng cách sau đó. Giờ dấu `|` nằm chính giữa khoảng cách hai
  mục. Tổng chiều rộng thanh không đổi.

## Có gì mới trong 1.2.23

- **Tô màu cảnh báo:** số liệu đổi sang màu vàng hổ phách khi
  vượt ngưỡng — nhiệt độ CPU/GPU trên 70°, tải CPU/GPU trên 80%,
  RAM trên 80%. Cả trên thanh taskbar (cả mục đổi màu) lẫn trong
  panel (con số bên phải đổi màu). Màu vàng đậm trên taskbar sáng,
  vàng sáng trên taskbar tối, nên đọc được cả hai nền.

## Có gì mới trong 1.2.22

- **Thêm tùy chọn "Run as administrator" trong Settings:** PawnIO
  chỉ cho tài khoản admin mở thiết bị của nó, nên app chạy quyền
  thường sau khi restart sẽ mất nhiệt độ + công suất CPU. Bật tùy
  chọn này, app tự chạy với quyền admin mỗi lần đăng nhập bằng một
  task trong Task Scheduler (quyền cao nhất, không hiện bảng UAC),
  thay cho tự chạy bằng Run key; app cũng khởi động lại với quyền
  admin ngay lúc bật để đọc lại được cảm biến CPU. Tắt thì quay về
  tự chạy thường. Gỡ cài đặt cũng xóa task này.

## Có gì mới trong 1.2.21

- **Sửa lỗi ẩn mục rồi restart lại hiện:** các mục từng ẩn mặc
  định ở bản cũ (GPU power, GPU/VRAM clock) được bỏ ẩn ở mỗi lần
  mở app để nâng cấp — nhưng vì làm ở mọi lần mở nên ẩn lại cũng
  mất sau khi restart. Giờ chỉ bỏ ẩn khi nâng cấp từ đúng bản cũ
  đó, có ghi số phiên bản vào `settings.txt` để nhớ.
- **Sửa lỗi CPU power nhảy lên mục Bluetooth:** khi không đọc
  được công suất CPU, mục đó mất dữ liệu mới nên panel đoán loại
  theo tên và xếp nhầm sang mục Bluetooth. Giờ mục hệ thống mất
  dữ liệu vẫn ở đúng mục System (lấy loại từ khóa `sys:cpupower`).
- **PawnIO thử lại khi lỗi:** trước đây thử mở driver một lần,
  lỗi là thôi luôn tới khi mở lại app (đúng lúc vừa đăng nhập
  driver chưa kịp chạy thì mất nhiệt độ + công suất CPU cả buổi).
  Giờ thử lại mỗi 30 giây; `--dump` ghi rõ nguồn nhiệt độ CPU
  (`PawnIO` / `Thermal Zone`) và trạng thái PawnIO (`pawnio:`).
  Hỏi cài PawnIO cũng đợi 30 giây sau khi mở app thay vì 8 giây.

## Có gì mới trong 1.2.20

- **Đổi tên 3 mục trong Settings cho đúng nghĩa:** `Show Bluetooth
  devices` → `Show Bluetooth Devices`, `Show Bluetooth Devices
  Position` → `Show Bluetooth Devices On the Left`, `Show System
  Position` → `Show System On the Left`.

## Có gì mới trong 1.2.19

- **Tự cài driver PawnIO:** trên máy mới chưa có PawnIO thì nhiệt
  độ và công suất CPU không đọc được — giờ app tự lo. Khi mục CPU
  đang hiện mà driver chưa có, app hỏi một lần sau khi khởi động
  (từ chối thì không hỏi lại); nút `Install PawnIO driver` trong
  Settings để cài lại bất cứ lúc nào. App tải file cài chính thức
  `PawnIO_setup.exe 2.2.0` từ GitHub, kiểm tra mã SHA-256 khớp mới
  chạy (cùng cách TraySense gốc làm), cần quyền admin nên sẽ hiện
  bảng UAC một lần, cài xong app tự mở lại bình thường.

## Có gì mới trong 1.2.18

- **Nhiệt độ GPU, đường đọc dự phòng qua ADL:** nhiệt độ GPU qua
  D3DKMT bỗng im bặt trên máy Bạch (hai lần `--dump` liên tiếp đều
  báo `D3DKMT: no sensor`, trong khi trước đó vẫn hiện 52–63°),
  nên thêm đường đọc qua ADL PMLog — cảm biến nhiệt edge (số 8),
  dự phòng cảm biến GFX (số 28) vì một số APU chỉ công bố GFX mà
  không có edge (cùng cách LibreHardwareMonitor dùng). Thứ tự đọc
  giờ là D3DKMT → ADL → NVML, và vẫn dùng chung một lần đọc ADL
  mỗi chu kỳ với công suất + xung nhịp nên không tốn thêm gì.
  `--dump` giờ ghi rõ nguồn nhiệt độ GPU (`52C (ADL)`) và lý do
  chi tiết khi không đọc được (`query failed` / `temp 0` / …).

## Có gì mới trong 1.2.17

- **Nhiệt độ SSD, đường đọc thứ tư:** trên máy Bạch cả ba đường
  trực tiếp đều bị từ chối (`ATA SMART refused`, đến cả truy vấn
  tên ổ cũng thất bại), nên thêm đường đọc qua WMI
  (`MSStorageDriver_ATAPISmartData`) — kênh khác hẳn ba đường
  trên, có driver chặn IOCTL trực tiếp nhưng vẫn công bố dữ liệu
  SMART qua WMI. Đường WMI chết thì chỉ thử một lần rồi thôi, để
  không tốn tài nguyên mỗi chu kỳ. `--dump` giờ liệt kê lý do của
  từng đường đọc, thay vì chỉ đường cuối cùng.

## Có gì mới trong 1.2.16

- **Nhiệt độ SSD, đường đọc thứ ba:** trên máy Bạch driver NVMe
  chỉ trả về 12 byte rỗng cho truy vấn log SMART (`outLen=12`),
  nên thêm đường đọc SMART cổ điển của ổ ATA/SATA
  (`SMART_RCV_DRIVE_DATA`, thuộc tính nhiệt độ 0xC2, dự phòng
  0xBE). Thứ tự thử giờ là: thuộc tính nhiệt độ của Windows →
  nhật ký SMART NVMe → SMART ATA. `--dump` cũng ghi thêm dòng
  `drive:` với tên ổ, để biết chắc ổ đó là NVMe hay SATA khi
  các đường đọc không khớp.

## Có gì mới trong 1.2.15

- **Sửa nhiệt độ SSD NVMe:** đường đọc nhật ký SMART ở 1.2.14 bị
  driver trả về rỗng, vì bộ đệm trả về phải điền sẵn `Version` và
  `Size` thì driver mới trả dữ liệu log — giờ đã điền. Ngoài ra
  `--dump` ghi chi tiết hơn khi vẫn không đọc được (độ dài trả về,
  độ dài dữ liệu, vị trí dữ liệu), để còn biết đường sửa tiếp.

## Có gì mới trong 1.2.14

- **Xung nhịp gộp vào khung GPU/VRAM:** xung GPU hiện chung khung với
  GPU (`GPU 6% 51° 2W 2400MHz`), xung VRAM hiện chung khung với VRAM
  (`VRAM 78% 3.1/4.0 GB 1800MHz`) — giống cách công suất đã gộp trước
  đây. Trong panel hai dòng xung nhịp vẫn riêng để ẩn/hiện bằng mắt.
  Nâng cấp từ bản cũ sẽ tự bật hai dòng này (trước đây ẩn mặc định).
- **Nhiệt độ SSD trên ổ NVMe:** ngoài cách đọc cũ qua
  StorageDeviceTemperatureProperty, app thử thêm nhật ký SMART của
  ổ NVMe (log page 2, nhiệt độ tổng hợp). Nhiều ổ NVMe, nhất là
  trên mini PC, không trả nhiệt độ qua cách cũ nên mục SSD không
  hiện — giờ sẽ hiện. Nếu vẫn không đọc được, `--dump` ghi rõ lý do
  (dòng `ssd temp:`).

## Có gì mới trong 1.2.13

- **Tự cập nhật (auto-update):** app tự kiểm tra bản mới trên
  GitHub Releases (10 giây sau khi mở, rồi mỗi ngày một lần).
  Có bản mới thì hỏi bạn có cập nhật không; bấm Yes là app tải
  file exe mới, tự thay file cũ rồi mở lại bản mới — không cần
  tải zip, giải nén hay cài lại bằng tay. Trong Settings có thêm
  công tắc `Check for updates automatically` và nút
  `Check for updates` để kiểm tra ngay. Tắt công tắc thì app
  không tự kiểm tra nữa (vẫn bấm nút kiểm tra tay được).

## Có gì mới trong 1.2.12

- **Link Help trong Settings** đổi từ repo Battery Bar gốc sang
  repo GitHub mới của bạn: https://github.com/btanh258/TaskbarWidget.

## Có gì mới trong 1.2.11

- **Nhẹ hơn mỗi lần refresh:** mỗi chu kỳ 2 giây app chỉ gọi driver
  ADL 1 lần thay vì 3 lần (công suất GPU + 2 xung nhịp dùng chung
  một lần đọc PMLog). Nhịp tăng CPU trong Task Manager vẫn sẽ
  thấy — đó là bản chất của việc đọc cảm biến theo chu kỳ — nhưng
  phần việc thừa đã bỏ. Muốn nhịp thưa hơn nữa thì tăng
  `System refresh` trong Settings.

## Có gì mới trong 1.2.10

- **Công suất GPU trên card AMD giờ đọc đúng:** trước đây công suất
  GPU chỉ qua NVML (NVIDIA) rồi D3DKMT, mà trường Power của D3DKMT
  trên APU AMD (như Radeon 780M) trả về một số cố định — đó là lý
  do nó cứ đứng yên ở 2W. Bản này port thêm đường ADL PMLog của
  TraySense (`ADL2_New_QueryPMLogData_Get`, sensor 73 = board power,
  23 = ASIC power), thử sau NVML và trước D3DKMT. Công suất giờ sẽ
  nhảy theo tải thật: lúc rảnh vài W, lúc chơi game/render lên
  hàng chục W.
- **Xung nhịp GPU/VRAM trên AMD cũng đọc được qua ADL** (sensor 1 =
  GPU clock, 2 = VRAM clock), nên 2 mục này giờ hiện được trên card
  AMD khi mở mắt trong panel (trước đây chỉ có trên NVIDIA).

## Có gì mới trong 1.2.9

- **Đổi icon ứng dụng:** nhân vật bông đeo tai nghe gõ laptop do bạn
  gửi (`Picture1.png`). Ảnh gốc nền đen nên mình đã tách nền đen
  (flood-fill từ viền) để icon trong suốt, hiện đẹp cả trên taskbar
  sáng lẫn tối, rồi render ra `icon.ico` đủ 7 cỡ 16–256px. File
  `src/icon.svg` của icon monitor cũ đã bỏ. Lưu ý: Windows có thể
  còn cache icon cũ ở shortcut, khởi động lại Explorer (hoặc đăng
  xuất/đăng nhập lại) là thấy icon mới.

## Có gì mới trong 1.2.8

- **Đổi icon ứng dụng:** màn hình monitor với biểu đồ tròn (file
  `src/icon.svg` do bạn gửi), render ra `icon.ico` đủ 7 cỡ
  16–256px. Icon mới hiện ở shortcut Start menu, Task Manager và
  Alt-Tab. Lưu ý: Windows có thể còn cache icon cũ ở shortcut, khởi
  động lại Explorer (hoặc đăng xuất/đăng nhập lại) là thấy icon mới.

## Có gì mới trong 1.2.7

- **Công suất CPU:** port thêm `read_cpu_power` của TraySense — đọc
  bộ đếm năng lượng RAPL qua PawnIO (Intel: MSR 0x606/0x611, AMD:
  0xC0010299/0xC001029B), công suất = độ tăng năng lượng / thời gian.
  Cần driver PawnIO đã cài sẵn, giống nhiệt độ CPU. Lần quét đầu chỉ
  để lấy mốc nên công suất hiện từ lần quét thứ 2.
- **Công suất gộp vào khung usage, nhưng vẫn là mục riêng:** trên
  taskbar hiện `CPU 12% 54° 20W` và `GPU 4% 44° 120W`; trong panel
  CPU power / GPU power vẫn là 2 dòng riêng, có mắt ẩn/hiện riêng.
  Ẩn dòng công suất thì khung usage trở về `CPU 12% 54°`. Cả hai
  hiện mặc định; khi nâng cấp, GPU power cũ (mặc định ẩn) được tự
  mở ra.
- **Hiệu năng:** mỗi lần quét (mặc định 2 giây) app chỉ đọc vài
  counter PDH, 2 thanh ghi MSR qua PawnIO và 1 truy vấn NVML/D3DKMT
  — tổng cộng dưới 1 ms CPU, không chạy vòng lặp nền, strip chỉ vẽ
  lại khi số thay đổi. Muốn quét thưa hơn thì tăng `System refresh`
  trong Settings.

## Có gì mới trong 1.2.6

- **Nhiệt độ GPU giờ hiện cả trên card AMD (và Intel):** trước đây
  nhiệt độ GPU chỉ đọc qua NVML nên chỉ có trên NVIDIA. Bản này port
  thêm đường D3DKMT của TraySense (`D3DKMTOpenAdapterFromLuid` +
  `D3DKMTQueryAdapterInfo`, kiểu ADAPTERPERFDATA) — mở adapter theo
  LUID rồi đọc `Temperature / 10`, thử trước NVML, đúng thứ tự của
  TraySense. Công suất GPU cũng có thêm đường dự phòng D3DKMT
  (`Power / 10`) khi NVML không đọc được.
- **Chẩn đoán:** chạy `TaskbarWidget.exe --dump out.txt` sẽ thấy lý
  do khi không đọc được nhiệt độ GPU (ví dụ `D3DKMT: no sensor;
  NVML: unavailable`).

## Có gì mới trong 1.2.5

- **GPU gộp usage + nhiệt độ một mục** như CPU: `GPU 4% 44°` (nhiệt
  độ chỉ hiện khi đọc được sensor; dòng phụ vẫn hiện tên GPU, hoặc
  `Hot` khi quá nóng). Dòng `sys:gput` cũ tự xóa khỏi settings khi
  nâng cấp, giống `sys:cput` ở 1.1.0.
- **Thứ tự cụm System đổi thành CPU → GPU → RAM → VRAM → Disk → SSD**
  rồi tới GPU power, GPU/VRAM clock — GPU lên trước RAM như yêu cầu.
- **Thêm mục Network, nằm cuối cụm System:** tốc độ mạng xuống/lên
  `NET ↓1.2M ↑320K` (tổng mọi card mạng trừ loopback, đo qua
  Performance Counter `Network Interface`). Hiện mặc định, ẩn được
  bằng mắt trong panel. Lưu ý: lần quét đầu chưa có số liệu nên mục
  Network hiện từ lần quét thứ 2 (sau ~2 giây).

## Có gì mới trong 1.2.4

- **Sửa lỗi các mục nhảy lộn xộn khi đổi icon:** trước đây danh sách
  trong panel và cụm Bluetooth trên strip xếp theo *loại icon* — nên
  mỗi lần bấm đổi icon, thiết bị đó đổi vị trí xếp hạng và cả danh
  sách xô lệch theo. Giờ các thiết bị Bluetooth xếp theo **tên** (tên
  không đổi khi đổi icon) nên bấm bao nhiêu lần dòng đó vẫn đứng yên
  một chỗ, chỉ icon đổi. Nhóm System giữ thứ tự cố định như cũ (loại
  của nó không bao giờ đổi). Bỏ luôn quy tắc "thiết bị mất kết nối
  chìm xuống cuối": thiết bị offline vẫn giữ đúng vị trí của nó, hiện
  `Not connected`, khi kết nối lại cũng không nhảy.

## Có gì mới trong 1.2.3

- **Icon Bluetooth đổi sang bộ SVG bạn gửi:** tai nghe, chuột, bàn phím,
  iPhone, tay cầm (máy chơi game cầm tay), loa — file SVG gốc nằm trong
  `icons/`, đã rasterize thành PNG đen-trắng và nhúng trong exe nên vẫn
  chỉ có 1 file duy nhất. Icon tự đổi màu theo taskbar sáng/tối và theo
  theme của panel, cả trên strip lẫn trong panel.
- **Thêm loại Speaker (loa):** tự đoán từ tên thiết bị (speaker,
  soundbar, boombox, HomePod, Echo, Sonos, …). Vòng xoay icon khi bấm
  ô vuông bên trái mỗi dòng trong panel giờ có thêm Speaker. Loại
  Other giữ icon font cũ vì không có file SVG cho nó.
- **Bỏ in đậm icon, về bình thường:** font icon về Regular, bỏ hẳn
  kiểu vẽ 2 lượt của 1.2.1. Đề mục hệ thống (**CPU**, **RAM**, …) trên
  strip vẫn in đậm như trước, chỉ icon là về bình thường.

## Có gì mới trong 1.2.2

- **Thêm 1 icon điện thoại nữa:** bên cạnh **Phone** (hình ống nghe,
  E716) có thêm **Smartphone** (hình smartphone đứng, E8C7). Vòng xoay
  icon khi bấm ô vuông bên trái mỗi dòng trong panel giờ là:
  Headphones → Mouse → Keyboard → Controller → Phone → Smartphone →
  Other. Chế độ demo (`--demo`) thêm một Demo Smartphone để xem thử.
- **Đổi thao tác chuột trên strip:** bấm **chuột trái không làm gì**,
  bấm **chuột phải mở panel**. Menu chuột phải cũ (Open / Refresh now /
  Exit) bỏ — nút Refresh và Quit vẫn có trong panel.
- Bản này làm tiếp trên 1.2.0, vẫn giữ lại icon in đậm và nhận diện tên
  điện thoại mở rộng của 1.2.1.

## Có gì mới trong 1.2.1

- **Icon điện thoại:** loại **Phone** đã có từ 1.1.0 nhưng giờ nhận diện
  được nhiều tên hơn (Galaxy A/M/J/F, OPPO, vivo, Realme, Honor, Huawei,
  Xperia, Motorola, Nokia, Nothing Phone, ROG Phone, Poco, Infinix,
  Tecno, mã máy Samsung `SM-…`, …) nên điện thoại kết nối Bluetooth sẽ
  tự hiện icon điện thoại thay vì icon chung. Bấm ô vuông bên trái mỗi
  dòng trong panel để đổi icon thủ công — Phone nằm trong vòng xoay
  Headphones → Mouse → Keyboard → Controller → Phone → Other.
  Chế độ demo (`--demo`) cũng thêm một Demo Phone để xem thử icon.
- **Icon in đậm, dễ thấy hơn:** font icon trên strip và trong panel
  chuyển sang Bold, lại vẽ thêm một lượt lệch ~1px — vì font icon
  Segoe MDL2 Assets không có sẵn bản bold nên nét mảnh trước đây rất
  khó nhìn ở cỡ taskbar. Áp dụng cho mọi icon (tai nghe, chuột, bàn
  phím, tay cầm, điện thoại, mắt ẩn/hiện, bánh răng, …), không chỉ
  icon điện thoại.

## Có gì mới trong 1.2.0

- **Hai cụm tách riêng, mỗi cụm chọn trái/phải:** cụm pin/Bluetooth và
  cụm thông số máy tính là 2 strip riêng. Trong Settings, mỗi cụm có
  công tắc Position riêng — **bật = nằm bên trái taskbar, tắt = nằm
  bên phải (cạnh khay)**:
  - `Show Bluetooth Devices Position` — vị trí cụm pin/Bluetooth.
  - `Show System Position` — vị trí cụm thông số máy tính.
  - Hai cụm cùng một bên thì Bluetooth luôn nằm trái System.
  - Lưu ý: vị trí trái là mép trái taskbar, có thể che nút Start /
    Widgets trên Windows 11 — dùng `Offset` để dịch sang phải.
- **Settings phân theo cụm** (Bluetooth / System / General): mỗi cụm có
  Show (ẩn/hiện cả cụm), Position (trái/phải) và tần suất quét riêng
  (`Check every` cho pin, `System refresh` cho hệ thống). `Text size`,
  `Offset`, `Start with Windows` dùng chung cho cả 2 cụm.
- **VRAM thêm dung lượng:** `VRAM 6% 0.5/8.0 GB` ngay trên strip
  (giống RAM).
- Sửa lỗi 1.1.0: cột giá trị trong panel từng hiện nhầm cả dòng chữ
  của strip (vd `RAM 61% 9.8/16.0 GB`); giờ panel chỉ hiện giá trị
  ngắn (`61%`, `12% 54°`).

## Có gì mới trong 1.1.0

- **CPU gộp một mục:** usage và nhiệt độ hiển thị chung — `CPU 12% 54°`
  (nhiệt độ chỉ hiện khi đọc được sensor). Dòng `sys:cput` cũ tự xóa
  khỏi settings khi nâng cấp.
- **RAM thêm dung lượng:** `RAM 61% 9.8/16.0 GB` ngay trên strip.
- **Dấu `|` ngăn cách** các mục trên strip; đề mục hệ thống (**CPU**,
  **RAM**, **GPU**, **SSD**, …) in đậm.
- **Thiết bị Bluetooth đổi được icon:** thêm loại **Phone** (điện thoại)
  bên cạnh Headphones / Mouse / Keyboard / Controller / Other. Bấm ô
  vuông bên trái mỗi dòng trong panel để đổi icon; loại thiết bị cũng
  tự đoán từ tên (iPhone, Pixel, Galaxy S, OnePlus, …).
- **Nâng cấp từ 1.1.0:** setting `sysfirst` cũ không còn dùng nữa
  (thay bằng `sysleft`/`btleft`); nếu trước đây bạn để System nằm
  trái thì sau khi lên 1.2.0 cả 2 cụm mặc định nằm phải, bật lại
  Position trong Settings là được.

## Cài đặt

Windows 10/11. Chạy `dist\TaskbarWidget.exe` (build bằng `.\build.ps1`).
Chọn **Yes** để cài: copy vào `%LOCALAPPDATA%\Programs\TaskbarWidget`,
tự chạy cùng Windows, thêm vào Start menu. Chọn **No** để chạy thử một lần.

Bấm chuột phải vào strip để mở panel. Bấm bánh răng để mở Settings.
Bấm chuột trái vào strip không làm gì.

## Panel

- Trang chính liệt kê pin từng thiết bị, rồi mục **System** với từng chỉ số
  hệ thống: tên, giá trị, thanh tiến trình (với các chỉ số %), dòng phụ
  (vd `9.8 / 16.0 GB`, tên GPU, `Hot` khi quá nóng).
- Bấm con mắt để ẩn/hiện từng dòng trên taskbar. Bấm ô vuông bên trái để
  đổi kiểu icon — chỉ áp dụng cho thiết bị pin, nhãn của chỉ số hệ thống
  là cố định (`CPU`, `RAM`, `GPU`, `VRAM`, `SSD`, …).
- Các chỉ số mặc định hiện trên taskbar: CPU, GPU, RAM, SSD temp,
  Network, CPU/GPU power. VRAM, disk util, GPU/VRAM clock mặc định
  ẩn — mở panel bấm con mắt để hiện.

## Settings (trang bánh răng)

Giữ nguyên toàn bộ setting của Battery Bar, thêm 2 dòng cho hệ thống:

| Setting | Mặc định | Ghi chú |
| --- | --- | --- |
| Check every | 60 sec | Tần suất quét pin |
| Text size | 12 px | Cỡ chữ trên strip |
| Offset | 0 px | Dịch strip: cụm bên phải dịch sang trái, cụm bên trái dịch sang phải |
| Show % sign | on | Hiện dấu `%` (pin) |
| Start with Windows | on khi cài | Ghi `HKCU\...\Run` |
| **System refresh** | **2 sec** | Tần suất quét CPU/RAM/GPU — giống TraySense. Pin vẫn quét riêng 60s, không bị quét nhanh theo. |
| **Show System** | on | Tắt là ẩn toàn bộ cụm hệ thống trên strip |
| **Show System Position** | off | Bật: cụm hệ thống nằm trái taskbar. Tắt: nằm phải (cạnh khay) |
| **Show Bluetooth devices** | on | Tắt là ẩn toàn bộ cụm pin/Bluetooth trên strip |
| **Show Bluetooth Devices Position** | off | Bật: cụm pin/Bluetooth nằm trái taskbar. Tắt: nằm phải (cạnh khay) |

Các nút Copy diagnostics / Help / Uninstall / Quit giữ nguyên. Copy
diagnostics giờ copy cả log pin lẫn log hệ thống.

## Chỉ số hệ thống — lấy từ đâu (port từ TraySense)

| Chỉ số | Cách lấy |
| --- | --- |
| CPU % | `GetSystemTimes` (delta idle/total) |
| RAM % | `GlobalMemoryStatusEx` |
| GPU % | PDH `\GPU Engine(*)\Utilization Percentage`, gom theo LUID, lấy engine bận nhất |
| VRAM % | PDH `\GPU Adapter Memory(*)\Dedicated Usage` / tổng VRAM (WMI `Win32_VideoController`) |
| SSD temp | `IOCTL_STORAGE_QUERY_PROPERTY` trên ổ chứa Windows |
| Disk util | `100 - PDH \PhysicalDisk(*)\% Idle Time` trên ổ chứa Windows |
| CPU temp | PawnIO (MSR Intel / SMN AMD) nếu driver PawnIO đã được cài; nếu không, fallback PDH Thermal Zone (kém chính xác hơn) |
| GPU temp / power / clock | NVML (`nvml.dll`) — chủ yếu card NVIDIA |
| Ngưỡng nóng | CPU/GPU ≥ 80°C `Hot`, ≥ 90°C `Very hot`; SSD ≥ 65°C / ≥ 75°C |

Chỉ số nào không đọc được sensor thì **không hiện** (giống TraySense),
không hiện `--`. Nhiệt độ CPU cần driver PawnIO: bản này **không tự tải
và cài driver** (cần admin + driver kernel-mode). Nếu máy đã chạy TraySense
trước đó thì driver đã có sẵn và Taskbar Widget dùng luôn. Xem
`THIRD_PARTY.md` (module PawnIO là LGPL-2.1).

## Chưa port trong bản 1.0.0

- Speed test (đo tốc độ mạng), hover graph (biểu đồ vài phút gần nhất),
  chế độ tray-icon 7 icon, chạy trên nhiều màn hình phụ, tự cài PawnIO.
  Những phần này cần thêm việc (xem `src/SystemMonitor.cs` đầu file).
  (Tự kiểm tra update đã có từ bản 1.2.13 — xem `src/Updater.cs`.)
- Strip chỉ nằm trên taskbar chính (giống Battery Bar), không chèn vào
  taskbar phụ như TraySense.

## Build

Chỉ cần Windows (dùng `csc.exe` có sẵn trong .NET Framework 4.x, không
cần SDK hay NuGet):

```powershell
.\build.ps1            # -> dist\TaskbarWidget.exe
.\build.ps1 -Portable  # + copy TaskbarWidget.exe ra cạnh script
```

Chạy thử không cần thiết bị thật:

```powershell
.\dist\TaskbarWidget.exe --portable --demo            # thiết bị + chỉ số giả
.\dist\TaskbarWidget.exe --portable --demo-settings   # mở thẳng trang Settings
.\dist\TaskbarWidget.exe --dump out.txt               # ghi log mọi nguồn rồi thoát
```

## Gỡ cài đặt

Panel → bánh răng → **Uninstall**. Xóa startup, shortcut Start menu,
settings (`%APPDATA%\TaskbarWidget`) và exe đã cài.

## Giấy phép

Bản gộp này phát hành theo **AGPL-3.0** (`LICENSE`, từ TraySense) vì có
code port từ TraySense. Code gốc Battery Bar là MIT (`LICENSE-BatteryBar`).
Module PawnIO trong `pawnio/` là LGPL-2.1, xem `THIRD_PARTY.md` và
`pawnio/COPYING`. Nếu bạn chia sẻ bản sửa đổi, phải công bố toàn bộ mã
nguồn theo AGPL-3.0 và giữ lại các ghi chú bản quyền.
