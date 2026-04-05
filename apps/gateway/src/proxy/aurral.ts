import type { FastifyReply, FastifyRequest } from 'fastify';

import type { GatewayConfig } from '../config.js';
import type { SessionTokenPayload } from '../auth/tokens.js';

const hopByHopHeaders = new Set([
  'connection',
  'content-length',
  'host',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'te',
  'trailers',
  'transfer-encoding',
  'upgrade',
  'x-forwarded-user',
  'x-forwarded-role'
]);

export async function proxyToAurral(
  request: FastifyRequest,
  reply: FastifyReply,
  config: Pick<GatewayConfig, 'AURRAL_URL' | 'AURRAL_FORWARD_ADMIN_ROLE'>,
  session: Pick<SessionTokenPayload, 'name' | 'role'>
): Promise<void> {
  const target = new URL(request.raw.url ?? '/', config.AURRAL_URL);
  const headers = new Headers();

  for (const [key, value] of Object.entries(request.headers)) {
    if (hopByHopHeaders.has(key) || value === undefined) {
      continue;
    }

    if (Array.isArray(value)) {
      headers.set(key, value.join(', '));
      continue;
    }

    headers.set(key, value);
  }

  headers.set('x-forwarded-user', session.name);
  headers.set('x-forwarded-role', getForwardedRole(session, config));

  if (request.headers.host) {
    headers.set('x-forwarded-host', request.headers.host);
  }

  headers.set('x-forwarded-proto', request.protocol);

  const requestBody = getProxyRequestBody(request);

  const init: RequestInit & { duplex?: 'half' } = {
    method: request.method,
    headers,
    redirect: 'manual'
  };

  if (requestBody !== null) {
    init.body = requestBody;
    init.duplex = 'half';
  }

  const response = await fetch(target, init);

  reply.code(response.status);

  response.headers.forEach((value, key) => {
    if (key === 'content-length' || key === 'transfer-encoding') {
      return;
    }

    reply.header(key, value);
  });

  const responseBody = Buffer.from(await response.arrayBuffer());
  reply.send(responseBody);
}

function supportsRequestBody(method: string): boolean {
  return !['GET', 'HEAD'].includes(method.toUpperCase());
}

function getForwardedRole(
  session: Pick<SessionTokenPayload, 'role'>,
  config: Pick<GatewayConfig, 'AURRAL_FORWARD_ADMIN_ROLE'>
): SessionTokenPayload['role'] {
  if (session.role === 'admin' && !config.AURRAL_FORWARD_ADMIN_ROLE) {
    return 'user';
  }

  return session.role;
}

function getProxyRequestBody(request: FastifyRequest): BodyInit | null {
  if (!supportsRequestBody(request.method)) {
    return null;
  }

  const body = request.body;

  if (body === undefined || body === null) {
    return null;
  }

  if (typeof body === 'string' || body instanceof ArrayBuffer) {
    return body;
  }

  if (Buffer.isBuffer(body)) {
    return body as unknown as BodyInit;
  }

  if (body instanceof Uint8Array) {
    return body as unknown as BodyInit;
  }

  return JSON.stringify(body);
}
