using Meta.XR;
using UnityEngine;

namespace Accessibilita
{
    /// <summary>
    /// Il "bastone bianco" dell'app: sonda l'ambiente davanti all'utente con un
    /// ventaglio di raggi e segnala l'ostacolo piu vicino, con vibrazione continua
    /// sul controller sinistro e un avviso vocale quando si fa vicino.
    ///
    /// Perche la mappa di profondita e non la MRUK Scene: la Scene richiede che
    /// l'utente abbia scansionato la stanza, e restituisce una geometria statica —
    /// non vede una sedia spostata cinque minuti fa, ne una persona. La profondita
    /// e quello che c'e davvero adesso, e non richiede alcuna preparazione.
    ///
    /// Perche un ventaglio e non un raggio solo: un singolo raggio passa a lato di
    /// uno stipite o fra le gambe di un tavolo e dichiara via libera. Sondare un
    /// settore e cio che rende il segnale affidabile abbastanza da fidarsene.
    ///
    /// Divisione del lavoro: la mano destra esplora e punta cio che sta davanti
    /// all'altezza del busto, questo ventaglio guarda IN BASSO — gambe, piedi,
    /// gradini, sgabelli, cose che un raggio all'altezza degli occhi non vede mai
    /// e contro cui si inciampa davvero.
    ///
    /// Il controller non si tiene in mano: si aggancia alla cintura. Cosi la mano
    /// sinistra resta libera, il sensore sta gia all'altezza giusta per sondare il
    /// pavimento davanti, e soprattutto segue il BUSTO invece della testa — che
    /// mentre si cammina gira a guardarsi intorno, e un allarme che ruota con lo
    /// sguardo direbbe bugie sulla direzione di marcia.
    ///
    /// Il pavimento va escluso esplicitamente, altrimenti e lui l'ostacolo piu
    /// vicino a ogni frame e l'avviso suonerebbe di continuo. Con l'origine di
    /// tracciamento a livello del pavimento, un colpo sotto qualche centimetro di
    /// quota e terreno, non ostacolo.
    /// </summary>
    public class RilevaOstacoli : MonoBehaviour
    {
        [SerializeField] private EnvironmentRaycastManager m_raycast;

        [Header("Ventaglio di sondaggio")]
        [Tooltip("Apertura totale del ventaglio in orizzontale, in gradi.")]
        [SerializeField] private float m_apertura = 50f;

        [Tooltip("Quanti raggi in orizzontale. Dispari, cosi uno punta dritto avanti.")]
        [SerializeField] private int m_raggiOrizzontali = 5;

        [Tooltip("Di quanti gradi ogni raggio punta VERSO IL BASSO. Sempre positivi: " +
                 "questo ventaglio guarda solo in giu. Dalla cintura, 5 coglie uno " +
                 "spigolo di tavolo e 50 il pavimento a meno di un metro, cioe il " +
                 "gradino sul quale stai per salire.")]
        [SerializeField] private float[] m_inclinazioniGiu = { 5f, 20f, 35f, 50f };

        [Tooltip("Colpi sotto questa quota (m dal pavimento) sono terreno, non ostacoli. " +
                 "Senza questa soglia il pavimento sarebbe l'ostacolo piu vicino sempre.")]
        [SerializeField] private float m_quotaPavimento = 0.12f;

        [Tooltip("Origine del ventaglio: il controller agganciato alla cintura. Se non " +
                 "e connesso si ripiega sulla testa, cosi l'app funziona comunque.")]
        [SerializeField] private bool m_origineDalController = true;

        [Tooltip("Distanza massima sondata, in metri.")]
        [SerializeField] private float m_portata = 3f;

        [Header("Soglie")]
        [Tooltip("Sotto questa distanza (m) scatta l'avviso vocale.")]
        [SerializeField] private float m_distanzaAvviso = 1.2f;

        [Tooltip("Secondi minimi fra due avvisi vocali, per non diventare assillante.")]
        [SerializeField] private float m_intervalloAvviso = 4f;

        [Header("Vibrazione (mano sinistra)")]
        [SerializeField] private OVRInput.Controller m_controller = OVRInput.Controller.LTouch;
        [SerializeField] private float m_intervalloVicino = 0.025f;
        [SerializeField] private float m_intervalloLontano = 0.8f;
        [SerializeField] private float m_durataBattito = 0.02f;

