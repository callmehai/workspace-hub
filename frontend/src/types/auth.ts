export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
}

/** Khớp AuthResponse của backend: { accessToken, expiresIn, user }. */
export interface AuthResponse {
  accessToken: string;
  expiresIn: number;
  user: UserDto;
}

/** Error envelope chuẩn của backend (ExceptionMiddleware, SCRUM-24). */
export interface ApiError {
  error: string;
  message: string;
  details: string[];
  traceId: string;
}
