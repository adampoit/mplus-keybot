import fs from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import type { IncomingMessage, ServerResponse } from 'node:http';

const [certificatePath, keyPath, listenPort, targetPort] = process.argv.slice(2);

if (!certificatePath || !keyPath || !listenPort || !targetPort) {
	console.error('usage: e2e-proxy.ts <certificate> <key> <listen-port> <target-port>');
	process.exit(2);
}

const target = {
	hostname: '127.0.0.1',
	port: Number(targetPort),
};

const server = https.createServer(
	{
		cert: fs.readFileSync(certificatePath),
		key: fs.readFileSync(keyPath),
	},
	(request: IncomingMessage, response: ServerResponse<IncomingMessage>) => {
		const headers = { ...request.headers };
		delete headers.connection;
		headers.host = `${target.hostname}:${target.port}`;
		headers['x-forwarded-proto'] = 'https';
		headers['x-forwarded-host'] = request.headers.host ?? 'localhost';

		const upstream = http.request(
			{
				...target,
				method: request.method,
				path: request.url,
				headers,
			},
			(upstreamResponse) => {
				response.writeHead(upstreamResponse.statusCode ?? 502, upstreamResponse.headers);
				upstreamResponse.pipe(response);
			},
		);

		upstream.on('error', (error) => {
			console.error(error);
			if (!response.headersSent) response.writeHead(502);
			response.end('upstream unavailable');
		});

		request.on('error', (error) => {
			console.error(error);
			upstream.destroy();
		});
		request.pipe(upstream);
	},
);

server.on('error', (error) => {
	console.error(error);
	process.exitCode = 1;
});

server.listen(Number(listenPort), '127.0.0.1', () => {
	console.log(`HTTPS proxy listening on https://127.0.0.1:${listenPort}`);
});
