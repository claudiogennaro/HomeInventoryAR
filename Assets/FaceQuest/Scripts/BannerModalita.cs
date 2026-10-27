// La scritta in alto che dice in quale fase ci troviamo: TRAIN o SEARCH.
//
// E trasparente e piccola, attaccata alla testa e un po' sopra la linea dello
// sguardo: deve potersi leggere con un'occhiata ma non coprire le persone che si
// stanno inquadrando. Non ha collider, quindi il raggio del puntatore ci passa
// attraverso.
//
// Perche sempre visibile e non solo nel pannello: il pannello sta di lato e si
// puo spostare, e quando si e fra le persone la domanda "sto raccogliendo volti o
// sto solo riconoscendo?" non deve richiedere di cercarlo. In Train il database
// cresce, e questo va sempre sotto gli occhi di chi lo sta facendo crescere.
//
// Un MonoBehaviour, un file: vedi la nota in BersaglioAR.cs.

using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public class BannerModalita : MonoBehaviour
    {
        [SerializeField] private FaceQuestManager m_manager;
        [SerializeField, Tooltip("Distanza davanti agli occhi, in metri.")]
        private float m_distanza = 1.0f;
        [SerializeField, Tooltip("Quanto sopra la linea dello sguardo, in metri.")]
        private float m_altezza = 0.30f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Opacita del testo: 1 = pieno, basso = quasi invisibile.")]
        private float m_opacita = 0.55f;

        private const float Larghezza = 700f;
        private const float Altezza = 130f;
        private const float Scala = 0.0007f;

        private Text m_titolo;
        private Text m_sotto;
        private bool m_costruito;
        private ModoFace m_modoMostrato;
        private bool m_attivoMostrato;

        public void Collega(FaceQuestManager manager) => m_manager = manager;

        private void Start()
        {
            // Si aggancia alla testa: e l'unico modo in cui una scritta "in alto
            // sullo schermo" ha senso in un visore, dove lo schermo e ovunque si
            // guardi.
            Transform testa = null;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null) testa = rig.centerEyeAnchor;
            else if (Camera.main != null) testa = Camera.main.transform;
            if (testa == null) testa = transform;

            var canvas = UIFacile.Pannello("BannerModalita", testa,
                new Vector2(Larghezza, Altezza), Scala, new Color(0f, 0f, 0f, 0f));
            canvas.transform.localPosition = new Vector3(0f, m_altezza, m_distanza);
            canvas.transform.localRotation = Quaternion.identity;

            m_titolo = UIFacile.Testo("Titolo", canvas.transform, "", 64, TextAnchor.UpperCenter);
            m_titolo.fontStyle = FontStyle.Bold;
            UIFacile.Posiziona(m_titolo.rectTransform, new Vector2(0f, -4f), new Vector2(Larghezza, 80f));

            m_sotto = UIFacile.Testo("Sottotitolo", canvas.transform, "", 26, TextAnchor.UpperCenter);
            UIFacile.Posiziona(m_sotto.rectTransform, new Vector2(0f, -84f), new Vector2(Larghezza, 40f));

            m_costruito = true;
            Aggiorna();
        }

        private void Update()
        {
            if (!m_costruito || m_manager == null) return;
            if (m_manager.Modo == m_modoMostrato && m_manager.RiconoscimentoAttivo == m_attivoMostrato) return;
            Aggiorna();
        }

        private void Aggiorna()
        {
            if (m_manager == null) return;

            m_modoMostrato = m_manager.Modo;
            m_attivoMostrato = m_manager.RiconoscimentoAttivo;

            var train = m_modoMostrato == ModoFace.Train;
            var colore = train ? new Color(1f, 0.68f, 0.22f) : new Color(0.50f, 0.82f, 1f);
            colore.a = m_attivoMostrato ? m_opacita : m_opacita * 0.6f;

            m_titolo.text = train ? "TRAIN" : "SEARCH";
            m_titolo.color = colore;

            m_sotto.text = !m_attivoMostrato
                ? "recognition paused"
                : train
                    ? "new faces can be added to the database"
                    : "recognising people already in the database";
            m_sotto.color = new Color(colore.r, colore.g, colore.b, colore.a * 0.85f);
        }
    }
}
