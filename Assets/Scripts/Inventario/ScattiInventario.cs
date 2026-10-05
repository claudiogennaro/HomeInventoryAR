using System;
using System.IO;
using Meta.XR;
using UnityEngine;

namespace InventarioAR
{
    /// <summary>
    /// Ritaglia dal frame della camera l'immagine di un oggetto e la salva su disco.
    ///
    /// Perche un ritaglio e non l'intero fotogramma: l'utente deve riconoscere
    /// l'oggetto, non la stanza. Un ritaglio strettp intorno all'oggetto e anche
    /// molto piu leggero da salvare e da mostrare.
    ///
    /// Il ritaglio si calcola dal mondo, non dai pixel: si proietta il centro
    /// dell'oggetto e un punto sul suo bordo nello spazio viewport della camera,
    /// e la differenza da la dimensione da tagliare. Cosi la dimensione del
    /// ritaglio si adatta da se alla distanza — un mouse a mezzo metro e un tavolo
    /// a due metri risultano inquadrati allo stesso modo.
    /// </summary>
    public static class ScattiInventario
    {
        private const string Cartella = "scatti";
        private const int LatoMassimo = 256;

        /// <summary>Margine attorno all'oggetto, in frazione della sua dimensione.</summary>
        private const float Margine = 0.45f;

        private static string CartellaScatti
        {
            get
            {
                var p = Path.Combine(Application.persistentDataPath, Cartella);
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string PercorsoDi(string nomeFile) =>
            string.IsNullOrEmpty(nomeFile) ? null : Path.Combine(CartellaScatti, nomeFile);

        /// <summary>
        /// Cattura il ritaglio dell'oggetto dal frame corrente.
        /// </summary>
        /// <returns>Nome del file salvato, o null se non e stato possibile.</returns>
        public static string Cattura(PassthroughCameraAccess camera, Vector3 posizione, float raggio, string id)
        {
            if (camera == null || !camera.IsPlaying) return null;

            var sorgente = camera.GetTexture();
            if (sorgente == null || sorgente.width <= 0) return null;

            var pose = camera.GetCameraPose();
            var centro = camera.WorldToViewportPoint(posizione, pose);

            // Se l'oggetto e fuori dall'inquadratura non c'e niente da ritagliare.
            if (centro.x < 0f || centro.x > 1f || centro.y < 0f || centro.y > 1f) return null;

            // Un punto sul bordo dell'oggetto, spostato lateralmente rispetto alla
            // camera: la sua distanza dal centro in viewport e la mezza larghezza.
            var destra = pose.rotation * Vector3.right;
            var bordo = camera.WorldToViewportPoint(posizione + destra * Mathf.Max(raggio, 0.03f), pose);

            var mezzaLarghezza = Mathf.Abs(bordo.x - centro.x) * (1f + Margine);
            if (mezzaLarghezza < 0.02f) mezzaLarghezza = 0.02f;
            if (mezzaLarghezza > 0.5f) mezzaLarghezza = 0.5f;

            var lato = Mathf.RoundToInt(mezzaLarghezza * 2f * sorgente.width);
            var x = Mathf.RoundToInt((centro.x - mezzaLarghezza) * sorgente.width);
            var y = Mathf.RoundToInt((centro.y - mezzaLarghezza) * sorgente.height);

            x = Mathf.Clamp(x, 0, sorgente.width - 1);
            y = Mathf.Clamp(y, 0, sorgente.height - 1);
            lato = Mathf.Min(lato, Mathf.Min(sorgente.width - x, sorgente.height - y));
            if (lato < 16) return null;

            try
            {
                return Ritaglia(sorgente, x, y, lato, id);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scatti] Ritaglio fallito: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Copia la regione richiesta passando da una RenderTexture.
        ///
        /// Serve perche la texture della camera non e una Texture2D leggibile: un
        /// Blit su RenderTexture e poi ReadPixels e la via che funziona per
        /// qualunque tipo di sorgente. Costa qualche millisecondo, ma succede una
        /// volta per oggetto.
        /// </summary>
        private static string Ritaglia(Texture sorgente, int x, int y, int lato, string id)
        {
            var rt = RenderTexture.GetTemporary(sorgente.width, sorgente.height, 0, RenderTextureFormat.ARGB32);
            var precedente = RenderTexture.active;

            Texture2D ritaglio = null;
            try
            {
                Graphics.Blit(sorgente, rt);
                RenderTexture.active = rt;

                ritaglio = new Texture2D(lato, lato, TextureFormat.RGB24, false);
                ritaglio.ReadPixels(new Rect(x, y, lato, lato), 0, 0);
                ritaglio.Apply(false);

                // Rimpicciolimento: 256 px bastano per riconoscere un oggetto, e
                // tengono il file sotto i 30 KB.
                if (lato > LatoMassimo) ritaglio = Riduci(ritaglio, LatoMassimo);

                var nome = $"{id}.png";
                File.WriteAllBytes(Path.Combine(CartellaScatti, nome), ritaglio.EncodeToPNG());
                Debug.Log($"[Scatti] Salvato {nome} ({lato}px)");
                return nome;
            }
            finally
            {
                RenderTexture.active = precedente;
                RenderTexture.ReleaseTemporary(rt);
                if (ritaglio != null) UnityEngine.Object.Destroy(ritaglio);
            }
        }

        private static Texture2D Riduci(Texture2D origine, int lato)
        {
            var rt = RenderTexture.GetTemporary(lato, lato, 0, RenderTextureFormat.ARGB32);
            var precedente = RenderTexture.active;
            try
            {
                Graphics.Blit(origine, rt);
                RenderTexture.active = rt;

                var ridotta = new Texture2D(lato, lato, TextureFormat.RGB24, false);
                ridotta.ReadPixels(new Rect(0, 0, lato, lato), 0, 0);
                ridotta.Apply(false);

                UnityEngine.Object.Destroy(origine);
                return ridotta;
            }
            finally
            {
                RenderTexture.active = precedente;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static Texture2D Carica(string nomeFile)
        {
            var percorso = PercorsoDi(nomeFile);
            if (percorso == null || !File.Exists(percorso)) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            return tex.LoadImage(File.ReadAllBytes(percorso)) ? tex : null;
        }

        public static void Cancella(string nomeFile)
        {
            var percorso = PercorsoDi(nomeFile);
            try { if (percorso != null && File.Exists(percorso)) File.Delete(percorso); }
            catch (Exception e) { Debug.LogWarning($"[Scatti] Cancellazione fallita: {e.Message}"); }
        }

        public static void CancellaTutti()
        {
            try { if (Directory.Exists(CartellaScatti)) Directory.Delete(CartellaScatti, true); }
            catch (Exception e) { Debug.LogWarning($"[Scatti] Pulizia fallita: {e.Message}"); }
        }
    }
}
