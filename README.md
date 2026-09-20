# Orbit

소개 페이지: <https://luckyman0277.github.io/orbit/> (한국어: <https://luckyman0277.github.io/orbit/ko/>)

Codex CLI, Claude Code CLI와 일반 셸을 한 창에서 쓰는 Windows 데스크톱 앱입니다. 터미널, 파일 편집기, 작업 폴더, 프롬프트 보관함을 함께 제공하고, 휴대폰·노트북 등 어떤 기기에서든 계정으로 로그인해 같은 터미널을 이어서 쓸 수 있습니다.

호스트는 C# / WinForms, 화면은 WebView2 하나, 터미널은 Windows ConPTY + xterm.js입니다. 백그라운드 색인·감시·상시 실행 런타임 없이 가볍게 동작하도록 설계했습니다.

## 기능

- **통합 터미널**: Codex, Claude Code, PowerShell, CMD를 탭으로 실행. 좌우·상하 분할과 탭 재정렬 지원 (최대 8개).
- **저장된 대화 이어 열기**: Codex·Claude의 로컬 대화 기록을 검색해 원래 작업 폴더에서 다시 엽니다.
- **파일 탐색기 · 편집기**: 주요 언어 구문 강조, UTF-8/UTF-16/CP949 인코딩과 LF/CRLF 유지, 외부 변경 충돌 감지.
- **Markdown 미리보기**: 표·목록·체크리스트·코드 블록 렌더링과 원문 편집 전환.
- **API 키 보관**: 키를 PC에 암호화(DPAPI)해 두면 새로 여는 Claude·Codex 터미널에 환경변수로 자동으로 들어가고, AI 대화에는 값이 노출되지 않습니다. 키는 **프로젝트별** 또는 모든 프로젝트용으로 저장하며, 여러 프로젝트의 터미널을 동시에 열어도 각자 자기 프로젝트의 키만 받습니다. AI가 필요한 키를 `orbit-secret request NAME`으로 요청하면 어느 프로젝트·터미널의 요청인지 적힌 카드가 뜨고, 사용자가 직접 값을 넣습니다. 일반 PowerShell·CMD에는 자동으로 넣지 않으며 `orbit-secret run -- 명령`으로 씁니다. 데스크톱은 화면 위쪽의 열쇠 버튼(홈·작업 화면 모두), 휴대폰은 **메뉴 → API 키**입니다. 휴대폰도 세션을 프로젝트별로 묶어 상태 점과 함께 보여주고, ＋에서 프로젝트와 에이전트를 함께 골라 새 세션을 엽니다.
- **클릭 가능한 링크**: 터미널에 출력된 파일 경로(`file.js:12:3` 형식 포함)·URL·폴더 경로를 바로 열기.
- **외부 연결**: 계정으로 로그인하면 휴대폰·노트북 등 어떤 기기·네트워크에서든 실행 중인 터미널을 원격으로 조작. Tailscale Funnel 또는 임시 Cloudflare 터널로 공개 HTTPS 주소를 발급하고, 작은 클라우드 계정 서비스(`cloud/`)가 로그인한 기기를 그 주소로 안내합니다. 자세한 내용은 [docs/REMOTE.md](docs/REMOTE.md) 참고.
- **다크 / 라이트 테마**, 터미널 글자 크기·출력 보관량 등 개인화 설정.

## 요구 사항

