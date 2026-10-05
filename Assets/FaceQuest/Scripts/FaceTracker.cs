// Tracking temporale dei volti.
//
// Serve per una ragione di costo: il detector puo girare a 5-15 Hz, ma la rete
// di embedding non deve girare su ogni volto a ogni giro. Il tracker tiene
// l'identita attaccata alla traccia e chiede un nuovo embedding solo quando c'e
// motivo: traccia appena nata, identita ancora incerta, posa cambiata molto,
// oppure e passato abbastanza tempo dall'ultima conferma.
//
// L'associazione fra rilevamenti e tracce e greedy su IoU. Con pochi volti in
// una stanza e piu che sufficiente, e non introduce le modalita di guasto
// difficili da capire di un assegnamento globale.

using System.Collections.Generic;
using UnityEngine;

namespace FaceQuest
{
    public class Traccia
    {
        public int Id;
        public VoltoRilevato Ultimo;
        public Rect BoxLisciata;
        public bool Inizializzata;

        public float TempoAggiornamento;
        public float TempoIdentificazione = -999f;

        public string PersonID;
        public string Etichetta = "...";
        public float Confidenza;
        public EsitoIdentita Esito = EsitoIdentita.DaFare;
        public int TentativiIncerti;

        /// <summary>
        /// Gli embedding che hanno detto "non la conosco", in attesa di conferma.
        ///
        /// Un'identita non nasce da un fotogramma solo. Prima ne servono diversi
        /// di seguito, e devono somigliarsi fra loro: se un volto mosso o un
        /// falso positivo produce un embedding casuale, il successivo non gli
        /// somigliera e la creazione non parte.
        /// </summary>
        public readonly List<float[]> Candidati = new List<float[]>();
        public int VerdettiNuova;

        public void ScordaCandidati()
        {
            Candidati.Clear();
            VerdettiNuova = 0;
        }

        /// <summary>true mentre un embedding e in corso: evita di chiederne due.</summary>
        public bool InLavorazione;

        // Stato geometrico al momento dell'ultima identificazione, per capire se
        // la posa e cambiata abbastanza da giustificarne un'altra.
        public Vector2 CentroIdentificazione;
        public float ScalaIdentificazione;
        public float YawIdentificazione;

        // Posizione nel mondo, riempita dal renderer.
        public Vector3 PosizioneMondo;
        public Vector2 DimensioneMondo;
        public float Distanza;
        public bool PosizioneValida;
    }

    public class FaceTracker : MonoBehaviour
    {
        [Header("Associazione")]
        [SerializeField, Tooltip("IoU minima per considerare lo stesso volto fra due giri.")]
        private float m_iouAssociazione = 0.3f;
        [SerializeField, Tooltip("Dopo quanti secondi senza rilevamenti la traccia muore.")]
        private float m_tempoVita = 0.8f;
        [SerializeField, Range(0f, 1f), Tooltip("0 = nessun smoothing, 1 = box immobile.")]
        private float m_smoothing = 0.55f;

        [Header("Quando ricalcolare l'identita")]
        [SerializeField] private float m_intervalloRiconferma = 2.5f;
        [SerializeField] private float m_intervalloIncerta = 0.4f;
        [SerializeField, Tooltip("Variazione di scala del volto che forza un nuovo embedding.")]
        private float m_variazioneScala = 0.35f;
        [SerializeField, Tooltip("Variazione di yaw (gradi) che forza un nuovo embedding.")]
        private float m_variazioneYaw = 18f;

        private readonly List<Traccia> m_tracce = new List<Traccia>();
        private readonly List<Traccia> m_daIdentificare = new List<Traccia>();
        private int m_prossimoId = 1;

        public IReadOnlyList<Traccia> Tracce => m_tracce;

