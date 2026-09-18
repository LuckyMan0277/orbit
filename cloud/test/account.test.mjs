import { test } from 'node:test';
import assert from 'node:assert/strict';
import { unstable_dev } from 'wrangler';

async function withWorker(fn) {
  const worker = await unstable_dev('src/index.js', {
    config: 'wrangler.toml',
    experimental: { disableExperimentalWarning: true }
  });
  try {
    await fn(worker);
  } finally {
    await worker.stop();
  }
}

function post(worker, path, body) {
  return worker.fetch(path, { method: 'POST', body: JSON.stringify(body) });
}

function uniqueEmail() {
  return `test-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`;
}

test('signup registers the pc and login returns it back', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    const signupRes = await post(worker, '/signup', {
      email,
      password: 'correct horse battery staple',
      url: 'https://pc1.tailnet.ts.net',
      deviceToken: 'a'.repeat(32)
    });
    assert.equal(signupRes.status, 200);
    const { pushSecret } = await signupRes.json();
    assert.ok(pushSecret && pushSecret.length >= 32);

    const loginRes = await post(worker, '/login', { email, password: 'correct horse battery staple' });
    assert.equal(loginRes.status, 200);
    const loginBody = await loginRes.json();
    assert.equal(loginBody.url, 'https://pc1.tailnet.ts.net');
    assert.equal(loginBody.deviceToken, 'a'.repeat(32));
  });
});

test('signup twice with the same email is rejected', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    const first = await post(worker, '/signup', { email, password: 'correct horse battery staple', url: 'https://a.ts.net', deviceToken: 'a'.repeat(32) });
    assert.equal(first.status, 200);
    const second = await post(worker, '/signup', { email, password: 'another password entirely', url: 'https://b.ts.net', deviceToken: 'b'.repeat(32) });
    assert.equal(second.status, 409);
  });
});

test('login with the wrong password is rejected', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    await post(worker, '/signup', { email, password: 'correct horse battery staple', url: 'https://a.ts.net', deviceToken: 'a'.repeat(32) });
    const res = await post(worker, '/login', { email, password: 'wrong password' });
    assert.equal(res.status, 401);
  });
});

test('login for an account that never linked a pc fails clearly', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    await post(worker, '/account/unlink', { email, pushSecret: 'nonsense' });
    const res = await post(worker, '/login', { email, password: 'whatever12345' });
    assert.equal(res.status, 401);
  });
});

test('account/push updates the url without needing the password', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    const signupRes = await post(worker, '/signup', { email, password: 'correct horse battery staple', url: 'https://old.ts.net', deviceToken: 'a'.repeat(32) });
    const { pushSecret } = await signupRes.json();

    const pushRes = await post(worker, '/account/push', { email, pushSecret, url: 'https://new.ts.net', deviceToken: 'c'.repeat(32) });
    assert.equal(pushRes.status, 200);

    const loginRes = await post(worker, '/login', { email, password: 'correct horse battery staple' });
    const loginBody = await loginRes.json();
    assert.equal(loginBody.url, 'https://new.ts.net');
    assert.equal(loginBody.deviceToken, 'c'.repeat(32));
  });
});

test('account/push with a stale pushSecret is rejected', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    await post(worker, '/signup', { email, password: 'correct horse battery staple', url: 'https://old.ts.net', deviceToken: 'a'.repeat(32) });
    const res = await post(worker, '/account/push', { email, pushSecret: 'not-the-real-secret', url: 'https://evil.ts.net', deviceToken: 'x'.repeat(32) });
    assert.equal(res.status, 401);
  });
});

test('account/link re-attaches an existing account to a different pc and rotates the push secret', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    const signupRes = await post(worker, '/signup', { email, password: 'correct horse battery staple', url: 'https://laptop.ts.net', deviceToken: 'a'.repeat(32) });
    const { pushSecret: oldSecret } = await signupRes.json();

    const linkRes = await post(worker, '/account/link', { email, password: 'correct horse battery staple', url: 'https://desktop.ts.net', deviceToken: 'd'.repeat(32) });
    assert.equal(linkRes.status, 200);
    const { pushSecret: newSecret } = await linkRes.json();
    assert.notEqual(newSecret, oldSecret);

    const loginRes = await post(worker, '/login', { email, password: 'correct horse battery staple' });
    const loginBody = await loginRes.json();
    assert.equal(loginBody.url, 'https://desktop.ts.net');

    const oldPushRes = await post(worker, '/account/push', { email, pushSecret: oldSecret, url: 'https://should-fail.ts.net', deviceToken: 'z'.repeat(32) });
    assert.equal(oldPushRes.status, 401);
  });
});

test('account/unlink clears the pc so login reports no_pc_linked', async () => {
  await withWorker(async (worker) => {
    const email = uniqueEmail();
    const signupRes = await post(worker, '/signup', { email, password: 'correct horse battery staple', url: 'https://a.ts.net', deviceToken: 'a'.repeat(32) });
    const { pushSecret } = await signupRes.json();

    const unlinkRes = await post(worker, '/account/unlink', { email, pushSecret });
    assert.equal(unlinkRes.status, 200);

    const loginRes = await post(worker, '/login', { email, password: 'correct horse battery staple' });
    assert.equal(loginRes.status, 409);
    const body = await loginRes.json();
    assert.equal(body.error, 'no_pc_linked');
  });
});

test('GET / serves the login page and posting a login redirects into the mobile workspace', async () => {
  await withWorker(async (worker) => {
    const page = await worker.fetch('/', { method: 'GET' });
    assert.equal(page.status, 200);
    assert.match(page.headers.get('content-type') || '', /text\/html/);
    const html = await page.text();
    assert.match(html, /id="email"/);
    assert.match(html, /id="password"/);
    assert.match(html, /#login=/);
  });
});

test('rejects malformed email and short password on signup', async () => {
  await withWorker(async (worker) => {
    const badEmail = await post(worker, '/signup', { email: 'not-an-email', password: 'longenoughpassword', url: 'https://a.ts.net', deviceToken: 'a'.repeat(32) });
    assert.equal(badEmail.status, 400);

    const shortPassword = await post(worker, '/signup', { email: uniqueEmail(), password: 'short', url: 'https://a.ts.net', deviceToken: 'a'.repeat(32) });
    assert.equal(shortPassword.status, 400);
  });
});