        [Tooltip("Esponente della mappatura distanza-ritmo. Sotto 1 il ritmo cambia " +
                 "molto da vicino e poco da lontano: e li che serve discriminare.")]
        [SerializeField] private float m_curva = 0.6f;
        [SerializeField] private float m_intensita = 0.8f;
        [SerializeField] private float m_distanzaMinima = 0.5f;

        [Header("Debug")]
        [Tooltip("Disegna i raggi sondati. Inutile all'utente, prezioso a chi sviluppa.")]
        [SerializeField] private bool m_mostraRaggi = true;
        [SerializeField] private Material m_materialeRaggi;

        public float DistanzaOstacolo { get; private set; } = float.PositiveInfinity;
        public bool Ostacolo => !float.IsPositiveInfinity(DistanzaOstacolo);
        public string Stato { get; private set; } = "ostacoli: sondaggio non attivo";

        private float _prossimoBattito;
        private bool _battitoAttivo;
        private float _ultimoAvviso = -99f;
        private LineRenderer[] _linee;
        private int _colpiti;

        private void Start()
        {
            if (m_raycast == null) m_raycast = FindAnyObjectByType<EnvironmentRaycastManager>();
            PreparaLinee();
        }

        private void Update()
        {
            Sonda();
            Avvisa();
            Vibra();
        }

        private void Sonda()
        {
            var testa = TrovaTesta();

            if (testa == null || m_raycast == null || !EnvironmentRaycastManager.IsSupported)
            {
                DistanzaOstacolo = float.PositiveInfinity;
                Stato = "ostacoli: profondita non disponibile";
                return;
            }

            if (!TrovaOrigine(testa, out var origine, out var avanti))
            {
                DistanzaOstacolo = float.PositiveInfinity;
                return;
            }

            var minima = float.PositiveInfinity;
            _colpiti = 0;
            var indice = 0;
            var pavimento = 0;

            foreach (var inclinazione in m_inclinazioniGiu)
            {
                // L'abbassamento si costruisce con le componenti, non ruotando attorno
                // all'asse trasversale: quale segno di rotazione sia "giu" dipende dalla
                // convenzione di Unity, e mi ha gia fatto puntare l'intero ventaglio
                // verso il soffitto. Vector3.down non e ambiguo.
                var giu = Mathf.Sin(inclinazione * Mathf.Deg2Rad);
                var avantiScala = Mathf.Cos(inclinazione * Mathf.Deg2Rad);

                for (var i = 0; i < m_raggiOrizzontali; i++)
                {
                    var t = m_raggiOrizzontali == 1 ? 0.5f : i / (float)(m_raggiOrizzontali - 1);
                    var azimut = Mathf.Lerp(-m_apertura * 0.5f, m_apertura * 0.5f, t);

                    var orizzontale = Quaternion.AngleAxis(azimut, Vector3.up) * avanti;
                    var direzione = (orizzontale * avantiScala + Vector3.down * giu).normalized;

                    var fine = origine + direzione * m_portata;

                    if (m_raycast.Raycast(new Ray(origine, direzione), out var hit, m_portata))
                    {
                        fine = hit.point;

                        if (hit.point.y < m_quotaPavimento)
                        {
                            // Terreno: utile da disegnare, da non contare come ostacolo.
                            pavimento++;
                        }
                        else
                        {
                            // Distanza ORIZZONTALE: quel che conta e fra quanti passi
                            // ci arrivi, non quanto e lunga la diagonale fino ai piedi.
                            var piano = hit.point - origine;
                            piano.y = 0f;
                            var d = piano.magnitude;

                            if (d < minima) minima = d;
                            _colpiti++;
                        }
                    }

                    DisegnaLinea(indice++, origine, fine);
                }
            }

            DistanzaOstacolo = minima;

            Stato = float.IsPositiveInfinity(minima)
                ? $"gambe: via libera ({indice} raggi, {pavimento} a terra)"
                : $"gambe: {minima:0.0} m ({_colpiti}/{indice} raggi, {pavimento} a terra)";
        }

