import path from 'node:path';
import { fileURLToPath } from 'node:url';

import cookie from '@fastify/cookie';
import fastifyStatic from '@fastify/static';
import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { z } from 'zod';

import { authenticateWithJellyfin } from './auth/jellyfin.js';
import { verifyHandoffToken } from './auth/tokens.js';
import { normalizeReturnTo, readConfig } from './config.js';
import { proxyToAurral } from './proxy/aurral.js';
import {
  clearGatewaySession,
  hasManualReauthFlag,
  readGatewaySession,
  setGatewaySession,
  setManualReauthFlag
} from './session/cookies.js';

const loginSchema = z.object({
  username: z.string().min(1),
  password: z.string().min(1),
  returnTo: z.string().optional()
});

const currentFile = fileURLToPath(import.meta.url);
const currentDir = path.dirname(currentFile);
const defaultWebRoot = path.resolve(currentDir, '../../web/dist');

const config = readConfig();
const app = Fastify({
  logger: {
    level: process.env.LOG_LEVEL ?? 'info'
  }
});

await app.register(cookie, {
  secret: config.COOKIE_SECRET
});

await app.register(fastifyStatic, {
  root: defaultWebRoot,
  prefix: `${config.UI_BASE_PATH}/`
});

app.get('/healthz', async () => ({ status: 'ok' }));

app.get('/auth/public-config', async () => ({
  appName: config.APP_NAME,
  allowPasswordLogin: config.ALLOW_PASSWORD_LOGIN,
  allowJellyfinHandoff: config.ALLOW_JELLYFIN_HANDOFF && Boolean(config.JELLYFIN_PUBLIC_URL),
  continueWithJellyfinUrl:
    config.ALLOW_JELLYFIN_HANDOFF && config.JELLYFIN_PUBLIC_URL
      ? `${config.JELLYFIN_PUBLIC_URL.replace(/\/+$/, '')}${config.JELLYFIN_BRIDGE_PATH}`
      : null,
  signupUrl: config.SIGNUP_URL ?? null,
  passwordResetUrl: config.PASSWORD_RESET_URL ?? null
}));

app.get('/auth/session', async (request) => {
  const session = readGatewaySession(request, config);

  if (!session) {
    return { authenticated: false };
  }

  return {
    authenticated: true,
    user: {
      id: session.sub,
      username: session.name,
      role: session.role
    }
  };
});

app.post('/auth/login', async (request, reply) => {
  if (!config.ALLOW_PASSWORD_LOGIN) {
    reply.code(404);
    return { error: 'Password login is disabled' };
  }

  const parsed = loginSchema.safeParse(request.body);

  if (!parsed.success) {
    reply.code(400);
    return { error: parsed.error.message };
  }

  const user = await authenticateWithJellyfin({
    baseUrl: config.JELLYFIN_URL,
    username: parsed.data.username,
    password: parsed.data.password,
    appName: config.APP_NAME,
    deviceName: `${config.APP_NAME} Gateway`,
    deviceId: 'jellygate-gateway',
    appVersion: config.APP_VERSION
  });

  setGatewaySession(reply, config, {
    sub: user.userId,
    name: user.username,
    role: user.role
  });

  return {
    ok: true,
    returnTo: normalizeReturnTo(parsed.data.returnTo)
  };
});

app.post('/auth/logout', async (_request, reply) => {
  clearGatewaySession(reply, config);
  return { ok: true };
});

app.get('/auth/handoff', async (request, reply) => {
  const querySchema = z.object({
    token: z.string().min(1),
    returnTo: z.string().optional()
  });
  const query = querySchema.parse(request.query);
  const payload = verifyHandoffToken(query.token, config.HANDOFF_SECRET);

  setGatewaySession(reply, config, {
    sub: payload.sub,
    name: payload.name,
    role: payload.role
  });

  const returnTo = normalizeReturnTo(query.returnTo ?? payload.returnTo);
  reply.redirect(returnTo);
});

app.get('/auth/continue-with-jellyfin', async (request, reply) => {
  if (!config.ALLOW_JELLYFIN_HANDOFF || !config.JELLYFIN_PUBLIC_URL) {
    reply.code(404);
    return { error: 'Jellyfin handoff is disabled' };
  }

  const query = z.object({ returnTo: z.string().optional(), manual: z.string().optional() }).parse(request.query);
  const target = new URL(`${config.JELLYFIN_PUBLIC_URL.replace(/\/+$/, '')}${config.JELLYFIN_BRIDGE_PATH}`);
  target.searchParams.set('returnTo', normalizeReturnTo(query.returnTo));

  if (query.manual === '1') {
    target.searchParams.set('manual', '1');
  }

  reply.redirect(target.toString());
});

const handleProxyRequest = async (request: FastifyRequest, reply: FastifyReply) => {
  const session = readGatewaySession(request, config);

  if (!session && isAurralLogoutRequest(request)) {
    clearGatewaySession(reply, config);
    setManualReauthFlag(reply, config);
    reply.header('Clear-Site-Data', '"cache", "storage"');
    return { success: true };
  }

  if (!session) {
    if (prefersHtml(request)) {
      const returnTo = normalizeReturnTo(request.raw.url);

      if (config.ALLOW_JELLYFIN_HANDOFF && config.JELLYFIN_PUBLIC_URL) {
        const handoffUrl = new URL('/auth/continue-with-jellyfin', 'http://local');
        handoffUrl.searchParams.set('returnTo', returnTo);

        if (hasManualReauthFlag(request)) {
          handoffUrl.searchParams.set('manual', '1');
        }

        reply.redirect(handoffUrl.pathname + handoffUrl.search);
        return;
      }

      if (config.ALLOW_PASSWORD_LOGIN) {
        const loginUrl = new URL(`${config.UI_BASE_PATH}/`, 'http://local');
        loginUrl.searchParams.set('returnTo', returnTo);
        reply.redirect(loginUrl.pathname + loginUrl.search);
        return;
      }
    }

    reply.code(401);
    return { error: 'Authentication required' };
  }

  if (isAurralLogoutRequest(request)) {
    clearGatewaySession(reply, config);
    setManualReauthFlag(reply, config);
    reply.header('Clear-Site-Data', '"cache", "storage"');
  }

  await proxyToAurral(request, reply, config, session);
};

app.all('/', handleProxyRequest);
app.all('/*', handleProxyRequest);

await app.listen({
  host: config.HOST,
  port: config.PORT
});

function prefersHtml(request: FastifyRequest): boolean {
  const accept = request.headers.accept;
  const raw = Array.isArray(accept) ? accept.join(',') : typeof accept === 'string' ? accept : '';
  return raw.includes('text/html');
}

function isAurralLogoutRequest(request: FastifyRequest): boolean {
  const path = request.raw.url?.split('?')[0] ?? '';
  return request.method.toUpperCase() === 'POST' && path === '/api/auth/logout';
}
