export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
  createdAt: string;
}

export interface AuthResponse {
  token: string;
  expiresIn: number;
  user: UserDto;
}
