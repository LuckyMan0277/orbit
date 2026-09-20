# 원격 API v1

PWA와 향후 모바일 앱이 공유할 작은 HTTP API입니다. 모든 API는 `/api/v1/` 아래에서 `POST`, `Content-Type: application/json`을 사용합니다. 외부 접속은 HTTPS 터널을 통해 제공합니다.

## 계정 로그인

기기 등록은 더 이상 이 로컬 API의 일부가 아닙니다. PC의 Orbit이 별도의 클라우드 계정 서비스(`cloud/` 참고)에 계정을 연결해 두면, 어떤 기기든 그 서비스의 로그인 페이지에서 이메일·비밀번호로 로그인해 `{ url, deviceToken }`을 받고 `<url>/mobile.html#login=<deviceToken>`으로 리다이렉트됩니다. 이 로컬 API는 그렇게 발급된 토큰만 알며, 로그인 자체는 처리하지 않습니다.

토큰은 이후 `Authorization: Bearer <token>` 헤더로 보냅니다. 서버에는 토큰의 해시를 저장합니다. API 응답과 터미널 출력은 HTTP 캐시·서비스워커 캐시에 저장하지 않습니다. PC 쪽에서 새 기기를 개별 승인하는 절차는 없습니다 — 계정 로그인 자체가 그 승인을 대신합니다.

## 세션 사용

| 경로 | 요청 | 응답의 주요 필드 |
| --- | --- | --- |
| `sessions` | `{}` | `sessions[]`: `id`, `name`, `pid`, `profile`, `project`(터미널을 연 프로젝트 폴더), `seq`(출력 순번), `lastOutputMs`(마지막 출력 후 경과 ms, 없으면 -1), `asking`(API 키 요청 대기 중) |
| `terminals/create` | `{ profile, secrets? }` | 생성한 터미널의 `session` |
| `secrets` | `{ project? }` | `keys[]`(`name`, `scope`)와 `names[]` (값은 포함하지 않음) |
| `secrets/set` | `{ name, value, scope?, project? }` | `keys[]`, `names[]` |
| `secrets/delete` | `{ name, scope?, project? }` | `keys[]`, `names[]` |
| `secrets/answer` | `{ request, status }` | `{ ok }` — AI의 값 요청에 대한 응답(`saved`·`denied`) |
| `snapshot` | `{ session }` | ANSI 화면 `data`, `seq`, `cols`, `rows` |
| `output` | `{ session, seq }` | `items[]`: `seq`, `data`; `reset` |
| `input` | `{ session, data }` | `{ ok: true }` |

1. 클라이언트 터미널을 snapshot의 열·행 크기로 맞춥니다.
2. ANSI 화면의 렌더링 완료를 기다린 뒤 snapshot의 `seq`를 저장합니다.
3. `output`으로 뒤의 출력을 받습니다. 새 출력이 없으면 최대 25초 기다립니다.
4. 출력의 렌더링이 끝났을 때만 마지막 `seq`를 반영합니다.
5. `reset` 응답이면 새 snapshot을 받아 화면을 복구합니다. 오래된 출력 일부만 새 터미널에 재생하지 않습니다.

휴대폰 화면은 `sessions`를 몇 초마다 다시 읽어 각 세션의 상태(작업 중 / 확인해 보세요 / 키 요청 / 대기)를 프로젝트별로 묶어 보여줍니다. 지금 보고 있는 세션의 출력·스냅샷에는 영향을 주지 않습니다. 새 세션을 열 때는 `terminals/create`의 `project`로 프로젝트를 고르고, 보고 있는 세션의 프로젝트가 기본값입니다.

세션 전환·로그아웃 시 기존 요청을 취소하고 이전 응답을 버립니다. 입력은 자동 재시도하지 않습니다. 응답 유실 시 같은 명령이 두 번 실행될 수 있기 때문입니다.

`terminals/create`는 등록한 기기의 인증을 요구합니다. `profile`은 `codex`, `claude`, `powershell`, `cmd`만 허용하며, PC의 현재 작업 폴더에서 데스크톱과 같은 생성 경로를 사용합니다. PC·원격을 합쳐 최대 8개를 열 수 있습니다. 생성 요청도 자동 재시도하지 않습니다. 응답을 잃었다면 세션 목록을 새로고침해 생성 여부를 확인합니다.

PWA 입력은 세션 ID를 고정한 순차 전송으로 처리합니다. 프롬프트는 터미널의 bracketed paste 모드가 켜져 있으면 해당 구분자로 감싸고, **보내기**에만 마지막 Enter를 붙입니다. **입력만**은 마지막 Enter를 붙이지 않습니다. 서버의 입력 한도는 구분자를 포함해 8,192자입니다.

클라이언트는 PC 화면을 반영하는 터미널입니다. 터미널 장치 질의에 대한 자동 응답은 PC 쪽 렌더러가 담당하며, 원격 렌더러가 같은 응답을 중복 전송하지 않아야 합니다. PWA는 xterm의 `disableStdin`과 별도의 입력칸·특수키 버튼을 사용합니다.

## 비밀 값 (API 키)

터미널에 입력한 내용은 그 터미널에서 실행 중인 Codex·Claude에게 그대로 전달됩니다. API 키를 AI에게 보이지 않고 쓰게 하려면 `secrets/set`으로 PC에 등록해 둡니다.

