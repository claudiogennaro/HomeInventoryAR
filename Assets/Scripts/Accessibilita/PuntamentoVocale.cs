using InventarioAR;
using Meta.XR;
using UnityEngine;

namespace Accessibilita
{
    /// <summary>
    /// Punti col controller destro, l'app dice cosa stai puntando; il controller
    /// sinistro vibra tanto piu velocemente quanto piu l'oggetto e vicino.
    ///
    /// Perche si sceglie il bersaglio per ANGOLO e non per distanza dal raggio:
    /// puntare a mano libera non e preciso, e un oggetto lontano perdonerebbe uno
    /// scarto in metri molto maggiore di uno vicino. L'angolo e la misura che
    /// corrisponde a quanto l'utente sente di "star puntando" qualcosa.
    ///
    /// Il bersaglio viene dal RegistroInventario e non dai rilevamenti grezzi,
    /// perche i rilevamenti sfarfallano: senza identita stabili la voce direbbe
    /// "sedia, mouse, sedia" seguendo il rumore del detector.
    ///
    /// La mano destra fa DUE cose che non si disturbano perche usano canali
    /// diversi: la VOCE dice che oggetto stai puntando (informazione lenta, che
    /// arriva quando cambia il bersaglio), il TATTO dice a che distanza sta la
    /// superficie colpita dal raggio (informazione continua, che cambia mentre
    /// muovi la mano). Cosi il raggio destro si usa come il bastone bianco: lo
    /// spazzi davanti a te e senti la geometria.
    ///
    /// Perche la sonda batte sulla superficie e non sull'oggetto riconosciuto:
    /// per capire se passi da una porta serve sapere dov'e lo stipite, e lo
    /// stipite non e una classe COCO. Il ventaglio resta la scansione grossolana
    /// del cammino; questo e il dito che tocca.
    ///
    /// Il segnale piu importante e l'ASSENZA di battito: quando il raggio non
    /// colpisce niente entro la portata c'e spazio libero. Spazzando in
    /// orizzontale, il varco e l'arco di silenzio fra due zone che battono.
    /// </summary>
    public class PuntamentoVocale : MonoBehaviour
    {
        [SerializeField] private InventarioManager m_inventario;
        [SerializeField] private EnvironmentRaycastManager m_raycast;

        [Header("Puntamento")]
        [Tooltip("Apertura del cono di puntamento, in gradi.")]
        [SerializeField] private float m_aperturaGradi = 14f;

        [Tooltip("Oltre questa distanza (m) un oggetto non viene annunciato.")]
        [SerializeField] private float m_portata = 5f;

        [Tooltip("Osservazioni minime perche un oggetto sia annunciabile. Va tenuto BASSO: " +
                 "in un'app che assiste, tacere costa piu che dire qualcosa di incerto. " +
                 "Nell'inventario valeva l'opposto, perche un oggetto sbagliato veniva " +
                 "ancorato e salvato per sempre.")]
        [SerializeField] private int m_osservazioniMinime = 2;

        [Tooltip("Di quanto un nuovo candidato deve essere piu allineato del bersaglio " +
                 "attuale per sostituirlo. Senza questo margine due oggetti quasi " +
                 "equidistanti si alternerebbero a ogni frame.")]
        [SerializeField] private float m_margineCambio = 3f;

        [Tooltip("Secondi per cui il nuovo candidato deve restare il migliore prima " +
                 "di diventare il bersaglio.")]
        [SerializeField] private float m_attesaCambio = 0.25f;

        [Header("Sonda della superficie (il bastone)")]
        [Tooltip("Sonda con la profondita dell'ambiente la superficie colpita dal raggio destro.")]
        [SerializeField] private bool m_sondaSuperficie = true;

        [Tooltip("Distanza massima sondata dal raggio destro, in metri. Piu corta della " +
                 "portata del ventaglio: il bastone serve a tastare l'intorno, non a " +
                 "misurare il fondo della stanza.")]
        [SerializeField] private float m_portataSonda = 3f;

