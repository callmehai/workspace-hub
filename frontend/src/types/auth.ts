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
