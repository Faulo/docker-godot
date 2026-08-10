@echo off
setlocal
cd /d "%~dp0"
call load-env.bat

set "BUILD_EXIT_CODE=1"
set "DOCKER_OS=%~1"
set "DOCKER_CONTEXT_ARGS="

if not defined DOCKER_IMAGE (
    echo Missing DOCKER_IMAGE in .env
    goto build_done
)

if not defined DOCKER_OS (
    echo Usage: docker-build.bat ^<linux^|windows^>
    goto build_done
)

set "DOCKER_CONTEXT_ARGS=--context %DOCKER_OS%"

if not exist "%DOCKER_OS%\Dockerfile" (
    echo Missing Dockerfile: %DOCKER_OS%\Dockerfile
    goto build_done
)

docker %DOCKER_CONTEXT_ARGS% build --pull --tag tmp/%DOCKER_IMAGE%:latest --file "%DOCKER_OS%\Dockerfile" .
set "BUILD_EXIT_CODE=%ERRORLEVEL%"

:build_done
pause
endlocal & exit /b %BUILD_EXIT_CODE%
