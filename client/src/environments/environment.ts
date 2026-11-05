export const environment = {
  production: false,
  // Relative on purpose. In deployed environments CloudFront routes /api/* to the
  // ALB, so the same built artifact works in every environment. Locally the dev
  // server proxies /api to the API (see proxy.conf.json).
  apiUrl: '/api/v1',
  // Minutes without activity before the warning, and before the sign-out, plus how often an active
  // person's session is renewed. The API's Session:IdleLifetime must be longer than the sign-out
  // time plus the renewal. Set them long here while developing, but do not commit that.
  idle: { warnAfterMinutes: 15, signOutAfterMinutes: 20, heartbeatMinutes: 10 },
};
