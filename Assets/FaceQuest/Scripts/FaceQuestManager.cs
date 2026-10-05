// L'orchestratore della pipeline.
//
//   Passthrough Camera
//     -> copia del frame (una sola, riusata da tutti gli stadi)
//     -> Face Detection (SCRFD)
//     -> Face Tracking
//     -> Face Alignment + Crop 112x112   (solo per le tracce che lo chiedono)
//     -> Face Embedding (ArcFace/MobileFaceNet)
//     -> Face Quality
//     -> Similarity Search + decisione a tre soglie
//     -> Database runtime
//     -> Box AR + etichetta, pannello People
//
// Tre principi che spiegano le scelte di questo file:
//
// Il main thread non si blocca mai. Ogni stadio pesante e una coroutine che
// attende la lettura asincrona dalla GPU: il rendering AR continua a 72-90 Hz
// mentre la pipeline AI gira ai suoi 5-15 Hz. Se si vede lo scatto del
// passthrough, qualcosa qui e diventato sincrono.
//
// Il frame si copia una volta. La texture della camera cambia sotto i piedi: se
// il detector guardasse un fotogramma e l'allineamento un altro, i landmark
// finirebbero fuori posto in modo intermittente, che e il guasto piu costoso da
// cercare.
//
// Gli embedding si calcolano con parsimonia. Non uno per volto per frame, ma
// solo quando il tracker dice che serve, e al massimo due per giro.

