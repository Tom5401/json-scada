echo Builds the JSON-SCADA runtime pack (json-scada-runtimes.7z).
echo This only needs to be re-run when third-party runtimes are updated.
echo Required tools:
echo - 7-Zip installed at C:\Program Files\7-Zip\7z.exe

set JSPATH=C:\jsbuild
set SEVENZIP="C:\Program Files\7-Zip\7z.exe"
set OUTFILE=%JSPATH%\platform-windows\installer-release\json-scada-runtimes.7z

mkdir %JSPATH%\platform-windows\installer-release

echo Removing old runtime pack...
del /f /q %OUTFILE% 2>nul

echo Packaging runtime directories...
%SEVENZIP% a -t7z -mx=5 -mmt=on %OUTFILE% ^
  %JSPATH%\platform-windows\nodejs-runtime ^
  %JSPATH%\platform-windows\jdk-runtime ^
  %JSPATH%\platform-windows\metabase-runtime ^
  %JSPATH%\platform-windows\grafana-runtime ^
  %JSPATH%\platform-windows\mongodb-runtime ^
  %JSPATH%\platform-windows\mongodb-compass-runtime ^
  %JSPATH%\platform-windows\mongodb-conf ^
  %JSPATH%\platform-windows\postgresql-runtime ^
  %JSPATH%\platform-windows\nginx_php-runtime ^
  %JSPATH%\platform-windows\browser-runtime ^
  %JSPATH%\platform-windows\browser-data ^
  %JSPATH%\platform-windows\ua-edge-translator-runtime ^
  %JSPATH%\platform-windows\inkscape-runtime

echo Done. Runtime pack written to: %OUTFILE%
echo.
echo To install: extract %OUTFILE% to C:\json-scada\ using 7-Zip before running the NSIS installer.
