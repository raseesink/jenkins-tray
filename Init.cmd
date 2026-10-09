@echo off
dotnet restore src\JenkinsTray.Windows\JenkinsTray.Windows.csproj --locked-mode
exit /b %errorlevel%
