// Dev environment — apiBase is empty so requests stay relative ("/api/...") and the
// Angular dev server's proxy.conf.json forwards them to localhost:5150.
export const environment = {
  production: false,
  apiBase: '',
};
