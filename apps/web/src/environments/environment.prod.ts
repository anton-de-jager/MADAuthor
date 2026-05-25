// Production environment - apiBase points at the API subdomain so cross-origin requests
// (madauthor.madprospects.com -> madauthorapi.madprospects.com) hit the right host.
// The auth interceptor reads this to rewrite relative /api/* and /hubs/* URLs.
export const environment = {
  production: true,
  apiBase: 'https://madauthorapi.madprospects.com',
};
