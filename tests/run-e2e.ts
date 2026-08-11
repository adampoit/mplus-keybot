import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync, readFileSync, rmSync, type WriteStream } from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const [apiPackage, webPackage] = process.argv.slice(2);
if (!apiPackage || !webPackage || process.argv.length !== 4) {
	console.error(`usage: ${process.argv[1]} <api-package> <web-package>`);
	process.exit(2);
}

const repoRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const apiPackagePath = resolve(apiPackage);
const webPackagePath = resolve(webPackage);
const runRoot = join(process.env.RUNNER_TEMP || process.env.TMPDIR || '/tmp', 'mplus-keybot-e2e');
const logRoot = join(runRoot, 'logs');

const apiPort = 8082;
const webPort = 8083;
const testServicesPort = 5010;
const proxyPort = 8443;
const pathBase = '/mplus-keybot';
const publicBase = `https://127.0.0.1:${proxyPort}${pathBase}`;
const databasePath = join(runRoot, 'mplus-data.db');
const certificatePath = join(runRoot, 'certificate.pem');
const keyPath = join(runRoot, 'key.pem');

type ManagedProcess = {
	name: string;
	child: ChildProcess;
	logPath: string;
	logStream: WriteStream;
	error?: Error;
};

const processes: ManagedProcess[] = [];
let cleaning = false;

function startProcess(
	name: string,
	command: string,
	args: string[],
	environment: Record<string, string> = {},
	showOutput = false,
) {
	const logPath = join(logRoot, `${name}.log`);
	const logStream = createWriteStream(logPath);
	const child = spawn(command, args, {
		env: { ...process.env, ...environment },
		stdio: ['ignore', 'pipe', 'pipe'],
	});
	if (showOutput) {
		child.stdout?.on('data', (chunk) => {
			process.stdout.write(chunk);
			logStream.write(chunk);
		});
		child.stderr?.on('data', (chunk) => {
			process.stderr.write(chunk);
			logStream.write(chunk);
		});
	} else {
		child.stdout?.pipe(logStream);
		child.stderr?.pipe(logStream);
	}
	const processInfo: ManagedProcess = { name, child, logPath, logStream };
	child.once('error', (error) => {
		processInfo.error = error;
	});
	processes.push(processInfo);
	return processInfo;
}

function waitForExit(processInfo: ManagedProcess): Promise<number> {
	if (processInfo.child.exitCode !== null) return Promise.resolve(processInfo.child.exitCode);

	return new Promise((resolve) => {
		processInfo.child.once('close', (code) => resolve(code ?? 1));
	});
}

function requestStatus(urlValue: string): Promise<number> {
	const url = new URL(urlValue);
	const options = {
		hostname: url.hostname,
		port: url.port || undefined,
		path: `${url.pathname}${url.search}`,
		method: 'GET',
		...(url.protocol === 'https:' ? { rejectUnauthorized: false } : {}),
	};

	const client = url.protocol === 'https:' ? https : http;
	return new Promise((resolve, reject) => {
		const request = client.request(options, (response) => {
			const status = response.statusCode ?? 0;
			response.resume();
			response.once('end', () => resolve(status));
		});
		request.setTimeout(5000, () => request.destroy(new Error('request timed out')));
		request.once('error', reject);
		request.end();
	});
}

async function waitForHttp(name: string, url: string, processInfo: ManagedProcess) {
	for (let attempt = 0; attempt < 120; attempt++) {
		if (processInfo.error) throw new Error(`${name} failed to start: ${processInfo.error.message}`);
		if (processInfo.child.exitCode !== null) throw new Error(`${name} exited before becoming ready`);

		try {
			const status = await requestStatus(url);
			if (status >= 200 && status < 400) return;
		} catch {
			// The service may still be starting.
		}

		await new Promise((resolve) => setTimeout(resolve, 500));
	}

	throw new Error(`Timed out waiting for ${name} at ${url}`);
}

function generateCertificate() {
	const result = spawnSync(
		'openssl',
		[
			'req',
			'-x509',
			'-newkey',
			'rsa:2048',
			'-nodes',
			'-keyout',
			keyPath,
			'-out',
			certificatePath,
			'-subj',
			'/CN=localhost',
			'-days',
			'1',
		],
		{ stdio: 'ignore' },
	);
	if (result.error) throw result.error;
	if (result.status !== 0) throw new Error(`openssl exited with status ${result.status}`);
}

