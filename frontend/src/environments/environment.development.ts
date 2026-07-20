export const environment = {
  production: false,

  // Vacio a proposito: las peticiones salen relativas al propio dev server
  // (http://localhost:4200) y proxy.conf.json las reenvia a la API. Asi el
  // navegador ve un unico origen, que es lo que necesita la cookie httpOnly:
  // con Secure + SameSite=Strict, un origen https distinto la bloquearia.
  apiBaseUrl: '',
};