        /// <summary>
        /// Origine e direzione del ventaglio. Col controller alla cintura entrambe
        /// vengono da li: la posizione e gia all'altezza giusta, e la direzione
        /// segue il busto anziche lo sguardo.
        ///
        /// Del controller si prende pero solo il suo "avanti" proiettato
        /// sull'orizzontale, mai l'inclinazione: come e agganciato alla cintura
        /// dipende da come lo si e infilato, e non deve cambiare dove guarda il
        /// ventaglio. Le inclinazioni le decide il codice.
        /// </summary>
        private bool TrovaOrigine(Transform testa, out Vector3 origine, out Vector3 avanti)
        {
            origine = testa.position;
            avanti = testa.forward;

            if (m_origineDalController &&
                (OVRInput.GetConnectedControllers() & m_controller) != 0)
            {
                var pos = OVRInput.GetLocalControllerPosition(m_controller);
                var rot = OVRInput.GetLocalControllerRotation(m_controller);

                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null && rig.trackingSpace != null)
                {
                    pos = rig.trackingSpace.TransformPoint(pos);
                    rot = rig.trackingSpace.rotation * rot;
                }

                origine = pos;

                var avantiControl = rot * Vector3.forward;
                avantiControl.y = 0f;

                // Se il controller punta quasi a picco, il suo "avanti" orizzontale
                // e rumore: meglio la testa che una direzione inventata.
                if (avantiControl.sqrMagnitude > 0.04f) avanti = avantiControl;
            }

            avanti.y = 0f;
            if (avanti.sqrMagnitude < 0.0001f) return false;

            avanti.Normalize();
            return true;
        }

        /// <summary>
        /// Avvisa a voce solo quando l'ostacolo e vicino, e non piu di una volta ogni
        /// pochi secondi. Un avviso continuo verrebbe ignorato come il rumore di
        /// fondo che sarebbe — e coprirebbe l'annuncio degli oggetti puntati, che e
        /// l'altro canale e non deve essere soffocato.
        /// </summary>
        private void Avvisa()
        {
            if (!Ostacolo || DistanzaOstacolo > m_distanzaAvviso) return;
            if (Time.time - _ultimoAvviso < m_intervalloAvviso) return;

            _ultimoAvviso = Time.time;
            SintesiVocale.Istanza?.Annuncia(false, "attenzione");
        }

        private void Vibra()
        {
            if (!Ostacolo || DistanzaOstacolo > m_portata)
            {
                Ferma();
                return;
            }

            var t = Mathf.InverseLerp(m_distanzaMinima, m_portata, DistanzaOstacolo);
            t = Mathf.Pow(Mathf.Clamp01(t), m_curva);
            var intervallo = Mathf.Lerp(m_intervalloVicino, m_intervalloLontano, t);

            if (Time.time < _prossimoBattito) return;

            if (_battitoAttivo)
            {
                OVRInput.SetControllerVibration(0f, 0f, m_controller);
                _battitoAttivo = false;
                _prossimoBattito = Time.time + intervallo;
            }
            else
            {
                OVRInput.SetControllerVibration(1f, m_intensita, m_controller);
                _battitoAttivo = true;
                _prossimoBattito = Time.time + m_durataBattito;
            }
        }

        private void Ferma()
        {
            if (!_battitoAttivo) return;
            OVRInput.SetControllerVibration(0f, 0f, m_controller);
            _battitoAttivo = false;
        }

        private void OnDisable() => Ferma();

        // ---------- visualizzazione di servizio ----------

        private void PreparaLinee()
        {
            if (!m_mostraRaggi) return;

            var quante = m_raggiOrizzontali * (m_inclinazioniGiu?.Length ?? 0);
            _linee = new LineRenderer[quante];

            for (var i = 0; i < quante; i++)
            {
                var go = new GameObject($"RaggioSonda{i}");
                go.transform.SetParent(transform, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = lr.endWidth = 0.004f;
                lr.useWorldSpace = true;
                if (m_materialeRaggi != null) lr.material = m_materialeRaggi;
                lr.startColor = lr.endColor = new Color(0.2f, 0.7f, 1f, 0.5f);

                _linee[i] = lr;
            }
        }

        private void DisegnaLinea(int indice, Vector3 da, Vector3 a)
        {
            if (_linee == null || indice >= _linee.Length) return;
            _linee[indice].SetPosition(0, da);
            _linee[indice].SetPosition(1, a);
        }

        private static Transform TrovaTesta()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