async function finish(status: number): Promise<never> {
	if (cleaning) return new Promise(() => undefined);
	cleaning = true;

	for (const processInfo of processes) {
		if (processInfo.child.exitCode === null) processInfo.child.kill('SIGTERM');
	}
	await Promise.all(processes.map(waitForExit));
	for (const processInfo of processes) processInfo.logStream.end();

	if (status !== 0) {
		console.error('E2E services failed. Logs:');
		for (const processInfo of processes) {
			if (!existsSync(processInfo.logPath)) continue;
			console.error(`--- ${processInfo.logPath}`);
			console.error(readFileSync(processInfo.logPath, 'utf8'));
		}
	}

	process.exit(status);
}

async function run() {
	process.chdir(repoRoot);
	rmSync(runRoot, { recursive: true, force: true });
	mkdirSync(logRoot, { recursive: true });
	generateCertificate();

	const testServicesDll = join(
		repoRoot,
		'src/MPlusKeybot.TestServices/bin/Release/net10.0/MPlusKeybot.TestServices.dll',
	);
	if (!existsSync(testServicesDll))
		throw new Error(`Missing ${testServicesDll}; build the test projects before running E2Es.`);

	const testServices = startProcess('test-services', 'dotnet', [testServicesDll], {
		ASPNETCORE_ENVIRONMENT: 'Development',
		PORT: String(testServicesPort),
	});
	await waitForHttp(testServices.name, `http://127.0.0.1:${testServicesPort}/health`, testServices);

	const api = startProcess('api', join(apiPackagePath, 'bin/mplus-keybot'), [], {
		ASPNETCORE_ENVIRONMENT: 'Development',
		PORT: String(apiPort),
		Database__Path: databasePath,
		Discord__Token: '',
		Follow__Announcer: 'Webhook',
		Follow__WebhookUrl: `http://127.0.0.1:${testServicesPort}/announcements`,
		Blizzard__OAuthAuthority: `http://localhost:${testServicesPort}`,
		Blizzard__OAuthMetadataAddress: `http://localhost:${testServicesPort}/.well-known/openid-configuration`,
		Blizzard__ClientId: 'test',
		Blizzard__ClientSecret: 'secret',
		Blizzard__ApiBaseUrl: `http://127.0.0.1:${testServicesPort}`,
		Web__PublicBaseUrl: publicBase,
		Web__PathBase: pathBase,
	});
	await waitForHttp(api.name, `http://127.0.0.1:${apiPort}${pathBase}/api/health`, api);

	const web = startProcess('web', join(webPackagePath, 'bin/mplus-keybot-web'), [], {
		HOST: '127.0.0.1',
		PORT: String(webPort),
		NODE_ENV: 'production',
		API_BASE_URL: `http://127.0.0.1:${apiPort}`,
	});
	await waitForHttp(web.name, `http://127.0.0.1:${webPort}${pathBase}/health`, web);

	const proxy = startProcess('proxy', process.execPath, [
		'--experimental-strip-types',
		join(repoRoot, 'tests/e2e-proxy.ts'),
		certificatePath,
		keyPath,
		String(proxyPort),
		String(webPort),
	]);
	await waitForHttp(proxy.name, `${publicBase}/health`, proxy);

	const tests = startProcess(
		'tests',
		'dotnet',
		[
			'test',
			'tests/MPlusKeybot.Tests/MPlusKeybot.Tests.csproj',
			'--configuration',
			'Release',
			'--no-build',
			'--filter',
			'Category=E2E',
		],
		{
			MPLUS_KEYBOT_E2E_MODE: 'external',
			MPLUS_KEYBOT_E2E_BASE_URL: publicBase,
			MPLUS_KEYBOT_E2E_DATABASE_PATH: databasePath,
			MPLUS_KEYBOT_E2E_TEST_SERVICES_URL: `http://127.0.0.1:${testServicesPort}`,
		},
		true,
	);
	return await waitForExit(tests);
}

process.once('SIGINT', () => void finish(130));
process.once('SIGTERM', () => void finish(143));

try {
	await finish(await run());
} catch (error) {
	console.error(error instanceof Error ? error.message : error);
	await finish(1);
}
