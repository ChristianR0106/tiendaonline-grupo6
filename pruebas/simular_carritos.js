import http from 'k6/http';
import { sleep, check } from 'k6';

export const options = {
  vus: 30,
  duration: '15m',
};

export default function () {
  const baseUrl = 'http://ec2-100-52-153-159.compute-1.amazonaws.com';

  // 1. Obtener la página principal
  const homeRes = http.get(`${baseUrl}/`);
  
  check(homeRes, {
    'home status 200': (r) => r.status === 200,
  });

  // Extraer el token anti-forgery del HTML (si existe)
  let token = '';
  const match = homeRes.body.match(/name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"/);
  if (match) {
    token = match[1];
  }

  // 2. Preparar el cuerpo de la petición POST
  const payload = {
    id: 1,
    quantity: 1,
    __RequestVerificationToken: token
  };

  const params = {
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    redirects: 0, // Para capturar el 302 Redirect de ASP.NET Core
  };

  const cartRes = http.post(`${baseUrl}/Cart/Add`, payload, params);

  // Imprimir el status en la primera iteración si falla
  if (cartRes.status !== 200 && cartRes.status !== 302 && __ITER === 0) {
    console.log(`[ERROR] Status: ${cartRes.status} | Body: ${cartRes.body.substring(0, 150)}`);
  }

  check(cartRes, {
    'cart status 200 or 302': (r) => r.status === 200 || r.status === 302,
  });

  sleep(10);
}