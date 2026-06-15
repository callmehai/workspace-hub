export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
<<<<<<< HEAD
  createdAt: string;
}

export interface AuthResponse {
  token: string;
=======
}

/** Khớp AuthResponse của backend: { accessToken, expiresIn, user }. */
export interface AuthResponse {
  accessToken: string;
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
  expiresIn: number;
  user: UserDto;
}
