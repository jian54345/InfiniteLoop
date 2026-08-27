@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

REM ========== config ==========
set "PYTHON=C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe"

set "GAME_EXE=F:\Punishing Gray Raven\Punishing Gray Raven Game\PGR.exe"
set "KRSDK_CACHE=%APPDATA%\KR_G279\A1856"
set "ASCNET_USER=test"
set "ASCNET_PASS=test"
set "MONGOD=F:\0.MongoDB\mongod.exe"
set "MONGO_DBPATH=.runtime\mongo"
set "PROXY_ADDR=127.0.0.1:8081"
set "REG_PROXY=HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings"
REM ============================

echo.
echo [InfiniteLoop] cwd=%CD%
echo [InfiniteLoop] PYTHON=%PYTHON%
echo.

"%PYTHON%" -c "print('python-ok')"
if errorlevel 1 (
    echo [ERROR] cannot run python
    pause
    exit /b 1
)

if not exist "run_steam.py" (
    echo [ERROR] run_steam.py not found
    pause
    exit /b 1
)

if not exist "%MONGOD%" (
    echo [ERROR] mongod not found: %MONGOD%
    pause
    exit /b 1
)

if not exist "%GAME_EXE%" (
    echo [WARN] game not found, skip launch: %GAME_EXE%
    set "GAME_EXE="
)

REM ========== set system proxy ==========
echo [Proxy] setting proxy to %PROXY_ADDR% ...
reg add "%REG_PROXY%" /v ProxyEnable /t REG_DWORD /d 1 /f >nul
reg add "%REG_PROXY%" /v ProxyServer /t REG_SZ /d "%PROXY_ADDR%" /f >nul
echo [Proxy] enabled: %PROXY_ADDR%
echo.

echo [InfiniteLoop] starting run_steam.py ...
echo.

if defined GAME_EXE (
    "%PYTHON%" run_steam.py --with-mongo --mongod "%MONGOD%" --mongo-dbpath "%MONGO_DBPATH%" --launch-cmd "%GAME_EXE%"
) else (
    "%PYTHON%" run_steam.py --with-mongo --mongod "%MONGOD%" --mongo-dbpath "%MONGO_DBPATH%"
)

set "EXIT_CODE=!ERRORLEVEL!"

echo.
echo [InfiniteLoop] exit code=!EXIT_CODE!
pause
exit /b !EXIT_CODE!