import http from 'k6/http';
import { sleep, check } from 'k6';

export const options = {
  vus: 30,
  duration: '15m',
};

export default function () {
  const baseUrl = 'http://ec2-100-52-153-159.compute-1.amazonaws.com';

  // 1. Cargar portada
  const homeRes = http.get(`${baseUrl}/`);
  
  check(homeRes, {
    'home status 200': (r) => r.status === 200,
  });

  // Extraer Token CSRF de ASP.NET Core si existe
  let token = '';
  const match = homeRes.body.match(/name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"/);
  if (match) {
    token = match[1];
  }

  // 2. Enviar datos exactamente como los espera CartController.cs (Add, id)
  const payload = {
    id: '1',
    quantity: '1',
  };

  if (token) {
    payload['__RequestVerificationToken'] = token;
  }

  const params = {
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
  };

  // Enviar el POST a /Cart/Add
  const cartRes = http.post(`${baseUrl}/Cart/Add`, payload, params);

  // Mostrar error exacto si ocurre en la primera vuelta
  if (cartRes.status !== 200 && cartRes.status !== 302 && __ITER === 0) {
    console.log(`[RESPUESTA SERVIDOR] Status: ${cartRes.status} | URL: ${cartRes.url}`);
  }

  check(cartRes, {
    'cart status OK': (r) => r.status === 200 || r.status === 302,
  });

  sleep(10);
}