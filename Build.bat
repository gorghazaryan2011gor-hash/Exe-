@echo off
cd /d "%~dp0"
echo Собираю CppToExe.exe ...
echo (в первый раз нужен интернет - скачивается пакет WebView2)
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Сборка не удалась. Прочитай текст ошибки выше.
    echo Если пишет "SDK не найден" - поставь .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

echo.
echo Готово! Файл: publish\CppToExe.exe
echo Его можно скопировать куда угодно и отправить другим - он ничего не требует, кроме Windows.
explorer "publish"
pause
