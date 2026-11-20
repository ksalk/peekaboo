export interface User {
  id: string;
  username: string;
  isAdmin: boolean;
  createdAt: string;
}

export interface Nvr {
  id: string;
  name: string;
  baseUrl: string;
  createdAt: string;
  updatedAt: string;
}

export type CameraStream = 'main' | 'sub';

export interface Camera {
  id: string;
  nvrId: string;
  nvrName: string;
  name: string;
  mainStreamId: string;
  subStreamId: string;
  createdAt: string;
  updatedAt: string;
}
