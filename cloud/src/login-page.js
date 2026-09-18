// A single stable URL that works from any device, any network: log in here and
// get redirected straight into the linked PC's mobile workspace. No QR, no link
// to carry over -- this page IS the thing you bookmark or type in from scratch.
export const LOGIN_PAGE_HTML = `<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Orbit 원격 로그인</title>
<style>
:root{color-scheme:dark;--bg:#191921;--panel:#24242f;--line:#4a4a5d;--text:#f1eff8;--muted:#aaa8b8;--accent:#cbb8ff;--ink:#2b1f42;--bad:#ffb4c0}
*{box-sizing:border-box}body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:var(--bg);color:var(--text);font-family:Inter,"Segoe UI","Malgun Gothic",sans-serif}
form{width:min(100% - 32px,380px);padding:28px;border:1px solid var(--line);border-radius:20px;background:var(--panel)}
h1{margin:0 0 6px;font-size:20px}p{margin:0 0 18px;color:var(--muted);font-size:13px;line-height:1.6}
input{width:100%;padding:11px;margin:8px 0;border:1px solid var(--line);border-radius:11px;background:#111118;color:var(--text);font-size:14px}
button{width:100%;margin-top:10px;padding:11px;border:1px solid var(--accent);border-radius:11px;background:var(--accent);color:var(--ink);font-weight:700;font-size:14px}
button:disabled{opacity:.5}
#error{display:none;margin-top:12px;color:var(--bad);font-size:13px;line-height:1.5}
</style></head>
<body>
<form id="login">
<h1>Orbit 원격 로그인</h1>
<p>PC의 Orbit에서 미리 연결해 둔 계정으로 로그인하면 그 PC의 작업 공간으로 바로 연결됩니다.</p>
<input id="email" type="email" placeholder="이메일" autocomplete="username" required>
<input id="password" type="password" placeholder="비밀번호" autocomplete="current-password" required>
<button id="submit" type="submit">로그인</button>
<p id="error"></p>
</form>
<script>
const form = document.getElementById('login'), submit = document.getElementById('submit'), error = document.getElementById('error');
form.addEventListener('submit', async (event) => {
  event.preventDefault();
  error.style.display = 'none'; submit.disabled = true;
  try {
    const response = await fetch('/login', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email: document.getElementById('email').value, password: document.getElementById('password').value })
    });
    const body = await response.json();
    if (!response.ok) {
      const messages = { invalid_email: '이메일 형식이 올바르지 않습니다.', invalid_credentials: '이메일 또는 비밀번호가 올바르지 않습니다.', no_pc_linked: '이 계정에 연결된 PC가 없습니다. PC의 Orbit에서 먼저 계정을 연결하세요.', rate_limited: '잠시 후 다시 시도해 주세요.' };
      throw new Error(messages[body.error] || '로그인하지 못했습니다.');
    }
    location.href = body.url.replace(/\\/$/, '') + '/mobile.html#login=' + encodeURIComponent(body.deviceToken);
  } catch (failure) {
    error.textContent = failure.message; error.style.display = 'block'; submit.disabled = false;
  }
});
</script>
</body></html>`;
