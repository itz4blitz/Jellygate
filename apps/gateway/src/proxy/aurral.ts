import { Readable } from 'node:stream';

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
  config: Pick<GatewayConfig, 'AURRAL_URL'>,
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
  headers.set('x-forwarded-role', session.role);

  if (request.headers.host) {
    headers.set('x-forwarded-host', request.headers.host);
  }

  headers.set('x-forwarded-proto', request.protocol);

  const init: RequestInit & { duplex?: 'half' } = {
    method: request.method,
    headers,
    body: supportsRequestBody(request.method) ? (request.raw as unknown as BodyInit) : null,
    redirect: 'manual'
  };

  if (supportsRequestBody(request.method)) {
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

  if (!response.body) {
    reply.send(await response.text());
    return;
  }

  reply.send(Readable.fromWeb(response.body as never));
}

function supportsRequestBody(method: string): boolean {
  return !['GET', 'HEAD'].includes(method.toUpperCase());
}
