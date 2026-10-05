// La finestrella per dare o cambiare il nome a una persona, e le conferme di
// cancellazione.
//
// La tastiera e costruita a mano invece di usare quella di sistema: su Horizon
// OS la tastiera virtuale richiede il suo prefab e il suo modello, e quando non
// compare non si capisce perche. Ventisei tasti con un collider ciascuno sono
// prevedibili, funzionano col solo controller destro e non dipendono da nulla.
//
// La cancellazione ha sempre una conferma. Non per prudenza generica: qui si
// cancellano dati biometrici, e l'operazione e irreversibile per costruzione
// (i file cifrati vengono rimossi, non marcati).

using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public class PersonEditorUI : MonoBehaviour
    {
        [SerializeField] private PersonDatabase m_database;
        [SerializeField] private FaceQuestManager m_manager;
        [SerializeField] private PersonPersistenceManager m_persistenza;

        private const float Larghezza = 620f;
        private const float Altezza = 470f;
        private const float Scala = 0.0009f;
        private const int MaxCaratteri = 18;

        private static readonly string[] Righe = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };

        private GameObject m_radice;
        private Text m_titolo;
        private Text m_campo;
        private Text m_messaggio;
        private GameObject m_tastiera;
        private GameObject m_bottoniNome;
        private GameObject m_bottoniConferma;
        private RawImage m_anteprima;

        private Persona m_persona;
        private readonly StringBuilder m_testo = new StringBuilder();
        private enum Modo { Chiuso, Nome, ConfermaPersona, ConfermaTutto }
        private Modo m_modo = Modo.Chiuso;

        public bool Aperto => m_modo != Modo.Chiuso;

        public void Collega(PersonDatabase db, FaceQuestManager manager, PersonPersistenceManager persistenza)
        {
            m_database = db; m_manager = manager; m_persistenza = persistenza;
        }

        private void Start()
        {
            Costruisci();
            Chiudi();
        }

        private void Costruisci()
        {
            var canvas = UIFacile.Pannello("PannelloEditor", transform,
                new Vector2(Larghezza, Altezza), Scala, new Color(0.02f, 0.03f, 0.06f, 0.92f));
            m_radice = canvas.gameObject;
            var r = canvas.transform;

            m_titolo = UIFacile.Testo("Titolo", r, "Person", 34);
            UIFacile.Posiziona(m_titolo.rectTransform, new Vector2(20f, -16f), new Vector2(420f, 44f));

            m_anteprima = UIFacile.Raw("Anteprima", r);
            UIFacile.Posiziona(m_anteprima.rectTransform, new Vector2(Larghezza - 96f, -16f), new Vector2(76f, 76f));

            var etichetta = UIFacile.Testo("EtichettaNome", r, "Name:", 24);
            etichetta.color = new Color(1f, 1f, 1f, 0.6f);
            UIFacile.Posiziona(etichetta.rectTransform, new Vector2(20f, -66f), new Vector2(200f, 30f));

            var cornice = UIFacile.Immagine("Campo", r, new Color(1f, 1f, 1f, 0.10f));
            UIFacile.Posiziona(cornice.rectTransform, new Vector2(20f, -96f), new Vector2(Larghezza - 40f, 56f));

            m_campo = UIFacile.Testo("Valore", cornice.rectTransform, "", 34, TextAnchor.MiddleLeft);
            UIFacile.Posiziona(m_campo.rectTransform, new Vector2(14f, -4f), new Vector2(Larghezza - 70f, 48f));

            m_messaggio = UIFacile.Testo("Messaggio", r, "", 26);
            UIFacile.Posiziona(m_messaggio.rectTransform, new Vector2(20f, -70f), new Vector2(Larghezza - 40f, 120f));

            CostruisciTastiera(r);
            CostruisciBottoniNome(r);
            CostruisciBottoniConferma(r);
        }

        private void CostruisciTastiera(Transform r)
        {
            m_tastiera = new GameObject("Tastiera", typeof(RectTransform));
            m_tastiera.transform.SetParent(r, false);
            UIFacile.Posiziona((RectTransform)m_tastiera.transform, Vector2.zero, new Vector2(Larghezza, Altezza));

            const float lato = 52f;
            const float passo = 56f;
            var y = -168f;

            for (var i = 0; i < Righe.Length; i++)
            {
                var riga = Righe[i];
                var largRiga = riga.Length * passo - (passo - lato);
                var x0 = (Larghezza - largRiga) * 0.5f;

                for (var k = 0; k < riga.Length; k++)
                {
                    var c = riga[k];
                    UIFacile.Bottone($"Tasto{c}", m_tastiera.transform, c.ToString(), 28,
                        new Vector2(x0 + k * passo, y), new Vector2(lato, lato),
                        new Color(0.16f, 0.19f, 0.26f, 0.95f), () => Scrivi(c));
                }
                y -= passo;
            }

            UIFacile.Bottone("Spazio", m_tastiera.transform, "spazio", 24,
                new Vector2(120f, y), new Vector2(180f, lato),
                new Color(0.16f, 0.19f, 0.26f, 0.95f), () => Scrivi(' '));

            UIFacile.Bottone("Canc", m_tastiera.transform, "◄ canc", 24,
                new Vector2(310f, y), new Vector2(180f, lato),
                new Color(0.26f, 0.19f, 0.19f, 0.95f), Cancella);
        }

        private void CostruisciBottoniNome(Transform r)
        {
            m_bottoniNome = new GameObject("BottoniNome", typeof(RectTransform));
            m_bottoniNome.transform.SetParent(r, false);
            UIFacile.Posiziona((RectTransform)m_bottoniNome.transform, Vector2.zero, new Vector2(Larghezza, Altezza));

            var y = -404f;

            UIFacile.Bottone("Salva", m_bottoniNome.transform, "SAVE", 28,
                new Vector2(20f, y), new Vector2(170f, 52f),
                new Color(0.12f, 0.36f, 0.24f, 0.95f), Salva);

            UIFacile.Bottone("Annulla", m_bottoniNome.transform, "CANCEL", 28,
                new Vector2(200f, y), new Vector2(170f, 52f),
                new Color(0.20f, 0.22f, 0.28f, 0.95f), Chiudi);

            UIFacile.Bottone("CancellaPersona", m_bottoniNome.transform, "DELETE", 28,
                new Vector2(380f, y), new Vector2(220f, 52f),
                new Color(0.40f, 0.13f, 0.13f, 0.95f), ChiediConfermaCancellaPersona);
        }

        private void CostruisciBottoniConferma(Transform r)
        {
            m_bottoniConferma = new GameObject("BottoniConferma", typeof(RectTransform));
            m_bottoniConferma.transform.SetParent(r, false);
            UIFacile.Posiziona((RectTransform)m_bottoniConferma.transform, Vector2.zero, new Vector2(Larghezza, Altezza));

            UIFacile.Bottone("Confermo", m_bottoniConferma.transform, "SI, CANCELLA", 28,
                new Vector2(20f, -220f), new Vector2(280f, 60f),
                new Color(0.42f, 0.12f, 0.12f, 0.95f), Conferma);

            UIFacile.Bottone("NonConfermo", m_bottoniConferma.transform, "NO", 28,
                new Vector2(320f, -220f), new Vector2(280f, 60f),
                new Color(0.18f, 0.20f, 0.26f, 0.95f), Chiudi);
        }

        // ------------------------------------------------------------------

        public void Apri(Persona p)
        {
            m_persona = p;
            m_testo.Clear();
            if (!string.IsNullOrEmpty(p.UserAssignedName)) m_testo.Append(p.UserAssignedName);

            m_modo = Modo.Nome;
            m_titolo.text = $"Nome per {p.DisplayName}";
            m_anteprima.texture = p.Anteprima;
            m_anteprima.enabled = p.Anteprima != null;
            Mostra();
        }

        public void ChiediConfermaCancellaPersona()
        {
            if (m_persona == null) return;
            m_modo = Modo.ConfermaPersona;
            m_titolo.text = $"Cancellare {m_persona.DisplayName}?";
            m_messaggio.text = "Verranno cancellati il template biometrico, lo storico e il ritaglio del volto. " +
                               "L'operazione non e reversibile.";
            Mostra();
        }

        public void ChiediConfermaCancellaTutto()
        {
            m_modo = Modo.ConfermaTutto;
            m_persona = null;
            m_titolo.text = "Cancellare TUTTO il database?";
            m_messaggio.text = "Verranno cancellate tutte le persone, i template, lo storico, i ritagli e la chiave " +
                               "di cifratura. Dopo questa operazione nulla e recuperabile.";
            Mostra();
        }

        public void Chiudi()
        {
            m_modo = Modo.Chiuso;
            m_persona = null;
            if (m_radice != null) m_radice.SetActive(false);
        }

        private void Mostra()
        {
            m_radice.SetActive(true);
            var nome = m_modo == Modo.Nome;

            m_tastiera.SetActive(nome);
            m_bottoniNome.SetActive(nome);
            m_bottoniConferma.SetActive(!nome);
            m_campo.transform.parent.gameObject.SetActive(nome);
            m_messaggio.gameObject.SetActive(!nome);
            m_anteprima.enabled = m_persona?.Anteprima != null;

            AggiornaCampo();
        }

        private void Scrivi(char c)
        {
            if (m_testo.Length >= MaxCaratteri) return;
            // Prima lettera maiuscola, le altre minuscole: si scrive "Marco",
            // non "MARCO", senza dover gestire un tasto shift.
            m_testo.Append(m_testo.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            AggiornaCampo();
        }

        private void Cancella()
        {
            if (m_testo.Length > 0) m_testo.Length--;
            AggiornaCampo();
        }

        private void AggiornaCampo()
        {
            if (m_campo == null) return;
            m_campo.text = m_testo.Length == 0 ? "<i>senza nome</i>" : m_testo.ToString();
            m_campo.supportRichText = true;
        }

        private void Salva()
        {
            if (m_persona == null || m_database == null) { Chiudi(); return; }

            var nome = m_testo.ToString().Trim();
            m_database.AssegnaNome(m_persona, nome);
            m_manager?.NotificaRinomina(m_persona);
            m_persistenza?.Salva();

            Debug.Log($"[FaceQuest] {m_persona.PersonID} ora si chiama '{m_persona.DisplayName}' " +
                      $"(persistente: {m_persona.Persistent}).");
            Chiudi();
        }

        private void Conferma()
        {
            if (m_modo == Modo.ConfermaTutto)
            {
                m_manager?.CancellaTutto();
            }
            else if (m_persona != null)
            {
                m_manager?.CancellaPersona(m_persona);
            }
            Chiudi();
        }
    }
}
