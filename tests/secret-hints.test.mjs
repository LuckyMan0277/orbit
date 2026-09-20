import test from 'node:test';
import assert from 'node:assert/strict';
import { suggestSecretName, secretNameProblem, commonSecretNames } from '../src/secret-hints.js';

test('a pasted key suggests the variable name its prefix belongs to', () => {
  assert.equal(suggestSecretName('sk-ant-api03-abc'), 'ANTHROPIC_API_KEY');
  assert.equal(suggestSecretName('sk-proj-abc'), 'OPENAI_API_KEY');
  assert.equal(suggestSecretName('sk-abc123'), 'OPENAI_API_KEY');
  assert.equal(suggestSecretName('sk-or-v1-abc'), 'OPENROUTER_API_KEY');
  assert.equal(suggestSecretName('  ghp_abc  '), 'GITHUB_TOKEN');
  assert.equal(suggestSecretName('AIzaSyABC'), 'GEMINI_API_KEY');
  assert.equal(suggestSecretName('sk_live_abc'), 'STRIPE_SECRET_KEY');
});

test('an unknown or empty value suggests nothing', () => {
  assert.equal(suggestSecretName(''), '');
  assert.equal(suggestSecretName(undefined), '');
  assert.equal(suggestSecretName('just-some-secret'), '');
});

test('longer prefixes win over shorter ones that start the same way', () => {
  assert.notEqual(suggestSecretName('sk-ant-x'), 'OPENAI_API_KEY');
  assert.notEqual(suggestSecretName('sk-or-x'), 'OPENAI_API_KEY');
});

test('name problems match the host rule and every suggestion is valid', () => {
  assert.equal(secretNameProblem('OPENAI_API_KEY'), '');
  assert.notEqual(secretNameProblem(''), '');
  assert.notEqual(secretNameProblem('1KEY'), '');
  assert.notEqual(secretNameProblem('A B'), '');
  assert.notEqual(secretNameProblem('ORBIT_PIPE'), '');
  for (const name of commonSecretNames) assert.equal(secretNameProblem(name), '', name);
  for (const value of ['sk-ant-x', 'sk-x', 'ghp_x', 'AIzaX', 'hf_x', 'xoxb-x']) assert.equal(secretNameProblem(suggestSecretName(value)), '', value);
});
