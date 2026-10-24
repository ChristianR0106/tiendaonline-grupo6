import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
  vus: 30,
  duration: '15m',
};

export default function () {
  // Simular sesión única por VU
  const res = http.get('http://<ALB-DNS-NAME>/Cart');
  
  // Agregar producto al carrito
  http.post('http://<ALB-DNS-NAME>/Cart/AddToCart', {
    productId: '00000000-0000-0000-0000-000000000001',
    quantity: '1',
  });

  sleep(10);
}