        public void Aggiorna(List<VoltoRilevato> rilevamenti, float tempo)
        {
            var usati = new bool[rilevamenti.Count];

            // Prima si prova ad aggiornare le tracce esistenti, dalla piu recente:
            // se due tracce competono per lo stesso volto vince quella vista prima.
            m_tracce.Sort((a, b) => b.TempoAggiornamento.CompareTo(a.TempoAggiornamento));

            foreach (var t in m_tracce)
            {
                var migliore = -1;
                var migliorIou = m_iouAssociazione;
                for (var i = 0; i < rilevamenti.Count; i++)
                {
                    if (usati[i]) continue;
                    var iou = FaceDetector.IoU(t.Ultimo.Box, rilevamenti[i].Box);
                    if (iou > migliorIou) { migliorIou = iou; migliore = i; }
                }

                if (migliore < 0) continue;

                usati[migliore] = true;
                t.Ultimo = rilevamenti[migliore];
                t.TempoAggiornamento = tempo;

                if (!t.Inizializzata)
                {
                    t.BoxLisciata = t.Ultimo.Box;
                    t.Inizializzata = true;
                }
                else
                {
                    t.BoxLisciata = Mescola(t.BoxLisciata, t.Ultimo.Box, m_smoothing);
                }
            }

            for (var i = 0; i < rilevamenti.Count; i++)
            {
                if (usati[i]) continue;
                m_tracce.Add(new Traccia
                {
                    Id = m_prossimoId++,
                    Ultimo = rilevamenti[i],
                    BoxLisciata = rilevamenti[i].Box,
                    Inizializzata = true,
                    TempoAggiornamento = tempo
                });
            }

            for (var i = m_tracce.Count - 1; i >= 0; i--)
            {
                if (tempo - m_tracce[i].TempoAggiornamento > m_tempoVita && !m_tracce[i].InLavorazione)
                    m_tracce.RemoveAt(i);
            }
        }

        /// <summary>
        /// Le tracce che hanno bisogno di un embedding adesso, in ordine di
        /// urgenza: prima quelle senza identita, poi le incerte, poi le conferme.
        /// </summary>
        public List<Traccia> DaIdentificare(float tempo, int massimo)
        {
            m_daIdentificare.Clear();

            foreach (var t in m_tracce)
            {
                if (t.InLavorazione) continue;
                if (Urgenza(t, tempo) > 0) m_daIdentificare.Add(t);
            }

            m_daIdentificare.Sort((a, b) => Urgenza(b, tempo).CompareTo(Urgenza(a, tempo)));
            if (m_daIdentificare.Count > massimo) m_daIdentificare.RemoveRange(massimo, m_daIdentificare.Count - massimo);
            return m_daIdentificare;
        }

        private int Urgenza(Traccia t, float tempo)
        {
            if (t.Esito == EsitoIdentita.DaFare) return 3;

            var dt = tempo - t.TempoIdentificazione;
            if (t.Esito == EsitoIdentita.Incerta && dt >= m_intervalloIncerta) return 2;

            FaceQualityEstimator.PosaDaLandmark(t.Ultimo, out var yaw, out _, out _);
            var scala = t.Ultimo.Box.height;
            var cambioScala = t.ScalaIdentificazione > 1e-5f
                ? Mathf.Abs(scala - t.ScalaIdentificazione) / t.ScalaIdentificazione
                : 1f;

            if (cambioScala > m_variazioneScala || Mathf.Abs(yaw - t.YawIdentificazione) > m_variazioneYaw) return 1;
            return dt >= m_intervalloRiconferma ? 1 : 0;
        }

        /// <summary>Da chiamare quando un'identificazione si e conclusa.</summary>
        public void SegnaIdentificata(Traccia t, float tempo)
        {
            t.TempoIdentificazione = tempo;
            t.CentroIdentificazione = t.Ultimo.Box.center;
            t.ScalaIdentificazione = t.Ultimo.Box.height;
            FaceQualityEstimator.PosaDaLandmark(t.Ultimo, out var yaw, out _, out _);
            t.YawIdentificazione = yaw;
        }

        /// <summary>Le tracce che mostravano una persona cancellata tornano anonime.</summary>
        public void ScollegaPersona(string personID)
        {
            foreach (var t in m_tracce)
            {
                if (t.PersonID != personID) continue;
                t.PersonID = null;
                t.Etichetta = "...";
                t.Esito = EsitoIdentita.DaFare;
                t.ScordaCandidati();
            }
        }

        public void RinominaPersona(string personID, string nuovaEtichetta)
        {
            foreach (var t in m_tracce)
                if (t.PersonID == personID) t.Etichetta = nuovaEtichetta;
        }

        public void Azzera() => m_tracce.Clear();

        private static Rect Mescola(Rect vecchia, Rect nuova, float k)
        {
            k = Mathf.Clamp01(k);
            return new Rect(
                Mathf.Lerp(nuova.x, vecchia.x, k),
                Mathf.Lerp(nuova.y, vecchia.y, k),
                Mathf.Lerp(nuova.width, vecchia.width, k),
                Mathf.Lerp(nuova.height, vecchia.height, k));
        }
    }
}
