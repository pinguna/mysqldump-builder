# mysqldump 명령어 생성기 (mysqldump Command Builder)

체크박스로 조건을 고르면 서버에서 바로 실행할 수 있는 `mysqldump` / `mysql` 명령어와 실행 스크립트(.sh / .bat / .ps1)를 만들어 주는 Windows 도구입니다.
옵션을 외울 필요가 없고, 함께 쓸 수 없는 옵션 조합은 화면에서 막거나 경고합니다. 프로그램은 DB 에 접속하지 않고 명령어 문자열만 만듭니다.

![메인 화면](docs/images/main.png)

## 주요 기능

- **명령어 실시간 생성**: 접속·대상·조건을 고르면 오른쪽 명령어가 바로 바뀝니다. Linux bash / Windows cmd / PowerShell 문법을 각각 맞춰 냅니다.
- **규칙 검사**: 충돌하는 옵션(예: `--single-transaction` + `--lock-all-tables`)은 오류로 막고, 위험한 설정(평문 패스워드, `--force`, localhost 포트 무시 등)은 경고합니다.
- **Export + Import**: 덤프한 파일을 같은 스크립트에서 바로 대상 DB 에 넣습니다. 여러 DB 는 DB 마다 나눠 대상 이름을 바꿀 수 있습니다.
- **DDL / 데이터 파일 분리**: 조건 하나로 `_ddl.sql` 과 `_data.sql` 을 따로 만듭니다.
- **스크립트 저장**: 실패 시 종료 코드 전파, 실행 시점 기준 파일명, 소요 시간 출력. 스크립트 안 메시지는 서버 로케일과 무관하게 ASCII.
- **스크립트 목록**: 여러 설정을 한 스크립트로 묶어 순서대로 실행합니다.
- **프리셋**: 기본 4종 + 사용자 프리셋. 패스워드는 어디에도 저장하지 않습니다.
- **포터블**: 설치 없이 exe 하나. 설정은 exe 옆 JSON 파일에 저장됩니다. 한국어 / English, 다크 모드.

자세한 사용법은 **[사용 설명서](docs/USER_GUIDE.md)** 를 보세요.

## 다운로드와 실행

1. 저장소의 **Releases** 에서 `MysqlDumpBuilder-v버전-win-x64.zip` 을 받아 아무 폴더에나 풉니다 (`MysqlDumpBuilder.exe`, `options.schema.json`, `README.md`, 사용 설명서 `docs/`).
2. `MysqlDumpBuilder.exe` 를 실행합니다. Windows 10 이상, .NET 설치 불필요.
   - SmartScreen 경고가 뜨면 **추가 정보 → 실행** 을 누릅니다 (코드 서명을 하지 않은 프로그램).
3. 접속 정보와 대상을 고르고, 대상 OS 를 맞춘 뒤 **스크립트 복사**(Ctrl+Shift+C) 또는 **스크립트 저장** 을 누릅니다.

프리셋 "DDL + 데이터 (전체 백업)" 을 고르면 이런 명령어가 나옵니다.

```bash
mysqldump \
  -h 10.0.1.20 -P 3306 -u backup -p \
  --single-transaction --skip-lock-tables --set-gtid-purged=OFF \
  --routines --events \
  --hex-blob \
  shop \
  > shop_20261019_1343.sql
```

## 알아 둘 점

- **localhost 는 포트를 무시합니다.** 리눅스 클라이언트는 `localhost` 면 유닉스 소켓으로 접속합니다. 한 서버에 인스턴스가 여럿이면 **소켓** 칸에 경로를 적거나 `127.0.0.1` / TCP 강제를 쓰세요. 소켓 칸을 비우면 `my.cnf` 의 `[client] socket=` 을 따릅니다.
- **자동 실행(cron)에는 `-p` 를 쓸 수 없습니다.** `--login-path` 나 `--defaults-extra-file` 을 쓰세요.
- **Windows PowerShell 5.1** 은 값 안의 큰따옴표를 외부 프로그램에 제대로 넘기지 못합니다. WHERE 조건에는 작은따옴표를 쓰세요.
- MySQL 5.7 / 8.0+ 클라이언트 기준입니다. MariaDB 는 지원하지 않습니다.

## 개발

