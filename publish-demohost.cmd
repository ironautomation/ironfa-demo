@echo off
rem Build DemoHost release zip (.NET 8 SDK) for GitHub Releases
cd /d %~dp0
dotnet publish src\DemoHost -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out\DemoHost || exit /b 1
powershell -NoProfile -Command "Compress-Archive -Path out\DemoHost\* -DestinationPath out\DemoHost-ep02-win-x64.zip -Force"
echo out\DemoHost-ep02-win-x64.zip
