import { hashPassword, verifyPassword, randomToken, timingSafeEqual } from './crypto.js';
import { LOGIN_PAGE_HTML } from './login-page.js';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const MIN_PASSWORD = 8;
const MAX_PASSWORD = 256;
const RATE_LIMIT_WINDOW_SECONDS = 300;
const RATE_LIMIT_MAX = 10;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    // The one stable, memorable URL: open it from any device on any network, log
    // in, and land straight in the linked PC's mobile workspace -- no QR/link to
    // carry over first.
    if (request.method === 'GET' && url.pathname === '/') return new Response(LOGIN_PAGE_HTML, { status: 200, headers: { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' } });
    if (request.method !== 'POST') return json({ error: 'method_not_allowed' }, 405);
    try {
      switch (url.pathname) {
        case '/signup': return await signup(request, env);
        case '/login': return await login(request, env);
        case '/account/link': return await link(request, env);
        case '/account/push': return await push(request, env);
        case '/account/unlink': return await unlink(request, env);
        default: return json({ error: 'not_found' }, 404);
      }
    } catch (err) {
      if (err && err.status) return json({ error: err.message }, err.status);
      return json({ error: 'bad_request', message: String((err && err.message) || err) }, 400);
    }
  }
};

function json(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
}

function fail(message, status) {
  return Object.assign(new Error(message), { status });
}

function normalizeEmail(email) {
  if (typeof email !== 'string') throw fail('invalid_email', 400);
  const trimmed = email.trim().toLowerCase();
  if (trimmed.length > 320 || !EMAIL_RE.test(trimmed)) throw fail('invalid_email', 400);
  return trimmed;
}

function requireString(value, name, { min = 1, max = 4096 } = {}) {
  if (typeof value !== 'string' || value.length < min || value.length > max) throw fail(`invalid_${name}`, 400);
  return value;
}

async function readJson(request) {
  try {
    return await request.json();
  } catch {
    throw fail('invalid_json', 400);
  }
}

async function rateLimit(env, key) {
  const countKey = `rate:${key}`;
  const current = parseInt((await env.ACCOUNTS.get(countKey)) || '0', 10);
  if (current >= RATE_LIMIT_MAX) throw fail('rate_limited', 429);
  await env.ACCOUNTS.put(countKey, String(current + 1), { expirationTtl: RATE_LIMIT_WINDOW_SECONDS });
}

async function getAccount(env, email) {
  const raw = await env.ACCOUNTS.get(`account:${email}`);
  return raw ? JSON.parse(raw) : null;
}

async function putAccount(env, email, record) {
  await env.ACCOUNTS.put(`account:${email}`, JSON.stringify(record));
}

function requirePcFields(body) {
  return {
    url: requireString(body.url, 'url', { min: 1, max: 2048 }),
    deviceToken: requireString(body.deviceToken, 'deviceToken', { min: 16, max: 256 })
  };
}

async function signup(request, env) {
  const body = await readJson(request);
  const email = normalizeEmail(body.email);
  const password = requireString(body.password, 'password', { min: MIN_PASSWORD, max: MAX_PASSWORD });
  const { url, deviceToken } = requirePcFields(body);
  await rateLimit(env, `signup:${email}`);
  if (await getAccount(env, email)) return json({ error: 'account_exists' }, 409);
  const { hash, salt, iterations } = await hashPassword(password);
  const pushSecret = randomToken();
  const now = Date.now();
  await putAccount(env, email, {
    passwordHash: hash,
    passwordSalt: salt,
    iterations,
    url,
    deviceToken,
    pushSecret,
    createdAt: now,
    updatedAt: now
  });
  return json({ pushSecret });
}

async function login(request, env) {
  const body = await readJson(request);
  const email = normalizeEmail(body.email);
  const password = requireString(body.password, 'password', { min: 1, max: MAX_PASSWORD });
  await rateLimit(env, `login:${email}`);
  const account = await getAccount(env, email);
  if (!account || !(await verifyPassword(password, account))) return json({ error: 'invalid_credentials' }, 401);
  if (!account.url || !account.deviceToken) return json({ error: 'no_pc_linked' }, 409);
  return json({ url: account.url, deviceToken: account.deviceToken });
}

async function link(request, env) {
  const body = await readJson(request);
  const email = normalizeEmail(body.email);
  const password = requireString(body.password, 'password', { min: 1, max: MAX_PASSWORD });
  const { url, deviceToken } = requirePcFields(body);
  await rateLimit(env, `link:${email}`);
  const account = await getAccount(env, email);
  if (!account || !(await verifyPassword(password, account))) return json({ error: 'invalid_credentials' }, 401);
  const pushSecret = randomToken();
  await putAccount(env, email, { ...account, url, deviceToken, pushSecret, updatedAt: Date.now() });
  return json({ pushSecret });
}

async function push(request, env) {
  const body = await readJson(request);
  const email = normalizeEmail(body.email);
  const pushSecret = requireString(body.pushSecret, 'pushSecret', { min: 16, max: 256 });
  const { url, deviceToken } = requirePcFields(body);
  const account = await getAccount(env, email);
  if (!account || !account.pushSecret || !timingSafeEqual(account.pushSecret, pushSecret)) return json({ error: 'invalid_credentials' }, 401);
  await putAccount(env, email, { ...account, url, deviceToken, updatedAt: Date.now() });
  return json({ ok: true });
}

async function unlink(request, env) {
  const body = await readJson(request);
  const email = normalizeEmail(body.email);
  const pushSecret = requireString(body.pushSecret, 'pushSecret', { min: 16, max: 256 });
  const account = await getAccount(env, email);
  if (!account || !account.pushSecret || !timingSafeEqual(account.pushSecret, pushSecret)) return json({ error: 'invalid_credentials' }, 401);
  await putAccount(env, email, { ...account, url: null, deviceToken: null, pushSecret: null, updatedAt: Date.now() });
  return json({ ok: true });
}
