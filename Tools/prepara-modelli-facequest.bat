@echo off
rem Scarica e prepara i modelli di FaceQuest (vedi MODELS.md).
cd /d "%~dp0.."
python -m pip install --quiet onnx onnxruntime onnxsim numpy
if errorlevel 1 goto errore
python Tools\prepara_modelli_facequest.py %*
if errorlevel 1 goto errore
echo.
echo Fatto.
pause
exit /b 0
:errore
echo.
echo ERRORE: vedi i messaggi sopra. Serve Python 3 nel PATH.
pause
exit /b 1
