# HomeInventoryAR

Tre app sperimentali per **Meta Quest 3** in un unico progetto Unity, tutte basate
sulla passthrough camera e sull'elaborazione **locale** sul visore.

| App | Cosa fa | Scena | Menu Unity |
|---|---|---|---|
| **Inventario AR** | riconosce oggetti in casa e ne tiene un inventario che ricorda dove sono | `Fase2_Inventario` | `Inventario AR` |
| **Guida Vocale** | accessibilita: rileva ostacoli con la profondita e guida con voce e vibrazione | `GuidaVocale` | `Guida Vocale` |
| **FaceQuest** | rileva e ri-identifica volti in tempo reale (persone consenzienti, ambienti controllati) | `FaceQuest` | `FaceQuest` |

Documentazione di FaceQuest, con il racconto dei problemi incontrati e le prove
fatte: [`Assets/FaceQuest/README.md`](Assets/FaceQuest/README.md).

## Requisiti

* Unity **6000.0.80f1** (URP, IL2CPP, Android)
* `com.meta.xr.mrutilitykit` 85.0.0 (fornisce `PassthroughCameraAccess`)
* `com.unity.ai.inference` 2.2.1 (l'ex Sentis)
* Meta Quest 3 / 3S con Horizon OS v74 o successivo
* Il permesso `horizonos.permission.HEADSET_CAMERA` e gia nel manifest

## Come si costruisce

Ogni app ha nel menu di Unity voci che **creano la scena da codice** e, se si vuole,
fanno anche la build e l'installazione sul visore collegato. La scena non va
modificata a mano: si rigenera.

* FaceQuest: `FaceQuest > 2 - Crea la scena e installa sul Quest`
* Inventario AR: `Inventario AR > Crea scena Fase 2 e installa sul Quest`
* Guida Vocale: `Guida Vocale > Crea scena Guida Vocale e installa sul Quest`

Le tre app usano identificativi diversi, cosi installarne una non sostituisce le
altre. I file `.apk` non sono nel repository (`.gitignore`): si generano con la build.

## Privacy

FaceQuest tratta dati biometrici. L'elaborazione avviene **solo sul visore**: nessuna
immagine, nessun embedding e nessun identificativo lascia il dispositivo, e il codice
non contiene richieste di rete. Le persone battezzate si salvano cifrate (AES-256 con
HMAC) nello storage privato dell'app; le identita anonime non sopravvivono alla
sessione. **Va usata solo con persone informate e consenzienti.** Il repository non
contiene dati biometrici e `.gitignore` ne blocca la copia accidentale.

## Licenze e componenti di terzi

* **Meta Passthrough Camera API Samples** (`Assets/PassthroughCameraApiSamples`):
  Oculus SDK License Agreement, vedi `LICENSE.txt` nella cartella. Il progetto ne
  riusa il gestore della camera.
* **Modelli di FaceQuest** (`Assets/FaceQuest/Resources/*.onnx`): SCRFD 500M e
  ArcFace MobileFaceNet dal pacchetto `buffalo_sc` di
  [InsightFace](https://github.com/deepinsight/insightface), ripreparati (forma di
  ingresso fissa, normalizzazione dentro il grafo). I pesi preaddestrati di InsightFace
  sono rilasciati per **uso di ricerca non commerciale**: verificare la licenza
  prima di qualunque uso diverso o di una ridistribuzione pubblica.
* **Codice originale di questo progetto**: Copyright (c) 2026 Claudio Gennaro, ISTI-CNR.
  Licensed under the EUPL (European Union Public Licence v. 1.2), testo completo nel
  file [`LICENSE`](LICENSE). La licenza copre solo il codice originale; i componenti
  elencati sopra restano soggetti alle loro licenze, che non sono modificate da questa.