- Windows 10 1809 이상 / Windows 11, x64
- .NET Framework 4.8
- Microsoft Edge WebView2 Runtime
- Codex 또는 Claude Code CLI가 PATH에 설치되어 있어야 해당 버튼으로 실행할 수 있습니다 ([Codex CLI](https://learn.chatgpt.com/docs/codex/cli), [Claude Code](https://code.claude.com/docs/en/setup)).

앱 자체는 Codex·Claude CLI 설치나 그 CLI들의 로그인을 대신 진행하지 않습니다. 각 CLI의 로그인·승인·작업 과정은 터미널에 그대로 표시됩니다. (원격 접속용 Orbit 계정은 별개이며, 위 **외부 연결** 참고.)

## 설치와 업데이트

[Releases](https://github.com/LuckyMan0277/orbit/releases/latest)에서 `Orbit-Setup.exe`를 받아 실행하면 설치됩니다. 관리자 권한 없이 `%LocalAppData%\Programs\Orbit`에 설치되고, 바탕화면 아이콘(선택)과 시작 메뉴 항목이 만들어지며 Windows 설정의 앱 목록에서 제거할 수 있습니다. 설정·계정·등록 정보는 `%LocalAppData%\OrbitAgentDesktop`에 따로 있어 업데이트해도 유지됩니다.

Orbit은 실행할 때마다 GitHub에서 새 버전이 있는지 확인합니다(백그라운드 상시 확인은 없음). 새 버전이 있으면 상단에 **업데이트 vX.Y.Z** 버튼이 나타나고, 누르면 확인 후 설치 파일을 내려받아 Orbit을 종료 → 설치 → 자동 재실행합니다. 실행 중인 터미널은 종료되므로 작업이 끝난 뒤에 누르세요.

**알려진 제한**: 릴리스에는 아직 코드 서명이 없습니다. Windows의 스마트 앱 컨트롤이 켜진 PC에서는 설치 파일과 앱 내 업데이트 실행이 차단될 수 있습니다. 서명 적용 계획은 [docs/CODE_SIGNING.md](docs/CODE_SIGNING.md)를 참고하세요.

새 버전 배포: `v0.2.0` 같은 태그를 push하면 GitHub Actions가 빌드해 릴리스에 `Orbit-Setup.exe`를 올립니다(`git tag v0.2.0 && git push origin v0.2.0`). main에 push할 때는 빌드만 검증하고 릴리스는 만들지 않습니다.

## 실행

빌드된 실행 파일은 `release/Orbit/Orbit.exe`입니다. 배포할 때는 `release/Orbit` 폴더 전체를 복사하세요. 개발 도구와 `node_modules`는 앱 실행에 필요하지 않습니다.

## 사용법

1. **＋ 새 세션**(`Ctrl+Shift+N`)을 눌러 **프로젝트와 에이전트(Claude·Codex)를 함께 고릅니다.** 여러 프로젝트의 세션을 동시에 켜 두고 오가며 작업할 수 있습니다.
2. 왼쪽 **세션** 목록은 프로젝트별로 묶여 있고, 세션마다 상태 점이 있습니다(작업 중 / 확인 필요 / 대기 / 끝남). 다른 세션을 보는 동안 출력이 나온 세션은 "확인해 보세요"로 표시됩니다. 누르면 그 세션으로 넘어가고, 창의 제목·API 키 범위도 그 세션의 프로젝트를 따릅니다.
3. **저장된 대화**에서 이전 Codex·Claude 대화를 이어 열 수 있습니다. `Ctrl+Shift+T`는 현재 프로젝트에서 기본 셸을 엽니다.
4. 탭을 끌어 순서를 바꾸거나 패널 가장자리에 놓아 분할합니다. 탭을 더블클릭하면 이름을 바꿀 수 있습니다. 탭 옆 **＋**는 그 자리에 새 세션을 엽니다.
5. **파일** 탭(파일 탐색기)에서 파일을 클릭하면 오른쪽 편집기에 열립니다. `Ctrl+S`로 저장합니다. 프로젝트를 관리하려면(이름 바꾸기·제거) 왼쪽 위 **orbit**을 눌러 프로젝트 화면을 엽니다.
5. 터미널에 나온 주소·파일·폴더 경로를 클릭하면 각각 브라우저·편집기·탐색기로 열립니다.

### 단축키

| 기능 | 단축키 |
| --- | --- |
| 새 기본 터미널 | Ctrl+Shift+T |
| 현재 터미널 종료 | Ctrl+Shift+W |
| 터미널 분할 | Ctrl+Shift+D |
| 터미널 출력 검색 | Ctrl+Shift+F |
| 파일 열기 | Ctrl+O |
| 파일 저장 | Ctrl+S |
| 명령 찾기 | Ctrl+K 또는 Ctrl+P |
| 탐색기 표시 전환 | Ctrl+B |
| 터미널 선택 영역 복사 | Ctrl+Shift+C |
| 편집기 찾기 / 실행 취소 | Ctrl+F / Ctrl+Z |

## 개발 및 빌드

Node.js와 npm이 있는 환경에서:

```powershell
npm ci
npm test
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

빌드 스크립트는 Windows에 포함된 .NET Framework C# 컴파일러를 사용합니다. Visual Studio, Rust, C++ 빌드 도구는 따로 필요하지 않습니다. WebView2 SDK가 없으면 Microsoft NuGet에서 `.tools` 폴더로 자동 다운로드합니다.

```powershell
# 네이티브 파일 입출력과 실제 터미널(ConPTY) 자체 검사
$p = Start-Process release/Orbit/Orbit.exe -ArgumentList '--self-test' -PassThru -Wait
Get-Content release/Orbit/self-test-results.txt

# 실제 창, 셸, 파일 저장, 화면 캡처까지 확인하는 UI 검사
Start-Process release/Orbit/Orbit.exe -ArgumentList '--ui-test' -Wait
Get-Content artifacts/ui-test/report.txt

# 브라우저에서 화면 레이아웃만 미리 보기 (샘플 데이터, 실제 터미널/파일 시스템 없음)
npm run preview
```

## 프로젝트 구조

| 경로 | 역할 |
| --- | --- |
| `native/Program.cs` | Windows 창, WebView2, 호스트 메시지 처리 |
| `native/ConPty.cs` | 실제 터미널(ConPTY), 입출력, 프로세스 수명 관리 |
| `native/Files.cs` | 텍스트 인코딩, 파일 저장과 외부 변경 감지 |
| `native/RemoteServer.cs` | 원격 기기 인증(Bearer 토큰)과 원격 API |
| `native/RemoteAccount.cs` | 계정 서비스 연결·로그인 지원(계정 토큰 발급·해지) |
| `native/RemoteTunnel.cs`, `native/RemoteTailscale.cs` | 공개 HTTPS 주소 발급(Cloudflare/Tailscale Funnel) |
| `src/app.js` | 작업 공간, 탭, 탐색기와 도구 |
| `src/terminal.js` | xterm.js 연동과 클릭 가능한 링크 |
| `src/editor.js` | 필요할 때 불러오는 CodeMirror 편집기 |
| `cloud/` | 계정 로그인용 Cloudflare Worker(신호 서버) — 자세한 내용은 `cloud/wrangler.toml` 참고 |
| `scripts/build.ps1` | Windows 실행 파일 빌드 |
| `native/Updater.cs` | GitHub 릴리스 확인과 업데이트 설치 |
| `installer/orbit.iss` | 설치 프로그램(Inno Setup) 정의 |
| `.github/workflows/build.yml` | CI 빌드와 태그 릴리스 |

## 기술 문서

[Microsoft ConPTY](https://learn.microsoft.com/en-us/windows/console/pseudoconsoles) · [WebView2 WinForms](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winforms) · [xterm.js](https://xtermjs.org/docs/api/terminal/classes/terminal/) · [CodeMirror](https://codemirror.net/examples/bundle/)

## 라이선스

[MIT](LICENSE)