- 값은 PC의 Windows 사용자 DPAPI 키로 암호화해 `%LocalAppData%\OrbitAgentDesktop\secrets.dat`에 저장합니다. 값을 되돌려주는 API는 없고, 응답에는 이름과 범위만 나옵니다.
- **범위**: 키는 `scope: "global"`(모든 프로젝트, 기본값) 또는 `scope: "project"`(`project`는 프로젝트 폴더 경로)로 저장합니다. 터미널은 모든 프로젝트 키에 **자기 프로젝트의 키**를 더해 받고, 이름이 겹치면 프로젝트 키가 이깁니다. 프로젝트 폴더의 하위 폴더에서 연 터미널도 그 프로젝트에 속합니다. `secrets`·`secrets/delete`의 `project`는 어느 프로젝트 기준으로 볼지를 뜻합니다.
- **터미널의 프로젝트는 터미널을 연 작업 폴더로 그때 정해지고, 창이 나중에 어떤 프로젝트를 보여주든 바뀌지 않습니다.** 프로젝트 A와 B의 터미널을 동시에 열어 두어도 각자 자기 키만 받습니다.
- **자동 주입은 Claude·Codex 터미널에만** 합니다. 일반 PowerShell·CMD에는 넣지 않고, 필요하면 `orbit-secret run --`을 씁니다. `terminals/create`에 `secrets`(이름 배열)를 주면 프로필과 상관없이 그 이름만 넣습니다. 명령줄에는 들어가지 않고 Orbit 자신의 환경도 바뀌지 않으며, 이미 열린 터미널에는 영향이 없습니다.
- 이름은 영문·숫자·밑줄(64자 이하, 숫자로 시작 불가)이며 `PATH`, `COMSPEC` 같은 시스템 변수와 `ORBIT_` 접두어는 쓸 수 없습니다. 값은 8,192자 이하, 최대 100개를 저장할 수 있습니다. 이 프로젝트가 쓸 수 없는 이름을 `secrets`에 보내면 `400`입니다.
- 이전 버전에서 저장한 키는 자동으로 "모든 프로젝트" 키가 됩니다.

### AI가 직접 쓰는 방법: `orbit-secret`

Orbit이 연 모든 터미널의 `PATH`에는 `orbit-secret`(설치 폴더의 `bin\orbit-secret.exe`)이 들어 있습니다. 터미널마다 다른 토큰(`ORBIT_TERMINAL_AUTH`)과 명명된 파이프(`ORBIT_PIPE`)로 Orbit 창과 통신하고, 그 터미널의 프로젝트 기준으로 동작합니다.

| 명령 | 동작 |
| --- | --- |
| `orbit-secret list` | 이 프로젝트가 쓸 수 있는 이름 목록 (프로젝트 전용은 `(this project only)` 표시, 값은 출력하지 않음) |
| `orbit-secret request NAME [이유]` | 사용자에게 값을 묻습니다. 저장하면 `Saved.`(종료 코드 0), 거절하면 `declined`(2), 답이 없으면 3(최대 10분). **값은 이 명령의 출력에 나오지 않습니다.** 저장은 요청한 터미널의 프로젝트에 됩니다(사용자가 범위를 바꿀 수 있음). |
| `orbit-secret run [--only A,B] -- 명령...` | 이 프로젝트의 키(또는 지정한 것)를 환경변수로 넣어 명령을 실행하고 그 종료 코드를 돌려줍니다. 세션이 시작된 뒤에 저장된 키도 바로 씁니다. |

- 요청은 화면을 가로채지 않습니다. 데스크톱에서는 **어느 프로젝트의 어느 터미널이 어떤 키를 요청했는지** 적힌 카드가 오른쪽 아래에 쌓이고 해당 터미널 탭에 표시가 붙으며 작업표시줄이 깜빡입니다. 카드에서 **입력**을 누르면 입력창이 열립니다. 답하거나 터미널이 끝나면(다른 화면에서 답해도) 카드가 사라집니다(`secretRequestClosed`).
- 휴대폰에서 연 세션이면 `snapshot`·`output` 응답의 `secretRequests[]`(`id`, `name`, `reason`, `project`, `projectName`, `terminal`)로 전달되어 폰 화면에 입력창이 뜹니다. 폰은 `secrets/set`(`project`는 요청의 `project`)으로 저장한 뒤 `secrets/answer`로 결과를 알립니다.
- Claude 터미널은 `--append-system-prompt-file`로, Codex 터미널은 `-c developer_instructions=...`로 안내문(저장된 이름, 값을 출력하지 말 것, `orbit-secret` 사용법)을 자동으로 받습니다. 안내문에는 이름만 있고 값은 없습니다(Claude용 파일은 터미널이 끝나면 지워집니다). Codex는 실행하는 명령에서 이름에 KEY·SECRET·TOKEN이 든 환경변수를 기본으로 걸러내므로, Codex 안내문은 키가 필요한 명령을 항상 `orbit-secret run --`으로 실행하도록 안내합니다.
- 한계: AI가 셸에서 `printenv`·`echo $env:NAME`을 실행하면 그 출력은 대화에 남습니다. 이 기능은 값을 대화에 붙여넣거나 파일에 쓰게 만들지 않는 것이지, 셸을 쓸 수 있는 AI가 값을 읽는 것을 막지는 않습니다.

## 경계

- 지정된 터미널 프로필 생성, 명령 입력, 비밀 값 등록을 지원합니다. 파일 편집, 클립보드, 창 제어 API는 없습니다.
- 브라우저 Origin은 loopback 주소와 PC에서 지정한 HTTPS 원본만 허용합니다.
- PC에서 계정 연결을 해제하면 토큰 인증이 실패합니다. 연결 종료로 PC 터미널을 종료하지 않습니다.
- `--remote-test`와 `--remote-test-tunnel`은 별도 테스트 프로필과 샘플 CMD를 사용하는 개발 검증 모드입니다. `--remote-test-port=N`으로 테스트 포트를 분리할 수 있습니다.