        [Header("Raggio (solo per lo sviluppo)")]
        [Tooltip("Il raggio visibile non serve a chi non vede: serve a chi costruisce " +
                 "l'app, per capire cosa sta puntando senza fidarsi solo dell'udito.")]
        [SerializeField] private Transform m_raggio;
        [SerializeField] private Renderer m_rendererRaggio;
        [SerializeField] private float m_lunghezzaSenzaBersaglio = 2.5f;
        [SerializeField] private Color m_coloreLibero = new(0.6f, 0.6f, 0.65f);
        [SerializeField] private Color m_coloreBersaglio = new(0.2f, 0.85f, 0.4f);
        [SerializeField] private Color m_coloreSuperficie = new(1f, 0.7f, 0.2f);

        [Header("Vibrazione")]
        [Tooltip("Intervallo fra i battiti quando l'oggetto e vicinissimo, e quando e al limite.")]
        [SerializeField] private float m_intervalloVicino = 0.02f;
        [SerializeField] private float m_intervalloLontano = 0.7f;

        [Tooltip("Esponente della mappatura distanza-ritmo. Con 1 il ritmo e lineare " +
                 "nella distanza, e da vicino due quote diverse suonano quasi uguali. " +
                 "Sotto 1 la curva si impenna vicino: il ritmo cambia molto nei primi " +
                 "decimetri, poco al fondo della portata, che e dove serve il dettaglio " +
                 "quando tasti un varco.")]
        [SerializeField] private float m_curva = 0.6f;

        [Tooltip("Quale controller vibra. Il destro e quello che punta: sentire il " +
                 "ritorno nella stessa mano che mira e piu immediato che nell'altra.")]
        [SerializeField] private OVRInput.Controller m_controllerVibrante = OVRInput.Controller.RTouch;

        [Tooltip("Durata e intensita di ogni battito.")]
        [SerializeField] private float m_durataBattito = 0.02f;
        [SerializeField] private float m_intensitaBattito = 0.9f;

        [Tooltip("Distanza (m) alla quale la vibrazione e al ritmo massimo.")]
        [SerializeField] private float m_distanzaMinima = 0.25f;

        public OggettoInventario Puntato { get; private set; }
        public float DistanzaPuntato { get; private set; }

        /// <summary>Distanza della superficie colpita dal raggio destro, o infinito se il varco e libero.</summary>
        public float DistanzaSuperficie { get; private set; } = float.PositiveInfinity;
        public bool SuperficieColpita => !float.IsPositiveInfinity(DistanzaSuperficie);
        public string Stato { get; private set; } = "puntamento: nessun bersaglio";

        private OggettoInventario _candidato;
        private float _candidatoDa;
        private Vector3 _origineRaggio;
        private Vector3 _direzioneRaggio;
        private float _prossimoBattito;
        private bool _battitoAttivo;

        private void Update()
        {
            AggiornaBersaglio();
            SondaSuperficie();
            Annuncia();
            Vibra();
            AggiornaRaggio();
        }

        private void AggiornaBersaglio()
        {
            if (m_inventario == null) return;

            var origine = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
            var rotazione = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);

            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.trackingSpace != null)
            {
                origine = rig.trackingSpace.TransformPoint(origine);
                rotazione = rig.trackingSpace.rotation * rotazione;
            }

            var direzione = rotazione * Vector3.forward;

            _origineRaggio = origine;
            _direzioneRaggio = direzione;

            OggettoInventario migliore = null;
            var minAngolo = m_aperturaGradi;
            var distMigliore = 0f;

            foreach (var o in m_inventario.Oggetti)
            {
                // Non si richiede la conferma piena del registro: qui basta che
                // l'oggetto sia stato visto un paio di volte. La stabilita
                // dell'identita viene comunque dal clustering.
                if (o.Osservazioni < m_osservazioniMinime) continue;

                var verso = o.Posizione - origine;
                var dist = verso.magnitude;
                if (dist > m_portata || dist < 0.05f) continue;

                var angolo = Vector3.Angle(direzione, verso);
                if (angolo < minAngolo)
                {
                    minAngolo = angolo;
                    migliore = o;
                    distMigliore = dist;
                }
            }

