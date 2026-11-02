@echo off
setlocal EnableDelayedExpansion

cd /d "%~dp0..\src"
set "REPO=%~dp0.."

tasklist /FI "IMAGENAME eq Lassistant.exe" 2>nul | find /I "Lassistant.exe" >nul
if not errorlevel 1 (
  echo Lassistant.exe is running. Trying to close it...
  taskkill /IM Lassistant.exe /F >nul 2>&1
  timeout /t 3 /nobreak >nul
)
tasklist /FI "IMAGENAME eq LamaraWord.exe" 2>nul | find /I "LamaraWord.exe" >nul
if not errorlevel 1 (
  echo LamaraWord.exe is running. Trying to close it...
  taskkill /IM LamaraWord.exe /F >nul 2>&1
  timeout /t 3 /nobreak >nul
)

set "OUT="
set "PROJ=Lassistant.App\Lassistant.App.csproj"
set "OPTS=-c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true"

echo Building Lassistant (Release, win-x64, self-contained)...

for %%O in (
  "..\publish\win-x64"
  "..\publish\win-x64-new"
  "..\publish\win-x64-build"
) do (
  if "!OUT!"=="" (
    echo Trying %%~O ...
    dotnet publish %PROJ% %OPTS% -o %%~O
    if not errorlevel 1 set "OUT=%%~O"
  )
)

if "!OUT!"=="" (
  set "STAMP=%date:~-4,4%%date:~3,2%%date:~0,2%_%time:~0,2%%time:~3,2%%time:~6,2%"
  set "STAMP=!STAMP: =0!"
  set "OUT=..\publish\win-x64-!STAMP!"
  echo All default folders are locked. Trying !OUT! ...
  dotnet publish %PROJ% %OPTS% -o "!OUT!"
  if errorlevel 1 (
    echo.
    echo BUILD FAILED: Lassistant.exe is locked by a running process.
    echo Close ALL Lassistant windows in Task Manager ^(Ctrl+Shift+Esc^), then run publish.bat again.
    exit /b 1
  )
)

echo Copying config, templates, docs and scripts...
xcopy /E /I /Y "..\config" "%OUT%\config\" >nul
xcopy /E /I /Y "..\templates" "%OUT%\templates\" >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0copy-publish-docs.ps1" -RepoRoot "%REPO%" -OutDir "%OUT%"
if errorlevel 1 (
  echo WARNING: docs\rukovodstva and docs\pravila were not copied.
)
if not exist "%OUT%\scripts" mkdir "%OUT%\scripts"
if exist "..\scripts\ner_helper.py" copy /Y "..\scripts\ner_helper.py" "%OUT%\scripts\" >nul
if exist "..\scripts\contract_ner" xcopy /E /I /Y "..\scripts\contract_ner" "%OUT%\scripts\contract_ner\" >nul
if exist "..\models\contract-ner" (
  echo Copying Contract NER model...
  xcopy /E /I /Y "..\models\contract-ner" "%OUT%\models\contract-ner\" >nul
)

echo.
echo SUCCESS: %OUT%\Lassistant.exe
if not "%OUT%"=="..\publish\win-x64" (
  echo.
  echo NOTE: Default folder was locked. Run the new exe from the path above.
)
endlocal
