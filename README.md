

# 🌸 **NihongoSekai - Nền tảng học tiếng Nhật và văn hoá**

**NihongoSekai** là nền tảng học trực tuyến giúp người học nâng cao kỹ năng giao tiếp tiếng Nhật và hiểu biết sâu hơn về văn hoá Nhật Bản.
Hệ thống cung cấp các **khoá học trực tuyến**, **lớp học trực tiếp qua video call**, **bài kiểm tra**, và các **tính năng quản lý học tập thông minh**.

---

## ✨ **Tính năng nổi bật**

* 🎥 **Khoá học trực tuyến** với video bài giảng, bài kiểm tra và mục tiêu học tập rõ ràng.
* 🧑‍🏫 **Lớp học trực tiếp (Classroom)** do người bản xứ (Partner) tổ chức, liên kết với Google Meet.
* 🛒 **Hệ thống mua khoá học & giỏ hàng**, mỗi khoá học chỉ có thể mua một lần.
* 📊 **Theo dõi tiến độ học tập (Course Progress)**: phần trăm hoàn thành, đánh dấu video đã xem và quiz đã làm.
* ⭐ **Đánh giá và nhận xét khoá học (Course Rating)** từ người học.
* 📌 **Dashboard dành cho Admin**: quản lý người dùng, khoá học, đơn hàng, và thống kê.
* 🤝 **Quản lý Partner**: xét duyệt hồ sơ, quản lý **Classroom Template** và **Session**.
* 🔑 **Tích hợp đăng nhập Google (OAuth2)** và xác thực qua email.
* 💬 **Chatbot hỗ trợ học viên** với giao diện hiện đại, popup và hiệu ứng mượt mà.
* 📅 **Đồng bộ lịch học với Google Calendar** cho Learner và Partner.

---

## ⚙️ **Cài đặt**

### **Yêu cầu môi trường**

* **.NET 8.0** hoặc cao hơn.
* **SQL Server** (Local hoặc Remote).
* **Node.js** (dành cho chức năng Partner quản lý Google Calendar).
* **Entity Framework Core** và **ASP.NET Identity**.

### **Các bước cài đặt**

```bash
# Clone dự án về máy
git clone https://github.com/heptasweet/SWD392_NihongoSekai_1.git
cd nihongosekai

# Khôi phục các package
dotnet restore

# Chạy migration và seed dữ liệu mẫu
dotnet ef database update

# Chạy ứng dụng
dotnet run
```

---

## 🚀 **Cách sử dụng**

1. Mở trình duyệt và truy cập: [**[https://nihongosekai.site](https://nihongosekai.site)**]
2. Đăng ký tài khoản **(Learner hoặc Partner)**, hoặc đăng nhập bằng Google.
3. Duyệt danh sách khoá học và thêm vào giỏ hàng.
4. Thanh toán khoá học và bắt đầu học qua trang **Chi tiết khoá học**.
5. **Partner**: quản lý **My Template** và **My Session**.
6. **Admin**: truy cập **/Admin/Index** để quản lý hệ thống.

---

## 🖼️ **Ảnh minh hoạ**



---

## 👥 **Tác giả & Người đóng góp**

Minh Khôi

Gia Khôi

Văn Hoàng

---

