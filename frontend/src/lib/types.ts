export interface User {
  id: string;
  username: string;
  isAdmin: boolean;
  createdAt: string;
}

export interface Camera {
  id: string;
  name: string;
  url: string;
  createdAt: string;
  updatedAt: string;
}
