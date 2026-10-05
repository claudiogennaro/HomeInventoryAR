using System;
using System.Collections.Generic;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;

namespace InventarioAR
{
    /// <summary>
    /// Collega il detector di Meta al RegistroInventario.
    ///
    /// Differenza rispetto al sample: li i marker si piazzano premendo A, qui la
    /// conferma e automatica. L'utente gira per casa e basta; quando un oggetto e
    /// stato visto abbastanza volte da essere credibile, entra nell'inventario.
    /// </summary>
    public class InventarioManager : MonoBehaviour
    {
        [Header("Riferimenti al detector di Meta")]
        [SerializeField] private SentisInferenceUiManager m_uiInference;
        [SerializeField] private Meta.XR.PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private DetectionSpawnMarkerAnim m_prefabMarker;

        [Tooltip("Il pannello del sample: lo riusiamo per mostrare il conteggio DENTRO il visore, " +
                 "cosi non serve leggere Logcat sul PC mentre si cammina.")]
        [SerializeField] private DetectionUiMenuManager m_uiMenu;

        [Header("Taratura")]
        [Tooltip("Scostamento perpendicolare alla linea di vista, in metri: tenuto STRETTO, " +
                 "e cio che distingue due oggetti affiancati.")]
        [SerializeField] private float m_tolleranzaLaterale = 0.18f;

        [Tooltip("Scostamento lungo la linea di vista, in metri: tenuto LARGO, " +
                 "e la direzione in cui il raycast di profondita sbaglia.")]
        [SerializeField] private float m_tolleranzaRadiale = 0.9f;

        [Tooltip("Quante inferenze consecutive servono per promuovere un candidato a oggetto.")]
        [SerializeField] private int m_osservazioniPerConferma = 6;

        [Tooltip("Se attivo, persone e animali non entrano nel registro. Va lasciato " +
                 "attivo per l'inventario e spento nell'app di assistenza, dove sapere " +
                 "che c'e qualcuno davanti e la cosa piu importante.")]
        [SerializeField] private bool m_ignoraEsseriViventi = true;

        [Tooltip("Osservazioni necessarie perche un oggetto venga ancorato e salvato su disco. " +
                 "Con 3-5 inferenze al secondo, 40 sono circa dieci secondi di sguardo.")]
        [SerializeField] private int m_osservazioniSolide = 40;

        [Tooltip("Dopo quanti secondi senza rivederlo un candidato non confermato viene scartato.")]
        [SerializeField] private float m_scadenzaCandidati = 4f;

        [Tooltip("Di quanto deve spostarsi la stima (m) prima di muovere il marker. " +
                 "Serve a non far strisciare le etichette per correzioni millimetriche.")]
        [SerializeField] private float m_scattoMarker = 0.06f;

        [Tooltip("Ignora i rilevamenti oltre questa distanza (m): il raycast di profondita " +
                 "diventa impreciso da lontano e produce posizioni inaffidabili.")]
        [SerializeField] private float m_distanzaMassima = 3f;

        private readonly RegistroInventario _registro = new();
        private readonly List<(string, int, Vector3, float)> _osservazioni = new();

        /// <summary>
        /// Marker gia piazzati, per oggetto. Serve a poterli spostare: la posizione
        /// di un oggetto continua a raffinarsi dopo la conferma, e un marker fissato
        /// alla prima stima resta indietro — da qui le etichette lontane dall'oggetto.
        /// </summary>
        private readonly Dictionary<OggettoInventario, Transform> _marker = new();
        private readonly List<OggettoInventario> _markerOrfani = new();

        /// <summary>
        /// Emesso quando un oggetto raccoglie abbastanza osservazioni da meritare
        /// un'ancora spaziale. Ci si aggancia PersistenzaInventario.
        /// </summary>
        public event Action<OggettoInventario> Solidificato;

        /// <summary>Emesso per l'ancora di un oggetto assorbito in un altro: va cancellata.</summary>
        public event Action<string> AncoraOrfana;

