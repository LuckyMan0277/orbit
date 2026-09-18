# 원격 API v1

PWA와 향후 모바일 앱이 공유할 작은 HTTP API입니다. 모든 API는 `/api/v1/` 아래에서 `POST`, `Content-Type: application/json`을 사용합니다. 외부 접속은 HTTPS 터널을 통해 제공합니다.

## 계정 로그인

기기 등록은 더 이상 이 로컬 API의 일부가 아닙니다. PC의 Orbit이 별도의 클라우드 계정 서비스(`cloud/` 참고)에 계정을 연결해 두면, 어떤 기기든 그 서비스의 로그인 페이지에서 이메일·비밀번호로 로그인해 `{ url, deviceToken }`을 받고 `<url>/mobile.html#login=<deviceToken>`으로 리다이렉트됩니다. 이 로컬 API는 그렇게 발급된 토큰만 알며, 로그인 자체는 처리하지 않습니다.

토큰은 이후 `Authorization: Bearer <token>` 헤더로 보냅니다. 서버에는 토큰의 해시를 저장합니다. API 응답과 터미널 출력은 HTTP 캐시·서비스워커 캐시에 저장하지 않습니다. PC 쪽에서 새 기기를 개별 승인하는 절차는 없습니다 — 계정 로그인 자체가 그 승인을 대신합니다.

## 세션 사용

| 경로 | 요청 | 응답의 주요 필드 |
| --- | --- | --- |
| `sessions` | `{}` | `sessions[]`: `id`, `name`, `pid`, `profile` |
| `terminals/create` | `{ profile }` | 생성한 터미널의 `session` |
| `snapshot` | `{ session }` | ANSI 화면 `data`, `seq`, `cols`, `rows` |
| `output` | `{ session, seq }` | `items[]`: `seq`, `data`; `reset` |
| `input` | `{ session, data }` | `{ ok: true }` |

1. 클라이언트 터미널을 snapshot의 열·행 크기로 맞춥니다.
2. ANSI 화면의 렌더링 완료를 기다린 뒤 snapshot의 `seq`를 저장합니다.
3. `output`으로 뒤의 출력을 받습니다. 새 출력이 없으면 최대 25초 기다립니다.
4. 출력의 렌더링이 끝났을 때만 마지막 `seq`를 반영합니다.
5. `reset` 응답이면 새 snapshot을 받아 화면을 복구합니다. 오래된 출력 일부만 새 터미널에 재생하지 않습니다.

세션 전환·로그아웃 시 기존 요청을 취소하고 이전 응답을 버립니다. 입력은 자동 재시도하지 않습니다. 응답 유실 시 같은 명령이 두 번 실행될 수 있기 때문입니다.

`terminals/create`는 등록한 기기의 인증을 요구합니다. `profile`은 `codex`, `claude`, `powershell`, `cmd`만 허용하며, PC의 현재 작업 폴더에서 데스크톱과 같은 생성 경로를 사용합니다. PC·원격을 합쳐 최대 8개를 열 수 있습니다. 생성 요청도 자동 재시도하지 않습니다. 응답을 잃었다면 세션 목록을 새로고침해 생성 여부를 확인합니다.

PWA 입력은 세션 ID를 고정한 순차 전송으로 처리합니다. 프롬프트는 터미널의 bracketed paste 모드가 켜져 있으면 해당 구분자로 감싸고, **보내기**에만 마지막 Enter를 붙입니다. **입력만**은 마지막 Enter를 붙이지 않습니다. 서버의 입력 한도는 구분자를 포함해 8,192자입니다.

클라이언트는 PC 화면을 반영하는 터미널입니다. 터미널 장치 질의에 대한 자동 응답은 PC 쪽 렌더러가 담당하며, 원격 렌더러가 같은 응답을 중복 전송하지 않아야 합니다. PWA는 xterm의 `disableStdin`과 별도의 입력칸·특수키 버튼을 사용합니다.

## 경계

- 지정된 터미널 프로필 생성과 명령 입력을 지원합니다. 파일 편집, 클립보드, 창 제어 API는 없습니다.
- 브라우저 Origin은 loopback 주소와 PC에서 지정한 HTTPS 원본만 허용합니다.
- PC에서 계정 연결을 해제하면 토큰 인증이 실패합니다. 연결 종료로 PC 터미널을 종료하지 않습니다.
- `--remote-test`와 `--remote-test-tunnel`은 별도 테스트 프로필과 샘플 CMD를 사용하는 개발 검증 모드입니다. `--remote-test-port=N`으로 테스트 포트를 분리할 수 있습니다.
