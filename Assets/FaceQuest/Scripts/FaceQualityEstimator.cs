// Giudizio di qualita del volto, prima di lasciargli toccare il template.
//
// Il punto non e scartare i volti brutti: e evitare che un volto di profilo,
// mosso o in controluce entri nel template di una persona. Un embedding di
// bassa qualita sposta il rappresentante verso il centro dello spazio, dove
// somiglia un po' a tutti: dopo qualche osservazione cattiva due persone
// diverse iniziano a scambiarsi. Meglio riconoscere piu tardi che imparare
// male.
//
// Le misure sono volutamente povere e locali (nessuna seconda rete): la
// nitidezza dalla varianza del laplaciano, la luminosita dalla media, la posa
// dalla geometria dei cinque landmark. L'occlusione non e misurata
// direttamente: la si intercetta di riflesso, perche un volto coperto perde
// punteggio nel detector e simmetria nei landmark.

using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FaceQuest
{
    public class FaceQualityEstimator : MonoBehaviour
    {
        [Header("Soglie di accettazione")]
        [SerializeField, Tooltip("Lato minimo del volto nel frame, in pixel.")]
        private float m_latoMinimo = 46f;
        [SerializeField, Tooltip("Lato a cui la dimensione non da piu vantaggio.")]
        private float m_latoOttimo = 110f;
        [SerializeField] private float m_nitidezzaMinima = 0.22f;
        [SerializeField] private float m_yawMassimo = 38f;
        [SerializeField] private float m_pitchMassimo = 28f;
        [SerializeField, Tooltip("Solo guardia contro l'assurdo (volto capovolto): il roll non penalizza, l'allineamento lo corregge.")]
        private float m_rollMassimo = 75f;
        [SerializeField] private float m_luminositaMinima = 0.16f;
        [SerializeField] private float m_luminositaMassima = 0.93f;
        [SerializeField, Tooltip("Punteggio complessivo minimo per aggiornare il template. In una stanza di casa, con luce scarsa, 0,5 era irraggiungibile.")]
        private float m_punteggioMinimo = 0.42f;

        private NativeArray<Color32> m_pixel;
        private bool m_occupato;

        public QualitaVolto Risultato { get; private set; }

        private void OnDestroy()
        {
            // Liberare l'array mentre una lettura dalla GPU e ancora in volo e un
            // crash nativo, non un'eccezione: il driver scrive in memoria che non
            // esiste piu. Si aspetta che tutte le richieste finiscano.
            if (!m_pixel.IsCreated) return;
            AsyncGPUReadback.WaitAllRequests();
            m_pixel.Dispose();
        }

        /// <summary>
        /// Valuta il volto allineato. La lettura dalla GPU e asincrona: un
        /// ReadPixels sincrono su ogni volto costerebbe piu dell'inferenza.
        /// </summary>
        public IEnumerator Valuta(RenderTexture allineato, VoltoRilevato rilevamento, float latoPixel)
        {
            if (m_occupato) yield break;
            m_occupato = true;

            var q = new QualitaVolto { LatoPixel = latoPixel };
            PosaDaLandmark(rilevamento, out q.Yaw, out q.Pitch, out q.Roll);

            var n = allineato.width * allineato.height;
            if (!m_pixel.IsCreated || m_pixel.Length != n)
            {
                if (m_pixel.IsCreated) m_pixel.Dispose();
                m_pixel = new NativeArray<Color32>(n, Allocator.Persistent);
            }

            var richiesta = AsyncGPUReadback.RequestIntoNativeArray(ref m_pixel, allineato);
            while (!richiesta.done) yield return null;

            if (richiesta.hasError)
            {
                // Senza pixel si giudica solo su dimensione e posa: meglio di niente,
                // ma si alza l'asticella perche manca metà dell'informazione.
                q.Nitidezza = 0.5f;
                q.Luminosita = 0.5f;
            }
            else
            {
                MisuraPixel(m_pixel, allineato.width, allineato.height, out q.Nitidezza, out q.Luminosita);
            }

            Giudica(ref q, rilevamento.Punteggio);
            Risultato = q;
            m_occupato = false;
        }

        private void Giudica(ref QualitaVolto q, float punteggioDetector)
        {
            var pDim = Mathf.InverseLerp(m_latoMinimo, m_latoOttimo, q.LatoPixel);
            var pNit = Mathf.Clamp01(q.Nitidezza);
            var pLum = 1f - Mathf.Clamp01(Mathf.Abs(q.Luminosita - 0.5f) * 2f);
            // La posa pesa solo per yaw e pitch, NON per il roll.
            //
            // Il roll e la rotazione nel piano dell'immagine, ed e esattamente
            // cio che l'allineamento corregge per costruzione: la similarita sui
            // cinque punti raddrizza il volto prima di darlo ad ArcFace. Contarlo
            // come difetto era un errore di ragionamento, e si e visto subito
            // nella prova reale: una persona distesa sul divano ha 40-60 gradi di
            // roll, il punteggio crollava e nessuna identita veniva mai creata,
            // pur riconoscendo il volto con 0,78 di confidenza.
            //
            // Yaw e pitch sono un altro discorso: sono rotazioni FUORI dal piano,
            // nascondono meta del volto e nessuna trasformazione 2D le annulla.
            var pPosa = 1f - Mathf.Clamp01(Mathf.Max(
                Mathf.Abs(q.Yaw) / Mathf.Max(1f, m_yawMassimo),
                Mathf.Abs(q.Pitch) / Mathf.Max(1f, m_pitchMassimo)));

            q.Punteggio = 0.28f * pDim + 0.24f * pNit + 0.14f * pLum + 0.28f * pPosa
                        + 0.06f * Mathf.Clamp01(punteggioDetector);

            q.Motivo = null;
            if (q.LatoPixel < m_latoMinimo) q.Motivo = $"volto piccolo ({q.LatoPixel:0} px)";
            else if (q.Nitidezza < m_nitidezzaMinima) q.Motivo = $"mosso o sfocato ({q.Nitidezza:0.00})";
            else if (Mathf.Abs(q.Yaw) > m_yawMassimo) q.Motivo = $"troppo di profilo (yaw {q.Yaw:0}°)";
            else if (Mathf.Abs(q.Pitch) > m_pitchMassimo) q.Motivo = $"testa alzata o abbassata (pitch {q.Pitch:0}°)";
            else if (Mathf.Abs(q.Roll) > m_rollMassimo) q.Motivo = $"volto quasi capovolto (roll {q.Roll:0}°)";
            else if (q.Luminosita < m_luminositaMinima) q.Motivo = "troppo buio";
            else if (q.Luminosita > m_luminositaMassima) q.Motivo = "sovraesposto";
            else if (q.Punteggio < m_punteggioMinimo) q.Motivo = $"qualita complessiva bassa ({q.Punteggio:0.00})";

            q.Accettabile = q.Motivo == null;
        }

        /// <summary>
        /// Nitidezza e luminosita sul ritaglio allineato.
        ///
        /// Il laplaciano si calcola solo sulla zona centrale (dove stanno occhi,
        /// naso e bocca): il bordo del ritaglio contiene capelli e sfondo, che
        /// sono nitidi anche quando il volto e mosso.
        /// </summary>
        private static void MisuraPixel(NativeArray<Color32> px, int w, int h, out float nitidezza, out float luminosita)
        {
            var x0 = w / 4; var x1 = w - w / 4;
            var y0 = h / 4; var y1 = h - h / 4;

            double somma = 0, sommaLap = 0, sommaLap2 = 0;
            var n = 0; var nLap = 0;

            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var g = Grigio(px[y * w + x]);
                    somma += g; n++;

                    if (x <= x0 || x >= x1 - 1 || y <= y0 || y >= y1 - 1) continue;
                    var lap = 4f * g
                            - Grigio(px[y * w + x - 1]) - Grigio(px[y * w + x + 1])
                            - Grigio(px[(y - 1) * w + x]) - Grigio(px[(y + 1) * w + x]);
                    sommaLap += lap; sommaLap2 += (double)lap * lap; nLap++;
                }
            }

            luminosita = n > 0 ? (float)(somma / n) / 255f : 0.5f;

            if (nLap < 16) { nitidezza = 0.5f; return; }
            var media = sommaLap / nLap;
            var varianza = sommaLap2 / nLap - media * media;

            // 300 di varianza e la soglia empirica sopra la quale un volto e
            // "abbastanza inciso" alla scala di 112x112.
            nitidezza = Mathf.Clamp01((float)(varianza / 300.0));
        }

        private static float Grigio(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>
        /// Posa approssimata dai cinque landmark.
        ///
        /// Non e una stima 3D: e la geometria che basta per decidere se buttare
        /// via il frame. Lo yaw viene dallo scostamento del naso dall'asse fra
        /// gli occhi, il pitch dalla sua altezza relativa fra occhi e bocca, il
        /// roll dall'inclinazione della retta fra gli occhi.
        /// </summary>
        public static void PosaDaLandmark(VoltoRilevato v, out float yaw, out float pitch, out float roll)
        {
            var oSx = v.L0; var oDx = v.L1; var naso = v.L2;
            var bocca = (v.L3 + v.L4) * 0.5f;
            var centroOcchi = (oSx + oDx) * 0.5f;

            roll = FaceAligner.RollGradi(oSx, oDx);

            var distOcchi = Vector2.Distance(oSx, oDx);
            if (distOcchi < 1e-5f) { yaw = 0f; pitch = 0f; return; }

            // Si lavora in un sistema allineato agli occhi, cosi il roll non
            // sporca la misura di yaw e pitch.
            var ex = (oDx - oSx) / distOcchi;
            var ey = new Vector2(-ex.y, ex.x);

            var dNaso = naso - centroOcchi;
            var nx = Vector2.Dot(dNaso, ex) / distOcchi;
            var ny = Vector2.Dot(dNaso, ey) / distOcchi;

            var dBocca = bocca - centroOcchi;
            var by = Vector2.Dot(dBocca, ey) / distOcchi;

            yaw = Mathf.Clamp(nx * 140f, -90f, 90f);

            // Nel template il naso sta al 49% della distanza occhi-bocca.
            var r = Mathf.Abs(by) > 1e-5f ? ny / by : 0.494f;
            pitch = Mathf.Clamp((r - 0.494f) * 150f, -90f, 90f);
        }
    }
}
