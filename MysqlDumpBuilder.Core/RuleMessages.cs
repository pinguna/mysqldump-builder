namespace MysqlDumpBuilder.Core;

/// <summary>
/// 규칙(Export R*, Import I*)과 입력 검증(V_*) 메시지. 코드 하나에 한글/영문 문구 한 쌍.
/// 메시지 문구만 바꾸고 싶을 때 이 파일만 고치면 된다.
/// 빈 번호는 폐기된 규칙이고 번호는 재사용하지 않는다:
///   R1, R2, R11~R13 — 초기 설계에서 다른 규칙에 합쳐지거나 뺀 번호. R14, I4 — --all-databases 모드를 없애면서 함께 삭제.
///   I7 — Import 만 할 때 파일명 템플릿을 읽을 파일로 쓴다는 안내. "읽을 파일" 칸이 생기면서 V_IMPORT_NO_FILE 로 대체.
/// </summary>
public static class RuleMessages
{
    private static readonly Dictionary<string, (Severity Severity, string Ko, string En)> Table = new()
    {
        // ---- 입력 검증 ----
        ["V_NO_DATABASE"] = (Severity.Error,
            "DB 이름을 입력하세요.",
            "Enter a database name."),
        ["V_NO_TABLES"] = (Severity.Error,
            "테이블 지정 모드에서는 테이블을 하나 이상 입력하세요.",
            "Enter at least one table in table mode."),
        ["V_NO_LOGIN_PATH"] = (Severity.Error,
            "--login-path 이름을 입력하세요.",
            "Enter the --login-path name."),
        ["V_NO_EXTRA_FILE"] = (Severity.Error,
            "--defaults-extra-file 경로를 입력하세요.",
            "Enter the --defaults-extra-file path."),
        ["V_NO_OUTPUT_FILE"] = (Severity.Error,
            "출력 파일명을 입력하세요.",
            "Enter an output file name."),
        ["V_GZIP_NOT_BASH"] = (Severity.Error,
            "gzip 파이프 출력은 Linux(bash)에서만 쓸 수 있습니다. 출력 방식을 바꾸거나 OS를 Linux로 바꾸세요.",
            "gzip pipe output is only available on Linux (bash). Change the output mode or target OS."),
        ["V_IGNORE_TABLE_FORMAT"] = (Severity.Error,
            "제외 테이블은 반드시 db.table 형식이어야 합니다 (와일드카드 불가): {0}",
            "Ignore-table entries must be in db.table form (no wildcards): {0}"),
        ["V_EMPTY_VALUE"] = (Severity.Error,
            "'{0}' 옵션의 값을 입력하세요.",
            "Enter a value for option '{0}'."),

        // ---- 규칙 ----
        ["R3_SINGLE_TX_VS_LOCK_ALL"] = (Severity.Error,
            "--single-transaction 과 --lock-all-tables 는 함께 쓸 수 없습니다.",
            "--single-transaction and --lock-all-tables are mutually exclusive."),
        ["R4_LOCK_TABLES_AUTO_OFF"] = (Severity.Info,
            "--single-transaction 선택으로 테이블 잠금(--lock-tables)이 자동으로 꺼졌습니다 (--skip-lock-tables).",
            "--single-transaction turns table locking off automatically (--skip-lock-tables)."),
        ["R5_NEEDS_DATABASES_MODE"] = (Severity.Error,
            "'{0}' 옵션은 여러 DB(--databases) 모드에서만 쓸 수 있습니다.",
            "'{0}' is only valid with --databases."),
        ["R6_WHERE_WITHOUT_TABLES"] = (Severity.Warning,
            "--where 조건이 모든 테이블에 적용됩니다. 조건의 컬럼이 없는 테이블에서 오류가 납니다. 테이블 지정 모드를 권장합니다.",
            "--where applies to every table and fails on tables lacking the column. Table mode is recommended."),
        ["R7_COMPACT_IMPLIES"] = (Severity.Info,
            "--compact 는 주석·DROP TABLE·LOCK·DISABLE KEYS·SET NAMES 생략을 모두 포함하므로 해당 옵션은 따로 내보내지 않습니다.",
            "--compact already implies skipping comments, DROP TABLE, LOCK, DISABLE KEYS and SET NAMES; those flags are not emitted separately."),
        ["R8_DROP_TABLE_MEANINGLESS"] = (Severity.Info,
            "데이터만 덤프할 때는 DROP TABLE 관련 옵션이 의미가 없어 생략했습니다.",
            "DROP TABLE options are meaningless for a data-only dump and were omitted."),
        ["R9_DATA_ONLY_WITH_TRIGGERS"] = (Severity.Warning,
            "데이터만 덤프하는데 트리거가 포함됩니다. 대상 DB에 같은 트리거가 있으면 복원이 실패합니다. '트리거 제외'를 권장합니다.",
            "Data-only dump still includes triggers; restore fails if the target already has them. Consider 'Exclude triggers'."),
        ["R10_OPTION_NOT_IN_CLIENT"] = (Severity.Error,
            "'{0}' 옵션은 선택한 클라이언트({1})에 없습니다.",
            "'{0}' does not exist in the selected client ({1})."),
        ["R15_PLAIN_PASSWORD"] = (Severity.Warning,
            "비밀번호가 명령어에 평문으로 들어갑니다. ps 와 셸 히스토리에 남습니다.",
            "The password is embedded in plain text and will show in ps and shell history."),
        ["R16_EXTRA_FILE_FIRST"] = (Severity.Info,
            "--defaults-extra-file 은 반드시 첫 번째 인자여야 하므로 맨 앞에 배치했습니다.",
            "--defaults-extra-file must be the first argument and was placed first."),
        ["R17_PS_REDIRECT_UTF16"] = (Severity.Info,
            "PowerShell 의 > 는 파일을 UTF-16 으로 다시 써서(한글도 깨짐) Import 때 '\\0' 오류가 나므로 --result-file 로 바꿔 넣었습니다.",
            "PowerShell's > rewrites the file as UTF-16 (and garbles non-ASCII), which fails on import with an '\\0' error, so --result-file is used instead."),
        ["R18_CONFLICT"] = (Severity.Error,
            "'{0}' 과 '{1}' 은 함께 쓸 수 없습니다.",
            "'{0}' and '{1}' cannot be used together."),
        ["R19_IMPLIED"] = (Severity.Info,
            "'{0}' 선택으로 '{1}' 이 자동으로 켜졌습니다.",
            "'{0}' turned on '{1}' automatically."),
        ["R20_IGNORE_TABLE_PREFIXED"] = (Severity.Info,
            "제외 테이블 '{0}' 에 DB 이름을 붙여 '{1}' 로 내보냈습니다.",
            "Ignore-table '{0}' was qualified as '{1}'."),
        ["R22_PS_DOUBLE_QUOTE_IN_VALUE"] = (Severity.Warning,
            "값 안에 큰따옴표(\")가 있습니다. Windows PowerShell 5.1 은 이를 외부 프로그램에 제대로 전달하지 못합니다. 작은따옴표로 바꾸거나 bash/cmd 를 쓰세요.",
            "A value contains a double quote (\"). Windows PowerShell 5.1 cannot pass it correctly to external programs. Use single quotes instead, or bash/cmd."),
        ["R21_PLAIN_PASSWORD_PS"] = (Severity.Info,
            "PowerShell 에서는 --password 값에 $ 가 있으면 변수로 해석되므로 작은따옴표로 감쌌습니다.",
            "In PowerShell a $ in the password would be expanded; the value was wrapped in single quotes."),

        // ---- Import 단계 (I1~) ----
        ["V_IMPORT_NO_DATABASE"] = (Severity.Error,
            "Import 대상 DB 이름을 입력하세요 (비우면 원본의 첫 DB 이름을 쓰는데, 그것도 없습니다).",
            "Enter the import target database (it defaults to the first source database, which is empty)."),
        ["I1_SAME_AS_SOURCE"] = (Severity.Warning,
            "Import 대상이 원본과 같습니다 (같은 서버의 같은 DB). 백업한 것을 같은 DB 에 다시 넣는(복원) 작업이면 그대로 쓰고, 다른 곳에 넣으려면 대상 DB 이름이나 접속 정보를 바꾸세요.",
            "The import target is the same as the source (same server, same database). Keep it to restore the backup in place; otherwise change the target database or connection."),
        ["I2_PS_SOURCE"] = (Severity.Info,
            "PowerShell 은 < 리다이렉션이 없어 --execute=\"source 파일\" 로 넣습니다 (경로의 \\ 는 mysql 이 명령으로 읽으므로 / 로 바꿉니다). --binary-mode 를 붙이면 source 가 SQL 로 보내져 1064 오류가 나고, Get-Content | mysql 은 한글이 ? 로 바뀌니 쓰지 마세요.",
            "PowerShell has no < redirection, so the file is loaded with --execute=\"source file\" (backslashes become /, since mysql reads \\ as a command). Do not add --binary-mode (source is then sent to the server, error 1064) or use Get-Content | mysql (non-ASCII becomes ?)."),
        ["I11_OTHER_INPUT_FILE"] = (Severity.Warning,
            "Export 가 만드는 파일이 아니라 읽을 파일({0})을 Import 합니다. Export 는 끝났고 Import 만 다시 하려면 작업을 Import 로 바꾸세요 (Export 를 또 하지 않도록). 읽을 파일을 비우면 Export 파일을 읽습니다.",
            "Importing the input file ({0}) instead of the file the export creates. To redo only the import, switch the job to Import so the export does not run again. Clear the input file to read the export file."),
        ["I10_UTF16_FILE"] = (Severity.Warning,
            "읽을 파일이 UTF-16 입니다 (PowerShell 의 > 로 만든 파일). mysql 은 '\\0' 오류로 읽지 못하고 한글은 이미 깨졌을 수 있습니다. --result-file 로 다시 Export 하세요.",
            "The input file is UTF-16 (made with PowerShell's >). mysql fails with an '\\0' error and non-ASCII text may already be garbled. Export it again with --result-file."),
        ["I3_GZIP_IMPORT"] = (Severity.Info,
            "gzip 파일은 gunzip -c 파일 | mysql 로 넣습니다.",
            "The gzip file is loaded with gunzip -c file | mysql."),
        ["I5_DATA_ONLY_NEEDS_TABLES"] = (Severity.Warning,
            "DML 만 Export 하므로 대상 DB 에 테이블이 이미 있어야 Import 됩니다.",
            "Only DML is exported, so the target database must already have the tables."),
        ["I6_FORCE"] = (Severity.Warning,
            "--force 는 SQL 오류가 나도 계속 진행합니다. 일부 테이블이 빠진 채 끝날 수 있으니 출력 로그를 확인하세요.",
            "--force keeps going after SQL errors; some tables may be missing at the end. Check the output log."),
        // I7 (Import 만 할 때 파일명 템플릿을 읽을 파일로 쓴다는 안내) 는 "읽을 파일" 칸이 생기면서 폐기. 아래 V_IMPORT_NO_FILE 이 대신한다.
        ["V_IMPORT_NO_FILE"] = (Severity.Error,
            "Import 할 파일을 입력하세요 (Import 대상 > 읽을 파일).",
            "Enter the file to import (Import target > Input file)."),
        ["R24_NO_USER"] = (Severity.Warning,
            "User 가 비어 있어 -u 없이 접속합니다. 그러면 OS 로그인 계정 이름으로 접속해 보통 Access denied 가 납니다. MySQL 계정을 적으세요.",
            "User is empty, so the command connects without -u. MySQL then uses the OS login name, which usually ends in Access denied. Enter the MySQL account."),
        ["R26_LOCALHOST_PORT"] = (Severity.Warning,
            "localhost 는 TCP 가 아니라 유닉스 소켓으로 접속해 포트 {0} 이 무시됩니다. my.cnf 의 [client] socket= 또는 기본 소켓의 인스턴스(보통 3306)에 붙습니다. my.cnf 의 소켓이 포트 {0} 인스턴스 것이면 그대로 두어도 됩니다. 아니면 소켓 경로를 적거나, Host 를 127.0.0.1 로 바꾸거나, TCP 강제를 켜세요.",
            "localhost connects through the Unix socket, not TCP, so port {0} is ignored and you reach the instance behind socket= in the [client] section of my.cnf, or the default socket (usually 3306). If the my.cnf socket belongs to the port {0} instance, you can leave it. Otherwise enter the socket path, use 127.0.0.1 as Host, or turn on Force TCP."),
        ["R27_SOCKET_IGNORED"] = (Severity.Info,
            "TCP 강제가 켜져 있어 소켓 경로는 넣지 않습니다.",
            "Force TCP is on, so the socket path is left out."),
        ["R25_CONTENT_SPLIT"] = (Severity.Info,
            "DDL 과 데이터를 파일 두 개로 나눕니다: _ddl 파일(테이블·뷰·루틴·이벤트·트리거 구조, --no-data)과 _data 파일(데이터만, --no-create-info --skip-triggers). Import 하면 DDL 파일을 먼저 넣습니다.",
            "Structure and data go to two files: the _ddl file (tables, views, routines, events, triggers; --no-data) and the _data file (rows only; --no-create-info --skip-triggers). Import loads the DDL file first."),
        ["R23_DDL_SKIPS_DML"] = (Severity.Info,
            "DDL 만 내보내므로 데이터(INSERT) 관련 옵션은 생략했습니다: {0}",
            "DDL-only export: data (INSERT) related options were left out: {0}"),
        ["I8_SPLIT_PER_DB"] = (Severity.Info,
            "여러 DB + Import 는 DB 마다 mysqldump 와 mysql 을 따로 만듭니다 (--databases 한 파일에는 USE 문이 들어 있어 대상 DB 이름을 바꿀 수 없습니다). --databases, --add-drop-database, --no-create-db 는 쓰지 않습니다.",
            "Multi DB + Import generates one mysqldump and one mysql per database (a single --databases file carries USE statements, so target names could not be changed). --databases, --add-drop-database and --no-create-db are not used."),
        ["I9_TARGET_COUNT_MISMATCH"] = (Severity.Error,
            "Import 대상 DB 가 {0}개인데 원본 DB 는 {1}개입니다. 쉼표로 구분해 원본과 같은 순서로 적거나, 비워서 같은 이름을 쓰세요.",
            "{0} import target database(s) for {1} source database(s). List them comma-separated in the same order as the source, or leave blank to reuse the source names."),
    };

    public static BuildMessage Make(string code, string? optionId = null, params object[] args)
    {
        if (!Table.TryGetValue(code, out var entry))
            throw new ArgumentException($"알 수 없는 메시지 코드: {code}");
        var ko = args.Length == 0 ? entry.Ko : string.Format(entry.Ko, args);
        var en = args.Length == 0 ? entry.En : string.Format(entry.En, args);
        return new BuildMessage(code, entry.Severity, new LocalizedText(ko, en), optionId);
    }
}
