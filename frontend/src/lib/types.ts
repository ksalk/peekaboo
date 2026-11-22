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
  transport: 'rtsp' | 'sdk';
  sdkPort: number;
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

export type AlarmTarget = 'human' | 'vehicle' | 'unknown';

export interface AlarmRange {
  cameraId: string;
  cameraName: string;
  nvrId: string;
  nvrName: string;
  channelId: number;
  start: string;
  end: string;
  durationSeconds: number;
  target: AlarmTarget;
  targetLabel: string | null;
  inferredStart: boolean;
  inferredEnd: boolean;
}

export interface AlarmSearchResult {
  start: string;
  end: string;
  truncated: boolean;
  ranges: AlarmRange[];
  errors: AlarmSearchError[];
}

export interface AlarmSearchError {
  nvrId: string;
  nvrName: string;
  message: string;
}
