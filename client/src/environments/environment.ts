export const environment = {
  production: false,
  // Relative on purpose. In deployed environments CloudFront routes /api/* to the
  // ALB, so the same built artifact works in every environment. Locally the dev
  // server proxies /api to the API (see proxy.conf.json).
  apiUrl: '/api/v1',
};
