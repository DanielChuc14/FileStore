export const environment = {
  production: false,

  // La API se consume bajo /api, que proxy.conf.json reenvia al backend. El
  // prefijo evita que rutas del panel (/files, /trash) choquen con endpoints
  // de la API, y mantiene un unico origen, que es lo que necesita la cookie
  // httpOnly: con Secure y SameSite=Strict, otro origen la bloquearia.
  apiBaseUrl: '/api/v1',
};