        public IReadOnlyList<OggettoInventario> Oggetti => _registro.Oggetti;
        public int NumeroConfermati => _registro.NumeroConfermati;

        /// <summary>Soglia oltre la quale un oggetto viene ancorato: serve al pannello.</summary>
        public int OsservazioniSolide => m_osservazioniSolide;

        private Transform _contenitoreMarker;

        private void Awake()
        {
            // Contenitore nostro, invece del ContentParent del sample: quello era
            // ancorato dal DetectionManager, che ora e spento.
            _contenitoreMarker = new GameObject("MarkerInventario").transform;

            _registro.TolleranzaLaterale = m_tolleranzaLaterale;
            _registro.TolleranzaRadiale = m_tolleranzaRadiale;
            _registro.OsservazioniPerConferma = m_osservazioniPerConferma;
            _registro.OsservazioniSolide = m_osservazioniSolide;
            _registro.ScadenzaCandidati = m_scadenzaCandidati;
            if (m_ignoraEsseriViventi) _registro.IgnoraEsseriViventi();
        }

        private void OnEnable()
        {
            if (m_uiInference == null)
            {
                Debug.LogError("[Inventario] Manca il riferimento a SentisInferenceUiManager: " +
                               "trascinalo nell'Inspector, altrimenti non arriva nessun rilevamento.");
                return;
            }

            // Ci agganciamo all'evento che il detector emette dopo ogni inferenza,
            // non a Update(): cosi una "osservazione" corrisponde a un'inferenza vera
            // e il conteggio non dipende dal frame rate.
            m_uiInference.OnObjectsDetected.AddListener(OnRilevamenti);
        }

        private void OnDisable()
        {
            if (m_uiInference != null)
                m_uiInference.OnObjectsDetected.RemoveListener(OnRilevamenti);
        }

        private void OnRilevamenti(int _)
        {
            _osservazioni.Clear();

            var testa = TrovaTesta();

            foreach (var box in m_uiInference.m_boxDrawn)
            {
                if (box.BoxRectTransform == null) continue;

                var posizione = box.BoxRectTransform.position;

                // Scarta i rilevamenti lontani: oltre qualche metro la mappa di
                // profondita e rumorosa e la posizione stimata puo sbagliare di
                // parecchio, creando doppioni. Tre metri e anche il limite entro
                // cui Meta consiglia di ancorare, quindi il taglio ci servira
                // comunque in Fase 3.
                if (testa != null && Vector3.Distance(testa.position, posizione) > m_distanzaMassima)
                    continue;

                // Meta-diagonale del box in metri: la usiamo come "raggio" dell'oggetto,
                // per proporzionare la soglia di fusione alla sua dimensione reale.
                var dim = box.BoxRectTransform.sizeDelta;
                var raggio = Mathf.Max(dim.x, dim.y) * 0.5f;

                // Il file delle classi ha fine-riga di Windows e il codice del sample
                // lo divide solo su '\n': ogni nome si porta dietro un '\r' invisibile.
                // Ripulirlo QUI, all'ingresso, evita che l'errore si propaghi ovunque —
                // faceva fallire la traduzione dei nomi e anche l'esclusione di "person",
                // che non ha mai combaciato con nulla.
                var classe = box.ClassName?.Trim();
                if (string.IsNullOrEmpty(classe)) continue;

                _osservazioni.Add((classe, box.ClassId, posizione, raggio));
            }

            var puntoDiVista = testa != null ? testa.position : Vector3.zero;
            var nuovi = _registro.Aggiorna(_osservazioni, Time.time, puntoDiVista);

            foreach (var oggetto in nuovi)
            {
                PiazzaMarker(oggetto);
                Debug.Log($"[Inventario] Confermato: {oggetto}. Totale confermati: {_registro.NumeroConfermati}");
            }

            foreach (var solido in _registro.AppenaSolidi)
            {
                // Lo scatto si prende ADESSO, nel frame in cui l'oggetto e stato
                // appena visto: aspettare significherebbe fotografare una stanza in
                // cui l'utente si e gia girato.
                if (string.IsNullOrEmpty(solido.Scatto))
                    solido.Scatto = ScattiInventario.Cattura(
                        m_cameraAccess, solido.Posizione, solido.Raggio, solido.Id) ?? "";

                Solidificato?.Invoke(solido);
            }

            foreach (var orfana in _registro.AncoreDaCancellare)
                AncoraOrfana?.Invoke(orfana);
            _registro.AncoreDaCancellare.Clear();

            AggiornaMarker();
            AggiornaPannello();
        }

