const net = require('net');
const pipePath = '\\\\.\\pipe\\opencode-browser';
console.log('Connecting to:', pipePath);
const sock = net.createConnection(pipePath);
sock.on('connect', () => { console.log('CONNECTED'); sock.end(); process.exit(0); });
sock.on('error', (e) => { console.log('ERROR:', e.code, e.message); process.exit(1); });
setTimeout(() => { console.log('TIMEOUT after 3s'); process.exit(1); }, 3000);
