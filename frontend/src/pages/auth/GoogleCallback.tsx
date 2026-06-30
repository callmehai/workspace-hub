import { useEffect, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useMutation } from "@tanstack/react-query";
import { isAxiosError } from "axios";
import { Loader2 } from "lucide-react";
import toast from "react-hot-toast";
import { useAuth } from "../../hooks/useAuth";
import { authApi } from "../../lib/authApi";
import type { ApiError } from "../../types/auth";

/**
 * Callback của Google Sign-In (đăng nhập app). Google redirect về đây với ?code&state,
 * ta đổi lấy JWT + user rồi login. KHÁC `/oauth/callback` (connect service để sync).
 */
export const GoogleCallback = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { login } = useAuth();

  const code = searchParams.get("code");
  const state = searchParams.get("state");

  const { mutate, isError } = useMutation({
    mutationFn: () => authApi.googleCallback(code!, state!),
    onSuccess: (data) => {
      login(data.accessToken, data.user);
      toast.success("Đăng nhập thành công!");
      navigate("/", { replace: true });
    },
    onError: (err) => {
      const message = isAxiosError<ApiError>(err)
        ? err.response?.data?.message
        : undefined;
      toast.error(message ?? "Đăng nhập bằng Google thất bại.");
    },
  });

  const called = useRef(false);
  useEffect(() => {
    if (called.current) return;
    called.current = true;

    if (!code || !state) {
      toast.error("Đăng nhập thất bại");
      setTimeout(() => navigate("/login", { replace: true }), 2000);
      return;
    }
    mutate();
  }, [code, state, mutate, navigate]);

  const failed = isError || !code || !state;

  return (
    <div className="flex min-h-screen w-full items-center justify-center bg-gray-50">
      {failed ? (
        <div className="flex flex-col items-center space-y-4">
          <p className="font-medium text-red-500">
            Đăng nhập bằng Google thất bại.
          </p>
          <button
            onClick={() => navigate("/login", { replace: true })}
            className="rounded-md bg-brand-600 px-4 py-2 text-white transition-colors hover:bg-brand-700"
          >
            Quay lại đăng nhập
          </button>
        </div>
      ) : (
        <div className="flex flex-col items-center space-y-4">
          <Loader2 className="h-8 w-8 animate-spin text-brand-600" />
          <p className="font-medium text-gray-600">
            Đang đăng nhập bằng Google...
          </p>
        </div>
      )}
    </div>
  );
};
