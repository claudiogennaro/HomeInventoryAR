// Il pannello resta fermo nella stanza e si sposta afferrandolo con il raggio,
// come le finestre di sistema del Quest.
//
// Perche sostituisce SeguiTesta sul pannello delle persone: un pannello che
// insegue la testa e comodo finche si guarda dritto, ma durante una prova la
// testa si gira di continuo verso le persone, e il pannello le insegue
// coprendole. Un pannello fermo si appoggia una volta sola dove non da
// fastidio — accanto al muro, sopra il divano — e li resta, esattamente come
// una finestra del sistema.
//
// SeguiTesta resta in uso per la finestra del nome, che e transitoria e deve
// comparire davanti a chi la ha chiesta.
//
// Un MonoBehaviour, un file: vedi la nota in BersaglioAR.cs.

using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public class PannelloAfferrabile : MonoBehaviour
    {
        [Header("Posa iniziale (una volta sola, all'avvio)")]
        [SerializeField] private float m_distanza = 0.78f;
        [SerializeField, Tooltip("Negativo = a sinistra.")] private float m_lato = -0.40f;
        [SerializeField] private float m_altezza = -0.02f;

        [Header("Presa")]
        [SerializeField, Tooltip("Distanza minima e massima a cui si puo portare il pannello, in metri.")]
        private float m_distanzaMinima = 0.35f;
        [SerializeField] private float m_distanzaMassima = 3f;
        [SerializeField, Tooltip("Metri al secondo con cui lo stick avvicina o allontana il pannello mentre lo tieni.")]
        private float m_velocitaStick = 1.2f;
        [SerializeField, Tooltip("Assegnato dal costruttore di scena.")]
        private PuntatoreAR m_puntatore;

        /// <summary>
        /// Vero mentre un pannello e in mano.
        ///
        /// Serve a chi legge lo stick per altro: mentre si trascina, lo stick
        /// avvicina e allontana il pannello e non deve anche far scorrere la
        /// lista sotto le dita. E statico perche la domanda e "c'e una presa in
        /// corso", non "chi la sta facendo".
        /// </summary>
        public static bool PresaInCorso { get; private set; }

        private Transform m_testa;
        private Transform m_mano;
        private BersaglioAR m_maniglia;
        private Image m_barra;
        private bool m_inPresa;
        private float m_distanzaPresa;
        private Vector3 m_scostamento;      // dal punto sul raggio al centro del pannello
        private bool m_piazzato;

        public void Collega(PuntatoreAR puntatore) => m_puntatore = puntatore;

        private void Start()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            m_testa = rig != null ? rig.centerEyeAnchor : Camera.main?.transform;
            if (m_puntatore == null) m_puntatore = FindAnyObjectByType<PuntatoreAR>();
        }

        private void Update()
        {
            if (m_testa == null) return;

            // La posa iniziale si prende al primo fotogramma utile e non in
            // Start: all'avvio il rig non ha ancora la posa della testa, e il
            // pannello finirebbe piantato nell'origine della scena.
            if (!m_piazzato)
            {
                if (m_testa.position.sqrMagnitude < 1e-6f && m_testa.forward.z > 0.999f) return;
                Ricentra();
                m_piazzato = true;
                CostruisciManiglia();
                return;
            }

            if (m_maniglia == null) CostruisciManiglia();
            if (FaceComandi.RicentraPremuto()) Ricentra();

            if (m_mano == null && m_puntatore != null) m_mano = m_puntatore.Mano;
            if (m_mano == null) return;

            if (!m_inPresa) ForsePrendi();
            else Trascina();
        }

        private void ForsePrendi()
        {
            if (m_puntatore == null || m_puntatore.Sotto != m_maniglia) return;
            if (!FaceComandi.ClickTenuto()) return;

            m_inPresa = true;
            PresaInCorso = true;

            // Si conserva la distanza a cui il pannello si trovava e lo
            // scostamento rispetto al punto sul raggio: cosi il pannello non
            // salta sotto il puntatore nel momento in cui lo afferri, ma resta
            // dove sta e si muove con la mano. E la differenza fra "afferrare" e
            // "teletrasportare".
            m_distanzaPresa = Mathf.Clamp(
                Vector3.Distance(m_mano.position, transform.position),
                m_distanzaMinima, m_distanzaMassima);
            m_scostamento = transform.position - PuntoSulRaggio();

            Colora(new Color(0.45f, 0.95f, 0.75f, 0.95f));
        }

        private void Trascina()
        {
            if (!FaceComandi.ClickTenuto())
            {
                m_inPresa = false;
                PresaInCorso = false;
                Colora(ColoreRiposo);
                return;
            }

            var stick = FaceComandi.StickVerticale();
            if (Mathf.Abs(stick) > 0.35f)
                m_distanzaPresa = Mathf.Clamp(
                    m_distanzaPresa + stick * m_velocitaStick * Time.deltaTime,
                    m_distanzaMinima, m_distanzaMassima);

            transform.position = PuntoSulRaggio() + m_scostamento;
            GuardaChiLoTiene();
        }

        private Vector3 PuntoSulRaggio() => m_mano.position + m_mano.forward * m_distanzaPresa;

        /// <summary>Riporta il pannello davanti a chi lo guarda, nella posa di partenza.</summary>
        public void Ricentra()
        {
            var avanti = m_testa.forward; avanti.y = 0f;
            if (avanti.sqrMagnitude < 1e-4f) avanti = m_testa.forward;
            avanti.Normalize();

            var destra = Vector3.Cross(Vector3.up, avanti).normalized;
            transform.position = m_testa.position + avanti * m_distanza
                               + destra * m_lato + Vector3.up * m_altezza;
            GuardaChiLoTiene();

            if (m_inPresa)
            {
                m_distanzaPresa = Mathf.Clamp(m_distanza, m_distanzaMinima, m_distanzaMassima);
                m_scostamento = Vector3.zero;
            }
        }

        /// <summary>
        /// Il pannello guarda sempre la testa, ma solo attorno all'asse
        /// verticale: inclinarlo per seguire uno sguardo basso lo farebbe
        /// sembrare storto rispetto alla stanza, e un pannello di testo storto si
        /// legge peggio di uno visto di sbieco.
        /// </summary>
        private void GuardaChiLoTiene()
        {
            var verso = transform.position - m_testa.position;
            verso.y = 0f;
            if (verso.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(verso.normalized, Vector3.up);
        }

        private static Color ColoreRiposo => new Color(1f, 1f, 1f, 0.22f);

        /// <summary>
        /// La barra che si afferra, sopra il pannello.
        ///
        /// Si costruisce dopo il pannello e non insieme: la sua larghezza e la
        /// sua altezza vengono dal canvas che PeoplePanelController ha appena
        /// creato, e quel canvas nasce nel proprio Start. Scriverla a misura
        /// fissa vorrebbe dire ritrovarsela scollata dal bordo ogni volta che il
        /// pannello cambia contenuto.
        /// </summary>
        private void CostruisciManiglia()
        {
            Canvas canvas = null;
            foreach (var c in GetComponentsInChildren<Canvas>())
            {
                canvas = c;
                break;
            }
            if (canvas == null) return;

            var rt = (RectTransform)canvas.transform;
            var dim = rt.rect.size;
            const float altezzaBarra = 34f;

            var go = new GameObject("BarraDiPresa", typeof(RectTransform));
            go.transform.SetParent(rt, false);
            var barraRt = (RectTransform)go.transform;
            barraRt.anchorMin = barraRt.anchorMax = new Vector2(0.5f, 1f);
            barraRt.pivot = new Vector2(0.5f, 0f);
            barraRt.sizeDelta = new Vector2(dim.x, altezzaBarra);
            barraRt.anchoredPosition = new Vector2(0f, 6f);

            m_barra = go.AddComponent<Image>();
            m_barra.color = ColoreRiposo;
            UIFacile.Collisore(barraRt);

            var presa = UIFacile.Testo("Etichetta", barraRt, "≡  trascina", 18, TextAnchor.MiddleCenter);
            presa.color = new Color(1f, 1f, 1f, 0.7f);
            UIFacile.Stendi(presa.rectTransform);

            m_maniglia = go.AddComponent<BersaglioAR>();
            m_maniglia.Evidenzia = acceso =>
            {
                if (m_inPresa) return;
                Colora(acceso ? new Color(0.45f, 0.72f, 1f, 0.55f) : ColoreRiposo);
            };
        }

        private void Colora(Color c)
        {
            if (m_barra != null) m_barra.color = c;
        }
    }
}
