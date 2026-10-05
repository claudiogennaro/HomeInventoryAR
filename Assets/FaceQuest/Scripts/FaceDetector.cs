// Face detection con SCRFD 500M (InsightFace) sul motore di inferenza di Unity.
//
// Il modello incluso e stato preparato per questo progetto: ingresso fisso
// 384x288 e normalizzazione (x*255-127.5)/128 cucita dentro il grafo ONNX, cosi
// il tensore che gli passiamo e semplicemente l'immagine in 0..1 e non c'e un
// passaggio di preprocessing da sbagliare. 384x288 non e una scelta estetica:
// e 4:3 esatto come tutte le risoluzioni della camera passthrough, quindi il
// ridimensionamento non deforma i volti, ed entrambi i lati sono multipli di 32
// come la rete richiede.
//
// Uscite: nove tensori, tre per ciascuno stride (8, 16, 32) — punteggi, box e
// landmark. I punteggi sono gia passati per una sigmoid dentro il grafo.
// I box non sono coordinate ma DISTANZE dal centro dell'ancora, in unita di
// stride: e la convenzione di SCRFD.
//
// Le nove uscite NON vengono prese per posizione ma riconosciute dalla loro
// FORMA: (N,1) sono punteggi, (N,4) box, (N,10) landmark, e N dice a quale
// stride appartengono. Fidarsi dell'ordine del grafo significa che, se un
// importatore lo riordina, il codice legge i box come punteggi e produce
// rilevamenti plausibili ma completamente sbagliati — un guasto silenzioso.
// Con la classificazione per forma, o funziona o lo dice.

using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

namespace FaceQuest
{
    public class FaceDetector : MonoBehaviour
    {
        [Header("Modello")]
        [SerializeField] private ModelAsset m_modello;
        [SerializeField, Tooltip("Nome del modello in Assets/FaceQuest/Resources, usato se il riferimento sopra e vuoto.")]
        private string m_modelloDaRisorse = "scrfd_500m_384x288";
        [SerializeField, Tooltip("CPU e la configurazione che il sample di Meta usa sul Quest, ed e quella verificata su questo visore. GPUCompute e piu veloce ma qui faceva chiudere l'app.")]
        private BackendType m_backend = BackendType.CPU;

        [Header("Soglie")]
        [SerializeField, Range(0.1f, 0.95f)] private float m_soglia = 0.45f;
        [SerializeField, Range(0.1f, 0.9f)] private float m_iou = 0.4f;
        [SerializeField, Tooltip("Volti piu bassi di questa frazione dell'altezza del frame vengono ignorati.")]
        private float m_altezzaMinima = 0.04f;

        private static readonly int[] Stride = { 8, 16, 32 };
        private const int Ancore = 2;      // SCRFD 500M: due ancore per cella
        private const int NumUscite = 9;

        private Worker m_worker;
        private int m_larghezza, m_altezza;
        private bool m_occupato;
        private bool m_classificato;

        private readonly Tensor<float>[] m_uscite = new Tensor<float>[NumUscite];
        private readonly int[] m_idxPunteggi = { -1, -1, -1 };
        private readonly int[] m_idxBox = { -1, -1, -1 };
        private readonly int[] m_idxKps = { -1, -1, -1 };
        private readonly List<VoltoRilevato> m_grezzi = new List<VoltoRilevato>();

        public int LarghezzaIngresso => m_larghezza;
        public int AltezzaIngresso => m_altezza;
        public bool Pronto => m_worker != null;
        public float Soglia { get => m_soglia; set => m_soglia = value; }

        /// <summary>
        /// Assegnazione diretta del modello, usata dal costruttore di scena.
        /// Scrivere il campo e poi marcare l'oggetto sporco e piu affidabile che
        /// passare da SerializedObject: il riferimento a un ModelAsset importato
        /// da .onnx e finito a zero, in silenzio, per quella via.
        /// </summary>
        public void ImpostaModello(ModelAsset modello) => m_modello = modello;

        /// <summary>Il punteggio piu alto visto nell'ultimo giro, soglia esclusa.</summary>
        public float PunteggioMassimo { get; private set; }

        /// <summary>Descrizione in chiaro dello stato, per la riga di diagnostica.</summary>
        public string Stato { get; private set; } = "non avviato";

