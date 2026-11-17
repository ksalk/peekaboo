import { expect, type APIRequestContext, type APIResponse } from '@playwright/test';
import { feedUrl } from './env';

export interface CameraDto {
  id: string;
  name: string;
  url: string;
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

export async function createCamera(
  request: APIRequestContext,
  name: string,
  feedPath = 'feed1',
): Promise<CameraDto> {
  const response = await request.post('/api/cameras', {
    data: { name, url: feedUrl(feedPath) },
  });

  return expectCreated<CameraDto>(response);
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
