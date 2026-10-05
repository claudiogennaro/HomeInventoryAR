using System.Collections.Generic;
using UnityEngine;

namespace InventarioAR
{
    /// <summary>
    /// La funzione per cui l'app esiste: scegli un oggetto e qualcosa ti porta li.
    ///
    /// La selezione scorre con il tasto X e si annulla con Y (controller sinistro):
    /// A e B sono gia usati dal codice del sample. Scorrere una lista con un tasto
    /// e piu grezzo di puntare e cliccare, ma non richiede un sistema di puntamento
    /// e con una manciata di oggetti funziona benissimo.
    /// </summary>
    public class NavigazioneInventario : MonoBehaviour
    {
        [SerializeField] private InventarioManager m_inventario;
        [SerializeField] private Transform m_freccia;
        [Tooltip("Tutti i pezzi della freccia: vanno colorati insieme, altrimenti " +
                 "il segnale arancione/verde si legge come una freccia bicolore.")]
        [SerializeField] private Renderer[] m_pezziFreccia;

        [Header("Posizione della freccia rispetto alla testa")]
        [SerializeField] private float m_distanza = 0.55f;
        [SerializeField] private float m_scostamentoVerticale = -0.18f;

        [Tooltip("Sotto questa distanza consideriamo che l'utente sia arrivato.")]
        [SerializeField] private float m_raggioArrivo = 1.2f;

        [Header("Uscita")]
        [Tooltip("Secondi di pressione continua su B per chiudere l'app. " +
                 "Tenuto premuto e non un tocco: B fa gia altro nel codice del sample, " +
                 "e un'uscita accidentale a meta inventario sarebbe seccante.")]
        [SerializeField] private float m_tempoUscita = 1.5f;

        [Tooltip("Secondi di pressione continua sullo stick per azzerare l'inventario. " +
                 "Piu lungo dell'uscita, perche l'azzeramento e irreversibile.")]
        [SerializeField] private float m_tempoAzzeramento = 3f;

        [SerializeField] private Color m_coloreLontano = new(0.95f, 0.62f, 0.12f);
        [SerializeField] private Color m_coloreArrivato = new(0.30f, 0.80f, 0.45f);

        private readonly List<OggettoInventario> _selezionabili = new();
        private int _indice = -1;

        public OggettoInventario Selezionato { get; private set; }
        public float DistanzaDalBersaglio { get; private set; }
        public bool Arrivato => Selezionato != null && DistanzaDalBersaglio <= m_raggioArrivo;

        /// <summary>Da 0 a 1 mentre si tiene premuto B. Serve al pannello per il conto alla rovescia.</summary>
        public float ProgressoUscita { get; private set; }

        /// <summary>Da 0 a 1 mentre si tiene premuto lo stick.</summary>
        public float ProgressoAzzeramento { get; private set; }

        /// <summary>Emesso quando l'utente ha confermato l'azzeramento.</summary>
        public event System.Action AzzeramentoRichiesto;

        private float _premutoDa = -1f;
        private float _stickDa = -1f;

        private void Update()
        {
            if (Comandi.ScorriPremuto()) Scorri();
            if (Comandi.AnnullaPremuto()) Annulla();

            GestisciUscita();
            GestisciAzzeramento();

            AggiornaFreccia();
        }

        private void GestisciUscita()
        {
            if (Comandi.UscitaTenuta())
            {
                if (_premutoDa < 0f) _premutoDa = Time.time;

                ProgressoUscita = Mathf.Clamp01((Time.time - _premutoDa) / m_tempoUscita);

                if (ProgressoUscita >= 1f)
                {
                    // OnApplicationPause/OnDisable fanno partire il salvataggio
                    // dell'inventario prima che il processo termini.
                    Debug.Log("[Navigazione] Uscita richiesta dall'utente.");
                    Application.Quit();
                }
            }
            else
            {
                _premutoDa = -1f;
                ProgressoUscita = 0f;
            }
        }

        /// <summary>
        /// Azzeramento tenendo premuto lo stick per tre secondi. Un click breve
        /// resta "annulla selezione": la pressione prolungata e la versione
        /// impegnativa dello stesso gesto, e tre secondi sono piu dell'uscita
        /// perche cancellare l'inventario non si annulla.
        /// </summary>
        private void GestisciAzzeramento()
        {
            if (Comandi.AzzeramentoTenuto())
            {
                if (_stickDa < 0f) _stickDa = Time.time;
                ProgressoAzzeramento = Mathf.Clamp01((Time.time - _stickDa) / m_tempoAzzeramento);

                if (ProgressoAzzeramento >= 1f)
                {
                    Debug.Log("[Navigazione] Azzeramento inventario richiesto dall'utente.");
                    Annulla();
                    AzzeramentoRichiesto?.Invoke();
                    _stickDa = Time.time + 1f;   // evita di riazzerare subito
                    ProgressoAzzeramento = 0f;
                }
            }
            else
            {
                _stickDa = -1f;
                ProgressoAzzeramento = 0f;
            }
        }

        private void Scorri()
        {
            // La lista si ricostruisce a ogni pressione: l'inventario cambia sotto
            // di noi mentre si cammina, e un indice memorizzato punterebbe altrove.
            _selezionabili.Clear();
            foreach (var o in m_inventario.Oggetti)
                if (o.Confermato) _selezionabili.Add(o);

            if (_selezionabili.Count == 0)
            {
                Annulla();
                return;
            }

            // Riprende dall'oggetto attuale, cosi premere X due volte avanza di due
            // invece di ripartire da capo.
            var partenza = Selezionato != null ? _selezionabili.IndexOf(Selezionato) : -1;
            _indice = (partenza + 1) % _selezionabili.Count;
            Selezionato = _selezionabili[_indice];

            Debug.Log($"[Navigazione] Selezionato {Selezionato.Classe} ({_indice + 1}/{_selezionabili.Count})");
        }

        private void Annulla()
        {
            Selezionato = null;
            _indice = -1;
        }

        private void AggiornaFreccia()
        {
            if (m_freccia == null) return;

            var testa = TrovaTesta();

            if (Selezionato == null || testa == null)
            {
                if (m_freccia.gameObject.activeSelf) m_freccia.gameObject.SetActive(false);
                return;
            }

            if (!m_freccia.gameObject.activeSelf) m_freccia.gameObject.SetActive(true);

            DistanzaDalBersaglio = Vector3.Distance(testa.position, Selezionato.Posizione);

            // La freccia sta davanti all'utente e ruota verso il bersaglio: e una
            // bussola, non un oggetto piazzato nel mondo. Cosi resta visibile anche
            // quando il bersaglio e dietro le spalle.
            var avanti = testa.forward;
            avanti.y = 0f;
            if (avanti.sqrMagnitude < 0.0001f) avanti = Vector3.forward;
            avanti.Normalize();

            m_freccia.position = testa.position + avanti * m_distanza + Vector3.up * m_scostamentoVerticale;

            var verso = Selezionato.Posizione - m_freccia.position;
            if (verso.sqrMagnitude > 0.0001f)
                m_freccia.rotation = Quaternion.LookRotation(verso.normalized, Vector3.up);

            var colore = Arrivato ? m_coloreArrivato : m_coloreLontano;
            foreach (var r in m_pezziFreccia)
                if (r != null) r.material.color = colore;
        }

        private static Transform TrovaTesta()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
