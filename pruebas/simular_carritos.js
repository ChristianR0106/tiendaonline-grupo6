import http from 'k6/http';
import { sleep, check } from 'k6';

export const options = {
  vus: 30,
  duration: '15m',
};

export default function () {
  const baseUrl = 'http://ec2-100-52-153-159.compute-1.amazonaws.com';

  // 1. Obtener la portada para extraer la cookie de sesión y el token Anti-forgery
  const homeRes = http.get(`${baseUrl}/`);
  
  check(homeRes, {
    'home status 200': (r) => r.status === 200,
  });

  // Extraer el token anti-forgery del formulario HTML
  let token = '';
  const match = homeRes.body.match(/name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"/);
  if (match) {
    token = match[1];
  }

  // 2. Enviar el POST exacto que espera CartController.cs (Acción: Add, Campo: id)
  const payload = {
    id: '1', // ID entero del producto existente (ejemplo: 1)
    quantity: '1',
    __RequestVerificationToken: token
  };

  const headers = {
    'Content-Type': 'application/x-www-form-urlencoded',
  };

  const cartRes = http.post(`${baseUrl}/Cart/Add`, payload, { headers });

  check(cartRes, {
    'cart status 200 or 302': (r) => r.status === 200 || r.status === 302,
  });

  sleep(10);
}