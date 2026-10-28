# Orbit

소개 페이지: <https://luckyman0277.github.io/orbit/> (한국어: <https://luckyman0277.github.io/orbit/ko/>)

Orbit은 빈 화면에서 시작해 사용자가 연 터미널만 실행하는 Windows 터미널 데스크톱 앱입니다. 각 창에는 터미널 탭을 여러 개 열고, 탭을 끌어 화면을 좌우·상하 또는 네 모서리로 나눌 수 있습니다. 새 OS 창도 열 수 있습니다. 글꼴 준비와 창 크기가 확인된 뒤 각 터미널 크기를 맞추며, 출력 기록 5,000줄을 유지합니다. 기존 작업 공간·편집기·원격 기능 코드는 다음 확장을 위해 보관되어 있습니다.

호스트는 C# / WinForms, 화면은 WebView2 하나, 터미널은 Windows ConPTY + xterm.js입니다. 백그라운드 색인·감시·상시 실행 런타임 없이 가볍게 동작하도록 설계했습니다.

## 기능

- **탭과 화면 분할**: 창마다 최대 8개 터미널 탭을 열 수 있습니다. 탭을 가운데로 끌면 탭 그룹에 합치거나 순서를 바꾸고, 가장자리로 끌면 좌우·상하 분할, 모서리로 끌면 4분할 배치가 됩니다. 빈 영역에는 선택한 기본 셸을 여는 `새 터미널` 버튼과 셸 선택 드롭다운이 있습니다. 분할 경계도 끌어 크기를 조정합니다.
- **빈 시작 화면**: Orbit은 터미널 프로세스 없이 시작합니다. `새 터미널` 옆 드롭다운에서 CMD, PowerShell 또는 Git Bash를 기본 셸로 선택한 뒤 버튼이나 `Ctrl+Shift+T`로 실행합니다. 선택한 기본 셸은 다음 실행에도 유지됩니다. 새 창도 빈 화면으로 시작합니다.
- **브라우저 미리보기**: 데스크톱 앱과 같은 빈 시작 화면을 보여줍니다. 브라우저에서는 셸이 실행되지 않습니다.

## 요구 사항

- Windows 10 1809 이상 / Windows 11, x64
- .NET Framework 4.8
- Microsoft Edge WebView2 Runtime
- CMD와 Windows PowerShell은 기본 제공됩니다. Git Bash를 사용하려면 Git for Windows가 설치되어 있어야 하며, 앱은 Git의 `bash.exe`를 ConPTY로 실행합니다. Codex와 Claude Code는 셸에서 직접 실행할 수 있습니다.

## 설치와 업데이트

[Releases](https://github.com/LuckyMan0277/orbit/releases/latest)에서 `Orbit-Setup.exe`를 받아 실행하면 설치됩니다. 관리자 권한 없이 `%LocalAppData%\Programs\Orbit`에 설치되고, 바탕화면 아이콘(선택)과 시작 메뉴 항목이 만들어지며 Windows 설정의 앱 목록에서 제거할 수 있습니다. 설정·계정·등록 정보는 `%LocalAppData%\OrbitAgentDesktop`에 따로 있어 업데이트해도 유지됩니다.

**알려진 제한**: 릴리스에는 아직 코드 서명이 없습니다. Windows의 스마트 앱 컨트롤이 켜진 PC에서는 설치 프로그램이 차단될 수 있습니다. 서명 적용 계획은 [docs/CODE_SIGNING.md](docs/CODE_SIGNING.md)를 참고하세요.

새 버전 배포: `v0.2.0` 같은 태그를 push하면 GitHub Actions가 빌드해 릴리스에 `Orbit-Setup.exe`를 올립니다(`git tag v0.2.0 && git push origin v0.2.0`). main에 push할 때는 빌드만 검증하고 릴리스는 만들지 않습니다.

## 실행

빌드된 실행 파일은 `release/Orbit/Orbit.exe`입니다. 배포할 때는 `release/Orbit` 폴더 전체를 복사하세요. 개발 도구와 `node_modules`는 앱 실행에 필요하지 않습니다.

## 사용법

1. Orbit을 시작하면 빈 화면이 보입니다. `새 터미널` 옆 드롭다운에서 CMD, PowerShell 또는 Git Bash를 고른 뒤 `새 터미널` 또는 `Ctrl+Shift+T`를 누르면 선택한 셸이 실행됩니다. 셸 선택은 기본값으로 저장됩니다. Orbit 메뉴에서 새 창·새 탭을 열 수 있고, 빈 영역은 분할 메뉴나 `Ctrl+Shift+D`·`Ctrl+Shift+E`로 나눌 수 있습니다.
2. 탭을 같은 영역 가운데로 끌면 탭 순서를 바꾸거나 다른 탭 그룹으로 옮길 수 있습니다. 영역의 가장자리에 놓으면 좌우·상하로 나뉘고, 모서리에 놓으면 해당 4분할 영역으로 이동합니다. 비어 있는 영역에서 셸 드롭다운으로 기본 셸을 선택해 엽니다.
3. 탭을 선택해 입력 대상을 바꾸고, 탭을 닫으면 해당 터미널만 종료됩니다. 마지막 터미널을 닫으면 빈 시작 화면으로 돌아갑니다. 탭 하나를 자기 영역의 가장자리나 모서리로 끌면 이동한 터미널과 비어 있는 시작 영역이 함께 남습니다.
4. 터미널에서 선택한 텍스트는 복사하고, 붙여넣기는 줄바꿈을 명령 실행으로 보내지 않도록 처리합니다. 마우스 휠로 출력 기록을 스크롤할 수 있습니다. 기록을 위로 읽는 동안 새 출력이 와도 현재 위치를 유지하고, 아래쪽에 있을 때는 새 출력을 따라갑니다.

### 단축키

| 기능 | 단축키 |
| --- | --- |
| 새 창 | Ctrl+Shift+N |
| 새 터미널 탭 | Ctrl+Shift+T |
| 같은 영역의 다음/이전 탭 | Ctrl+Tab / Ctrl+Shift+Tab |
| 오른쪽으로 분할 | Ctrl+Shift+D |
| 아래로 분할 | Ctrl+Shift+E |
| 현재 터미널 다시 시작 | Ctrl+Shift+R |
| 현재 터미널 닫기 | Ctrl+Shift+W |
| 터미널 선택 영역 복사 | Ctrl+Shift+C |
| 터미널 붙여넣기 | Ctrl+V |

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

# 실제 창에서 빈 시작 화면, 명시적 PowerShell 열기, CMD 전환과 리사이즈를 확인하는 UI 검사
Start-Process release/Orbit/Orbit.exe -ArgumentList '--ui-test' -Wait
Get-Content artifacts/ui-test/report.txt

# Git Bash 설치 검색과 ConPTY 실행/출력/종료 확인 (Git for Windows 필요)
$p = Start-Process release/Orbit/Orbit.exe -ArgumentList '--shell-profile-test' -PassThru -Wait
Get-Content release/Orbit/shell-profile-test-results.txt

# 브라우저에서 샘플 터미널 화면 미리 보기 (실제 셸 실행 없음)
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
| `src/terminal-app.js` | 빈 시작 화면, 터미널 탭과 화면 분할 |
| `src/terminal.js` | xterm.js와 ConPTY 연결, 입력·복사·붙여넣기·리사이즈 |
| `src/app.js` | 이후 확장을 위해 보관한 작업 공간 화면 |
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
