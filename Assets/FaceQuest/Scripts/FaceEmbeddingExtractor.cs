// Embedding biometrico con MobileFaceNet addestrato con ArcFace loss
// (w600k_mbf di InsightFace, 512 dimensioni).
//
// Perche questo e non ArcFace-R50 o AdaFace: su Snapdragon XR2 Gen 2 conta il
// costo per volto, e qui la rete e ~13 MB e poche decine di MFLOP. La verifica
// fatta prima di scegliere, su volti reali ripresi a scala e luminosita
// diverse, ha dato somiglianza 0.82-0.94 per la stessa persona e al massimo
// 0.22 fra persone diverse: un margine cosi ampio rende inutile spendere dieci
// volte tanto per un modello piu grande. AdaFace e MagFace hanno vantaggi su
// volti di bassa qualita, ma nessuno dei due ha un ONNX mobile mantenuto e
// verificabile con la stessa facilita.
//
// Come per il detector, la normalizzazione (x*255-127.5)/127.5 e cucita nel
// grafo: in ingresso va l'immagine allineata in 0..1.

using System;
using System.Collections;
using Unity.InferenceEngine;
using UnityEngine;

namespace FaceQuest
{
    public class FaceEmbeddingExtractor : MonoBehaviour
    {
        [SerializeField] private ModelAsset m_modello;
        [SerializeField, Tooltip("Nome del modello in Assets/FaceQuest/Resources, usato se il riferimento sopra e vuoto.")]
        private string m_modelloDaRisorse = "arcface_mbf_112";
        [SerializeField, Tooltip("Vedi FaceDetector: CPU e la configurazione verificata su questo visore.")]
        private BackendType m_backend = BackendType.CPU;

        private Worker m_worker;
        private int m_lato = 112;
        private int m_dimensioni = 512;
        private bool m_occupato;

        public int Lato => m_lato;
        public int Dimensioni => m_dimensioni;
        public bool Pronto => m_worker != null;

        /// <summary>Assegnazione diretta del modello, usata dal costruttore di scena.</summary>
        public void ImpostaModello(ModelAsset modello) => m_modello = modello;

        /// <summary>L'ultimo embedding calcolato, gia normalizzato L2.</summary>
        public float[] Ultimo { get; private set; }

        private void Awake()
        {
            // Vedi FaceDetector: Resources e la seconda strada, quella che non
            // dipende da un riferimento serializzato.
            if (m_modello == null && !string.IsNullOrEmpty(m_modelloDaRisorse))
                m_modello = Resources.Load<ModelAsset>(m_modelloDaRisorse);

            if (m_modello == null)
            {
                Debug.LogError($"[FaceQuest] FaceEmbeddingExtractor senza modello: ne riferimento in scena " +
                               $"ne Resources/{m_modelloDaRisorse}.");
                return;
            }

            try
            {
                var model = ModelLoader.Load(m_modello);
                var shape = model.inputs[0].shape;
                m_lato = shape.Get(2);
                m_worker = new Worker(model, m_backend);
                Ultimo = new float[m_dimensioni];

                Debug.Log($"[FaceQuest] Embedder caricato: ingresso {m_lato}x{m_lato}, backend {m_backend}.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FaceQuest] Caricamento dell'embedder fallito: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            if (m_worker == null) return;
            m_worker.PeekOutput(0)?.CompleteAllPendingOperations();
            m_worker.Dispose();
            m_worker = null;
        }

        /// <summary>
        /// Calcola l'embedding del volto allineato. Il risultato finisce in
        /// <see cref="Ultimo"/> e viene restituito true solo se e valido.
        /// </summary>
        public IEnumerator Estrai(Texture voltoAllineato, Action<bool> esito)
        {
            if (m_worker == null || voltoAllineato == null || m_occupato)
            {
                esito?.Invoke(false);
                yield break;
            }

            m_occupato = true;
            var ok = false;
            try
            {
                using var tensore = new Tensor<float>(new TensorShape(1, 3, m_lato, m_lato));
                TextureConverter.ToTensor(voltoAllineato, tensore);

                m_worker.Schedule(tensore);

                var att = (m_worker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
                while (!att.IsCompleted) yield return null;
                using var uscita = att.GetResult();

                var arr = uscita.AsReadOnlyNativeArray();
                if (arr.Length != m_dimensioni)
                {
                    if (Ultimo == null || Ultimo.Length != arr.Length) Ultimo = new float[arr.Length];
                    m_dimensioni = arr.Length;
                }

                // Normalizzazione L2: da qui in poi il confronto fra due
                // embedding e un prodotto scalare, e la soglia ha lo stesso
                // significato per tutti i volti.
                var norma = 0f;
                for (var i = 0; i < arr.Length; i++) norma += arr[i] * arr[i];
                norma = Mathf.Sqrt(norma);
                if (norma < 1e-6f) { esito?.Invoke(false); yield break; }

                for (var i = 0; i < arr.Length; i++) Ultimo[i] = arr[i] / norma;
                ok = true;
            }
            finally
            {
                m_occupato = false;
            }

            esito?.Invoke(ok);
        }

        /// <summary>Somiglianza fra due embedding normalizzati: coseno = prodotto scalare.</summary>
        public static float Somiglianza(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return -1f;
            var s = 0f;
            for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
            return s;
        }
    }
}
