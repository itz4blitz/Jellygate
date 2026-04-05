export interface PublicConfig {
  appName: string;
  allowPasswordLogin: boolean;
  allowJellyfinHandoff: boolean;
  continueWithJellyfinUrl: string | null;
  signupUrl: string | null;
  passwordResetUrl: string | null;
}

export interface SessionResponse {
  authenticated: boolean;
  user?: {
    id: string;
    username: string;
    role: 'admin' | 'user';
  };
}

export async function fetchPublicConfig(): Promise<PublicConfig> {
  const response = await fetch('/auth/public-config');
  return response.json() as Promise<PublicConfig>;
}

export async function fetchSession(): Promise<SessionResponse> {
  const response = await fetch('/auth/session');
  return response.json() as Promise<SessionResponse>;
}

export async function login(input: {
  username: string;
  password: string;
  returnTo: string;
}): Promise<{ ok: boolean; returnTo: string }> {
  const response = await fetch('/auth/login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(input)
  });

  if (!response.ok) {
    const error = (await response.json()) as { error?: string };
    throw new Error(error.error ?? 'Login failed');
  }

  return response.json() as Promise<{ ok: boolean; returnTo: string }>;
}
