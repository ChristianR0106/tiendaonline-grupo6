import http from 'k6/http';
import { sleep, check } from 'k6';

export const options = {
  vus: 30,
  duration: '15m',
};

export default function () {
  const baseUrl = 'http://ec2-100-52-153-159.compute-1.amazonaws.com';

  // 1. Obtener la página principal (para iniciar sesión / cookie)
  const homeRes = http.get(`${baseUrl}/`);
  check(homeRes, { 'home status 200': (r) => r.status === 200 });

  // 2. Agregar al carrito
  const cartRes = http.post(`${baseUrl}/Cart/AddToCart`, {
    productId: '00000000-0000-0000-0000-000000000001',
    quantity: '1',
  });

  check(cartRes, {
    'cart status 200 or 302': (r) => r.status === 200 || r.status === 302,
  });

  sleep(10);
}