요구 사항: .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`)

```
MysqlDumpBuilder.sln
├─ MysqlDumpBuilder.Core/          명령어 빌더 (UI 없음) — 스키마 로더, 빌더, 셸별 포맷터, 프리셋, 스크립트
│   └─ Resources/options.schema.json   기본 옵션 스키마 (DLL 에 내장)
├─ MysqlDumpBuilder.Core.Tests/    xUnit — 시나리오, 규칙 R3~R27·I1~I9, 포맷터, 스크립트, Import, 소켓
├─ MysqlDumpBuilder.App/           WinForms 화면
├─ docs/                           사용 설명서와 스크린샷
├─ publish.cmd                     테스트 → 단일 exe 빌드 → publish\ 폴더
└─ release.cmd                     publish.cmd 실행 → release\MysqlDumpBuilder-v버전-win-x64.zip
```

```
dotnet test MysqlDumpBuilder.Core.Tests      # 테스트
dotnet run --project MysqlDumpBuilder.App    # 실행
publish.cmd                                  # 배포용 exe 만들기
release.cmd                                  # Releases 에 올릴 zip 만들기
```

- 규칙 메시지 문구는 `Core/RuleMessages.cs`, 화면 문구는 `App/UiStrings.cs`, 색은 `App/AppTheme.cs`(Light / Dark 팔레트)에 모여 있습니다.
- 규칙 번호(R, I)에 빈 번호가 있는 것은 폐기된 규칙입니다. 이유는 `Core/RuleMessages.cs` 머리 주석에 있습니다.

### 새 버전 올리기 (GitHub Releases)

1. `MysqlDumpBuilder.App/MysqlDumpBuilder.App.csproj` 의 `<Version>` 을 올립니다 (예: `0.2.0`).
2. `release.cmd` 를 실행하면 테스트 → exe 빌드 → `release\MysqlDumpBuilder-v0.2.0-win-x64.zip` 까지 만들어집니다.
3. GitHub 저장소 **Releases → Draft a new release** 에서 태그 `v0.2.0` 을 만들고 zip 을 첨부합니다.

exe 는 70MB 가 넘고 빌드마다 바뀌므로 git 에는 넣지 않습니다 (`publish/`, `release/` 는 `.gitignore`).

### 옵션을 추가하려면

`MysqlDumpBuilder.Core/Resources/options.schema.json` 에 항목을 하나 추가하면 끝입니다. 화면의 체크박스는 스키마에서 자동으로 만들어집니다.

```json
{
  "id": "skip_dump_date",            // 내부 id (프리셋에서 참조)
  "flag": "--skip-dump-date",        // 체크했을 때 내보낼 플래그
  "uncheckedFlag": null,             // 체크 안 했을 때 내보낼 플래그 (기본값 명시용, 없으면 생략)
  "group": "format",                 // groups 의 id 중 하나
  "order": 760,                      // 명령어 안에서의 순서 (3xx 접속, 4xx 호환, 5xx 일관성, 6xx 내용, 7xx 형식, 8xx 성능)
  "label": { "ko": "...", "en": "..." },
  "help":  { "ko": "...", "en": "..." },
  "type": "flag",                    // "flag" 또는 "value" (값 입력란이 붙음, --flag=값)
  "defaultChecked": false,
  "risk": "low",                     // low / medium / high / critical → 글자색
  "tier": "advanced",                // basic(기본 노출) / advanced(고급 접기)
  "conflicts": [],                   // 함께 켤 수 없는 옵션 id
  "implies": {},                     // 켜면 강제되는 옵션 (id: true/false)
  "requiresMode": [],                // 유효한 대상 모드 (MultipleDatabases)
  "clients": [],                     // 존재하는 클라이언트 (mysql57, mysql80). 비우면 전부
  "dataOnly": false                  // true 면 조건이 DDL 일 때 잠기고 명령어에서 빠짐
}
```

## 로드맵

- 1단계 (완료): 명령어 생성·복사, 규칙 경고, 내장/사용자 프리셋, 한글·영문
- 2단계 (완료): .sh / .bat / .ps1 스크립트, 스크립트 목록, Import, DDL / 데이터 파일 분리, 소켓
- 3단계 (검토): Source DB 접속 → 스키마·테이블 트리 선택, 프로그램 안에서 실행

## 라이선스

[MIT License](LICENSE) © 2026 PinguNa — 개인·회사 구분 없이 자유롭게 쓰고, 고치고, 다시 배포할 수 있습니다. 다시 배포할 때는 `LICENSE` 파일(저작권 표시)을 함께 넣어 주세요. 프로그램은 있는 그대로 제공되며 사용 결과에 대한 보증은 없습니다.

내장 글꼴 Noto Sans KR 은 SIL Open Font License 입니다. 실행 아이콘은 직접 그린 것입니다.
