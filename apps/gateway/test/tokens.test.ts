import { describe, expect, it } from 'vitest';

import {
  issueHandoffToken,
  issueSessionToken,
  verifyHandoffToken,
  verifySessionToken
} from '../src/auth/tokens.js';

describe('token helpers', () => {
  it('round-trips a session token', () => {
    const token = issueSessionToken(
      {
        sub: 'user-1',
        name: 'alice',
        role: 'admin',
        iat: 1,
        exp: Math.floor(Date.now() / 1000) + 60
      },
      'x'.repeat(32)
    );

    expect(verifySessionToken(token, 'x'.repeat(32)).name).toBe('alice');
  });

  it('round-trips a handoff token', () => {
    const token = issueHandoffToken(
      {
        sub: 'user-1',
        name: 'alice',
        role: 'user',
        iat: 1,
        exp: Math.floor(Date.now() / 1000) + 60,
        jti: 'handoff-1',
        returnTo: '/artists/1'
      },
      'y'.repeat(32)
    );

    expect(verifyHandoffToken(token, 'y'.repeat(32)).returnTo).toBe('/artists/1');
  });
});
