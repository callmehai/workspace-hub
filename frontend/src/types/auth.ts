export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
}

/**
 * Body trả về sau login/register/google (SCRUM-62).
 * Access token KHÔNG còn trong body — nằm trong HttpOnly cookie wh_access.
 */
export interface AuthResultDto {
  expiresIn: number;
  user: UserDto;
}

/** Khớp GoogleAuthStartResponse: { authorizationUrl, state }. */
export interface GoogleAuthStartResponse {
  authorizationUrl: string;
  state: string;
}

/** Error envelope chuẩn của backend (ExceptionMiddleware, SCRUM-24). */
export interface ApiError {
  error: string;
  message: string;
  details: string[];
  traceId: string;
}
