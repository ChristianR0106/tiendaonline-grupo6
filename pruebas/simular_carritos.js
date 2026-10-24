import http from 'k6/http';
import { sleep, check } from 'k6';

export const options = {
  vus: 30,
  duration: '15m',
};

export default function () {
  const baseUrl = 'http://ec2-100-52-153-159.compute-1.amazonaws.com';

  // 1. Cargar la portada
  const homeRes = http.get(`${baseUrl}/`);
  
  check(homeRes, {
    'home status 200': (r) => r.status === 200,
  });

  // Extraer token anti-forgery si existe
  let token = '';
  const match = homeRes.body.match(/name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"/);
  if (match) {
    token = match[1];
  }

  // 2. Probar la ruta real AddToCart con productId
  const payload = {
    productId: '00000000-0000-0000-0000-000000000001',
    quantity: '1',
  };

  if (token) {
    payload['__RequestVerificationToken'] = token;
  }

  const params = {
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    redirects: 0,
  };

  // RUTA CORREGIDA: /Cart/AddToCart
  const cartRes = http.post(`${baseUrl}/Cart/AddToCart`, payload, params);

  if (cartRes.status !== 200 && cartRes.status !== 302 && __ITER === 0) {
    console.log(`[ERROR] Status: ${cartRes.status} | Body: ${cartRes.body.substring(0, 150)}`);
  }

  check(cartRes, {
    'cart status 200 or 302': (r) => r.status === 200 || r.status === 302,
  });

  sleep(10);
}