import type { APIRequestContext, APIResponse } from '@playwright/test';
import { nvrBaseUrl, uniqueName } from './env';

export interface NvrDto {
  id: string;
  name: string;
  baseUrl: string;
}

export interface CameraDto {
  id: string;
  nvrId: string;
  nvrName: string;
  name: string;
  mainStreamId: string;
  subStreamId: string;
}

export interface UserDto {
  id: string;
  username: string;
  isAdmin: boolean;
}

async function expectCreated<T>(response: APIResponse): Promise<T> {
  if (response.status() !== 201) {
    throw new Error(`Request failed: ${response.status()} ${await response.text()}`);
  }

  return (await response.json()) as T;
}

export async function createNvr(
  request: APIRequestContext,
  name: string,
  baseUrl = nvrBaseUrl,
): Promise<NvrDto> {
  const response = await request.post('/api/nvrs', {
    data: { name, baseUrl },
  });

  return expectCreated<NvrDto>(response);
}

export async function createCamera(
  request: APIRequestContext,
  nvrId: string,
  name: string,
  mainStreamId: string,
  subStreamId: string,
): Promise<CameraDto> {
  const response = await request.post(`/api/nvrs/${encodeURIComponent(nvrId)}/cameras`, {
    data: { name, mainStreamId, subStreamId },
  });

  return expectCreated<CameraDto>(response);
}

export async function createCameraFixture(
  request: APIRequestContext,
  name: string,
  mainStreamId: string,
  subStreamId: string,
): Promise<CameraDto> {
  const nvr = await createNvr(request, uniqueName('NVR'));
  return createCamera(request, nvr.id, name, mainStreamId, subStreamId);
}

export async function createUser(
  request: APIRequestContext,
  username: string,
  password: string,
  isAdmin = false,
): Promise<UserDto> {
  const response = await request.post('/api/users', {
    data: { username, password, isAdmin },
  });

  return expectCreated<UserDto>(response);
}

export async function deleteUser(request: APIRequestContext, id: string): Promise<void> {
  await request.delete(`/api/users/${id}`);
}
