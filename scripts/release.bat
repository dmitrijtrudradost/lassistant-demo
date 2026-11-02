@echo off
setlocal EnableDelayedExpansion

rem Own publish into release\, then zip. Does not call or modify publish.bat.
rem The unpacked folder is deleted after the archive is created.

cd /d "%~dp0.."
set "REPO=%CD%"
set "PROJ=%REPO%\src\Lassistant.App\Lassistant.App.csproj"

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "(Select-String -LiteralPath '%PROJ%' -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1).Matches.Groups[1].Value"`) do set "VER=%%V"
if "%VER%"=="" (
  echo Could not read Version from src\Lassistant.App\Lassistant.App.csproj
  exit /b 1
)

set "NAME=Lassistant_v%VER%_win-x64"
set "OUT=%REPO%\release\%NAME%"
set "ZIP=%REPO%\release\%NAME%.zip"

echo Release %VER%
echo Folder: %OUT%

tasklist /FI "IMAGENAME eq Lassistant.exe" 2>nul | find /I "Lassistant.exe" >nul
if not errorlevel 1 (
  echo Lassistant.exe is running. Trying to close it...
  taskkill /IM Lassistant.exe /F >nul 2>&1
  timeout /t 3 /nobreak >nul
)

if exist "%OUT%" rmdir /s /q "%OUT%"
if not exist "%REPO%\release" mkdir "%REPO%\release"

set "OPTS=-c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true"
echo Publishing into release\%NAME% ...
dotnet publish "%PROJ%" %OPTS% -o "%OUT%"
if errorlevel 1 (
  echo BUILD FAILED.
  exit /b 1
)

echo Copying config, templates, docs, scripts and models...
xcopy /E /I /Y "%REPO%\config" "%OUT%\config\" >nul
xcopy /E /I /Y "%REPO%\templates" "%OUT%\templates\" >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%REPO%\scripts\copy-publish-docs.ps1" -RepoRoot "%REPO%" -OutDir "%OUT%"
if errorlevel 1 (
  echo WARNING: docs were not copied.
)
if not exist "%OUT%\scripts" mkdir "%OUT%\scripts"
if exist "%REPO%\scripts\ner_helper.py" copy /Y "%REPO%\scripts\ner_helper.py" "%OUT%\scripts\" >nul
if exist "%REPO%\scripts\contract_ner" (
  robocopy "%REPO%\scripts\contract_ner" "%OUT%\scripts\contract_ner" /E /NFL /NDL /NJH /NJS /XD __pycache__ /XF *.pyc
  if errorlevel 8 (
    echo Failed to copy scripts\contract_ner
    exit /b 1
  )
)
if exist "%REPO%\models\contract-ner" (
  echo Copying Contract NER model...
  robocopy "%REPO%\models\contract-ner" "%OUT%\models\contract-ner" /E /NFL /NDL /NJH /NJS /XD __pycache__ /XF *.pyc
  if errorlevel 8 (
    echo Failed to copy models\contract-ner
    exit /b 1
  )
)

del /q "%OUT%\*.pdb" 2>nul
del /s /q "%OUT%\*.pyc" 2>nul
for /d /r "%OUT%" %%D in (__pycache__) do if exist "%%D" rmdir /s /q "%%D"
if exist "%OUT%\user" rmdir /s /q "%OUT%\user"
if exist "%OUT%\diagnostics" rmdir /s /q "%OUT%\diagnostics"
if exist "%OUT%\feedback" rmdir /s /q "%OUT%\feedback"

echo Preparing contract-ner.json for shipment...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$p=Join-Path $env:OUT 'config\contract-ner.json';" ^
  "$cfg=Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json;" ^
  "$cfg.python_path='';" ^
  "$cfg.min_score=0.05;" ^
  "$cfg.auto_accept_score=0.30;" ^
  "$cfg.model_path='models/contract-ner/ner_model_1.1';" ^
  "$json=$cfg | ConvertTo-Json -Depth 6;" ^
  "[System.IO.File]::WriteAllText($p, $json, (New-Object System.Text.UTF8Encoding $false))"
if errorlevel 1 (
  echo Failed to prepare contract-ner.json
  exit /b 1
)

if exist "%ZIP%" del /q "%ZIP%"
echo Creating %ZIP% ...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "Add-Type -AssemblyName System.IO.Compression.FileSystem;" ^
  "[System.IO.Compression.ZipFile]::CreateFromDirectory($env:OUT, $env:ZIP, [System.IO.Compression.CompressionLevel]::Optimal, $true)"
if errorlevel 1 (
  echo ZIP FAILED. Folder left at %OUT%
  exit /b 1
)

rmdir /s /q "%OUT%"
echo.
echo SUCCESS: %ZIP%
endlocal
