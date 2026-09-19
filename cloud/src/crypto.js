// Cloudflare Workers reject PBKDF2 iteration counts above 100000 in production
// (local workerd does not enforce this, so tests cannot catch it).
const ITERATIONS = 100000;
const HASH = 'SHA-256';

function randomBytes(n) {
  const bytes = new Uint8Array(n);
  crypto.getRandomValues(bytes);
  return bytes;
}

function toHex(bytes) {
  return [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');
}

function fromHex(hex) {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < bytes.length; i++) bytes[i] = parseInt(hex.substr(i * 2, 2), 16);
  return bytes;
}

async function pbkdf2(password, salt, iterations) {
  const keyMaterial = await crypto.subtle.importKey('raw', new TextEncoder().encode(password), 'PBKDF2', false, ['deriveBits']);
  const bits = await crypto.subtle.deriveBits({ name: 'PBKDF2', salt, iterations, hash: HASH }, keyMaterial, 256);
  return new Uint8Array(bits);
}

export async function hashPassword(password) {
  const salt = randomBytes(16);
  const hash = await pbkdf2(password, salt, ITERATIONS);
  return { hash: toHex(hash), salt: toHex(salt), iterations: ITERATIONS };
}

export async function verifyPassword(password, stored) {
  const salt = fromHex(stored.passwordSalt);
  const hash = await pbkdf2(password, salt, stored.iterations || ITERATIONS);
  return timingSafeEqual(toHex(hash), stored.passwordHash);
}

export function timingSafeEqual(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

export function randomToken(bytes = 32) {
  return toHex(randomBytes(bytes));
}
