@echo off
REM ------------------------------------------------------------
REM  Build the zip to attach to a GitHub Release.
REM  Runs publish.cmd (tests + single exe), then packs
REM  publish\MysqlDumpBuilder.exe + options.schema.json + README.md + docs\ (user guide)
REM  + LICENSE and the embedded font license (MIT / OFL require them in copies)
REM  into release\MysqlDumpBuilder-v<version>-win-x64.zip
REM  (<version> comes from MysqlDumpBuilder.App.csproj).
REM  (English only: cmd.exe mis-parses non-ASCII batch files.)
REM ------------------------------------------------------------
setlocal
cd /d "%~dp0"

call "%~dp0publish.cmd"
if errorlevel 1 exit /b 1

set "VER="
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<Version>" MysqlDumpBuilder.App\MysqlDumpBuilder.App.csproj') do set "VER=%%v"
if not defined VER (
  echo Could not read ^<Version^> from MysqlDumpBuilder.App.csproj
  exit /b 1
)

if not exist release mkdir release
set "ZIP=release\MysqlDumpBuilder-v%VER%-win-x64.zip"
if exist "%ZIP%" del /q "%ZIP%"

echo [4/4] Packing %ZIP%
powershell -NoProfile -Command "Compress-Archive -Path 'publish\MysqlDumpBuilder.exe','publish\options.schema.json','publish\README.md','docs','LICENSE','MysqlDumpBuilder.App\Resources\NotoSansKR-OFL.txt' -DestinationPath '%ZIP%'"
if errorlevel 1 exit /b 1

echo.
echo Done: %cd%\%ZIP%
echo Next: GitHub Releases ^> Draft a new release ^> tag v%VER% ^> attach the zip
endlocal
