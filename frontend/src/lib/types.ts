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

export interface RecordingSegment {
  start: string;
  end: string;
  durationSeconds: number;
  codecType: string | null;
  playbackUrl: string;
}

export interface RecordingSearchResult {
  cameraId: string;
  cameraName: string;
  start: string;
  end: string;
  truncated: boolean;
  recordings: RecordingSegment[];
}
