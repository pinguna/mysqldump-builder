@echo off
REM ------------------------------------------------------------
REM  mysqldump Command Builder - portable single-exe build
REM  Output: publish\MysqlDumpBuilder.exe (+ options.schema.json)
REM  Target PC needs no .NET install (self-contained).
REM  (English only: cmd.exe mis-parses non-ASCII batch files.)
REM ------------------------------------------------------------
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  if exist "C:\Program Files\dotnet\dotnet.exe" (
    set "PATH=C:\Program Files\dotnet;%PATH%"
  ) else (
    echo .NET 8 SDK is required: winget install Microsoft.DotNet.SDK.8
    exit /b 1
  )
)

echo [1/3] Running tests
dotnet test MysqlDumpBuilder.Core.Tests -nologo -v q
if errorlevel 1 (
  echo Tests failed. Publish aborted.
  exit /b 1
)

echo [2/3] Publishing single exe
dotnet publish MysqlDumpBuilder.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish -nologo -v q
if errorlevel 1 exit /b 1

echo [3/3] Cleaning publish folder
del /q publish\*.pdb 2>nul
copy /y README.md publish\README.md >nul 2>nul

echo.
echo Done: %cd%\publish\MysqlDumpBuilder.exe
dir publish
endlocal
