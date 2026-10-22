using MysqlDumpBuilder.Core;

namespace MysqlDumpBuilder.App;

/// <summary>
/// 화면에 보이는 모든 문구 (한글/영문). 옵션 체크박스의 라벨은 스키마(options.schema.json)에 있고,
/// 경고 메시지는 Core 의 RuleMessages 에 있다. 문구를 바꾸려면 이 파일만 고치면 된다.
/// </summary>
public static class UiStrings
{
    private static readonly Dictionary<string, LocalizedText> Table = new()
    {
        ["Title"] = new("mysqldump 명령어 생성기", "mysqldump Command Builder"),

        // 상단 바
        ["Preset"] = new("프리셋", "Preset"),
        ["PresetSave"] = new("프리셋 저장", "Save preset"),
        ["PresetDelete"] = new("삭제", "Delete"),
        ["PresetNone"] = new("(선택 안 함)", "(none)"),
        ["PresetBuiltInPrefix"] = new("[기본] ", "[default] "),
        ["PresetNameTitle"] = new("프리셋 저장", "Save preset"),
        ["PresetNamePrompt"] = new("프리셋 이름 (접속 정보는 저장되지만 패스워드는 저장되지 않습니다):", "Preset name (connection info is saved; the password is not):"),
        ["PresetOverwriteConfirm"] = new("같은 이름의 프리셋이 있습니다. 덮어쓸까요?", "A preset with this name exists. Overwrite?"),
        ["PresetDeleteConfirm"] = new("프리셋 '{0}' 을(를) 삭제할까요?", "Delete preset '{0}'?"),
        ["PresetBuiltInReadOnly"] = new("기본 프리셋은 삭제할 수 없습니다. 수정하려면 다른 이름으로 저장하세요.", "Default presets cannot be deleted. Save under another name to customize."),
        ["Language"] = new("언어", "Language"),
        ["Reset"] = new("초기화", "Reset"),
        ["ResetConfirm"] = new("모든 옵션을 기본값으로 되돌릴까요? (접속 정보는 유지)", "Reset all options to defaults? (connection info is kept)"),

        // [A] 접속
        ["GroupConnection"] = new("Export 접속 정보", "Export connection"),
        ["Host"] = new("Host", "Host"),
        ["Port"] = new("Port", "Port"),
        ["Socket"] = new("소켓", "Socket"),
        ["SocketHint"] = new("유닉스 소켓 경로 (-S), 예: /var/lib/mysql/mysql.sock. Host 가 localhost 이거나 비어 있을 때만 씁니다 — 리눅스 클라이언트는 localhost 면 TCP 대신 소켓으로 붙고 포트를 무시합니다. 비우면 my.cnf 의 [client] socket= (--defaults-extra-file 을 쓰면 그 파일 포함), 거기에도 없으면 클라이언트 기본 경로를 씁니다. Windows 클라이언트는 localhost 도 TCP 라 필요 없습니다.", "Unix socket path (-S), e.g. /var/lib/mysql/mysql.sock. Used only when Host is localhost or empty: Linux clients connect to localhost through the socket and ignore the port. Leave empty to use socket= from the [client] section of my.cnf (including a --defaults-extra-file), or the client default path if none is set. Windows clients use TCP for localhost and do not need it."),
        ["User"] = new("User", "User"),
        ["PasswordMode"] = new("패스워드", "Password"),
        ["PwPrompt"] = new("실행 시 입력 (-p)", "Prompt at run time (-p)"),
        ["PwLoginPath"] = new("저장된 로그인 정보 (--login-path)", "Saved login (--login-path)"),
        ["PwExtraFile"] = new("설정 파일 (--defaults-extra-file)", "Config file (--defaults-extra-file)"),
        ["PwPlain"] = new("평문 포함 (--password=) ⚠", "Plain text (--password=) ⚠"),
        ["PwValueLoginPath"] = new("login-path 이름", "login-path name"),
        ["PwValueExtraFile"] = new("설정 파일 경로", "config file path"),
        ["PwValuePlain"] = new("패스워드", "password"),
        ["Client"] = new("클라이언트", "Client"),
        ["ClientMysql57"] = new("MySQL 5.7", "MySQL 5.7"),
        ["ClientMysql80"] = new("MySQL 8.0+", "MySQL 8.0+"),
        ["BinaryPath"] = new("mysqldump 경로", "mysqldump path"),

        // [B] 대상
        ["GroupTarget"] = new("Export 대상", "Export source"),
        ["TargetSingle"] = new("단일 DB", "Single DB"),
        ["TargetTables"] = new("DB + 테이블 지정", "DB + tables"),
        ["TargetMulti"] = new("여러 DB (--databases)", "Multi DB (--databases)"),
        ["Databases"] = new("DB Name", "DB Name"),
        ["DatabasesHint"] = new("여러 DB 모드에서는 공백이나 쉼표로 구분", "Separate with spaces or commas in multi-database mode"),
        ["Tables"] = new("테이블", "Tables"),
        ["TablesHint"] = new("공백이나 쉼표로 구분", "Separate with spaces or commas"),
        ["IgnoreTables"] = new("제외 테이블", "Ignore tables"),
        ["IgnoreTablesHint"] = new("한 줄에 하나, db.table 형식 (DB 하나 모드에서는 테이블명만 써도 됨)", "One per line, db.table form (table name alone is fine in one-database mode)"),
        ["Where"] = new("WHERE 조건", "WHERE"),

        // [C] 옵션
        ["GroupOptions"] = new("이관 조건", "Dump options"),
        ["ContentMode"] = new("조건", "Scope"),
        ["ContentBoth"] = new("DDL + DML", "DDL + DML"),
        ["ContentStructure"] = new("DDL (--no-data)", "DDL (--no-data)"),
        ["ContentData"] = new("DML (--no-create-info)", "DML (--no-create-info)"),
        ["ContentSplit"] = new("DDL + DML 파일 분리", "DDL + DML, split files"),
        ["InsertMode"] = new("INSERT 형태", "INSERT style"),
        ["InsertNormal"] = new("INSERT", "INSERT"),
        ["InsertIgnore"] = new("INSERT IGNORE (중복 키 건너뜀)", "INSERT IGNORE (skip duplicates)"),
        ["InsertReplace"] = new("REPLACE (중복 키 덮어씀)", "REPLACE (overwrite duplicates)"),
        ["ShowAdvanced"] = new("고급 옵션 표시", "Show advanced options"),

        // [D] 출력
        ["GroupOutput"] = new("출력", "Output"),
        ["OutResultFile"] = new("--result-file=파일 (Windows 권장)", "--result-file=file (recommended on Windows)"),
        ["OutRedirect"] = new("> 파일 (리다이렉션)", "> file (redirect)"),
        ["OutGzip"] = new("| gzip > 파일.gz (Linux 전용)", "| gzip > file.gz (Linux only)"),
        ["FileTemplate"] = new("파일명 템플릿", "File name template"),
        ["FileTemplateHint"] = new("토큰: {db} {host} {date:yyyyMMdd} {time:HHmm}", "Tokens: {db} {host} {date:yyyyMMdd} {time:HHmm}"),
        ["ScriptTemplate"] = new("스크립트명 템플릿", "Script name template"),
        ["ScriptTemplateHint"] = new("토큰은 파일명과 같습니다. 확장자(.sh/.bat/.ps1)는 대상 OS에 맞춰 자동으로 붙습니다.", "Same tokens as the file name. The extension (.sh/.bat/.ps1) follows the target shell."),
        ["FileNamePreview"] = new("미리보기", "Preview"),
        ["PreviewTitle"] = new("옵션 미리보기", "Options preview"),
        ["ScriptPreviewTitle"] = new("스크립트 미리보기:", "Script preview:"),
        ["BatchPreviewTitle"] = new("스크립트 미리보기 ({0}건)", "Script preview ({0} items)"),
        ["BatchCount"] = new(" ({0}건)", " ({0} items)"),
        ["BatchErrors"] = new("(스크립트 목록 {0}번 항목 오류: {1}\n항목을 더블클릭해 불러온 뒤 고치고 \"덮어쓰기\"를 누르세요)", "(script list item {0} has an error: {1}\nDouble-click the item to load it, fix it, then press \"Overwrite\")"),
        ["BatchAddBlocked"] = new("오류가 있는 설정은 목록에 넣을 수 없습니다. 경고·설명의 오류를 먼저 해결하세요.", "Settings with errors cannot be added. Fix the errors listed under Warnings and notes first."),
        ["BatchLoaded"] = new("목록 {0}번 항목을 불러왔습니다. 고친 뒤 \"덮어쓰기\"를 누르세요.", "Loaded item {0}. Edit it, then press \"Overwrite\"."),
        ["DarkMode"] = new("다크 모드", "Dark mode"),
        ["BatchItemPrefix"] = new("[목록 {0}] ", "[item {0}] "),
        ["ImportMsgPrefix"] = new("[Import] ", "[Import] "),

        // [D2] 작업 종류와 Import 대상
        ["JobKind"] = new("작업", "Job"),
        ["JobExport"] = new("Export", "Export"),
        ["JobExportImport"] = new("Export + Import", "Export + Import"),
        ["JobImport"] = new("Import", "Import"),
        ["JobHint"] = new("Export: mysqldump 로 파일 생성. Export + Import: 생성한 파일을 바로 대상 DB 에 넣기. Import: 이미 있는 파일(Import 대상 > 읽을 파일)을 대상 DB 에 넣기.", "Export: create the file with mysqldump. Export + Import: then load it into the target. Import: load an existing file (Import target > Input file) into the target."),
        ["GroupImport"] = new("Import 대상", "Import target"),
        ["ImportInputFile"] = new("읽을 파일", "Input file"),
        ["ImportInputFileHint"] = new("대상 DB 에 넣을 .sql 또는 .sql.gz 파일. 스크립트를 실행하는 서버 기준 경로입니다. 여러 DB 면 {db} 가 각 DB 이름으로 바뀝니다 (예: backup/{db}.sql). .gz 는 gunzip 파이프로 넣습니다 (bash). Export + Import 에서는 비워 두면 Export 가 만드는 파일을 읽고, 적으면 그 파일을 읽습니다 (이미 받아 둔 파일로 Import 만 다시 할 때).", "The .sql or .sql.gz file to load, as a path on the server that runs the script. For Multi DB, {db} becomes each database name (e.g. backup/{db}.sql). .gz is piped through gunzip (bash). With Export + Import, leave it blank to read the exported file, or enter a file to read that instead (to redo the import from an earlier dump)."),
        ["ImportInputFollowsExport"] = new("{0} (자동)", "{0} (auto)"),
        ["Browse"] = new("찾아보기…", "Browse…"),
        ["InputFileFilter"] = new("SQL 파일 (*.sql;*.gz)|*.sql;*.gz|모든 파일 (*.*)|*.*", "SQL files (*.sql;*.gz)|*.sql;*.gz|All files (*.*)|*.*"),
        ["ImportSameConnection"] = new("접속 정보가 원본(Source)과 같음", "Same connection as the source"),
        ["ImportDatabase"] = new("대상 DB", "Target DB"),
        ["ImportDatabaseHint"] = new("원본 DB 를 따라 채워집니다. 다른 이름으로 넣으려면 고치세요. 여러 DB 면 쉼표로 원본과 같은 순서로 적습니다 (예: test123, test2, test3). 비우면 원본과 같은 이름.", "Filled from the source databases; edit to import under a different name. For Multi DB, list comma-separated in the same order as the source (e.g. test123, test2, test3). Blank = same names."),
        ["ImportBinaryPath"] = new("mysql 경로", "mysql path"),
        ["ImportCharset"] = new("문자셋", "Charset"),
        ["ImportCreateDb"] = new("DB 생성", "Create DB"),
        ["ImportCreateDbHint"] = new("CREATE DATABASE IF NOT EXISTS 와 USE 를 파일 앞에 붙여 한 번에 넣습니다 (mysql 실행은 한 번).", "Prepends CREATE DATABASE IF NOT EXISTS and USE to the file, so mysql still runs once."),
        ["ImportForce"] = new("오류 무시 (--force)", "Ignore errors (--force)"),
        ["ImportFkOff"] = new("FK 검사 끄기", "Disable FK checks"),
        ["ImportFkOffHint"] = new("--init-command=\"SET FOREIGN_KEY_CHECKS=0\". 파일에 SET 문이 없을 때(--compact 등) 테이블 순서 때문에 실패하는 것을 막습니다.", "--init-command=\"SET FOREIGN_KEY_CHECKS=0\". Prevents ordering failures when the file has no SET statements (e.g. --compact)."),

        // [G] Export 목록
        ["BatchTitle"] = new("스크립트 목록", "Script list"),
        ["BatchAdd"] = new("현재 설정 추가", "Add current settings"),
        ["BatchAddHint"] = new("왼쪽 화면의 설정을 목록 끝에 새 항목으로 추가합니다. 오류가 있거나 같은 항목이 이미 있으면 눌리지 않습니다. 목록에 항목이 있으면 복사·저장은 목록 전체를 순서대로 한 스크립트로 냅니다. 항목을 더블클릭하면 화면으로 불러옵니다.", "Appends the current settings as a new item. When the list is not empty, Copy and Save emit every item in order as one script. Double-click an item to load it back."),
        ["BatchUpdate"] = new("덮어쓰기", "Overwrite"),
        ["BatchUpdateHint"] = new("선택한 항목을 현재 화면 설정으로 바꿉니다 (더블클릭으로 불러와 고친 뒤 되돌려 넣을 때).", "Replaces the selected item with the current settings (after loading it by double-click and editing)."),
        ["BatchRemove"] = new("삭제", "Remove"),
        ["BatchUp"] = new("위로", "Up"),
        ["BatchDown"] = new("아래로", "Down"),
        ["BatchClear"] = new("초기화", "Reset"),
        ["BatchClearConfirm"] = new("스크립트 목록을 초기화할까요? (항목이 모두 지워집니다)", "Reset the script list? (all items are removed)"),
        ["BatchColTarget"] = new("대상 DB", "Target DB"),
        ["BatchImportSuffix"] = new(" + Import", " + Import"),
        ["BatchColNo"] = new("#", "#"),
        ["BatchColScope"] = new("조건", "Scope"),
        ["BatchColFile"] = new("파일명", "File name"),

        // [E] 결과
        ["GroupCommand"] = new("생성된 명령어", "Generated command"),
        ["Shell"] = new("대상 OS", "Target shell"),
        ["ShellBash"] = new("Linux (bash)", "Linux (bash)"),
        ["ShellCmd"] = new("Windows (cmd)", "Windows (cmd)"),
        ["ShellPs"] = new("Windows (PowerShell)", "Windows (PowerShell)"),
        ["MultiLine"] = new("여러 줄", "Multi-line"),
        ["ExplicitDefaults"] = new("기본값 명시", "Show defaults"),
        ["Copy"] = new("스크립트 복사", "Copy script"),
        ["Copied"] = new("클립보드에 복사했습니다.", "Copied to clipboard."),
        ["NothingToCopy"] = new("오류를 먼저 해결하세요.", "Fix the errors first."),
        ["NoCommand"] = new("(오류를 해결하면 명령어가 생성됩니다)", "(the command appears once the errors are fixed)"),
        ["ScriptPreview"] = new("스크립트 미리보기", "Script preview"),
        ["ScriptPreviewHint"] = new("켜면 명령어 대신 내보낼 스크립트 파일 내용(.sh/.bat/.ps1)을 보여 주고, 복사도 스크립트 전체를 복사합니다.", "Shows the script file (.sh/.bat/.ps1) instead of the bare command; Copy then copies the whole script."),
        ["ExportScript"] = new("스크립트 저장", "Save script"),
        ["ExportScriptHint"] = new("대상 OS에 맞는 실행 스크립트 파일로 저장합니다. 날짜·시간 토큰은 실행 시점에 치환됩니다.", "Saves a runnable script for the target shell. Date/time tokens are evaluated when the script runs."),
        ["ScriptSaved"] = new("저장됨: {0}", "Saved: {0}"),
        ["ScriptSavedChmod"] = new("저장됨: {0} — 서버에서 chmod +x 로 실행 권한을 주세요.", "Saved: {0} — run chmod +x on the server."),
        ["ScriptFilterSh"] = new("Linux 셸 스크립트 (*.sh)|*.sh|모든 파일 (*.*)|*.*", "Linux shell script (*.sh)|*.sh|All files (*.*)|*.*"),
        ["ScriptFilterBat"] = new("Windows 배치 파일 (*.bat)|*.bat|모든 파일 (*.*)|*.*", "Windows batch file (*.bat)|*.bat|All files (*.*)|*.*"),
        ["ScriptFilterPs1"] = new("PowerShell 스크립트 (*.ps1)|*.ps1|모든 파일 (*.*)|*.*", "PowerShell script (*.ps1)|*.ps1|All files (*.*)|*.*"),

        // [F] 메시지
        ["GroupMessages"] = new("경고·설명", "Warnings and notes"),
        ["SevError"] = new("오류", "Error"),
        ["SevWarning"] = new("경고", "Warning"),
        ["SevInfo"] = new("정보", "Info"),
        ["ColNo"] = new("#", "#"),
        ["ColSeverity"] = new("구분", "Level"),
        ["ColMessage"] = new("내용", "Message"),
        ["MessageDetailTitle"] = new("경고·설명 상세", "Message detail"),
        ["BatchDetailTitle"] = new("스크립트 목록 상세", "Script list item"),
        ["BatchLoad"] = new("왼쪽에 불러오기", "Load into form"),
        ["GoToOption"] = new("옵션으로 이동", "Go to option"),
        ["CopyText"] = new("복사", "Copy"),
        ["Close"] = new("닫기", "Close"),

    };

    public static Language Current { get; set; } = Language.Ko;

    public static string Get(string key) => Table.TryGetValue(key, out var t) ? t.Get(Current) : key;

    public static LocalizedText Text(string key) => Table.TryGetValue(key, out var t) ? t : new LocalizedText(key, key);
}
