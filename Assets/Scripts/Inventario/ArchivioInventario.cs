using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace InventarioAR
{
    /// <summary>Una riga dell'archivio su disco.</summary>
    [Serializable]
    public class RecordOggetto
    {
        public string Id;
        public string Classe;
        public int ClasseId;
        public string UuidAncora;
        public int Osservazioni;
        public float Raggio;
        public string Scatto;
        public string VistoIl;      // ISO 8601, leggibile a occhio se serve ispezionare il file
    }

    [Serializable]
    public class DatiArchivio
    {
        public List<RecordOggetto> Oggetti = new();
    }

    /// <summary>
    /// Legge e scrive l'inventario su disco.
    ///
    /// Nota su cosa NON viene salvato: la posizione. Le coordinate di questa
    /// sessione non hanno senso nella prossima, perche l'origine del tracking
    /// cambia a ogni avvio. Quello che rende ritrovabile un oggetto e l'UUID
    /// della sua ancora spaziale: e il sistema del Quest a sapere dove sta quel
    /// punto nella stanza reale, e a restituircelo quando glielo chiediamo.
    ///
    /// Classe C# pura: nessuna dipendenza da MonoBehaviour, testabile in editor.
    /// </summary>
    public static class ArchivioInventario
    {
        private const string NomeFile = "inventario.json";

        private static string Percorso => Path.Combine(Application.persistentDataPath, NomeFile);

        /// <returns>Quanti oggetti sono finiti nel file.</returns>
        public static int Salva(IReadOnlyList<OggettoInventario> oggetti)
        {
            var dati = new DatiArchivio();

            foreach (var o in oggetti)
            {
                // Senza ancora l'oggetto non e ritrovabile: inutile scriverlo.
                if (!o.Solido || string.IsNullOrEmpty(o.UuidAncora)) continue;

                dati.Oggetti.Add(new RecordOggetto
                {
                    Id = o.Id,
                    Classe = o.Classe,
                    ClasseId = o.ClasseId,
                    UuidAncora = o.UuidAncora,
                    Osservazioni = o.Osservazioni,
                    Raggio = o.Raggio,
                    Scatto = o.Scatto,
                    VistoIl = DateTime.UtcNow.ToString("o"),
                });
            }

            try
            {
                File.WriteAllText(Percorso, JsonUtility.ToJson(dati, true));
                Debug.Log($"[Archivio] Salvati {dati.Oggetti.Count} oggetti in {Percorso}");
                return dati.Oggetti.Count;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Archivio] Salvataggio fallito: {e.Message}");
                return 0;
            }
        }

        public static DatiArchivio Carica()
        {
            try
            {
                if (!File.Exists(Percorso))
                {
                    Debug.Log("[Archivio] Nessun archivio precedente, si parte da zero.");
                    return new DatiArchivio();
                }

                var dati = JsonUtility.FromJson<DatiArchivio>(File.ReadAllText(Percorso)) ?? new DatiArchivio();
                Debug.Log($"[Archivio] Caricati {dati.Oggetti.Count} oggetti da {Percorso}");
                return dati;
            }
            catch (Exception e)
            {
                // Un archivio illeggibile non deve impedire all'app di partire:
                // meglio ricominciare a imparare che non partire affatto.
                Debug.LogError($"[Archivio] Lettura fallita, riparto vuoto: {e.Message}");
                return new DatiArchivio();
            }
        }

        public static void Cancella()
        {
            try
            {
                if (File.Exists(Percorso)) File.Delete(Percorso);
                Debug.Log("[Archivio] Archivio cancellato.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Archivio] Cancellazione fallita: {e.Message}");
            }
        }
    }
}
