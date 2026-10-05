# Modelli neurali (non inclusi nel repository)

I file dei modelli **non sono versionati** in questo repository, perche' le loro licenze
non sono quelle del codice (EUPL 1.2) e in alcuni casi limitano la ridistribuzione.
Vanno messi nelle cartelle indicate prima di costruire le app. I file `.meta` accanto
sono gia' nel repository, quindi Unity li importa con le impostazioni giuste.

| App | File atteso | Origine | Licenza dei pesi |
|---|---|---|---|
| FaceQuest | `Assets/FaceQuest/Resources/scrfd_500m_384x288.onnx` | SCRFD 500M (`det_500m.onnx`) dal pacchetto `buffalo_sc` di InsightFace | uso di ricerca non commerciale (verificare il testo originale) |
| FaceQuest | `Assets/FaceQuest/Resources/arcface_mbf_112.onnx` | ArcFace MobileFaceNet (`w600k_mbf.onnx`) dal pacchetto `buffalo_sc` di InsightFace | uso di ricerca non commerciale (verificare il testo originale) |
| Home Inventory AR, Voice Guide | `Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model/yolov9onnx.onnx` (e `yolov9sentis.sentis`) | modello YOLOv9 fornito con i Passthrough Camera API Samples di Meta | da verificare: YOLOv9 ha origine GPL-3.0 e i sample Meta hanno una licenza propria |

Pacchetto InsightFace: <https://github.com/deepinsight/insightface> (modelli `buffalo_sc`).

## FaceQuest: preparazione automatica

I due modelli di FaceQuest non sono i file originali: hanno l'ingresso a forma fissa
(`[1,3,288,384]` per SCRFD, `[1,3,112,112]` per ArcFace) e la normalizzazione dei pixel
cucita all'inizio del grafo (nodi `norm_mul` e `norm_add`), cosi' il codice C# passa
direttamente pixel RGB in [0,1]. Lo script li scarica e li prepara:

```
Tools\prepara-modelli-facequest.bat
```

oppure, a mano (serve Python 3):

```
pip install onnx onnxruntime onnxsim numpy
python Tools/prepara_modelli_facequest.py
```

Con `--zip buffalo_sc.zip` usa uno zip gia' scaricato. Lo script **verifica il risultato**
confrontando, su immagini casuali, le uscite del modello originale con quelle del modello
convertito; se non coincidono (soglia 1e-3) si ferma senza scrivere nulla.
Limite dichiarato: la verifica e' numerica sulle uscite del modello, non sostituisce una
prova di FaceQuest sul visore.

## Home Inventory AR e Voice Guide: YOLOv9

Il modello YOLOv9 (80 classi COCO) e' quello dei Passthrough Camera API Samples di Meta
(repository `Unity-PassthroughCameraApiSamples`). Copiare `yolov9onnx.onnx` e
`yolov9sentis.sentis` nella cartella indicata in tabella. Non esiste ancora uno script di
download per questo modello.
