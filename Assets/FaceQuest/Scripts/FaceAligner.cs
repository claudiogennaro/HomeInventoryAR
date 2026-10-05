// Allineamento del volto: dalla posizione dei cinque landmark alla ritaglio
// 112x112 che ArcFace si aspetta.
//
// ArcFace non e stato addestrato su ritagli qualunque: il volto deve avere gli
// occhi, il naso e gli angoli della bocca in posizioni canoniche. Senza questo
// passaggio gli embedding di due foto della stessa persona con la testa
// inclinata in modo diverso risultano lontani, e la re-identificazione non
// funziona per un motivo che non si vede guardando le immagini.
//
// La trasformazione e una SIMILARITA (rotazione, scala uniforme, traslazione)
// ai minimi quadrati sui cinque punti. Non un'affinita generica: quella
// correggerebbe anche lo "schiacciamento" prospettico di un volto di profilo,
// deformandolo in modo che il modello non ha mai visto in addestramento.

using System;
using UnityEngine;

namespace FaceQuest
{
    public static class FaceAligner
    {
        /// <summary>Il template a 5 punti di ArcFace/InsightFace, su 112x112.</summary>
        public static readonly Vector2[] Template =
        {
            new Vector2(38.2946f, 51.6963f),
            new Vector2(73.5318f, 51.5014f),
            new Vector2(56.0252f, 71.7366f),
            new Vector2(41.5493f, 92.3655f),
            new Vector2(70.7299f, 92.2041f)
        };

        public const int Lato = 112;

        /// <summary>
        /// Calcola la trasformazione INVERSA (da pixel del ritaglio a pixel del
        /// frame sorgente), che e quella che serve allo shader.
        ///
        /// Forma chiusa invece della SVD di Umeyama: per una similarita 2D su
        /// cinque punti i due risultati coincidono (verificato) e questa non
        /// richiede algebra lineare a runtime.
        /// </summary>
        /// <param name="lm">I cinque landmark in pixel del frame, origine in alto a sinistra.</param>
        /// <param name="inversa">a, b, tx, c, d, ty della matrice inversa.</param>
        /// <param name="scalaVolto">Quanti pixel del frame entrano nei 112 del ritaglio.</param>
        public static bool CalcolaInversa(Vector2[] lm, float[] inversa, out float scalaVolto)
        {
            scalaVolto = 0f;
            if (lm == null || lm.Length < 5 || inversa == null || inversa.Length < 6) return false;

            Vector2 mediaSorg = Vector2.zero, mediaDest = Vector2.zero;
            for (var i = 0; i < 5; i++) { mediaSorg += lm[i]; mediaDest += Template[i]; }
            mediaSorg /= 5f; mediaDest /= 5f;

            float den = 0f, na = 0f, nb = 0f;
            for (var i = 0; i < 5; i++)
            {
                var u = lm[i] - mediaSorg;
                var x = Template[i] - mediaDest;
                den += u.x * u.x + u.y * u.y;
                na += u.x * x.x + u.y * x.y;
                nb += u.x * x.y - u.y * x.x;
            }

            // Cinque punti coincidenti: nessuna scala ricavabile. Succede solo su
            // rilevamenti degeneri, ma un NaN qui avvelenerebbe l'embedding.
            if (den < 1e-6f) return false;

            var a = na / den;
            var b = nb / den;
            var norma = a * a + b * b;
            if (norma < 1e-12f) return false;

            // Diretta: [a -b; b a] * p + t
            var tx = mediaDest.x - (a * mediaSorg.x - b * mediaSorg.y);
            var ty = mediaDest.y - (b * mediaSorg.x + a * mediaSorg.y);

            // Inversa di una similarita: trasposta divisa per il modulo quadro.
            var ia = a / norma;
            var ib = b / norma;   // [ia ib; -ib ia]
            inversa[0] = ia;
            inversa[1] = ib;
            inversa[2] = -(ia * tx + ib * ty);
            inversa[3] = -ib;
            inversa[4] = ia;
            inversa[5] = -(-ib * tx + ia * ty);

            // La scala diretta dice quanti pixel template per pixel sorgente:
            // il volto misura quindi 112 / s pixel nel frame.
            var s = Mathf.Sqrt(norma);
            scalaVolto = s > 1e-6f ? Lato / s : 0f;
            return true;
        }

        /// <summary>Gradi di rotazione nel piano, dalla retta fra gli occhi.</summary>
        public static float RollGradi(Vector2 occhioSx, Vector2 occhioDx)
        {
            var d = occhioDx - occhioSx;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// Wrapper sul materiale che esegue i blit affini. Un'istanza sola per
    /// pipeline: creare materiali a runtime e un modo lento di perdere memoria.
    /// </summary>
    public class BlitAffine : IDisposable
    {
        private static readonly int IdRow0 = Shader.PropertyToID("_Row0");
        private static readonly int IdRow1 = Shader.PropertyToID("_Row1");
        private static readonly int IdSrcTexel = Shader.PropertyToID("_SrcTexel");
        private static readonly int IdDstSize = Shader.PropertyToID("_DstSize");

        private readonly Material m_materiale;

        /// <param name="shader">
        /// Lo shader di allineamento. Passarlo come riferimento SERIALIZZATO e la
        /// via sicura: quello che si trova solo con Shader.Find puo non finire
        /// nella build su Android, e il sintomo sarebbero ritagli tutti neri —
        /// cioe nessun volto riconosciuto, senza un errore che lo dica.
        /// Se manca si ripiega su Shader.Find, ma lo si segnala.
        /// </param>
        public BlitAffine(Shader shader = null)
        {
            if (shader == null)
            {
                shader = Shader.Find("FaceQuest/AffineBlit");
                if (shader != null)
                    Debug.LogWarning("[FaceQuest] Shader di allineamento trovato con Shader.Find: " +
                                     "meglio assegnarlo nell'Inspector (rilancia il comando 1 del menu FaceQuest).");
            }

            if (shader == null)
            {
                Debug.LogError("[FaceQuest] Shader FaceQuest/AffineBlit non trovato: " +
                               "nessun volto potra essere ritagliato.");
                return;
            }

            m_materiale = new Material(shader);
        }

        public bool Valido => m_materiale != null;

        /// <summary>Riempie la destinazione con la sorgente intera (solo scala).</summary>
        public void Riempi(Texture sorgente, RenderTexture dest)
        {
            var inv = new[]
            {
                (float)sorgente.width / dest.width, 0f, 0f,
                0f, (float)sorgente.height / dest.height, 0f
            };
            Applica(sorgente, dest, inv);
        }

        public void Applica(Texture sorgente, RenderTexture dest, float[] inversa)
        {
            if (m_materiale == null || sorgente == null || dest == null) return;

            m_materiale.SetVector(IdRow0, new Vector4(inversa[0], inversa[1], inversa[2], 0f));
            m_materiale.SetVector(IdRow1, new Vector4(inversa[3], inversa[4], inversa[5], 0f));
            m_materiale.SetVector(IdSrcTexel, new Vector4(1f / sorgente.width, 1f / sorgente.height, 0f, 0f));
            m_materiale.SetVector(IdDstSize, new Vector4(dest.width, dest.height, 0f, 0f));

            Graphics.Blit(sorgente, dest, m_materiale);
        }

        public void Dispose()
        {
            if (m_materiale != null) UnityEngine.Object.Destroy(m_materiale);
        }
    }
}