        /// <summary>
        /// Scrive nel pannello il numero reale di oggetti confermati.
        ///
        /// Il metodo del sample accumula (+=), quindi passiamo prima -1 per
        /// azzerarlo e poi il totale: cosi il numero puo anche SCENDERE quando il
        /// consolidamento fonde due doppioni, che e proprio cio che vogliamo vedere.
        /// </summary>
        private void AggiornaPannello()
        {
            if (m_uiMenu == null) return;
            m_uiMenu.OnObjectsIndentified(-1);
            m_uiMenu.OnObjectsIndentified(_registro.NumeroConfermati);
        }

        private static Transform TrovaTesta()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
            return Camera.main != null ? Camera.main.transform : null;
        }

        private void PiazzaMarker(OggettoInventario oggetto)
        {
            if (m_prefabMarker == null) return;

            var marker = Instantiate(
                m_prefabMarker,
                oggetto.Posizione,
                Quaternion.identity,
                _contenitoreMarker);

            marker.SetYoloClassName(oggetto.Classe);
            _marker[oggetto] = marker.transform;
        }

        /// <summary>
        /// Riallinea i marker alla posizione corrente dei loro oggetti, e rimuove
        /// quelli rimasti orfani dopo una fusione o uno scarto.
        /// </summary>
        private void AggiornaMarker()
        {
            if (_marker.Count == 0) return;

            _markerOrfani.Clear();

            foreach (var coppia in _marker)
            {
                if (coppia.Value == null) { _markerOrfani.Add(coppia.Key); continue; }

                if (!_registro.Contiene(coppia.Key))
                {
                    Destroy(coppia.Value.gameObject);
                    _markerOrfani.Add(coppia.Key);
                    continue;
                }

                // Si sposta solo per scostamenti apprezzabili: la media mobile
                // corregge di pochi millimetri a ogni osservazione, e inseguirla
                // fedelmente fa strisciare le etichette per aria senza motivo.
                if (Vector3.Distance(coppia.Value.position, coppia.Key.Posizione) > m_scattoMarker)
                    coppia.Value.position = coppia.Key.Posizione;
            }

            foreach (var o in _markerOrfani) _marker.Remove(o);
        }

        /// <summary>
        /// Rimette in vita un oggetto letto da disco, alla posizione dove l'ancora
        /// spaziale e stata ritrovata. La posizione arriva dall'ancora, non dal
        /// file: e il sistema del Quest a sapere dove sta quel punto oggi.
        /// </summary>
        public void Ripristina(RecordOggetto record, Vector3 posizione)
        {
            var oggetto = new OggettoInventario(record.Classe, record.ClasseId, posizione, record.Raggio, Time.time)
            {
                Id = record.Id,
                Osservazioni = Mathf.Max(1, record.Osservazioni),
                UuidAncora = record.UuidAncora,
                Scatto = record.Scatto ?? "",
            };

            _registro.Ripristina(oggetto);
            PiazzaMarker(oggetto);
            AggiornaPannello();
        }

        /// <summary>Svuota l'inventario in memoria, i marker e l'archivio su disco.</summary>
        [ContextMenu("Svuota inventario")]
        public void Svuota()
        {
            _registro.Svuota();
            foreach (var t in _marker.Values) if (t != null) Destroy(t.gameObject);
            _marker.Clear();
            AggiornaPannello();
            foreach (var t in _marker.Values) if (t != null) Destroy(t.gameObject);
            _marker.Clear();
            Debug.Log("[Inventario] Svuotato.");
        }
    }
}
