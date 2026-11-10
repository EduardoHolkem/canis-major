@echo off
rem Abre o editor do Canis Major com o Python do ambiente desta pasta.
if not exist "%~dp0.venv\Scripts\pythonw.exe" (
    echo O ambiente .venv nao foi encontrado. Crie com:
    echo   python -m venv .venv
    echo   .venv\Scripts\python -m pip install -e .
    pause
    exit /b 1
)
start "" "%~dp0.venv\Scripts\pythonw.exe" -m canismajor %*
