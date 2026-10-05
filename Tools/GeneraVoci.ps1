# Genera le clip audio italiane per Guida Vocale usando la sintesi vocale di Windows.
#
# Perche sul PC e non sul visore: Horizon OS non ha nessun motore di sintesi
# installato (verificato: TextToSpeech.getEngines() restituisce zero motori).
# Il vocabolario dell'app e chiuso, quindi generare le clip una volta e piu
# semplice e piu prevedibile che dipendere dal software del visore.
#
# Uso, dalla cartella del progetto:
#   powershell -ExecutionPolicy Bypass -File Tools\GeneraVoci.ps1
#
# Prima serve l'elenco: in Unity, menu Guida Vocale > Esporta le frasi da generare.

$ErrorActionPreference = 'Stop'

$radice = Split-Path -Parent $PSScriptRoot
$elenco = Join-Path $radice 'Tools\frasi.txt'
$uscita = Join-Path $radice 'Assets\Resources\Voci'

if (-not (Test-Path $elenco)) {
    Write-Host "Non trovo $elenco." -ForegroundColor Red
    Write-Host "In Unity: menu 'Guida Vocale' > 'Esporta le frasi da generare'."
    exit 1
}

New-Item -ItemType Directory -Force -Path $uscita | Out-Null

Add-Type -AssemblyName System.Speech
$sintesi = New-Object System.Speech.Synthesis.SpeechSynthesizer

# Voce italiana se c'e, altrimenti la prima disponibile: meglio una pronuncia
# imperfetta che nessun audio, e l'avviso dice cosa manca.
$voci = $sintesi.GetInstalledVoices() | Where-Object { $_.Enabled }
$italiana = $voci | Where-Object { $_.VoiceInfo.Culture.Name -like 'it*' } | Select-Object -First 1

if ($italiana) {
    $sintesi.SelectVoice($italiana.VoiceInfo.Name)
    Write-Host "Voce: $($italiana.VoiceInfo.Name) ($($italiana.VoiceInfo.Culture.Name))" -ForegroundColor Green
} else {
    Write-Host "ATTENZIONE: nessuna voce italiana installata in Windows." -ForegroundColor Yellow
    Write-Host "Le clip usciranno con accento straniero. Per installarla:" -ForegroundColor Yellow
    Write-Host "  Impostazioni > Ora e lingua > Lingua > Aggiungi lingua > Italiano," -ForegroundColor Yellow
    Write-Host "  spuntando 'Sintesi vocale' fra le funzionalita opzionali." -ForegroundColor Yellow
    Write-Host "Voce usata: $($voci[0].VoiceInfo.Name)"
}

$sintesi.Rate = 1     # leggermente piu veloce del normale: gli annunci sono brevi

$fatte = 0
$saltate = 0

foreach ($riga in Get-Content -Path $elenco -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($riga)) { continue }

    $pezzi = $riga.Split('|', 2)
    if ($pezzi.Count -ne 2) { continue }

    $chiave = $pezzi[0].Trim()
    $testo  = $pezzi[1].Trim()
    $file   = Join-Path $uscita "$chiave.wav"

    if (Test-Path $file) { $saltate++; continue }

    $sintesi.SetOutputToWaveFile($file)
    $sintesi.Speak($testo)
    $sintesi.SetOutputToNull()

    $fatte++
    Write-Host "  $chiave  <- `"$testo`""
}

$sintesi.Dispose()

Write-Host ""
Write-Host "Generate $fatte clip, $saltate gia presenti." -ForegroundColor Green
Write-Host "Cartella: $uscita"
Write-Host "Torna in Unity, attendi l'importazione, poi rifai il build della Guida Vocale."
