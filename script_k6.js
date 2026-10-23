import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 20 }, // Ramp-up a 20 usuarios
    { duration: '1m', target: 50 },  // Carga constante de 50 usuarios
    { duration: '30s', target: 0 },  // Ramp-down
  ],
};

const queries = [
  'algo para escuchar musica',
  'laptop para programar',
  'teclado mecanico rgb',
  'calzado deportivo',
  'monitor gaming 144hz'
];

export default function () {
  const q = queries[Math.floor(Math.random() * queries.length)];
   http.get(`https://d2kj552bpbvsbn.cloudfront.net/?q=${encodeURIComponent(q)}`);
  sleep(1);
}
