import http from 'k6/http';
import { sleep, check } from 'k6';

export const options = {
  duration: '5m',
  vus: 100, // Simula alta concurrencia
};

export default function () {
  // Reemplaza <ALB-DNS-NAME> por la URL pública de tu Load Balancer / App
  const res = http.get('http://<ALB-DNS-NAME>/');
  
  check(res, {
    'status es 200': (r) => r.status === 200,
  });

  sleep(1);
}