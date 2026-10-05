// Il pannello "People" alla sinistra del campo visivo.
//
// Elenca le persone incontrate nella sessione, la piu recente in cima. Ogni
// riga mostra il ritaglio del volto, l'identificativo, il nome se assegnato e
// lo stato dell'identita.
//
// Nel pannello NON ci sono bottoni. I comandi stanno sui tasti del controller
// (A riconoscimento, Y cancella, B tenuto esce, stick per scorrere): puntare un
// bottone sospeso in aria e piu lento e meno preciso che premere un tasto, e il
// pannello sta di lato, spesso fuori dal campo visivo. Il puntatore serve solo
// dove non c'e alternativa: scegliere una persona e scriverne il nome.
//
// Il layout e calcolato, non scritto a mano: l'altezza del pannello viene dalla
// somma dei suoi pezzi. Con le coordinate a costanti fisse gli elementi
// finivano oltre il bordo inferiore e si sovrapponevano fra loro.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public class PeoplePanelController : MonoBehaviour
    {
        [SerializeField] private PersonDatabase m_database;
        [SerializeField] private PersonEditorUI m_editor;
        [SerializeField] private FaceQuestManager m_manager;
        [SerializeField] private int m_righeVisibili = 4;
        [SerializeField, Tooltip("Mostra in fondo al pannello cio che le due reti ricevono davvero.")]
        private bool m_anteprimePipeline = true;

        private const float Larghezza = 470f;
        private const float AltezzaRiga = 82f;
        private const float Scala = 0.00075f;
        private const float Margine = 12f;

        private class Riga
        {
            public GameObject Go;
            public RawImage Volto;
            public Text Nome;
            public Text Dettaglio;
            public Persona Persona;
        }

        private readonly List<Riga> m_righe = new List<Riga>();
        private readonly List<Persona> m_ordinate = new List<Persona>();
        private Text m_titolo;
        private Text m_statoRiconoscimento;
        private Text m_diagnostica;
        private RawImage m_anteprimaIngresso;
        private RawImage m_anteprimaVolto;
        private Transform m_contenitore;
        private int m_primaRiga;
        private bool m_daRifare = true;
        private float m_prossimoRefresh;

        // Scorrimento con lo stick: una pressione, una riga; tenendolo, ripete.
        private bool m_stickAttivo;
        private float m_prossimoPasso;

        public void Collega(PersonDatabase db, PersonEditorUI editor, FaceQuestManager manager)
        {
            m_database = db; m_editor = editor; m_manager = manager;
        }

        private void Start()
        {
            Costruisci();
            if (m_database != null) m_database.Cambiato += () => m_daRifare = true;
            Aggiorna();
        }

        private void Update()
        {
            LeggiStick();

            if (!m_daRifare || Time.time < m_prossimoRefresh) return;
            m_prossimoRefresh = Time.time + 0.25f;
            m_daRifare = false;
            Aggiorna();
        }

        /// <summary>
        /// Scorrimento con lo stick, con una prima pressione immediata e poi una
        /// ripetizione piu rapida: e il comportamento che tutti si aspettano da
        /// un tasto tenuto premuto, e senza la pausa iniziale una sola spinta
        /// farebbe scorrere mezza lista.
        /// </summary>
        private void LeggiStick()
        {
            // Mentre il pannello e in mano lo stick serve ad avvicinarlo o
            // allontanarlo: se scorresse anche la lista, spostare il pannello
            // farebbe scappare via le righe sotto le dita.
            if (PannelloAfferrabile.PresaInCorso)
            {
                m_stickAttivo = false;
                return;
            }

            var y = FaceComandi.StickVerticale();

            if (Mathf.Abs(y) < 0.35f)
            {
                m_stickAttivo = false;
                return;
            }

            var passo = y > 0f ? -1 : 1;   // stick in su = risali la lista

            if (!m_stickAttivo)
            {
                m_stickAttivo = true;
                m_prossimoPasso = Time.time + 0.45f;
                Scorri(passo);
            }
            else if (Time.time >= m_prossimoPasso)
            {
                m_prossimoPasso = Time.time + 0.15f;
                Scorri(passo);
            }
        }

        /// <summary>La riga di diagnostica in fondo al pannello. La scrive il manager.</summary>
        public void ImpostaDiagnostica(string testo)
        {
            if (m_diagnostica != null) m_diagnostica.text = testo;
        }

        /// <summary>
        /// Le due anteprime in fondo al pannello: a sinistra cio che vede il
        /// detector, a destra l'ultimo volto allineato dato ad ArcFace.
        ///
        /// Sono le RenderTexture vere, non una copia: costa un'assegnazione e
        /// risponde in un'occhiata alla domanda che nessun numero risolve — se
        /// l'immagine in ingresso e nera, storta o capovolta, il problema e
        /// prima della rete, non nelle soglie.
        /// </summary>
        public void ImpostaAnteprime(Texture ingresso, Texture volto)
        {
            if (m_anteprimaIngresso != null)
            {
                m_anteprimaIngresso.texture = ingresso;
                m_anteprimaIngresso.enabled = ingresso != null;
            }
            if (m_anteprimaVolto != null)
            {
                m_anteprimaVolto.texture = volto;
                m_anteprimaVolto.enabled = volto != null;
            }
        }

        private void Costruisci()
        {
            var righe = Mathf.Max(1, m_righeVisibili);

            // Le altezze sono misurate sul testo che ci va davvero dentro: righe
            // lunghe andavano a capo e sbordavano sull'elemento sotto, e nel
            // visore si leggevano due frasi sovrapposte.
            const float hTitolo = 44f, hComandi = 80f, hNota = 32f, hDiag = 96f, hAnteprime = 104f;
            var yRighe = 14f + hTitolo + 8f;
            var yComandi = yRighe + righe * AltezzaRiga + 10f;
            var yNota = yComandi + hComandi + 6f;
            var yDiag = yNota + hNota + 4f;
            var yAnteprime = yDiag + hDiag + 6f;
            var altezza = (m_anteprimePipeline ? yAnteprime + hAnteprime : yDiag + hDiag) + 14f;

            var canvas = UIFacile.Pannello("PannelloPeople", transform,
                new Vector2(Larghezza, altezza), Scala, new Color(0f, 0f, 0f, 0.80f));
            var radice = canvas.transform;

            m_titolo = UIFacile.Testo("Titolo", radice, "People", 38);
            UIFacile.Posiziona(m_titolo.rectTransform, new Vector2(Margine + 4f, -14f), new Vector2(240f, hTitolo));

            m_statoRiconoscimento = UIFacile.Testo("Stato", radice, "", 24, TextAnchor.MiddleRight);
            UIFacile.Posiziona(m_statoRiconoscimento.rectTransform,
                new Vector2(Larghezza - 250f - Margine, -16f), new Vector2(250f, hTitolo));

            m_contenitore = new GameObject("Righe", typeof(RectTransform)).transform;
            m_contenitore.SetParent(radice, false);
            UIFacile.Posiziona((RectTransform)m_contenitore, new Vector2(0f, -yRighe),
                new Vector2(Larghezza, righe * AltezzaRiga));

            for (var i = 0; i < righe; i++) m_righe.Add(CostruisciRiga(i));

            // Due colonne di righe corte invece di una riga lunga per comando:
            // cosi nessuna va a capo, e il testo resta dentro il suo riquadro.
            var colonna = (Larghezza - 3f * Margine) * 0.5f;

            var comandiSx = UIFacile.Testo("ComandiSx", radice,
                "A   riconoscimento\nB   tenuto: esci\nstick ▲▼   scorri · premuto: ricentra", 19);
            comandiSx.color = new Color(0.80f, 0.86f, 1f, 0.80f);
            comandiSx.lineSpacing = 1.2f;
            comandiSx.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFacile.Posiziona(comandiSx.rectTransform, new Vector2(Margine + 4f, -yComandi),
                new Vector2(colonna, hComandi));

            var comandiDx = UIFacile.Testo("ComandiDx", radice,
                "Y   cancella il database\ngrilletto   dai un nome\ngrilletto tenuto   sposta il pannello", 19);
            comandiDx.color = new Color(0.80f, 0.86f, 1f, 0.80f);
            comandiDx.lineSpacing = 1.2f;
            comandiDx.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFacile.Posiziona(comandiDx.rectTransform,
                new Vector2(Margine * 2f + colonna, -yComandi), new Vector2(colonna, hComandi));

            var nota = UIFacile.Testo("Nota", radice,
                "Riconoscimento locale — nessun dato lascia il visore.", 19);
            nota.color = new Color(1f, 1f, 1f, 0.5f);
            UIFacile.Posiziona(nota.rectTransform, new Vector2(Margine + 4f, -yNota),
                new Vector2(Larghezza - 2f * Margine, hNota));

            var cornice = UIFacile.Immagine("CorniceDiagnostica", radice, new Color(1f, 1f, 1f, 0.06f));
            UIFacile.Posiziona(cornice.rectTransform, new Vector2(Margine, -yDiag),
                new Vector2(Larghezza - 2f * Margine, hDiag));

            m_diagnostica = UIFacile.Testo("Diagnostica", cornice.rectTransform, "in avvio…", 17);
            m_diagnostica.color = new Color(0.75f, 0.90f, 1f, 0.85f);
            UIFacile.Posiziona(m_diagnostica.rectTransform, new Vector2(8f, -4f),
                new Vector2(Larghezza - 2f * Margine - 16f, hDiag - 8f));

            if (!m_anteprimePipeline) return;

            m_anteprimaIngresso = UIFacile.Raw("AnteprimaIngresso", radice);
            UIFacile.Posiziona(m_anteprimaIngresso.rectTransform,
                new Vector2(Margine, -yAnteprime), new Vector2(128f, 96f));
            m_anteprimaIngresso.enabled = false;

            var etIngresso = UIFacile.Testo("EtIngresso", radice, "ingresso\ndetector", 17);
            etIngresso.color = new Color(1f, 1f, 1f, 0.5f);
            UIFacile.Posiziona(etIngresso.rectTransform,
                new Vector2(Margine + 136f, -yAnteprime), new Vector2(100f, 50f));

            m_anteprimaVolto = UIFacile.Raw("AnteprimaVolto", radice);
            UIFacile.Posiziona(m_anteprimaVolto.rectTransform,
                new Vector2(Margine + 250f, -yAnteprime), new Vector2(96f, 96f));
            m_anteprimaVolto.enabled = false;

            var etVolto = UIFacile.Testo("EtVolto", radice, "volto\nallineato", 17);
            etVolto.color = new Color(1f, 1f, 1f, 0.5f);
            UIFacile.Posiziona(etVolto.rectTransform,
                new Vector2(Margine + 354f, -yAnteprime), new Vector2(100f, 50f));
        }

        private Riga CostruisciRiga(int indice)
        {
            var sfondo = UIFacile.Immagine($"Riga{indice}", m_contenitore, new Color(1f, 1f, 1f, 0.06f));
            var rt = UIFacile.Posiziona(sfondo.rectTransform,
                new Vector2(Margine, -indice * AltezzaRiga), new Vector2(Larghezza - 2f * Margine, AltezzaRiga - 8f));

            var volto = UIFacile.Raw("Volto", rt);
            UIFacile.Posiziona(volto.rectTransform, new Vector2(8f, -5f), new Vector2(64f, 64f));
            volto.color = new Color(1f, 1f, 1f, 0.9f);

            var nome = UIFacile.Testo("Nome", rt, "", 29);
            UIFacile.Posiziona(nome.rectTransform, new Vector2(82f, -6f), new Vector2(350f, 34f));

            var dettaglio = UIFacile.Testo("Dettaglio", rt, "", 19);
            dettaglio.color = new Color(1f, 1f, 1f, 0.6f);
            UIFacile.Posiziona(dettaglio.rectTransform, new Vector2(82f, -40f), new Vector2(350f, 28f));

            UIFacile.Collisore(rt);
            var bersaglio = sfondo.gameObject.AddComponent<BersaglioAR>();

            var riga = new Riga
            {
                Go = sfondo.gameObject,
                Volto = volto,
                Nome = nome,
                Dettaglio = dettaglio
            };

            bersaglio.Azione = () =>
            {
                if (riga.Persona != null && m_editor != null) m_editor.Apri(riga.Persona);
            };
            bersaglio.Evidenzia = dentro =>
                sfondo.color = dentro ? new Color(1f, 1f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.06f);

            return riga;
        }

        private void Scorri(int passo)
        {
            var massimo = Mathf.Max(0, m_ordinate.Count - m_righe.Count);
            var nuovo = Mathf.Clamp(m_primaRiga + passo, 0, massimo);
            if (nuovo == m_primaRiga) return;
            m_primaRiga = nuovo;
            Aggiorna();
        }

        public void Aggiorna()
        {
            if (m_database == null || m_titolo == null) return;

            m_ordinate.Clear();
            m_ordinate.AddRange(m_database.Persone);
            m_ordinate.Sort((a, b) => b.LastSeenTicks.CompareTo(a.LastSeenTicks));

            m_primaRiga = Mathf.Clamp(m_primaRiga, 0, Mathf.Max(0, m_ordinate.Count - m_righe.Count));

            if (m_ordinate.Count <= m_righe.Count)
                m_titolo.text = m_ordinate.Count == 0 ? "People" : $"People  ({m_ordinate.Count})";
            else
                m_titolo.text = $"People  {m_primaRiga + 1}–{Mathf.Min(m_primaRiga + m_righe.Count, m_ordinate.Count)}" +
                                $" di {m_ordinate.Count}";

            if (m_manager != null)
            {
                var attivo = m_manager.RiconoscimentoAttivo;
                m_statoRiconoscimento.text = attivo ? "● recognition ON" : "○ recognition OFF";
                m_statoRiconoscimento.color = attivo
                    ? new Color(0.35f, 0.95f, 0.45f)
                    : new Color(1f, 0.55f, 0.35f);
            }

            for (var i = 0; i < m_righe.Count; i++)
            {
                var indice = m_primaRiga + i;
                var riga = m_righe[i];

                if (indice >= m_ordinate.Count)
                {
                    riga.Go.SetActive(false);
                    riga.Persona = null;
                    continue;
                }

                var p = m_ordinate[indice];
                riga.Go.SetActive(true);
                riga.Persona = p;
                riga.Nome.text = p.DisplayName;
                riga.Nome.color = p.Persistent ? new Color(0.65f, 0.95f, 1f) : Color.white;
                riga.Volto.texture = p.Anteprima;
                riga.Volto.enabled = p.Anteprima != null;

                var stato = p.Persistent ? "nome assegnato" : "identita temporanea";
                var secondi = (float)(System.DateTime.UtcNow - p.UltimaVista).TotalSeconds;
                var quando = secondi < 3f ? "ora" : secondi < 90f ? $"{secondi:0}s" : $"{secondi / 60f:0} min";
                riga.Dettaglio.text = $"{p.PersonID} · {stato} · {p.ObservationCount} oss. · {quando}";
            }
        }
    }
}
