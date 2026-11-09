@echo off
rem Build DemoEtcher release zip (.NET Framework 4.8 · VS2022 또는 .NET SDK) for GitHub Releases
cd /d %~dp0
dotnet build src\DemoEtcher -c Release -p:DebugType=none -o out\DemoEtcher || exit /b 1
powershell -NoProfile -Command "Compress-Archive -Path out\DemoEtcher\* -DestinationPath out\DemoEtcher-ep05.zip -Force"
echo out\DemoEtcher-ep05.zip