            // Isteresi. Senza di questa, due oggetti quasi equidistanti dal raggio
            // si scambiano il ruolo di bersaglio a ogni frame: ogni scambio conta
            // come "bersaglio nuovo", quindi l'annuncio riparte e il nome viene
            // troncato — lo stesso sintomo di prima, con un'altra causa.
            if (migliore != Puntato)
            {
                var vecchioAncoraValido = Puntato != null && ContieneAncora(Puntato);

                if (vecchioAncoraValido && migliore != null)
                {
                    var angoloVecchio = AngoloVerso(Puntato, origine, direzione);

                    if (angoloVecchio <= m_aperturaGradi && minAngolo > angoloVecchio - m_margineCambio)
                    {
                        // Il nuovo non e abbastanza meglio: si resta dov'e.
                        _candidato = null;
                        DistanzaPuntato = Vector3.Distance(origine, Puntato.Posizione);
                        AggiornaStato(AngoloVerso(Puntato, origine, direzione));
                        return;
                    }
                }

                if (migliore != _candidato)
                {
                    _candidato = migliore;
                    _candidatoDa = Time.time;
                }

                if (migliore != null && Time.time - _candidatoDa < m_attesaCambio)
                {
                    // Candidato promettente ma non ancora stabile: si attende.
                    if (vecchioAncoraValido)
                    {
                        DistanzaPuntato = Vector3.Distance(origine, Puntato.Posizione);
                        AggiornaStato(AngoloVerso(Puntato, origine, direzione));
                        return;
                    }
                }
            }

            _candidato = null;
            Puntato = migliore;
            DistanzaPuntato = distMigliore;

