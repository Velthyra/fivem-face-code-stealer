@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET SDK bulunamadi. .NET 9 SDK kurup tekrar deneyin.
  exit /b 1
)

if not exist "Payload\FaceStReader.dll" exit /b 2
if not exist "Payload\FaceCodeInjector.exe" exit /b 2
if not exist "Payload\FaceSt.Core.dll" exit /b 2

dotnet publish "FaceSt.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o "dist"
if errorlevel 1 exit /b 1

echo Hazir: dist\FaceSt.exe
exit /b 0
