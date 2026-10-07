interface TokenClaims {
  jti?: string;
  tenant_id?: string;
  iat?: number;
  exp?: number;
}

const base64Url = (value: object): string =>
  btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');

// Shaped like the tokens the API issues. The signature is fake because the client never checks it.
export function makeToken(claims: TokenClaims = {}): string {
  return [
    base64Url({ alg: 'HS256', typ: 'JWT' }),
    base64Url({ sub: 'u1', tenant_id: 'tenant-42', ...claims }),
    'signature',
  ].join('.');
}

// The dates are far in the past on purpose: only the lifetime may matter, never this machine's clock.
export const tokenLivingFor = (seconds: number, jti: string): string =>
  makeToken({ jti, iat: 1_000, exp: 1_000 + seconds });
