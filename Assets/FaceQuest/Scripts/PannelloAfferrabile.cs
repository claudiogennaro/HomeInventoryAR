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
// Un pannello fermo, pero, si perde: basta voltarsi e camminare e resta lontano
// e minuscolo. Per questo ogni tanto torna da solo: se resta FUORI dalla zona
// comoda (lontano dallo sguardo o troppo distante) per piu di qualche secondo,
// scivola con calma nella posa di partenza. Non insegue la testa a ogni
// movimento — sarebbe di nuovo il pannello che copre le persone — e dopo che
// lo si e spostato a mano lo si lascia in pace per un po'.
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

        [Header("Ritorno davanti agli occhi")]
        [SerializeField, Tooltip("Se acceso, il pannello torna da solo quando resta fuori dalla zona comoda.")]
        private bool m_tornaDaSolo = true;
        [SerializeField, Tooltip("Oltre quest'angolo (gradi) dalla direzione dello sguardo il pannello e considerato perso.")]
        private float m_angoloMassimo = 50f;
        [SerializeField, Tooltip("Oltre questa distanza (metri) il pannello e troppo lontano e piccolo per leggersi.")]
        private float m_distanzaPerso = 1.4f;
        [SerializeField, Tooltip("Secondi che deve restare fuori zona prima di tornare: evita l'effetto pannello che perseguita.")]
        private float m_attesaRitorno = 2.5f;
        [SerializeField, Tooltip("Dopo averlo spostato a mano resta dov'e per questi secondi.")]
        private float m_pausaDopoPresa = 15f;
        [SerializeField, Tooltip("Durata (circa) dello scivolamento verso la posa di partenza.")]
        private float m_durataRitorno = 0.7f;

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
        private float m_fuoriDa;
        private bool m_scivola;
        private Vector3 m_velocita;
        private float m_nonPrimaDi;

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
            if (FaceComandi.RicentraPremuto())
            {
                Ricentra();
                m_scivola = false;
                m_fuoriDa = 0f;
            }

            if (!m_inPresa) TornaConCalma();

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
            m_scivola = false;
            m_fuoriDa = 0f;

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
                m_nonPrimaDi = Time.time + m_pausaDopoPresa;
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

        /// <summary>Dove sta la posa di partenza, rispetto alla testa di adesso.</summary>
        private Vector3 PosaDiPartenza()
        {
            var avanti = m_testa.forward; avanti.y = 0f;
            if (avanti.sqrMagnitude < 1e-4f) avanti = m_testa.forward;
            avanti.Normalize();

            var destra = Vector3.Cross(Vector3.up, avanti).normalized;
            return m_testa.position + avanti * m_distanza
                 + destra * m_lato + Vector3.up * m_altezza;
        }

        /// <summary>Riporta il pannello davanti a chi lo guarda, nella posa di partenza.</summary>
        public void Ricentra()
        {
            transform.position = PosaDiPartenza();
            GuardaChiLoTiene();

            if (m_inPresa)
            {
                m_distanzaPresa = Mathf.Clamp(m_distanza, m_distanzaMinima, m_distanzaMassima);
                m_scostamento = Vector3.zero;
            }
        }

        /// <summary>
        /// Il ritorno "ogni tanto". Conta quanto il pannello resta fuori zona; solo
        /// dopo m_attesaRitorno secondi di fila parte uno scivolamento morbido
        /// verso la posa di partenza. Basta riguardarlo per azzerare il conto,
        /// quindi chi lo sta usando non lo vede mai muoversi.
        /// </summary>
        private void TornaConCalma()
        {
            if (!m_tornaDaSolo) return;

            if (Time.time < m_nonPrimaDi)
            {
                m_fuoriDa = 0f;
                m_scivola = false;
                return;
            }

            if (!m_scivola)
            {
                var verso = transform.position - m_testa.position;
                var fuori = verso.magnitude > m_distanzaPerso
                            || Vector3.Angle(m_testa.forward, verso) > m_angoloMassimo;
                m_fuoriDa = fuori ? m_fuoriDa + Time.deltaTime : 0f;
                if (m_fuoriDa < m_attesaRitorno) return;

                m_scivola = true;
                m_velocita = Vector3.zero;
            }

            var meta = PosaDiPartenza();
            transform.position = Vector3.SmoothDamp(transform.position, meta, ref m_velocita,
                Mathf.Max(0.1f, m_durataRitorno));
            GuardaChiLoTiene();

            if ((meta - transform.position).sqrMagnitude < 0.0004f)
            {
                m_scivola = false;
                m_fuoriDa = 0f;
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

            var presa = UIFacile.Testo("Etichetta", barraRt, "≡  drag", 18, TextAnchor.MiddleCenter);
            presa.color = new Color(1f, 1f, 1f, 0.7f);
            UIFacile.Stendi(presa.rectTransform);

            CostruisciLoghi(rt, dim.x, 6f + altezzaBarra + 4f);

            m_maniglia = go.AddComponent<BersaglioAR>();
            m_maniglia.Evidenzia = acceso =>
            {
                if (m_inPresa) return;
                Colora(acceso ? new Color(0.45f, 0.72f, 1f, 0.55f) : ColoreRiposo);
            };
        }

        /// <summary>
        /// Il logo CNR sopra la barra di presa.
        ///
        /// Stanno su una targhetta chiara, non direttamente sul fondo scuro del
        /// pannello: sono blu scuro e sul nero del pannello, o sul passthrough,
        /// non si leggerebbero. I file stanno in Resources, cosi finiscono sempre
        /// nella build; se mancano la targhetta semplicemente non compare.
        /// </summary>
        private static void CostruisciLoghi(RectTransform pannello, float larghezza, float sopra)
        {
            var cnr = Resources.Load<Texture2D>("LogoCNR");
            if (cnr == null) return;

            const float altezzaTarga = 120f;
            var targa = UIFacile.Immagine("Loghi", pannello, new Color(1f, 1f, 1f, 0.94f));
            var tRt = targa.rectTransform;
            tRt.anchorMin = tRt.anchorMax = new Vector2(0.5f, 1f);
            tRt.pivot = new Vector2(0.5f, 0f);
            tRt.sizeDelta = new Vector2(larghezza, altezzaTarga);
            tRt.anchoredPosition = new Vector2(0f, sopra);
            targa.raycastTarget = false;

            // Un solo logo, al centro: con anche quello dell'ISTI i due non
            // stavano nella larghezza del pannello con una dimensione leggibile.
            AggiungiLogo(tRt, cnr, "CNR", 0f, 106f);
        }

        private static void AggiungiLogo(RectTransform targa, Texture2D tex, string nome, float x, float altezza)
        {
            if (tex == null) return;
            var raw = UIFacile.Raw("Logo" + nome, targa);
            raw.texture = tex;
            raw.raycastTarget = false;
            var rt = raw.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(altezza * tex.width / tex.height, altezza);
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        private void Colora(Color c)
        {
            if (m_barra != null) m_barra.color = c;
        }
    }
}
