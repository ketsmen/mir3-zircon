@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "PROJECT=%REPO_ROOT%\Tools\DropExportRunner\DropExportRunner.csproj"

dotnet run --project "%PROJECT%" -c Debug -- local-validate %*
exit /b %ERRORLEVEL%
