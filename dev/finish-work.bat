@echo off
rem End of a work session: show changes, commit everything with your message, push.
rem Databases, backups and secrets are ignored by .gitignore; still read the list before you confirm.
call "%~dp0_common.bat"
git status --short
echo.
set /p CONFIRM=Commit and push all of the above? (y/n): 
if /i not "%CONFIRM%"=="y" exit /b 0
set /p MESSAGE=Commit message: 
if "%MESSAGE%"=="" (
  echo A commit message is required.
  pause
  exit /b 1
)
git add -A
git commit -m "%MESSAGE%"
if errorlevel 1 (
  pause
  exit /b 1
)
git push
pause