using System.Collections;
using System.Collections.Generic;
using Meta.XR;
using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public class FaceQuestManager : MonoBehaviour
    {
        [Header("Sorgente")]
        [SerializeField] private PassthroughCameraAccess m_camera;

        [Header("Pipeline")]
        [SerializeField] private FaceDetector m_detector;
        [SerializeField] private FaceEmbeddingExtractor m_embedder;
        [SerializeField] private FaceQualityEstimator m_qualita;
        [SerializeField] private FaceTracker m_tracker;
        [SerializeField] private IdentityMatcher m_matcher;
        [SerializeField] private PersonDatabase m_database;
        [SerializeField] private PersonPersistenceManager m_persistenza;

        [Header("Presentazione")]
        [SerializeField] private BoundingBoxRenderer m_box;
        [SerializeField] private PeoplePanelController m_pannello;
        [SerializeField] private PersonEditorUI m_editor;

        [Header("Nascita di una nuova identita")]
        [SerializeField, Tooltip("Quanti fotogrammi di seguito devono dire 'non la conosco' prima di creare un'identita.")]
        private int m_confermePerCreare = 3;
        [SerializeField, Range(0f, 1f), Tooltip("Quanto devono somigliarsi fra loro i fotogrammi candidati.")]
        private float m_coerenzaCandidati = 0.45f;
        [SerializeField, Range(0f, 1f), Tooltip("Pavimento di qualita per creare: deve restare raggiungibile con la luce di casa. Cio che protegge dai falsi positivi e il consenso, non questa soglia.")]
        private float m_qualitaPerCreare = 0.40f;
        [SerializeField, Range(0f, 1f), Tooltip("Punteggio minimo del detector per creare un'identita.")]
        private float m_punteggioPerCreare = 0.55f;

        [Header("Manutenzione del database")]
        [SerializeField, Range(0f, 1f), Tooltip("Sopra questa somiglianza due identita sono la stessa persona e si fondono.")]
        private float m_sogliaFusione = 0.55f;
        [SerializeField, Tooltip("Secondi di silenzio dopo i quali un'identita con poche osservazioni viene scartata.")]
        private float m_silenzioFantasmi = 25f;
        [SerializeField, Tooltip("Fino a quante osservazioni un'identita e considerata un possibile fantasma.")]
        private int m_osservazioniFantasma = 2;
        [SerializeField, Tooltip("Dopo quanti secondi un'identita ancora senza nome sparisce dal pannello. 600 = 10 minuti. 0 disattiva la scadenza.")]
        private float m_vitaAnonime = 600f;
        [SerializeField, Tooltip("Un'anonima vista da meno di questi secondi non viene rimossa anche se scaduta: se sparisse mentre la persona e inquadrata rinascerebbe subito con un altro numero.")]
        private float m_graziaAnonime = 3f;

        [Header("Prestazioni")]
        [SerializeField, Tooltip("Quanti embedding al massimo per ciclo di pipeline.")]
        private int m_embeddingPerCiclo = 2;
        [SerializeField, Tooltip("Pausa minima fra due cicli, in secondi. 0 = piu veloce possibile.")]
        private float m_pausaCiclo = 0.02f;
        [SerializeField] private bool m_riconoscimentoAttivo = true;
        [SerializeField] private bool m_diagnosticaVisibile = true;
        [SerializeField, Tooltip("La riga 0 del tensore e in cima all'immagine. Se le box risultano specchiate in verticale, togli la spunta.")]
        private bool m_tensoreDallAlto = true;
        [SerializeField, Tooltip("Assegnato dal costruttore di scena: un riferimento serializzato finisce sempre nella build, Shader.Find no.")]
        private Shader m_shaderAffine;
        [SerializeField, Tooltip("Diagnostica: esegue solo il detector, senza allineamento, embedding e qualita. Serve per isolare in quale meta della pipeline sta un problema.")]
        private bool m_soloDetector;

        public bool RiconoscimentoAttivo => m_riconoscimentoAttivo;

        /// <summary>false in modalita demo: nessun volto e nessun template tocca lo storage.</summary>
        private bool ScriveSuDisco => m_persistenza != null && !m_persistenza.SoloInMemoria;

        private RenderTexture m_rtFrame;
        private RenderTexture m_rtDetector;
        private RenderTexture m_rtVolto;
        private BlitAffine m_blit;
        private Texture2D m_bufferVolto;

        private readonly List<VoltoRilevato> m_rilevamenti = new List<VoltoRilevato>();
        private readonly float[] m_inversa = new float[6];
        private readonly Vector2[] m_landmark = new Vector2[5];

        private float m_hzPipeline;
        private int m_embeddingTotali;
        private float m_tempoUscita;
        private float m_prossimaDiagnostica;
        private float m_prossimoLog;
        private float m_prossimaManutenzione;
        private QualitaVolto m_ultimaQualita;
        private int m_candidatiInAttesa;

        private void Awake()
        {
            m_blit = new BlitAffine(m_shaderAffine);
            FaceQuestConfig.TensoreDallAlto = m_tensoreDallAlto;
            if (m_persistenza != null) m_persistenza.Collega(m_database);
        }

        private void OnDestroy()
        {
            m_blit?.Dispose();
            if (m_rtFrame != null) m_rtFrame.Release();
            if (m_rtDetector != null) m_rtDetector.Release();
            if (m_rtVolto != null) m_rtVolto.Release();
            if (m_bufferVolto != null) Destroy(m_bufferVolto);
        }

        private IEnumerator Start()
        {
            if (m_camera == null || m_detector == null || m_embedder == null)
            {
                Debug.LogError("[FaceQuest] Riferimenti mancanti: la pipeline non parte.");
                yield break;
            }

            // Si attende il permesso e il primo fotogramma. PassthroughCameraAccess
            // registra un errore se lo si interroga prima: IsPlaying e l'unico
            // controllo lecito in questa fase.
            while (!m_camera.IsPlaying) yield return null;
            Debug.Log($"[FaceQuest] Camera pronta: {m_camera.CurrentResolution.x}x{m_camera.CurrentResolution.y}.");

            PreparaTexture();

            // Riscaldamento, e allo stesso tempo tracciamento del percorso.
            //
            // Il primo passaggio in una rete costa molto piu dei successivi: il
            // sample di Meta lo fa all'avvio per non bloccare il main thread al
            // primo oggetto inquadrato, e qui serve lo stesso. Il vantaggio in
            // piu e che ogni stadio lascia una riga nel log: se l'app si chiude,
            // l'ultima riga scritta dice a quale stadio e morta, che senza
            // debugger sul visore e l'unica informazione disponibile.
            Debug.Log("[FaceQuest] Riscaldamento del detector...");
            yield return m_detector.Rileva(m_rtDetector, m_rilevamenti);
            Debug.Log($"[FaceQuest] Detector riscaldato ({m_detector.Stato}).");

            if (m_soloDetector)
            {
                Debug.LogWarning("[FaceQuest] Modalita solo-detector: embedding, qualita e identita sono disattivati.");
            }
            else
            {
                Debug.Log("[FaceQuest] Riscaldamento dell'embedder...");
                var riscaldato = false;
                yield return m_embedder.Estrai(m_rtVolto, e => riscaldato = e);
                Debug.Log($"[FaceQuest] Embedder riscaldato (esito {riscaldato}).");
            }

            m_rilevamenti.Clear();
            Debug.Log("[FaceQuest] Pipeline avviata.");

            var tempoUltimoCiclo = Time.realtimeSinceStartup;

            while (true)
            {
                if (!m_riconoscimentoAttivo || !m_camera.IsPlaying)
                {
                    yield return null;
                    continue;
                }

                var frame = m_camera.GetTexture();
                if (frame == null) { yield return null; continue; }

                var posa = m_camera.GetCameraPose();

                Graphics.Blit(frame, m_rtFrame);
                m_blit.Riempi(m_rtFrame, m_rtDetector);

                yield return m_detector.Rileva(m_rtDetector, m_rilevamenti);

                var ora = Time.time;
                m_tracker.Aggiorna(m_rilevamenti, ora);
                m_box.Aggiorna(m_tracker.Tracce, posa);

                if (!m_soloDetector)
                {
                    var daFare = m_tracker.DaIdentificare(ora, Mathf.Max(1, m_embeddingPerCiclo));
                    for (var i = 0; i < daFare.Count; i++)
                        yield return Identifica(daFare[i]);
                }

                Manutenzione();

                var adesso = Time.realtimeSinceStartup;
                var dt = adesso - tempoUltimoCiclo;
                tempoUltimoCiclo = adesso;
                if (dt > 1e-4f) m_hzPipeline = Mathf.Lerp(m_hzPipeline, 1f / dt, 0.2f);

                if (m_pausaCiclo > 0f) yield return new WaitForSeconds(m_pausaCiclo);
                else yield return null;
            }
        }

        private void PreparaTexture()
        {
            var res = m_camera.CurrentResolution;
            m_rtFrame = new RenderTexture(res.x, res.y, 0, RenderTextureFormat.ARGB32) { name = "FaceQuest_Frame" };
            m_rtDetector = new RenderTexture(m_detector.LarghezzaIngresso, m_detector.AltezzaIngresso, 0,
                RenderTextureFormat.ARGB32) { name = "FaceQuest_Detector" };
            m_rtVolto = new RenderTexture(m_embedder.Lato, m_embedder.Lato, 0,
                RenderTextureFormat.ARGB32) { name = "FaceQuest_Volto" };
            m_bufferVolto = new Texture2D(m_embedder.Lato, m_embedder.Lato, TextureFormat.RGB24, false);

            // Azzerate subito: il contenuto di una RenderTexture appena creata non
            // e definito, e darlo in pasto a una rete (o leggerlo) significa
            // lavorare su memoria non inizializzata.
            Debug.Log("[FaceQuest] RenderTexture create, azzeramento...");
            Azzera(m_rtFrame);
            Azzera(m_rtDetector);
            Azzera(m_rtVolto);

            Debug.Log($"[FaceQuest] Texture pronte: frame {res.x}x{res.y}, " +
                      $"detector {m_rtDetector.width}x{m_rtDetector.height}, volto {m_rtVolto.width}.");
        }

        /// <summary>
        /// Azzera una RenderTexture con un blit e non con GL.Clear.
        ///
        /// GL.Clear e una chiamata in modalita immediata che va fatta durante il
        /// rendering: invocarla da una coroutine, fuori da quel contesto, e
        /// documentata come non supportata e su Vulkan — che e il backend del
        /// Quest — puo terminare il processo. Un blit di una texture nera fa la
        /// stessa cosa passando dalla strada normale.
        /// </summary>
        private static void Azzera(RenderTexture rt)
        {
            if (rt != null) Graphics.Blit(Texture2D.blackTexture, rt);
        }

        /// <summary>Allineamento, embedding, qualita e decisione per una singola traccia.</summary>
        private IEnumerator Identifica(Traccia t)
        {
            t.InLavorazione = true;
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    m_landmark[i] = FaceQuestConfig.APixelDallAlto(
                        t.Ultimo.Landmark(i), m_rtFrame.width, m_rtFrame.height);
                }

                if (!FaceAligner.CalcolaInversa(m_landmark, m_inversa, out var latoVolto)) yield break;

                m_blit.Applica(m_rtFrame, m_rtVolto, m_inversa);

                var ok = false;
                yield return m_embedder.Estrai(m_rtVolto, e => ok = e);
                if (!ok) yield break;

                m_embeddingTotali++;

                // La qualita si misura sullo stesso ritaglio che e appena stato
                // dato alla rete: e il giudizio su cio che la rete ha visto, non
                // su un'approssimazione.
                yield return m_qualita.Valuta(m_rtVolto, t.Ultimo, latoVolto);
                var q = m_qualita.Risultato;
                m_ultimaQualita = q;

                var embedding = m_embedder.Ultimo;
                var esito = m_matcher.Cerca(embedding, m_database.Persone);

                switch (esito.Esito)
                {
                    case EsitoIdentita.Riconosciuta:
                        {
                            var p = esito.Candidato;
                            m_database.Osserva(p, embedding, q.Punteggio, q.Accettabile);
                            AggiornaAnteprima(p, q);
                            t.PersonID = p.PersonID;
                            t.Etichetta = p.DisplayName;
                            t.Confidenza = esito.Somiglianza;
                            t.Esito = EsitoIdentita.Riconosciuta;
                            t.TentativiIncerti = 0;
                            t.ScordaCandidati();
                            break;
                        }

                    case EsitoIdentita.Incerta:
                        {
                            t.TentativiIncerti++;
                            t.Confidenza = esito.Somiglianza;

                            // Se la traccia aveva gia un'identita solida non la si
                            // butta per un frame ambiguo: si tiene l'etichetta e si
                            // salta l'aggiornamento del template. Cambiare nome a
                            // ogni frame dubbio e peggio che restare fermi.
                            if (t.PersonID != null)
                            {
                                var nota = m_database.PerID(t.PersonID);
                                if (nota != null) m_database.Osserva(nota, embedding, q.Punteggio, false);
                            }
                            else
                            {
                                t.Etichetta = "Unknown";
                                t.Esito = EsitoIdentita.Incerta;

                                // Qui NON si crea nulla, ed e la correzione che
                                // conta piu di tutte.
                                //
                                // "Incerta" vuol dire somiglianza fra la soglia
                                // bassa e quella alta: probabilmente una persona
                                // che conosciamo, vista male. La versione
                                // precedente, dopo quattro fotogrammi ambigui,
                                // creava una nuova identita — ed era una fabbrica
                                // di doppioni: la stessa persona in penombra
                                // compariva nel pannello come AA002 accanto al suo
                                // nome vero. Un fotogramma ambiguo non e una
                                // persona nuova: e un fotogramma ambiguo. Si
                                // aspetta che la somiglianza salga sopra la soglia
                                // alta o scenda sotto quella bassa.
                                t.ScordaCandidati();
                            }
                            break;
                        }

                    default:
                        {
                            // Identita nuova: mai da un fotogramma solo.
                            //
                            // Servono tre verdetti "non la conosco" di seguito, su
                            // volti di qualita alta, e i tre embedding devono
                            // somigliarsi FRA LORO. Il perche si vedeva nel
                            // pannello: identita con una sola osservazione, nate e
                            // mai piu riviste, una delle quali aveva per ritratto
                            // uno scorcio di mare. Un falso positivo produce un
                            // embedding casuale, e un embedding casuale non
                            // somiglia al successivo: il consenso non arriva e
                            // l'identita non nasce.
                            t.Confidenza = 0f;

                            var abbastanzaBuono = q.Accettabile
                                                  && q.Punteggio >= m_qualitaPerCreare
                                                  && t.Ultimo.Punteggio >= m_punteggioPerCreare;

                            if (!abbastanzaBuono)
                            {
                                t.Etichetta = "Unknown";
                                t.Esito = EsitoIdentita.Incerta;
                                t.ScordaCandidati();
                                break;
                            }

                            t.VerdettiNuova++;
                            t.Candidati.Add((float[])embedding.Clone());
                            m_candidatiInAttesa = t.VerdettiNuova;
                            while (t.Candidati.Count > Mathf.Max(2, m_confermePerCreare)) t.Candidati.RemoveAt(0);

                            if (t.VerdettiNuova < m_confermePerCreare || !CandidatiCoerenti(t.Candidati))
                            {
                                t.Etichetta = "Unknown";
                                t.Esito = EsitoIdentita.Incerta;
                                break;
                            }

                            CreaNuova(t, embedding, q);
                            t.ScordaCandidati();
                            break;
                        }
                }

                m_tracker.SegnaIdentificata(t, Time.time);
            }
            finally
            {
                t.InLavorazione = false;
            }
        }

        /// <summary>
        /// I candidati devono somigliarsi fra loro almeno quanto la soglia di
        /// coerenza: e il modo di distinguere "una persona nuova vista tre volte"
        /// da "tre misure casuali su qualcosa che non e un volto".
        /// </summary>
        private bool CandidatiCoerenti(List<float[]> candidati)
        {
            if (candidati.Count < 2) return false;

            for (var i = 0; i < candidati.Count; i++)
            {
                for (var j = i + 1; j < candidati.Count; j++)
                {
                    if (FaceEmbeddingExtractor.Somiglianza(candidati[i], candidati[j]) < m_coerenzaCandidati)
                        return false;
                }
            }
            return true;
        }

        private void CreaNuova(Traccia t, float[] embedding, QualitaVolto q)
        {
            var p = m_database.Crea(embedding, q.Punteggio);
            AggiornaAnteprima(p, q);
            t.PersonID = p.PersonID;
            t.Etichetta = p.DisplayName;
            t.Confidenza = 1f;
            t.Esito = EsitoIdentita.Nuova;
            t.TentativiIncerti = 0;
            Debug.Log($"[FaceQuest] Nuova identita {p.PersonID} (qualita {q.Punteggio:0.00}, {q.LatoPixel:0} px).");
        }

        /// <summary>
        /// Tiene come anteprima il miglior ritaglio visto per quella persona.
        /// La lettura sincrona dalla GPU e accettabile qui perche succede solo
        /// quando la qualita migliora, non a ogni osservazione.
        /// </summary>
        private void AggiornaAnteprima(Persona p, QualitaVolto q)
        {
            if (!q.Accettabile || q.Punteggio <= p.QualitaAnteprima + 0.02f) return;

            var precedente = RenderTexture.active;
            try
            {
                RenderTexture.active = m_rtVolto;
                m_bufferVolto.ReadPixels(new Rect(0, 0, m_rtVolto.width, m_rtVolto.height), 0, 0);
                m_bufferVolto.Apply(false);

                if (p.Anteprima == null)
                {
                    p.Anteprima = new Texture2D(m_rtVolto.width, m_rtVolto.height, TextureFormat.RGB24, false);
                }
                p.Anteprima.SetPixels32(m_bufferVolto.GetPixels32());
                p.Anteprima.Apply(false);
                p.QualitaAnteprima = q.Punteggio;

                // L'anteprima vive in memoria come Texture2D: su disco finisce
                // solo se la persistenza e attiva.
                if (p.Persistent && ScriveSuDisco) p.RepresentativeFaceFile = ArchivioVolti.Salva(p.Anteprima, p.PersonID);
            }
            finally
            {
                RenderTexture.active = precedente;
            }
        }

        // ------------------------------------------------------------------
        // Comandi

        private void Update()
        {
            if (m_database == null || m_tracker == null || m_box == null) return;

            // Mentre la finestra del nome e aperta i tasti di sistema tacciono:
            // la si sta usando col puntatore, e accendere o cancellare in quel
            // momento sarebbe sempre un incidente.
            if (m_editor == null || !m_editor.Aperto)
            {
                if (FaceComandi.RiconoscimentoPremuto()) CommutaRiconoscimento();
                if (FaceComandi.CancellaDbPremuto()) m_editor?.ChiediConfermaCancellaTutto();
            }

            if (FaceComandi.UscitaTenuta())
            {
                m_tempoUscita += Time.deltaTime;
                if (m_tempoUscita > 1f)
                {
                    Debug.Log("[FaceQuest] Uscita richiesta dal visore (B tenuto).");
                    Application.Quit();
                }
            }
            else m_tempoUscita = 0f;

            if (!m_diagnosticaVisibile || Time.time < m_prossimaDiagnostica) return;
            m_prossimaDiagnostica = Time.time + 0.25f;

            var riga = RigaDiagnostica();
            m_pannello?.ImpostaDiagnostica(riga);
            m_pannello?.ImpostaAnteprime(m_rtDetector, m_rtVolto);

            // Anche in logcat: quando il pannello e fuori dal campo visivo o
            // l'app si chiude subito, la riga in console e l'unica traccia.
            if (Time.time >= m_prossimoLog)
            {
                m_prossimoLog = Time.time + 3f;
                Debug.Log($"[FaceQuest] {riga.Replace("\n", " | ")}");
            }
        }

        /// <summary>
        /// Ogni pochi secondi il database si guarda allo specchio: fonde le
        /// identita che si somigliano troppo per essere persone diverse e scarta
        /// quelle nate da pochi fotogrammi e mai piu riviste.
        ///
        /// Il matching decide su un fotogramma alla volta e non puo tornare
        /// indietro; questo passaggio lavora sull'insieme, dove un doppione e
        /// evidente.
        /// </summary>
        private void Manutenzione()
        {
            if (Time.time < m_prossimaManutenzione) return;
            m_prossimaManutenzione = Time.time + 4f;

            var fuse = m_database.FondiSimili(m_sogliaFusione);
            var potate = m_database.PotaFantasmi(m_silenzioFantasmi, m_osservazioniFantasma);
            if (m_vitaAnonime > 0f)
                potate += m_database.PotaAnonimeScadute(m_vitaAnonime, m_graziaAnonime);

            if (fuse == 0 && potate == 0) return;

            // Le tracce che mostravano un'identita scomparsa devono rifare il
            // confronto, altrimenti resterebbero attaccate a un nome che non c'e.
            foreach (var t in m_tracker.Tracce)
            {
                if (t.PersonID == null) continue;
                if (m_database.PerID(t.PersonID) != null) continue;
                m_tracker.ScollegaPersona(t.PersonID);
            }
        }

        public void CommutaRiconoscimento()
        {
            m_riconoscimentoAttivo = !m_riconoscimentoAttivo;

            if (!m_riconoscimentoAttivo)
            {
                m_box.Pulisci();
                m_tracker.Azzera();
                m_rilevamenti.Clear();
            }

            m_database.NotificaCambio();
            Debug.Log($"[FaceQuest] Riconoscimento {(m_riconoscimentoAttivo ? "ATTIVO" : "SPENTO")}.");
        }

        public void NotificaRinomina(Persona p)
        {
            m_tracker.RinominaPersona(p.PersonID, p.DisplayName);
            if (p.Persistent && ScriveSuDisco && p.Anteprima != null && string.IsNullOrEmpty(p.RepresentativeFaceFile))
                p.RepresentativeFaceFile = ArchivioVolti.Salva(p.Anteprima, p.PersonID);

            // Si salva subito, non alla chiusura: il momento in cui l'utente
            // batte un nome e l'unico in cui dichiara che quella persona conta,
            // e una chiusura brusca (batteria, visore tolto, crash) non deve
            // buttare via proprio quella decisione.
            if (p.Persistent) m_persistenza?.Salva();

            m_database.NotificaCambio();
        }

        public void CancellaPersona(Persona p)
        {
            var id = p.PersonID;
            m_database.Cancella(p);
            m_tracker.ScollegaPersona(id);
            m_persistenza?.Salva();
            Debug.Log($"[FaceQuest] Persona {id} cancellata.");
        }

        public void CancellaTutto()
        {
            m_database.CancellaTutto();
            m_tracker.Azzera();
            m_box.Pulisci();
            Debug.Log("[FaceQuest] Database cancellato per intero (chiave di cifratura compresa).");
        }

        /// <summary>
        /// Le due righe che dicono, senza logcat, dove si e fermata la pipeline.
        ///
        /// L'ordine non e casuale: si legge da sinistra e il primo elemento "KO"
        /// e la causa. "max" e il punteggio piu alto uscito dal detector prima
        /// della soglia: se resta a 0,00 il problema e a monte (immagine nera,
        /// shader mancante, uscite non riconosciute); se sta fra 0,20 e la
        /// soglia, il modello vede qualcosa e la soglia e troppo alta.
        /// </summary>
        private string RigaDiagnostica()
        {
            var cam = m_camera == null
                ? "cam assente"
                : m_camera.IsPlaying
                    ? $"cam {m_camera.CurrentResolution.x}x{m_camera.CurrentResolution.y}"
                    : "cam in attesa (permesso?)";

            var det = m_detector == null ? "det assente"
                    : m_detector.Pronto ? $"det {m_detector.LarghezzaIngresso}x{m_detector.AltezzaIngresso}"
                    : $"det KO: {m_detector.Stato}";

            var emb = m_embedder != null && m_embedder.Pronto ? "emb ok" : "emb KO";
            var shader = m_blit != null && m_blit.Valido ? "shader ok" : "shader KO";
            var max = m_detector != null ? m_detector.PunteggioMassimo : 0f;

            // Terza riga: perche un volto non diventa un'identita. Senza questa,
            // "emb 47 · persone 0" non dice se il problema e la qualita, la posa
            // o il consenso — e si finisce a tarare a caso.
            var q = m_ultimaQualita;
            var motivo = string.IsNullOrEmpty(q.Motivo) ? "ok" : q.Motivo;

            return $"{cam} · {det} · {emb} · {shader}\n" +
                   $"{m_hzPipeline:0.0} Hz · volti {m_rilevamenti.Count} ({max:0.00}) · " +
                   $"tracce {m_tracker.Tracce.Count} · emb {m_embeddingTotali} · " +
                   $"persone {m_database.Persone.Count}\n" +
                   $"qualita {q.Punteggio:0.00} ({motivo}) · {q.LatoPixel:0} px · " +
                   $"yaw {q.Yaw:0}° pitch {q.Pitch:0}° · conferme {m_candidatiInAttesa}/{m_confermePerCreare}";
        }
    }
}
