// I ritagli dei volti su disco, cifrati.
//
// Un ritaglio 112x112 e la foto di una persona: sta accanto agli embedding e
// riceve lo stesso trattamento. L'estensione .bin invece di .png non e una
// finezza: un file che si chiama .png e che una galleria non riesce ad aprire
// e piu confondente di uno che dichiara di essere dati opachi.

using System;
using System.IO;
using UnityEngine;

namespace FaceQuest
{
    public static class ArchivioVolti
    {
        private static string Cartella
        {
            get
            {
                var p = Path.Combine(Cifratura.Cartella, "volti");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string Salva(Texture2D volto, string personID)
        {
            if (volto == null) return null;
            try
            {
                var nome = $"{personID}.bin";
                Cifratura.Cifra(Path.Combine(Cartella, nome), volto.EncodeToPNG());
                return nome;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FaceQuest] Salvataggio del volto fallito: {e.Message}");
                return null;
            }
        }

        public static Texture2D Carica(string nomeFile)
        {
            if (string.IsNullOrEmpty(nomeFile)) return null;
            var dati = Cifratura.Decifra(Path.Combine(Cartella, nomeFile));
            if (dati == null) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            return tex.LoadImage(dati) ? tex : null;
        }

        public static void Cancella(string nomeFile)
        {
            if (string.IsNullOrEmpty(nomeFile)) return;
            try
            {
                var p = Path.Combine(Cartella, nomeFile);
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FaceQuest] Cancellazione del volto fallita: {e.Message}");
            }
        }
    }
}