            AggiornaStato(minAngolo);
        }

        /// <summary>
        /// Un raggio solo, lungo la stessa direzione che l'utente sta puntando.
        /// Non sostituisce il ventaglio: il ventaglio non puo essere fine (15
        /// raggi che coprono 50 gradi non distinguono uno stipite) e questo non
        /// puo essere affidabile da solo (passa in mezzo alle gambe di un tavolo).
        /// Uno scandaglia il cammino, l'altro lo si punta dove si vuole guardare.
        /// </summary>
        private void SondaSuperficie()
        {
            DistanzaSuperficie = float.PositiveInfinity;

            if (!m_sondaSuperficie || m_raycast == null) return;
            if (!EnvironmentRaycastManager.IsSupported) return;
            if (_direzioneRaggio.sqrMagnitude < 0.0001f) return;

            // EnvironmentRaycastHit non porta una distanza: espone il punto di
            // intersezione in coordinate mondo, e la distanza va misurata.
            if (m_raycast.Raycast(new Ray(_origineRaggio, _direzioneRaggio),
                                  out var hit, m_portataSonda))
                DistanzaSuperficie = Vector3.Distance(_origineRaggio, hit.point);
        }

        /// <summary>
        /// La distanza che va al tatto. La superficie ha la precedenza sull'oggetto
        /// riconosciuto, perche e cio che ti fermerebbe davvero; l'oggetto vale
        /// solo quando la profondita non vede niente (capita con superfici scure,
        /// riflettenti o molto vicine).
        /// </summary>
        private float DistanzaTattile()
        {
            if (SuperficieColpita) return DistanzaSuperficie;
            if (Puntato != null && DistanzaPuntato <= m_portataSonda) return DistanzaPuntato;
            return float.PositiveInfinity;
        }

        private bool ContieneAncora(OggettoInventario o)
        {
            foreach (var x in m_inventario.Oggetti) if (x == o) return true;
            return false;
        }

        private static float AngoloVerso(OggettoInventario o, Vector3 origine, Vector3 direzione) =>
            Vector3.Angle(direzione, o.Posizione - origine);

        private void AggiornaStato(float angolo)
        {
            var migliore = Puntato;

            var sonda = SuperficieColpita ? $"{DistanzaSuperficie:0.00} m" : "libero";

            Stato = migliore == null
                ? $"puntamento: nessun bersaglio | sonda: {sonda}"
                : $"punti: {EtichetteItaliane.Traduci(migliore.Classe)} a {DistanzaPuntato:0.0} m ({angolo:0}°) | sonda: {sonda}";
        }

        private OggettoInventario _annunciato;
        private string _distanzaAnnunciata = "";

        /// <summary>
        /// Annuncia il bersaglio, distinguendo due casi che prima trattavo allo
        /// stesso modo — ed era il difetto.
        ///
        /// Prima, a ogni cambio di fascia di distanza rifacevo l'annuncio completo
        /// INTERROMPENDO quello in corso. Muovendo la mano la distanza oscilla fra
        /// due fasce, quindi il nome veniva troncato e ripartito senza mai finire:
        /// si sentivano solo le parole cortissime, ed e la ragione per cui "mouse"
        /// passava e "telecomando" no.
        ///
        /// Ora il nome si dice quando cambia il bersaglio, e la sola distanza si
        /// aggiunge in coda quando cambia fascia, senza interrompere niente.
        /// </summary>
        private void Annuncia()
        {
            var voce = SintesiVocale.Istanza;
            if (voce == null) return;

            if (Puntato == null)
            {
                _annunciato = null;
                _distanzaAnnunciata = "";
                return;
            }

            var nome = EtichetteItaliane.Traduci(Puntato.Classe);
            var distanza = EtichetteItaliane.Distanza(DistanzaPuntato);

            if (Puntato != _annunciato)
            {
                // Bersaglio nuovo: interrompere e giusto, l'utente ha spostato la mano.
                voce.Annuncia(true, nome, distanza);
                _annunciato = Puntato;
                _distanzaAnnunciata = distanza;
                return;
            }

            if (distanza != _distanzaAnnunciata)
            {
                // Stesso oggetto, ci si e avvicinati: basta la distanza, in coda.
                voce.Annuncia(false, distanza);
                _distanzaAnnunciata = distanza;
            }
        }

        /// <summary>
        /// La vicinanza si comunica col RITMO dei battiti, non con l'intensita.
        /// L'intensita di una vibrazione si giudica male in assoluto — quanto e
        /// "forte"? — mentre la differenza fra battiti lenti e battiti rapidi si
        /// coglie senza bisogno di un riferimento.
        /// </summary>
        private void Vibra()
        {
            var distanza = DistanzaTattile();

            if (float.IsPositiveInfinity(distanza))
            {
                // Silenzio = spazio libero. E il segnale, non la sua mancanza.
                Ferma();
                return;
            }

            var t = Mathf.InverseLerp(m_distanzaMinima, m_portataSonda, distanza);
            t = Mathf.Pow(Mathf.Clamp01(t), m_curva);
            var intervallo = Mathf.Lerp(m_intervalloVicino, m_intervalloLontano, t);

            if (Time.time < _prossimoBattito) return;

            if (_battitoAttivo)
            {
                OVRInput.SetControllerVibration(0f, 0f, m_controllerVibrante);
                _battitoAttivo = false;
                _prossimoBattito = Time.time + intervallo;
            }
            else
            {
                OVRInput.SetControllerVibration(1f, m_intensitaBattito, m_controllerVibrante);
                _battitoAttivo = true;
                _prossimoBattito = Time.time + m_durataBattito;
            }
        }

        /// <summary>
        /// Disegna il raggio dal controller: bianco quando non punta nulla, verde
        /// quando ha agganciato un oggetto, e lungo esattamente fino al bersaglio.
        /// </summary>
        private void AggiornaRaggio()
        {
            if (m_raggio == null) return;

            var origine = _origineRaggio;
            var direzione = _direzioneRaggio;
            if (direzione.sqrMagnitude < 0.0001f) return;

            var lunghezza = SuperficieColpita
                ? DistanzaSuperficie
                : Puntato != null ? DistanzaPuntato : m_lunghezzaSenzaBersaglio;

            m_raggio.position = origine + direzione * (lunghezza * 0.5f);
            m_raggio.rotation = Quaternion.LookRotation(direzione);
            m_raggio.localScale = new Vector3(0.006f, 0.006f, lunghezza);

            if (m_rendererRaggio != null)
                m_rendererRaggio.material.color = Puntato != null
                    ? m_coloreBersaglio
                    : SuperficieColpita ? m_coloreSuperficie : m_coloreLibero;
        }

        private void Ferma()
        {
            if (!_battitoAttivo) return;
            OVRInput.SetControllerVibration(0f, 0f, m_controllerVibrante);
            _battitoAttivo = false;
        }

        private void OnDisable() => Ferma();
    }
}
