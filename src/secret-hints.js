// Guess which environment variable a pasted API key belongs to, so people only have to paste it. Longer prefixes come first.
const prefixes = [
  ['sk-ant-', 'ANTHROPIC_API_KEY'],
  ['sk-or-', 'OPENROUTER_API_KEY'],
  ['sk-proj-', 'OPENAI_API_KEY'],
  ['sk_live_', 'STRIPE_SECRET_KEY'],
  ['sk_test_', 'STRIPE_SECRET_KEY'],
  ['sk-', 'OPENAI_API_KEY'],
  ['github_pat_', 'GITHUB_TOKEN'],
  ['ghp_', 'GITHUB_TOKEN'],
  ['gho_', 'GITHUB_TOKEN'],
  ['AIza', 'GEMINI_API_KEY'],
  ['gsk_', 'GROQ_API_KEY'],
  ['xai-', 'XAI_API_KEY'],
  ['pplx-', 'PERPLEXITY_API_KEY'],
  ['hf_', 'HF_TOKEN'],
  ['xoxb-', 'SLACK_BOT_TOKEN'],
];
export const commonSecretNames = ['OPENAI_API_KEY', 'ANTHROPIC_API_KEY', 'GEMINI_API_KEY', 'GITHUB_TOKEN', 'STRIPE_SECRET_KEY', 'HF_TOKEN'];
export function suggestSecretName(value) {
  const text = String(value || '').trim();
  const hit = prefixes.find(([prefix]) => text.startsWith(prefix));
  return hit ? hit[1] : '';
}
// Mirrors the host's rule (SecretVault.CheckName) so the form can say what is wrong before asking the PC.
export function secretNameProblem(name) {
  const text = String(name || '').trim();
  if (!text) return '이름을 입력해 주세요.';
  if (!/^[A-Za-z_][A-Za-z0-9_]{0,63}$/.test(text)) return '이름은 영문·숫자·밑줄만 쓸 수 있고 숫자로 시작할 수 없어요. (예: OPENAI_API_KEY)';
  if (/^ORBIT_/i.test(text)) return 'ORBIT_로 시작하는 이름은 쓸 수 없어요.';
  return '';
}
