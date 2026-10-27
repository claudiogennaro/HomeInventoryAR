// La finestra che propone di aggiungere un volto nuovo al database.
//
// Compare in Train quando l'app incontra un volto che non somiglia a nessuno di
// quelli noti: mostra il ritaglio e due bottoni, Add e Discard. Se l'utente non
// risponde sparisce da sola dopo m_durata secondi (5 di partenza) e la stessa
// persona potra essere riproposta piu tardi: non e un rifiuto, e un "adesso no".
//
// Una finestra alla volta, per costruzione: c'e una sola proposta in mano al
// popup. Chi decide quando mostrarne un'altra e il manager (FaceQuestManager),
// che guarda Visibile; qui non c'e coda, perche una coda di richieste e
// esattamente cio che farebbe sembrare l'app bloccata.
//
// La finestra non decide niente: segnala. Add e Discard (o la scadenza)
// partono come eventi; cosa farne — aprire la tastiera, scartare gli embedding —
// sta nel manager, che conosce il database.
//
// Come la finestra del nome, nasce da codice e sta davanti all'utente (la
// posiziona SeguiTesta, configurato dal costruttore di scena).
//
// Un MonoBehaviour, un file: vedi la nota in BersaglioAR.cs.

using System;
using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public class PopupNuovoVolto : MonoBehaviour
    {
        [SerializeField, Tooltip("Secondi per cui il popup resta visibile prima di sparire da solo.")]
        private float m_durata = 5f;

        private const float Larghezza = 400f;
        private const float Altezza = 330f;
        private const float Scala = 0.0009f;
        private const float LarghezzaBarra = 360f;

        /// <summary>L'utente ha premuto Add.</summary>
        public event Action<PropostaVolto> Aggiungi;

        /// <summary>L'utente ha premuto Discard, oppure il tempo e scaduto.</summary>
        public event Action<PropostaVolto> Scartata;

        private GameObject m_radice;
        private RawImage m_foto;
        private Text m_dettaglio;
        private RectTransform m_barra;
        private PropostaVolto m_proposta;
        private float m_fine;

        public bool Visibile => m_proposta != null;

        public float Durata
        {
            get => m_durata;
            set => m_durata = Mathf.Max(1f, value);
        }

        private void Start()
        {
            if (m_radice == null) Costruisci();
            m_radice.SetActive(m_proposta != null);
        }

        private void Costruisci()
        {
            var canvas = UIFacile.Pannello("PopupNuovoVolto", transform,
                new Vector2(Larghezza, Altezza), Scala, new Color(0.02f, 0.03f, 0.06f, 0.94f));
            m_radice = canvas.gameObject;
            var r = canvas.transform;

            var titolo = UIFacile.Testo("Titolo", r, "New face", 34);
            UIFacile.Posiziona(titolo.rectTransform, new Vector2(20f, -14f), new Vector2(Larghezza - 40f, 42f));

            m_foto = UIFacile.Raw("Foto", r);
            UIFacile.Posiziona(m_foto.rectTransform, new Vector2(20f, -66f), new Vector2(150f, 150f));
            m_foto.enabled = false;

            var domanda = UIFacile.Testo("Domanda", r, "Add this person\nto the database?", 26);
            UIFacile.Posiziona(domanda.rectTransform, new Vector2(188f, -70f), new Vector2(192f, 80f));

            m_dettaglio = UIFacile.Testo("Dettaglio", r, "", 20);
            m_dettaglio.color = new Color(1f, 1f, 1f, 0.6f);
            UIFacile.Posiziona(m_dettaglio.rectTransform, new Vector2(188f, -170f), new Vector2(192f, 40f));

            UIFacile.Bottone("Add", r, "ADD", 30,
                new Vector2(20f, -230f), new Vector2(172f, 62f),
                new Color(0.12f, 0.40f, 0.26f, 0.96f), () => Termina(true));

            UIFacile.Bottone("Discard", r, "DISCARD", 30,
                new Vector2(208f, -230f), new Vector2(172f, 62f),
                new Color(0.22f, 0.24f, 0.30f, 0.96f), () => Termina(false));

            // Una barra che si accorcia: dice quanto resta senza costringere a
            // leggere un numero, e fa capire che la finestra non e permanente.
            var pista = UIFacile.Immagine("Pista", r, new Color(1f, 1f, 1f, 0.12f));
            UIFacile.Posiziona(pista.rectTransform, new Vector2(20f, -306f), new Vector2(LarghezzaBarra, 8f));

            var riempimento = UIFacile.Immagine("Tempo", r, new Color(0.45f, 0.80f, 1f, 0.9f));
            m_barra = UIFacile.Posiziona(riempimento.rectTransform,
                new Vector2(20f, -306f), new Vector2(LarghezzaBarra, 8f));
        }

        /// <summary>
        /// Mostra la proposta. Se ce n'e gia una aperta la chiama il manager solo
        /// dopo aver controllato Visibile: qui la nuova sostituirebbe la vecchia
        /// senza avvisare nessuno, e la foto della prima andrebbe persa.
        /// </summary>
        public void Mostra(PropostaVolto proposta)
        {
            if (proposta == null) return;
            if (m_radice == null) Costruisci();

            m_proposta = proposta;
            m_fine = Time.time + m_durata;

            m_foto.texture = proposta.Foto;
            m_foto.enabled = proposta.Foto != null;
            m_dettaglio.text = proposta.Distanza > 0f ? $"about {proposta.Distanza:0.0} m away" : "";
            ImpostaBarra(1f);
            m_radice.SetActive(true);
        }

        /// <summary>
        /// Nasconde il popup SENZA segnalare nulla e restituisce la proposta che
        /// era aperta (o null). Serve quando e l'app, non l'utente, a chiudere: il
        /// passaggio a Search. Chi riceve la proposta ne distrugge la foto.
        /// </summary>
        public PropostaVolto Chiudi()
        {
            var p = m_proposta;
            m_proposta = null;
            if (m_radice != null) m_radice.SetActive(false);
            return p;
        }

        private void Update()
        {
            if (m_proposta == null) return;

            var resto = (m_fine - Time.time) / Mathf.Max(0.1f, m_durata);
            if (resto <= 0f)
            {
                Termina(false);
                return;
            }
            ImpostaBarra(resto);
        }

        private void ImpostaBarra(float frazione)
        {
            if (m_barra == null) return;
            m_barra.sizeDelta = new Vector2(LarghezzaBarra * Mathf.Clamp01(frazione), 8f);
        }

        private void Termina(bool aggiunta)
        {
            // Due pressioni ravvicinate (o una pressione nello stesso fotogramma
            // della scadenza) non devono produrre due eventi.
            var p = m_proposta;
            if (p == null) return;

            m_proposta = null;
            if (m_radice != null) m_radice.SetActive(false);

            if (aggiunta) Aggiungi?.Invoke(p);
            else Scartata?.Invoke(p);
        }
    }
}
