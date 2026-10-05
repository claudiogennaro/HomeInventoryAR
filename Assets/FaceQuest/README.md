# FaceQuest — riconoscimento e re-identificazione di volti in AR su Meta Quest 3

Prototipo sperimentale per ambienti controllati e persone consenzienti.
Tutta l'elaborazione biometrica avviene **sul visore**: nessuna immagine, nessun
embedding e nessun identificativo biometrico lascia il dispositivo.

Vive dentro il progetto esistente `HomeInventoryAR` come scena a sé
(`Assets/Scenes/FaceQuest.unity`), riusando il camera rig, il passthrough e i
pacchetti già verificati sul tuo visore.

---

## 1. Stato di verifica

| Cosa | Come è stato verificato |
|---|---|
| Modelli ONNX | eseguiti su volti reali, numeri riportati sotto |
| Decodifica SCRFD | confrontata con l'implementazione di riferimento InsightFace: 6 volti su 6 in una foto di gruppo, landmark corretti |
| Allineamento (forma chiusa) | coincide con l'Umeyama SVD a 1e-14 su casi casuali |
| Compilazione C# | tutti i 19 file compilati con Roslyn (C# 9) contro stub delle API di Unity 6, Inference Engine e Meta XR |
| Esecuzione sul Quest 3 | **verificata**: rilevamento, creazione di identità, assegnazione del nome e re-identificazione nella stessa sessione, ~11-12 Hz di pipeline |

Verificato sul visore, in una stanza di casa con luce scarsa: le box seguono i
volti, le identità nascono con il consenso di tre fotogrammi, il nome assegnato
compare subito sulla box e nel pannello.

**Etichette:** un volto non ancora battezzato si chiama `Ospite 1`, `Ospite 2`…
La chiave interna resta `AA001` (nome del file del ritaglio, log, aggancio fra
traccia e identità), ma non si vede: sono lo stesso contatore, quindi `AA003` e
`Ospite 3` sono la stessa persona.

**Persistenza: attiva.** Le persone a cui hai assegnato un nome sopravvivono
alla chiusura, cifrate; le identità anonime `AA…` no. Un'identità che resta
senza nome per più di **10 minuti** sparisce da sola dal pannello. Sezione 8.

**Non ancora provato sul visore:** la cancellazione di una singola persona
(DELETE nella finestra del nome) e di tutto il database (`Y` + conferma), e il
comportamento con due o più persone insieme — che è il caso in cui le soglie
contano davvero.

---

## 2. Requisiti (verificati sul tuo progetto)

