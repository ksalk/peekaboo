import { ApiError, UNAUTHORIZED_EVENT, api } from './api';
import type { User } from './types';

class AuthState {
  user = $state<User | null>(null);
  loading = $state(true);

  async load(): Promise<void> {
    try {
      this.user = await api.me();
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 401) {
        console.error(error);
      }
      this.user = null;
    } finally {
      this.loading = false;
    }
  }

  async login(username: string, password: string): Promise<void> {
    this.user = await api.login(username, password);
  }

  async logout(): Promise<void> {
    try {
      await api.logout();
    } finally {
      this.user = null;
    }
  }
}

export const auth = new AuthState();

window.addEventListener(UNAUTHORIZED_EVENT, () => {
  auth.user = null;
});
