(function () {
    // SCRUM-62: access token nằm trong HttpOnly cookie wh_access (không còn trong body).
    // Swagger UI chạy cùng origin với API → cookie tự gửi kèm mọi request, KHÔNG cần
    // inject Bearer thủ công nữa. Script này giờ chỉ (tuỳ chọn) auto-login để set cookie.
    //
    // Điền DEV_EMAIL/DEV_PASSWORD nếu muốn Swagger tự đăng nhập khi mở (chỉ dùng dev).
    // Để trống → bỏ qua, tự gọi /api/auth/login trong Swagger rồi cookie sẽ áp dụng.
    const DEV_EMAIL = "";
    const DEV_PASSWORD = "";

    function autoLogin() {
        if (!DEV_EMAIL || !DEV_PASSWORD) return; // chưa cấu hình → không làm gì

        fetch("/api/auth/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "include", // nhận cookie wh_access / wh_csrf
            body: JSON.stringify({ email: DEV_EMAIL, password: DEV_PASSWORD })
        })
            .then((res) => {
                if (res.ok) console.log("Swagger: đã auto-login, cookie auth được set.");
                else console.warn("Swagger auto-login thất bại:", res.status);
            })
            .catch(() => console.warn("Swagger auto-login failed — server chưa sẵn sàng?"));
    }

    const interval = setInterval(() => {
        if (window.ui && window.ui.authActions) {
            clearInterval(interval);
            autoLogin();
        }
    }, 300);
})();