        private void Awake()
        {
            // Due strade indipendenti per arrivare al modello.
            //
            // La prima e il riferimento serializzato assegnato dal costruttore di
            // scena. La seconda e Resources: i file in una cartella Resources
            // finiscono sempre nella build e si caricano per nome, senza dipendere
            // da un riferimento che si serializzi correttamente. La ragione e
            // concreta: in una build il campo serializzato e risultato vuoto sul
            // visore — "det KO: modello mancante" — mentre nell'editor era a posto.
            if (m_modello == null && !string.IsNullOrEmpty(m_modelloDaRisorse))
            {
                m_modello = Resources.Load<ModelAsset>(m_modelloDaRisorse);
                if (m_modello != null)
                    Debug.Log($"[FaceQuest] Detector caricato da Resources/{m_modelloDaRisorse}.");
            }

            if (m_modello == null)
            {
                Stato = "modello mancante";
                Debug.LogError($"[FaceQuest] FaceDetector senza modello: ne riferimento in scena " +
                               $"ne Resources/{m_modelloDaRisorse}.");
                return;
            }

            try
            {
                var model = ModelLoader.Load(m_modello);
                var shape = model.inputs[0].shape;
                m_altezza = shape.Get(2);
                m_larghezza = shape.Get(3);
                m_worker = new Worker(model, m_backend);
                Stato = $"{m_larghezza}x{m_altezza} {m_backend}";
                Debug.Log($"[FaceQuest] Detector caricato: ingresso {m_larghezza}x{m_altezza}, " +
                          $"backend {m_backend}, {model.outputs.Count} uscite.");
            }
            catch (System.Exception e)
            {
                Stato = "caricamento fallito";
                Debug.LogError($"[FaceQuest] Caricamento del detector fallito: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            if (m_worker == null) return;
            for (var i = 0; i < NumUscite; i++) m_worker.PeekOutput(i)?.CompleteAllPendingOperations();
            m_worker.Dispose();
            m_worker = null;
        }

        /// <summary>
        /// Esegue il detector sulla texture data (che deve essere gia della
        /// dimensione d'ingresso) e riempie <paramref name="risultato"/>.
        /// </summary>
        public IEnumerator Rileva(Texture ingresso, List<VoltoRilevato> risultato)
        {
            risultato.Clear();

            if (m_worker == null || ingresso == null || m_occupato) yield break;

            m_occupato = true;
            try
            {
                // Nessun SetDimensions: e deprecato e ignorato, sono le dimensioni
                // del tensore bersaglio a decidere (e a far ricampionare se
                // servisse). L'origine di default e in ALTO a sinistra, che e
                // esattamente l'ipotesi su cui e costruita tutta la pipeline.
                using var tensore = new Tensor<float>(new TensorShape(1, 3, m_altezza, m_larghezza));
                TextureConverter.ToTensor(ingresso, tensore);

                m_worker.Schedule(tensore);

                for (var i = 0; i < NumUscite; i++)
                {
                    if (!(m_worker.PeekOutput(i) is Tensor<float> uscita))
                    {
                        Stato = $"uscita {i} non disponibile";
                        yield break;
                    }

                    var attesa = uscita.ReadbackAndCloneAsync().GetAwaiter();
                    while (!attesa.IsCompleted) yield return null;
                    m_uscite[i] = attesa.GetResult();
                }

                // Un'eccezione qui dentro fermerebbe la coroutine della pipeline
                // senza un messaggio e l'app sembrerebbe semplicemente "spenta".
                try
                {
                    if (!Classifica()) yield break;
                    Decodifica();
                    SoppressioneNonMassimi(m_grezzi, risultato, m_iou);
                }
                catch (System.Exception e)
                {
                    Stato = "errore in decodifica";
                    Debug.LogError($"[FaceQuest] Decodifica del detector fallita: {e}");
                    yield break;
                }
            }
            finally
            {
                for (var i = 0; i < NumUscite; i++)
                {
                    m_uscite[i]?.Dispose();
                    m_uscite[i] = null;
                }
                m_occupato = false;
            }
        }

        /// <summary>
        /// Associa ciascuna uscita al suo ruolo guardandone la forma. Si fa una
        /// volta sola: le forme non cambiano fra un fotogramma e l'altro.
        /// </summary>
        private bool Classifica()
        {
            if (m_classificato) return true;

            for (var s = 0; s < 3; s++) { m_idxPunteggi[s] = -1; m_idxBox[s] = -1; m_idxKps[s] = -1; }

            for (var i = 0; i < NumUscite; i++)
            {
                var forma = m_uscite[i].shape;
                if (forma.rank != 2) continue;

                var s = IndiceStride(forma[0]);
                if (s < 0) continue;

                switch (forma[1])
                {
                    case 1: m_idxPunteggi[s] = i; break;
                    case 4: m_idxBox[s] = i; break;
                    case 10: m_idxKps[s] = i; break;
                }
            }

            for (var s = 0; s < 3; s++)
            {
                if (m_idxPunteggi[s] >= 0 && m_idxBox[s] >= 0 && m_idxKps[s] >= 0) continue;

                var forme = new string[NumUscite];
                for (var i = 0; i < NumUscite; i++) forme[i] = m_uscite[i].shape.ToString();
                Stato = "uscite del modello non riconosciute";
                Debug.LogError($"[FaceQuest] Le uscite del detector non corrispondono a SCRFD per lo stride " +
                               $"{Stride[s]} (ingresso {m_larghezza}x{m_altezza}). Forme trovate: {string.Join(", ", forme)}");
                m_classificato = true;   // inutile ripetere l'errore a ogni fotogramma
                return false;
            }

            m_classificato = true;
            Debug.Log($"[FaceQuest] Uscite del detector riconosciute per forma: " +
                      $"punteggi [{m_idxPunteggi[0]},{m_idxPunteggi[1]},{m_idxPunteggi[2]}] " +
                      $"box [{m_idxBox[0]},{m_idxBox[1]},{m_idxBox[2]}] " +
                      $"landmark [{m_idxKps[0]},{m_idxKps[1]},{m_idxKps[2]}].");
            return true;
        }

        private int IndiceStride(int righe)
        {
            for (var s = 0; s < 3; s++)
            {
                var celle = (m_larghezza / Stride[s]) * (m_altezza / Stride[s]) * Ancore;
                if (righe == celle) return s;
            }
            return -1;
        }

        private void Decodifica()
        {
            m_grezzi.Clear();
            var minAltezza = m_altezzaMinima * m_altezza;

            // Il massimo si accumula in una variabile locale e si pubblica alla
            // fine: azzerandolo all'inizio del ciclo, la riga di diagnostica —
            // che si aggiorna quattro volte al secondo — lo pescava spesso
            // mentre era ancora a zero, e sembrava che il detector non vedesse
            // nulla anche quando stava riconoscendo qualcuno.
            var massimo = 0f;

            for (var s = 0; s < 3; s++)
            {
                var stride = Stride[s];
                var celleX = m_larghezza / stride;

                NativeArray<float>.ReadOnly sc = m_uscite[m_idxPunteggi[s]].AsReadOnlyNativeArray();
                NativeArray<float>.ReadOnly bb = m_uscite[m_idxBox[s]].AsReadOnlyNativeArray();
                NativeArray<float>.ReadOnly kk = m_uscite[m_idxKps[s]].AsReadOnlyNativeArray();

                for (var idx = 0; idx < sc.Length; idx++)
                {
                    var punteggio = sc[idx];
                    if (punteggio > massimo) massimo = punteggio;
                    if (punteggio < m_soglia) continue;

                    // Le ancore di una stessa cella sono consecutive: e il layout
                    // di InsightFace, dove anchor_centers viene ripetuto per
                    // num_anchors sull'asse 1 e poi appiattito.
                    var cella = idx / Ancore;
                    var cx = (cella % celleX) * stride;
                    var cy = (cella / celleX) * stride;

                    var b = idx * 4;
                    var x1 = cx - bb[b + 0] * stride;
                    var y1 = cy - bb[b + 1] * stride;
                    var x2 = cx + bb[b + 2] * stride;
                    var y2 = cy + bb[b + 3] * stride;

                    if (y2 - y1 < minAltezza) continue;

                    var v = new VoltoRilevato
                    {
                        Punteggio = punteggio,
                        Box = new Rect(x1 / m_larghezza, y1 / m_altezza,
                                       (x2 - x1) / m_larghezza, (y2 - y1) / m_altezza)
                    };

                    var k = idx * 10;
                    for (var j = 0; j < 5; j++)
                    {
                        v.ScriviLandmark(j, new Vector2(
                            (cx + kk[k + 2 * j] * stride) / m_larghezza,
                            (cy + kk[k + 2 * j + 1] * stride) / m_altezza));
                    }

                    m_grezzi.Add(v);
                }
            }

            PunteggioMassimo = massimo;
        }

        private static void SoppressioneNonMassimi(List<VoltoRilevato> grezzi, List<VoltoRilevato> uscita, float soglia)
        {
            grezzi.Sort((a, b) => b.Punteggio.CompareTo(a.Punteggio));
            foreach (var c in grezzi)
            {
                var tenere = true;
                foreach (var g in uscita)
                {
                    if (IoU(c.Box, g.Box) > soglia) { tenere = false; break; }
                }
                if (tenere) uscita.Add(c);
            }
        }

        public static float IoU(Rect a, Rect b)
        {
            var x1 = Mathf.Max(a.xMin, b.xMin);
            var y1 = Mathf.Max(a.yMin, b.yMin);
            var x2 = Mathf.Min(a.xMax, b.xMax);
            var y2 = Mathf.Min(a.yMax, b.yMax);
            var inter = Mathf.Max(0f, x2 - x1) * Mathf.Max(0f, y2 - y1);
            var unione = a.width * a.height + b.width * b.height - inter;
            return unione <= 0f ? 0f : inter / unione;
        }
    }
}
