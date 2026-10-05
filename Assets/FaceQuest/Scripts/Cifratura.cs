// Cifratura dei dati biometrici a riposo.
//
// Un embedding facciale e un dato biometrico: non e reversibile in una foto,
// ma identifica una persona in modo stabile, quindi va trattato come tale.
// Tutto quello che finisce su disco — template, storico, ritagli dei volti —
// passa da qui.
//
// Limite da dichiarare, non da nascondere: la chiave e generata a caso al primo
// avvio e sta nello storage privato dell'app, accanto ai dati. Protegge da chi
// copia i file (per esempio via adb pull su un visore sbloccato), NON da chi ha
// accesso di root al visore. Per una protezione vera la chiave andrebbe nel
// Keystore Android con attestazione hardware, che richiede codice nativo e non
// e coperto da questo prototipo.
//
// AES-256-CBC con IV casuale per ogni file, piu HMAC-SHA256 su IV+testo cifrato:
// senza l'HMAC un file corrotto o manomesso si decifrerebbe in numeri casuali
// che il matcher prenderebbe per embedding validi.

using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace FaceQuest
{
    public static class Cifratura
    {
        private const string NomeChiave = "chiave.bin";
        private static byte[] s_chiave;   // 32 byte AES + 32 byte HMAC

        public static string Cartella
        {
            get
            {
                var p = Path.Combine(Application.persistentDataPath, "facequest");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        private static byte[] Chiave()
        {
            if (s_chiave != null) return s_chiave;

            var percorso = Path.Combine(Cartella, NomeChiave);
            if (File.Exists(percorso))
            {
                var letta = File.ReadAllBytes(percorso);
                if (letta.Length == 64) { s_chiave = letta; return s_chiave; }
                Debug.LogWarning("[FaceQuest] Chiave di cifratura non valida: ne genero una nuova, i dati vecchi diventano illeggibili.");
            }

            s_chiave = new byte[64];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(s_chiave);
            File.WriteAllBytes(percorso, s_chiave);
            return s_chiave;
        }

        public static void Cifra(string percorso, byte[] chiaro)
        {
            var k = Chiave();
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var chiaveAes = new byte[32];
            var chiaveMac = new byte[32];
            Buffer.BlockCopy(k, 0, chiaveAes, 0, 32);
            Buffer.BlockCopy(k, 32, chiaveMac, 0, 32);
            aes.Key = chiaveAes;
            aes.GenerateIV();

            byte[] cifrato;
            using (var enc = aes.CreateEncryptor())
                cifrato = enc.TransformFinalBlock(chiaro, 0, chiaro.Length);

            using var hmac = new HMACSHA256(chiaveMac);
            var corpo = new byte[aes.IV.Length + cifrato.Length];
            Buffer.BlockCopy(aes.IV, 0, corpo, 0, aes.IV.Length);
            Buffer.BlockCopy(cifrato, 0, corpo, aes.IV.Length, cifrato.Length);
            var tag = hmac.ComputeHash(corpo);

            using var fs = new FileStream(percorso, FileMode.Create, FileAccess.Write);
            fs.Write(tag, 0, tag.Length);       // 32 byte
            fs.Write(corpo, 0, corpo.Length);   // 16 byte di IV + cifrato
        }

        public static byte[] Decifra(string percorso)
        {
            if (!File.Exists(percorso)) return null;

            var tutto = File.ReadAllBytes(percorso);
            if (tutto.Length < 32 + 16 + 16) return null;

            var k = Chiave();
            var chiaveAes = new byte[32];
            var chiaveMac = new byte[32];
            Buffer.BlockCopy(k, 0, chiaveAes, 0, 32);
            Buffer.BlockCopy(k, 32, chiaveMac, 0, 32);

            var tag = new byte[32];
            var corpo = new byte[tutto.Length - 32];
            Buffer.BlockCopy(tutto, 0, tag, 0, 32);
            Buffer.BlockCopy(tutto, 32, corpo, 0, corpo.Length);

            using (var hmac = new HMACSHA256(chiaveMac))
            {
                var atteso = hmac.ComputeHash(corpo);
                var uguali = atteso.Length == tag.Length;
                // Confronto a tempo costante: non e paranoia gratuita, e la forma
                // corretta di confrontare un MAC.
                var diff = 0;
                for (var i = 0; uguali && i < tag.Length; i++) diff |= atteso[i] ^ tag[i];
                if (!uguali || diff != 0)
                {
                    Debug.LogError("[FaceQuest] Il file cifrato non supera il controllo di integrita: ignorato.");
                    return null;
                }
            }

            var iv = new byte[16];
            Buffer.BlockCopy(corpo, 0, iv, 0, 16);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = chiaveAes;
            aes.IV = iv;

            try
            {
                using var dec = aes.CreateDecryptor();
                return dec.TransformFinalBlock(corpo, 16, corpo.Length - 16);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FaceQuest] Decifratura fallita: {e.Message}");
                return null;
            }
        }

        /// <summary>Cancella chiave e dati: dopo questa chiamata nulla e piu recuperabile.</summary>
        public static void DistruggiTutto()
        {
            s_chiave = null;
            try
            {
                if (Directory.Exists(Cartella)) Directory.Delete(Cartella, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FaceQuest] Pulizia della cartella cifrata fallita: {e.Message}");
            }
        }
    }
}