* Unity **6000.0.80f1** — già in uso
* `com.meta.xr.mrutilitykit` **85.0.0** — fornisce `PassthroughCameraAccess`
* `com.unity.ai.inference` **2.2.1** (l'ex Sentis, ora Unity Inference Engine)
* Sample `Unity-PassthroughCameraApiSamples` già copiato in
  `Assets/PassthroughCameraApiSamples` — la scena di FaceQuest nasce da lì
* `AndroidManifest.xml` già contiene `horizonos.permission.HEADSET_CAMERA`:
  **non serve toccare nulla** sul lato Android
* Quest 3 / 3S con Horizon OS ≥ v74 (la Passthrough Camera API non esiste prima)

Nessun pacchetto nuovo da installare.

---

## 3. I modelli

Entrambi arrivano da **InsightFace**, pacchetto `buffalo_sc`
(<https://github.com/deepinsight/insightface>), e li ho **ripreparati** per
questo progetto:

### `Assets/FaceQuest/Resources/scrfd_500m_384x288.onnx` (2,5 MB)

SCRFD 500M, il detector. Modifiche fatte:

* ingresso fissato a **384×288** invece che dinamico. 384×288 è **4:3 esatto**
  come tutte le risoluzioni della camera passthrough (320×240 … 1280×960),
  quindi il ridimensionamento non schiaccia i volti; ed entrambi i lati sono
  multipli di 32, come la rete richiede. Costo quasi identico a 320×320
  (110k pixel contro 102k) ma senza deformazione;
* normalizzazione `(x·255 − 127,5)/128` **cucita dentro il grafo ONNX**: in
  ingresso va l'immagine in 0..1 e non c'è un passaggio di preprocessing da
  sbagliare;
* grafo semplificato e shape inference completa, così l'Inference Engine non
  incontra dimensioni dinamiche. Operatori residui: Conv, Add, Relu, Sigmoid,
  Reshape, Resize, Transpose — tutti supportati.

Uscite: 9 tensori (punteggi, box, landmark per gli stride 8/16/32). I punteggi
sono già passati per una sigmoid; i box sono **distanze dal centro dell'ancora
in unità di stride**, non coordinate.

### `Assets/FaceQuest/Resources/arcface_mbf_112.onnx` (13,6 MB)

MobileFaceNet addestrato con **ArcFace loss** su WebFace600K (`w600k_mbf`),
embedding a 512 dimensioni, ingresso 112×112, normalizzazione
`(x·255 − 127,5)/127,5` cucita nel grafo.

**Perché questo e non ArcFace-R50, AdaFace o MagFace.** Ho misurato la
separazione su volti reali ripresi a scala e luminosità diverse:

```
stessa persona   : coseno 0,82 … 0,94   (media 0,91)
persone diverse  : coseno max 0,22      (media 0,06)
```

Un margine di 0,6 fra le due distribuzioni rende inutile spendere dieci volte
tanto in calcolo per un modello più grande. AdaFace e MagFace hanno un vantaggio
reale sui volti di bassa qualità, ma nessuno dei due ha un ONNX mobile
mantenuto e verificabile con questa facilità; se un giorno servisse, il codice è
costruito per sostituire il modello cambiando un riferimento
(`FaceEmbeddingExtractor`), perché l'embedder non sa nulla del resto.

I due file stanno in una cartella **`Resources`**, e i componenti li caricano
per nome (`Resources.Load<ModelAsset>`) quando il riferimento serializzato è
vuoto. Non è ridondanza gratuita: nella prima build funzionante il riferimento
assegnato via `SerializedObject` è risultato **nullo sul visore** — nella scena
salvata si leggeva `m_modello: {fileID: 0}` mentre le soglie accanto erano
scritte correttamente, e l'app diceva `det KO: modello mancante`. Ora ci sono
due strade indipendenti: il riferimento in scena (assegnato direttamente sul
campo, e verificato subito dopo) e il caricamento per nome da `Resources`.

Le due reti girano con backend **`CPU`**, che è la configurazione che il sample
di Meta usa sul Quest (nel suo prefab `m_backend: 512`, cioè `CPU`). La prima
versione usava `GPUCompute` (`256`) e l'app si chiudeva da sola: il backend è il
primo posto da guardare quando l'inferenza uccide l'app invece di sbagliare. Con
`CPU` l'inferenza usa Burst e la pipeline sta comunque nella fascia 5–15 Hz per
cui è pensata. Il campo `Backend` nell'Inspector permette di riprovare
`GPUCompute` quando tutto il resto è stabile.

All'avvio entrambe le reti fanno un passaggio a vuoto (**riscaldamento**): il
primo passaggio costa molto più dei successivi, e farlo al primo volto
inquadrato bloccherebbe il main thread proprio nel momento peggiore. Ogni stadio
dell'avvio scrive una riga in `adb logcat`, così se l'app si chiude l'ultima riga
dice a quale stadio è morta:

```
[FaceQuest] Camera pronta: 1280x960.
[FaceQuest] Texture pronte: frame 1280x960, detector 384x288, volto 112.
[FaceQuest] Riscaldamento del detector...
[FaceQuest] Detector riscaldato (384x288 CPU).
[FaceQuest] Embedder riscaldato (esito True).
[FaceQuest] Pipeline avviata.
```

---

## 4. Come si costruisce e si installa

### Il guasto che è costato quattro giorni

Vale la pena raccontarlo, perché il sintomo non somigliava in nulla alla causa.
Per giorni l'app si è chiusa all'avvio con:

```
The file '.../assets/bin/Data/level0' is corrupted!
[Position out of bounds!]        thread: Loading.Preload
```

Accusava la scena costruita di essere corrotta. Non lo era: ho aperto l'APK e
verificato il file byte per byte — level0 si legge fino all'ultimo byte dei
metadati, 197 oggetti tutti dentro i limiti, `sharedassets0.assets` coerente nei
suoi 27 MB divisi in 26 pezzi tutti presenti, nessun riferimento pendente né
nella scena sorgente né in quella costruita. Il messaggio era un falso allarme.

Nel frattempo ho corretto tre cose che **non erano la causa**: il backend di
inferenza (da `GPUCompute` a `CPU`), un `GL.Clear` chiamato fuori dal rendering,
e la cache di build. Migliorie legittime, diagnosi sbagliate.

La causa l'ha trovata un esperimento, non un'ipotesi: il comando 4 costruisce la
scena del sample **senza un solo oggetto di FaceQuest**. Quella partiva. Quindi
il problema era in un mio componente, e a quel punto bastava guardare la scena:

```
m_Script: {fileID: 920716856}        ← nessun guid
--- !u!115 &920716856
MonoScript:
  m_Name:                             ← un MonoScript vuoto, dentro la scena
```

Tre MonoBehaviour stavano in un unico file, `PuntatoreAR.cs`: `BersaglioAR`,
`PuntatoreAR` e `SeguiTesta`. C# lo permette e l'editor non protesta. Ma **Unity
crea un asset MonoScript solo per la classe che porta il nome del file**; per le
altre scrive nella scena un MonoScript finto e senza nome, e un riferimento di
script privo di guid. L'editor lo risolve, il player no — e leggendo quel
componente il puntatore va oltre i dati, da cui "Position out of bounds", da cui
l'accusa alla scena.

Due componenti `SeguiTesta` su due GameObject che seguono la testa hanno fatto
sembrare rotta l'intera build.

**Regola, senza eccezioni: un MonoBehaviour, un file con il suo nome.** Ora il
comando 1 la verifica da sé: dopo aver salvato la scena cerca riferimenti di
script senza guid e MonoScript scritti dentro il file, e se ne trova si ferma con
un messaggio che spiega cosa fare, invece di produrre una build che si chiude sul
visore senza dire perché.

I comandi di build usano anche `CleanBuildCache` (ricostruiscono i dati del
player da zero) e il comando 1 fa un `AssetDatabase.Refresh(ForceUpdate)`: non
erano la causa, ma restano perché costano poco e togliono di mezzo una classe di
disallineamenti.

In Unity, menu **FaceQuest**:

1. **`1 · Crea la scena`** — apre la scena del sample, spegne il rilevamento
   oggetti (componente disabilitato = nessun `Awake` = YOLO non viene nemmeno
   caricato), costruisce l'albero di FaceQuest con i riferimenti già collegati e
   salva in `Assets/Scenes/FaceQuest.unity`.
2. **`2 · Crea la scena e installa sul Quest`** — come sopra, poi build
   `Builds/FaceQuest.apk` e avvio automatico sul visore.
3. **`3 · … AZZERA l'archivio e installa`** — come 2, ma la build cancella
   l'archivio cifrato al primo avvio. Da usare quando i template accumulati
   sporcano le prove.
4. **`4 · PROVA: installa la sola scena del sample`** — build della scena di
   Meta senza nessun oggetto di FaceQuest, con l'identità di FaceQuest. Divide in
   due un problema che l'analisi statica non risolve: se questa parte, la causa è
   in un mio componente; se si chiude, è nel progetto o nella build. È il comando
   che ha trovato il guasto raccontato sopra.

FaceQuest si installa come **applicazione separata** dall'inventario: i comandi
di build cambiano nome (`FaceQuest`) e identificativo del pacchetto
(`it.cnr.isti.facequest`) solo per la durata della build, e poi li rimettono
come erano. Con lo stesso identificativo, installare FaceQuest *sostituiva*
l'app inventario sul visore — stesso pacchetto, stessa icona, stessi dati. Se ti
serve di nuovo l'inventario, rilancia il suo menu: ora le due app convivono.

Il comando 1 assegna anche gli shader ai campi serializzati dei componenti (e
per sicurezza li aggiunge agli **Always Included Shaders**). Uno shader cercato
solo con `Shader.Find` può non finire nella build su Android: funzionerebbe
nell'editor e sparirebbe sul visore, con l'unico sintomo di ritagli neri e box
invisibili.

---

## 5. Comandi nel visore

| Azione | Comando |
|---|---|
| Riconoscimento ON / OFF | **A** |
| Cancellare il database | **Y** (apre la conferma, non cancella subito) |
| Uscire dall'app | **B tenuto** un secondo |
| Scorrere la lista delle persone | **stick su / giù** |
| Dare un nome a una persona | punta la sua riga col controller destro e premi il **grilletto** |

Nel pannello **non ci sono bottoni**: puntare un bottone sospeso in aria è più
lento e meno preciso che premere un tasto, e il pannello sta di lato, spesso
fuori dal campo visivo. Il puntatore resta solo dove non c'è alternativa:
scegliere una persona nella lista e scrivere il suo nome sulla tastiera AR.

`B` va **tenuto** e non premuto: è sotto il pollice destro, e una pressione
involontaria chiuderebbe l'app nel mezzo di una prova.

Il puntatore parte dall'ancora della mano destra: sul tuo visore risulta attivo
il solo controller destro (`ctrl:RTouch`), per questo ogni comando accetta anche
l'equivalente sull'altro controller e, dove ha senso, il pizzico a mani nude.

---

## 6. La pipeline

```
Passthrough Camera (PassthroughCameraAccess.GetTexture)
   └─ copia del frame  (una sola, riusata da tutti gli stadi)
      ├─ ridimensionamento GPU 384×288 ─→ SCRFD ─→ box + 5 landmark
      │                                      └─→ FaceTracker (IoU, smoothing)
      │                                              │
      │                                              ├─→ BoundingBoxRenderer  (box AR + etichetta)
      │                                              │
      │                                              └─ per le tracce che lo chiedono:
      └─ allineamento affine GPU 112×112 ────────────────→ ArcFace ─→ embedding L2
                                                            ├─→ FaceQualityEstimator
                                                            └─→ IdentityMatcher ─→ PersonDatabase
                                                                                      └─→ pannello People
```

Tre principi, che spiegano quasi ogni scelta nel codice:

**Il main thread non si blocca mai.** Ogni stadio pesante è una coroutine che
attende la lettura asincrona dalla GPU. La pipeline AI gira ai suoi 5–15 Hz
mentre il visore continua a disegnare a 72/90. Se vedi scattare il passthrough,
qualcosa è diventato sincrono.

**Il frame si copia una volta.** La texture della camera cambia sotto i piedi:
se il detector guardasse un fotogramma e l'allineamento un altro, i landmark
finirebbero fuori posto in modo intermittente — il guasto più costoso da
cercare. Anche la **posa** usata per piazzare le box è quella catturata col
fotogramma, non quella attuale: nel frattempo la testa si è mossa.

**Gli embedding si calcolano con parsimonia.** Non uno per volto per frame: solo
quando il tracker dice che serve (traccia nuova, identità incerta, posa cambiata
molto, o riconferma dopo 2,5 s), al massimo due per ciclo.

### La convenzione sull'asse verticale

È l'unica insidia geometrica del progetto. I rilevamenti sono normalizzati con
l'origine **in alto** a sinistra (come le righe del tensore); il viewport della
camera ha l'origine **in basso**. Tutti i passaggi fra i due mondi passano da
`FaceQuestConfig`, e c'è una sola casella da cambiare (`Tensore dall'alto` sul
`FaceQuestManager`) se una versione futura dell'Inference Engine cambiasse
comportamento. Il sintomo sarebbe inconfondibile: box specchiate in verticale e
nessun riconoscimento, perché i ritagli dati ad ArcFace sarebbero capovolti.

---

## 7. Identità, soglie, qualità

### Tre soglie, non una (`IdentityMatcher`)

```
somiglianza > 0,50  e  distacco dal secondo candidato > 0,06  →  RICONOSCIUTA
0,32 < somiglianza ≤ 0,50                                     →  INCERTA  → "Unknown", si raccolgono altri frame
somiglianza ≤ 0,32                                            →  NUOVA IDENTITÀ
```

Con una soglia sola si è costretti a scegliere fra riconoscere in fretta e non
sbagliare mai: il nearest-neighbour forzato assegna *sempre* un nome, e con
volti visti male finisce per attaccare il nome di Marco a Laura.

Il **margine** conta quanto la soglia: se primo e secondo candidato sono quasi
pari, la somiglianza alta non basta — vuol dire che l'informazione non
distingue le due persone.

Il confronto non usa solo il template aggregato ma **anche tutti gli embedding
in memoria**, prendendo il massimo: un volto visto di tre quarti somiglia molto
al ricordo di tre quarti e poco alla media di tutti i ricordi.

### Come nasce una nuova identità

Mai da un fotogramma solo. Servono **tre verdetti "non la conosco" di seguito**
sulla stessa traccia, su volti di qualità alta (punteggio ≥ 0,62 e detector
≥ 0,60), e i tre embedding devono somigliarsi **fra loro** (coseno ≥ 0,45).

Il perché si vedeva nel pannello: identità con una sola osservazione, nate e mai
più riviste, una delle quali aveva per ritratto uno scorcio di mare. Un falso
positivo produce un embedding casuale, e un embedding casuale non somiglia al
successivo: il consenso non arriva e l'identità non nasce.

E soprattutto: **nel ramo "incerta" non si crea più nulla.** La prima versione,
dopo quattro fotogrammi ambigui, creava una nuova identità. Era una fabbrica di
doppioni: "incerta" significa somiglianza fra 0,32 e 0,50, cioè *probabilmente
una persona che conosciamo, vista male* — e la stessa persona in penombra
compariva nel pannello come `AA002` accanto al suo nome vero. Un fotogramma
ambiguo non è una persona nuova: è un fotogramma ambiguo. Si aspetta che la
somiglianza salga sopra la soglia alta o scenda sotto quella bassa.

### Manutenzione del database

Il matching decide su un fotogramma alla volta e non può tornare indietro. Ogni
quattro secondi il database si guarda quindi allo specchio, dove un doppione è
evidente:

* **fusione** — due identità che si somigliano oltre 0,55 (massimo fra tutti i
  loro ricordi) sono la stessa persona: la più vecchia assorbe l'altra, con
  storico, conteggio e la migliore delle due anteprime. Due identità che hai
  battezzato con nomi diversi non si toccano mai: quella è una tua decisione,
  non un'ipotesi del sistema;
* **potatura** — un'identità anonima con al massimo 2 osservazioni e 25 secondi
  di silenzio viene scartata. È la stessa regola che ha ripulito l'inventario
  AR: ciò che è stato confermato debolmente e non cresce più, non era lì
  davvero. Un volto vero accumula osservazioni in fretta.

### Memoria per persona (`PersonDatabase`)

Fino a 12 embedding per identità, più un template aggregato (media pesata sulla
qualità, normalizzata L2; in alternativa il **medoide**, commutabile
nell'Inspector).

Quando lo storico è pieno non si butta il più vecchio ma il più **ridondante**:
si cerca la coppia di ricordi più simili fra loro e si scarta quello dei due con
qualità minore. Buttare il più vecchio riempirebbe lo storico di dodici
fotogrammi quasi identici degli ultimi due secondi — un solo embedding pagato
dodici volte.

### Qualità (`FaceQualityEstimator`)

Dimensione del volto, nitidezza (varianza del laplaciano sulla zona centrale del
ritaglio, non sul bordo dove capelli e sfondo sono nitidi anche se il volto è
mosso), luminosità, yaw/pitch/roll dalla geometria dei cinque landmark. Soglie
nell'Inspector.

Un frame scadente **conta come presenza ma non entra nel template**. Un
embedding di bassa qualità sposta il rappresentante verso il centro dello spazio,
dove somiglia un po' a tutti: dopo qualche osservazione cattiva due persone
iniziano a scambiarsi. Per lo stesso motivo una **identità nuova si crea solo da
un frame buono** — altrimenti il pannello si riempie di doppioni che non si
riuniranno mai.

L'occlusione non è misurata direttamente (servirebbe una seconda rete): la si
intercetta di riflesso, perché un volto coperto perde punteggio nel detector e
simmetria nei landmark.

### Distanza del volto senza mappa di profondità

La box ha bisogno di una posizione nella stanza, non solo nell'immagine. Invece
di interrogare la mappa di profondità — rumorosa e in ritardo su un volto in
movimento — si usa una grandezza che il volto porta con sé: la **distanza fra le
pupille**, ~63 mm in un adulto. Dall'angolo fra i due raggi che passano per gli
occhi si ricava la distanza con un errore di pochi centimetri, senza dipendere
dalla scena. Il valore è regolabile (`Distanza pupille`).

---

## 8. Persistenza e privacy

### Cosa sopravvive e cosa no

`PersonPersistenceManager` ha il flag `Solo in memoria` **spento**: la
persistenza cifrata è attiva. Il criterio è uno solo, ed è il nome.

* **Persona battezzata** → sopravvive: template, storico selezionato, ritaglio
  rappresentativo, metadati. Il salvataggio avviene **subito, nel momento in cui
  batti il nome**, non alla chiusura: è l'unico istante in cui dichiari che
  quella persona conta, e una chiusura brusca (batteria, visore tolto, crash)
  non deve buttare via proprio quella decisione. Si risalva anche su
  `OnApplicationPause` e `OnApplicationQuit`.
* **Identità anonima (`Ospite 1`, `Ospite 2`…)** → non sopravvive, e non
  aspetta nemmeno la chiusura: **dopo 10 minuti senza nome viene rimossa dal
  pannello**
  (`Vita anonime` = 600 s su `FaceQuestManager`; 0 disattiva la scadenza). Un
  volto che è in giro da dieci minuti e non ha ancora un nome non lo avrà mai, e
  intanto occupa una riga. Con una cautela: un'anonima vista negli ultimi
  `Grazia anonime` secondi (3 di default) non viene tolta, perché se sparisse
  mentre la persona è ancora inquadrata rinascerebbe subito con un altro
  AA-numero — peggio del problema che stiamo risolvendo. Sparisce appena esce
  dal campo visivo.

Le battezzate non si toccano mai, qualunque sia la loro età: il nome è una
decisione tua e vale più di qualsiasi timer.

Per tornare alla demo senza scritture su disco basta spuntare `Solo in memoria`.

### Il file su disco

**Cosa finisce nel file**: solo le persone a cui hai assegnato un nome.
Le identità `Ospite 1`, `Ospite 2`… sono un meccanismo interno di conteggio, non
una tua decisione: tenerle sul disco significherebbe accumulare dati biometrici di
chiunque sia passato davanti al visore. Alla chiusura vengono cancellate.

Per le persone nominate si salvano `PersonID`, nome, template, storico
selezionato, ritaglio rappresentativo e i metadati essenziali — tutto in
`persistentDataPath/facequest/`, **cifrato** AES-256-CBC con HMAC-SHA256 su
IV + testo cifrato. Senza l'HMAC un file corrotto o manomesso si decifrerebbe in
numeri casuali che il matcher prenderebbe per embedding validi.

**Limite da dichiarare, non da nascondere**: la chiave è generata a caso al primo
avvio e sta nello storage privato dell'app, accanto ai dati. Protegge da chi
copia i file (per esempio `adb pull` su un visore sbloccato), **non** da chi ha
accesso di root al visore. Per una protezione vera la chiave andrebbe nel
Keystore Android con attestazione hardware: richiede codice nativo e non è
coperta da questo prototipo. Se il prototipo diventasse uno studio con
partecipanti, è la prima cosa da aggiungere.

Altre garanzie implementate: nessuna rete (nessun `UnityWebRequest` in tutto
FaceQuest), stato del riconoscimento sempre visibile sul pannello, interruttore
ON/OFF a portata di pollice, cancellazione di **una** persona (dalla sua finestra)
e di **tutto** il database chiave compresa, entrambe con conferma esplicita
perché sono irreversibili per costruzione.

---

## 9. I file

```
Assets/FaceQuest/
├── Resources/                        (cartella Resources: finisce sempre nella build)
│   ├── scrfd_500m_384x288.onnx        detector preparato per questo progetto
│   └── arcface_mbf_112.onnx           embedder preparato per questo progetto
├── Shaders/
│   └── FaceAffineBlit.shader          ritaglio affine sulla GPU (allineamento + resize)
├── Scripts/
│   ├── FaceQuestTypes.cs              VoltoRilevato, Persona, QualitaVolto, EsitoIdentita
│   ├── FaceQuestConfig.cs             l'unico interruttore per l'asse verticale
│   ├── FaceDetector.cs                SCRFD: inferenza, decodifica delle ancore, NMS
│   ├── FaceAligner.cs                 similarità ai minimi quadrati sui 5 punti + BlitAffine
│   ├── FaceEmbeddingExtractor.cs      ArcFace/MobileFaceNet, normalizzazione L2, coseno
│   ├── FaceQualityEstimator.cs        dimensione, nitidezza, luce, posa
│   ├── FaceTracker.cs                 tracce su IoU, smoothing, *quando* rifare l'embedding
│   ├── IdentityMatcher.cs             ricerca e decisione a tre soglie + margine
│   ├── PersonDatabase.cs              identità, ID progressivi, storico, template
│   ├── PersonPersistenceManager.cs    formato binario + salvataggio/caricamento cifrato
│   ├── Cifratura.cs                   AES-256-CBC + HMAC, gestione della chiave
│   ├── ArchivioVolti.cs               ritagli dei volti su disco, cifrati
│   ├── BoundingBoxRenderer.cs         box AR, etichetta, profondità, stabilizzazione
│   ├── PeoplePanelController.cs       il pannello "People"
│   ├── PersonEditorUI.cs              finestra del nome, tastiera AR, conferme
│   ├── PuntatoreAR.cs                 puntatore, bersagli premibili, comandi, SeguiTesta
│   ├── UIFacile.cs                    costruzione della UI in world space da codice
│   └── FaceQuestManager.cs            l'orchestratore
└── Editor/
    └── FaceQuestSceneBuilder.cs       costruisce la scena, include gli shader, builda
```

Tutta la UI nasce **da codice**, non da prefab: un pannello con dodici
riferimenti collegati a mano nell'Inspector è impossibile da rigenerare identico
dopo una modifica, e i riferimenti persi sono il guasto più silenzioso di Unity.
Lo stesso vale per la scena: `FaceQuestSceneBuilder` è insieme il comando che la
crea e la documentazione di com'è fatta.

I moduli sono sostituibili uno per uno: il detector non sa nulla
dell'embedder, l'embedder non sa nulla del database, il database non sa nulla
della UI.

---

## 10. Diagnostica e guasti probabili

In fondo al pannello People ci sono tre strumenti, e sono la prima cosa da
guardare quando qualcosa non va.

**Due righe di stato.** Si leggono da sinistra: il primo elemento `KO` è la
causa.

```
cam 1280x960 · det 384x288 GPUCompute · emb ok · shader ok
8.4 Hz · volti 1 (max 0.78) · tracce 1 · non piazzati 0 · emb 12 · persone 1
```

`max` è il punteggio più alto uscito dal detector *prima* della soglia, ed è il
numero più informativo di tutti:

| `max` | significato |
|---|---|
| resta 0,00 | il problema è **a monte** della rete: immagine nera, shader mancante, uscite non riconosciute |
| fra 0,20 e la soglia | il modello vede qualcosa, la **soglia è troppo alta** |
| sopra la soglia ma `volti 0` | i volti vengono scartati perché troppo piccoli (`Altezza minima`) |
| `volti > 0` ma niente box | è il **piazzamento nel mondo** a fallire: guarda `non piazzati` |

**Due anteprime live.** A sinistra ciò che il detector riceve davvero
(384×288), a destra l'ultimo volto allineato dato ad ArcFace (112×112). Sono le
RenderTexture vere, non una copia. Rispondono in un'occhiata alla domanda che
nessun numero risolve: se l'immagine in ingresso è nera, storta o capovolta, il
problema è prima della rete e non nelle soglie. Se il volto allineato non è un
volto centrato e diritto, l'allineamento sta sbagliando e nessuna soglia
salverà il riconoscimento.

Le stesse righe finiscono in `adb logcat` ogni tre secondi, per quando il
pannello è fuori dal campo visivo.

| Sintomo | Causa quasi certa |
|---|---|
| `det KO: modello mancante` | né il riferimento in scena né `Resources` hanno dato il modello: controlla che i due `.onnx` siano in `Assets/FaceQuest/Resources` e importati |
| `det KO` con altro messaggio | il modello ONNX non si è caricato: il log dice perché |
| `shader KO` | lo shader di allineamento non è arrivato nella build: rilancia il comando 1 |
| l'app si chiude da sola all'avvio | guarda il logcat: se dice `level0 is corrupted` / `Position out of bounds` è la **cache di build**, non il codice — i comandi 2 e 3 ora fanno sempre una build pulita |
| `level0 is corrupted` anche dopo la build pulita | chiudi Unity, cancella la cartella `Library` del progetto e riapri: Unity la ricostruisce (alcuni minuti) |
| `cam in attesa` | permesso camera non concesso, oppure `PassthroughCameraAccess` spento in scena |
| anteprima ingresso nera | la copia del frame non avviene: shader, o camera che non consegna la texture |
| Box specchiate in verticale, nessun riconoscimento | convenzione dell'asse y: togli la spunta `Tensore dall'alto` sul FaceQuestManager |
| `volti > 0` ma nessuna box | piazzamento nel mondo: `non piazzati` cresce; controlla `Distanza pupille` e `Altezza volto` |
| Volti rilevati ma tutti "Unknown" | le soglie sono alte per il tuo caso, o la qualità scarta tutto: guarda il log `qualita …` e abbassa `Soglia alta` verso 0,45 |
| Due persone si scambiano il nome | alza `Soglia alta` e `Margine minimo`; verifica che la qualità non stia accettando frame di profilo |
| Una persona genera due identità | abbassa `Soglia bassa` verso 0,28, o alza il numero di tentativi incerti prima di creare |
| Pipeline sotto 3 Hz | porta `Backend` del detector su GPUCompute (se era CPU), o riduci `Embedding per ciclo` a 1 |
| Il pannello scappa mentre lo punti | `Zona morta` del SeguiTesta troppo piccola |

Log utile da `adb logcat`: tutte le righe iniziano con `[FaceQuest]`.

---

## 11. Parti che dipendono dalla versione dell'API Meta

Se in futuro aggiorni MRUK, queste sono le righe da ricontrollare — e sono
poche, perché l'API compare in tre soli file:

* `PassthroughCameraAccess.IsPlaying / GetTexture() / GetCameraPose() /
  CurrentResolution / ViewportPointToRay(Vector2, Pose?)` — usate in
  `FaceQuestManager` e `BoundingBoxRenderer`. L'API **è cambiata** di recente:
  fino alla v81 si passava da `WebCamTextureManager` e `PassthroughCameraUtils`,
  ora da questo componente. Il codice è scritto sull'API attuale (MRUK 85).
* La **posa** arriva da `OVRPlugin.GetNodePoseStateAtTime`: il sample di Meta
  la circonda di un controllo perché non è sempre affidabile. Se vedi le box
  "in ritardo" rispetto alla testa, è lì che si guarda.
* `OVRCameraRig.rightHandAnchor` / `centerEyeAnchor` per il puntatore.
* Le risoluzioni disponibili (`GetSupportedResolutions`) sono 12 da SDK v83, 5
  prima. FaceQuest non le impone: usa quella corrente, qualunque sia, perché
  sono tutte 4:3.

---

## 12. Robustezza delle nove uscite

Le nove uscite del detector **non** vengono prese per posizione ma riconosciute
dalla loro forma: `(N,1)` sono punteggi, `(N,4)` box, `(N,10)` landmark, e `N`
dice a quale stride appartengono. Fidarsi dell'ordine del grafo significa che,
se un importatore lo riordina, il codice legge i box come punteggi e produce
rilevamenti plausibili ma completamente sbagliati — un guasto silenzioso. Con la
classificazione per forma, o funziona o lo dice in chiaro nel log.

---

## 13. Cosa proverei, nell'ordine

1. Comando **3** del menu (build che azzera l'archivio) e guarda la riga di
   diagnostica: `pipeline Hz` e `volti` sono i due numeri che dicono se la parte
   difficile funziona.
2. Una persona sola davanti al visore: deve comparire una box con `AA001`, e
   deve restare `AA001` girando la testa, uscendo dal campo visivo e
   rientrando.
3. Dal pannello, clicca la sua foto e dai un nome: il nome deve comparire
   immediatamente sulla box e nel pannello.
4. Chiudi e riapri l'app: la persona nominata deve essere riconosciuta subito,
   le anonime devono essere sparite.
5. Cancellazioni: DELETE nella finestra del nome deve togliere quella sola
   persona; `Y` + conferma deve svuotare tutto il pannello.
6. Due persone insieme: il caso in cui si vedono davvero le soglie. Se si
   scambiano, si agisce su `Soglia alta` e `Margine minimo`.

Poi, se serve: mettere la chiave nel Keystore Android, provare AdaFace se i
volti di lato danno problemi, e valutare INT8 sul detector se la pipeline
risultasse lenta (per ora FP32 su GPU dovrebbe bastare largamente).
