import type { FastifyReply, FastifyRequest } from 'fastify';

import type { GatewayConfig } from '../config.js';
import { issueSessionToken, verifySessionToken, type SessionTokenPayload } from '../auth/tokens.js';

const manualReauthCookieName = 'jellygate_manual_reauth';

export function readGatewaySession(
  request: FastifyRequest,
  config: Pick<GatewayConfig, 'COOKIE_NAME' | 'COOKIE_SECRET'>
): SessionTokenPayload | null {
  const raw = request.cookies[config.COOKIE_NAME];

  if (!raw) {
    return null;
  }

  try {
    return verifySessionToken(raw, config.COOKIE_SECRET);
  } catch {
    return null;
  }
}

export function setGatewaySession(
  reply: FastifyReply,
  config: Pick<GatewayConfig, 'COOKIE_NAME' | 'COOKIE_SECRET' | 'COOKIE_SECURE' | 'SESSION_TTL_SECONDS'>,
  user: Omit<SessionTokenPayload, 'iss' | 'iat' | 'exp'>
): void {
  const issuedAt = Math.floor(Date.now() / 1000);
  const token = issueSessionToken(
    {
      ...user,
      iat: issuedAt,
      exp: issuedAt + config.SESSION_TTL_SECONDS
    },
    config.COOKIE_SECRET
  );

  reply.setCookie(config.COOKIE_NAME, token, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    secure: config.COOKIE_SECURE,
    maxAge: config.SESSION_TTL_SECONDS
  });

  clearManualReauth(reply);
}

export function clearGatewaySession(
  reply: FastifyReply,
  config: Pick<GatewayConfig, 'COOKIE_NAME'>
): void {
  reply.clearCookie(config.COOKIE_NAME, {
    path: '/'
  });
}

export function hasManualReauthFlag(request: FastifyRequest): boolean {
  return request.cookies[manualReauthCookieName] === '1';
}

export function setManualReauthFlag(
  reply: FastifyReply,
  config: Pick<GatewayConfig, 'COOKIE_SECURE'>
): void {
  reply.setCookie(manualReauthCookieName, '1', {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    secure: config.COOKIE_SECURE,
    maxAge: 600
  });
}

export function clearManualReauth(reply: FastifyReply): void {
  reply.clearCookie(manualReauthCookieName, {
    path: '/'
  });
}
