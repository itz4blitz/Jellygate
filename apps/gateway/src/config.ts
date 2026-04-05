import { z } from 'zod';

const booleanish = z
  .union([z.boolean(), z.string()])
  .transform((value) => {
    if (typeof value === 'boolean') {
      return value;
    }

    return value.toLowerCase() === 'true';
  });

const envSchema = z.object({
  HOST: z.string().default('0.0.0.0'),
  PORT: z.coerce.number().int().positive().default(3000),
  APP_NAME: z.string().default('Jellygate'),
  APP_VERSION: z.string().default('0.1.0'),
  UI_BASE_PATH: z.string().default('/__jellygate'),
  JELLYFIN_URL: z.url(),
  JELLYFIN_PUBLIC_URL: z.url().optional(),
  JELLYFIN_BRIDGE_PATH: z.string().default('/JellygateBridge/start'),
  AURRAL_URL: z.url(),
  COOKIE_NAME: z.string().default('jellygate_session'),
  COOKIE_SECRET: z.string().min(32),
  HANDOFF_SECRET: z.string().min(32),
  SESSION_TTL_SECONDS: z.coerce.number().int().positive().default(43200),
  ALLOW_PASSWORD_LOGIN: booleanish.default(true),
  ALLOW_JELLYFIN_HANDOFF: booleanish.default(true),
  SIGNUP_URL: z.url().optional().or(z.literal('')).transform((value) => value || undefined),
  PASSWORD_RESET_URL: z
    .url()
    .optional()
    .or(z.literal(''))
    .transform((value) => value || undefined)
});

export type GatewayConfig = z.infer<typeof envSchema>;

export function readConfig(env: NodeJS.ProcessEnv = process.env): GatewayConfig {
  const parsed = envSchema.safeParse(env);

  if (!parsed.success) {
    throw new Error(`Invalid Jellygate configuration: ${parsed.error.message}`);
  }

  return {
    ...parsed.data,
    UI_BASE_PATH: normalizeUiBasePath(parsed.data.UI_BASE_PATH),
    JELLYFIN_BRIDGE_PATH: normalizeBridgePath(parsed.data.JELLYFIN_BRIDGE_PATH)
  };
}

export function normalizeReturnTo(input: string | undefined): string {
  if (!input || !input.startsWith('/') || input.startsWith('//')) {
    return '/';
  }

  return input;
}

function normalizeUiBasePath(input: string): string {
  const trimmed = input.trim();

  if (trimmed === '' || trimmed === '/') {
    return '/__jellygate';
  }

  return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
}

function normalizeBridgePath(input: string): string {
  const trimmed = input.trim();

  if (trimmed === '' || trimmed === '/') {
    return '/JellygateBridge/start';
  }

  return trimmed.startsWith('/') ? trimmed : `/${trimmed}`;
}
