# Modelli neurali (non inclusi nel repository)

I file dei modelli **non sono versionati** in questo repository, perche' le loro licenze
non sono quelle del codice (EUPL 1.2) e in alcuni casi limitano la ridistribuzione.
Vanno copiati a mano nelle cartelle indicate prima di costruire le app. Unity li importa
dalla cartella `Resources` / `Model`; i file `.meta` accanto sono gia' nel repository.

| App | File atteso | Origine | Licenza dei pesi |
|---|---|---|---|
| FaceQuest | `Assets/FaceQuest/Resources/scrfd_500m_384x288.onnx` | SCRFD 500M (`det_500m.onnx`) dal pacchetto `buffalo_sc` di InsightFace | uso di ricerca non commerciale (verificare il testo originale) |
| FaceQuest | `Assets/FaceQuest/Resources/arcface_mbf_112.onnx` | ArcFace MobileFaceNet (`w600k_mbf.onnx`) dal pacchetto `buffalo_sc` di InsightFace | uso di ricerca non commerciale (verificare il testo originale) |
| Home Inventory AR, Voice Guide | `Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model/yolov9onnx.onnx` (e `yolov9sentis.sentis`) | modello YOLOv9 fornito con i Passthrough Camera API Samples di Meta | verificare: YOLOv9 e' GPL-3.0 e i sample Meta hanno una licenza propria |

Pacchetto InsightFace: <https://github.com/deepinsight/insightface> (modelli `buffalo_sc`).
Samples Meta: repository `Unity-PassthroughCameraApiSamples` di Meta.

## Adattamenti ai modelli di FaceQuest

I due modelli di FaceQuest non sono i file originali: sono stati ripreparati con
ingresso a forma fissa (`[1,3,288,384]` per SCRFD, `[1,3,112,112]` per ArcFace) e con la
normalizzazione dei pixel incorporata all'inizio del grafo (nodi `norm_mul` e `norm_add`),
cosi' il codice C# passa direttamente i pixel. **Lo script di conversione non e' ancora
incluso nel repository**: finche' non viene aggiunto, un modello scaricato dal pacchetto
originale non e' sostituibile senza questa conversione.
