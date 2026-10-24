import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
  vus: 30,
  duration: '15m',
};

export default function () {
  // Reemplaza con tu URL real
  const baseUrl = 'http://ec2-100-52-153-159.compute-1.amazonaws.com';
  // 1. Entrar a la tienda
  http.get(`${baseUrl}/Cart`);

  // 2. Agregar al carrito
  http.post(`${baseUrl}/Cart/AddToCart`, {
    productId: '00000000-0000-0000-0000-000000000001',
    quantity: '1',
  });

  sleep(10);
}