// Production environment — apiBase points at the API subdomain so cross-origin requests
// (madauthor.madproducts.co.za → madauthorapi.madproducts.co.za) hit the right host.
// The auth interceptor reads this to rewrite relative /api/* and /hubs/* URLs.
export const environment = {
  production: true,
  apiBase: 'https://madauthorapi.madproducts.co.za',
};
