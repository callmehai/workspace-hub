(function () {
    // Auto-inject Bearer token sau khi login thành công.
    const originalFetch = window.fetch;
    window.fetch = async function (...args) {
        const response = await originalFetch(...args);

        const url = args[0];
        if (typeof url === 'string' && url.includes('/api/auth/login') && response.status === 200) {
            try {
                const clone = response.clone();
                const data = await clone.json();

                const token = data.accessToken || data.token;
                if (token) {
                    const ui = window.ui;
                    if (ui && ui.authActions) {
                        ui.authActions.authorize({
                            Bearer: {
                                name: "Bearer",
                                schema: { type: "apiKey", in: "header", name: "Authorization" },
                                value: token
                            }
                        });
                        console.log("Swagger: Bearer token auto-injected.");
                    }
                }
            } catch (err) {
                console.error("Swagger auto-auth failed:", err);
            }
        }

        return response;
    };

    // Auto-login khi Swagger UI sẵn sàng.
    const DEV_EMAIL = "";
    const DEV_PASSWORD = "";

    function autoLogin() {
        fetch("/api/auth/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ email: DEV_EMAIL, password: DEV_PASSWORD })
        }).catch(() => console.warn("Swagger auto-login failed — server chưa sẵn sàng?"));
    }

    // Đợi window.ui khởi tạo xong rồi mới login.
    const interval = setInterval(() => {
        if (window.ui && window.ui.authActions) {
            clearInterval(interval);
            autoLogin();
        }
    }, 300);
})();
