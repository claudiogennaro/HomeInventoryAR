// Persistenza del database fra una sessione e l'altra.
//
// Cosa sopravvive: solo le persone a cui l'utente ha assegnato un nome. Le
// identita anonime (AA001, AA002, ...) sono un meccanismo interno di conteggio,
// non una decisione dell'utente, e tenerle sul disco significherebbe accumulare
// dati biometrici di chiunque sia passato davanti al visore. Alla chiusura
// vengono cancellate.
//
// Formato binario scritto a mano e non JSON: JsonUtility non serializza una
// lista di array (lo storico degli embedding), e i float in testo occupano il
// triplo. Il file finisce comunque cifrato, quindi la leggibilita non e un
// vantaggio che si perde.
//
// Il salvataggio avviene anche su OnApplicationPause: su Horizon OS togliersi
// il visore o passare a un'altra app non produce sempre un OnApplicationQuit.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FaceQuest
{
    public class PersonPersistenceManager : MonoBehaviour
    {
        private const string NomeFile = "persone.bin";
        private const int Versione = 1;

        [SerializeField] private PersonDatabase m_database;
        [SerializeField, Tooltip("Se attivo, il database vive solo in memoria e su disco non si scrive nulla. Spento: le persone battezzate sopravvivono alla chiusura, cifrate.")]
        private bool m_soloInMemoria;
        [SerializeField, Tooltip("Solo con la persistenza attiva: all'avvio cancella l'archivio invece di caricarlo.")]
        private bool m_azzeraArchivioAllAvvio;

        /// <summary>
        /// In modalita demo nulla viene scritto su disco: ne il database, ne i
        /// ritagli dei volti. Chi salva un file deve chiederlo qui prima.
        /// </summary>
        public bool SoloInMemoria => m_soloInMemoria;

        private string Percorso => Path.Combine(Cifratura.Cartella, NomeFile);

        public void Collega(PersonDatabase db) => m_database = db;

        private void Start()
        {
            if (m_soloInMemoria)
            {
                // Non ci limitiamo a non salvare: cancelliamo anche quello che le
                // prove precedenti hanno lasciato.
                //
                // Per una demo questa e la posizione piu pulita possibile, non una
                // rinuncia: nessun dato biometrico tocca lo storage, quindi non
                // esiste nessuna chiave da proteggere e cade del tutto il limite
                // dichiarato altrove (chiave accanto ai dati, difesa solo da chi
                // copia i file e non da chi ha accesso di root). Cio che non
                // viene scritto non si puo leggere.
                Cifratura.DistruggiTutto();
                Debug.Log("[FaceQuest] Modalita demo: database solo in memoria, su disco non si scrive nulla.");
                return;
            }

            if (m_azzeraArchivioAllAvvio)
            {
                Cifratura.DistruggiTutto();
                Debug.Log("[FaceQuest] Archivio persistente azzerato su richiesta.");
                return;
            }
            Carica();
        }

        private void OnApplicationPause(bool pausa)
        {
            if (pausa && !m_soloInMemoria) Salva();
        }

        private void OnApplicationQuit()
        {
            if (m_soloInMemoria)
            {
                Debug.Log("[FaceQuest] Chiusura in modalita demo: il database sparisce con il processo.");
                return;
            }

            // Prima si buttano le anonime, poi si salva: cosi il file contiene
            // esattamente le persone che l'utente ha voluto ricordare.
            var rimosse = m_database != null ? m_database.RimuoviAnonime() : 0;
            if (rimosse > 0) Debug.Log($"[FaceQuest] {rimosse} identita anonime scartate alla chiusura.");
            Salva();
        }

        public void Salva()
        {
            if (m_soloInMemoria || m_database == null) return;

            try
            {
                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(new[] { 'F', 'Q', 'D', 'B' });
                    w.Write(Versione);

                    var nominate = new List<Persona>();
                    foreach (var p in m_database.Persone) if (p.Persistent) nominate.Add(p);

                    w.Write(nominate.Count);
                    foreach (var p in nominate)
                    {
                        w.Write(p.PersonID ?? string.Empty);
                        w.Write(p.UserAssignedName ?? string.Empty);
                        w.Write(p.RepresentativeFaceFile ?? string.Empty);

                        var dim = p.FaceEmbedding?.Length ?? 0;
                        w.Write(dim);
                        for (var i = 0; i < dim; i++) w.Write(p.FaceEmbedding[i]);

                        w.Write(p.EmbeddingHistory.Count);
                        for (var i = 0; i < p.EmbeddingHistory.Count; i++)
                        {
                            var e = p.EmbeddingHistory[i];
                            w.Write(e.Length);
                            for (var d = 0; d < e.Length; d++) w.Write(e[d]);
                            w.Write(i < p.QualityHistory.Count ? p.QualityHistory[i] : 0.5f);
                        }

                        w.Write(p.FirstSeenTicks);
                        w.Write(p.LastSeenTicks);
                        w.Write(p.ObservationCount);
                    }
                }

                Cifratura.Cifra(Percorso, ms.ToArray());
                Debug.Log($"[FaceQuest] Archivio salvato e cifrato in {Percorso}.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FaceQuest] Salvataggio fallito: {e.Message}");
            }
        }

        public void Carica()
        {
            if (m_soloInMemoria || m_database == null) return;

            var dati = Cifratura.Decifra(Percorso);
            if (dati == null)
            {
                Debug.Log("[FaceQuest] Nessun archivio persistente da caricare.");
                return;
            }

            try
            {
                using var ms = new MemoryStream(dati);
                using var r = new BinaryReader(ms);

                var magic = r.ReadChars(4);
                if (magic[0] != 'F' || magic[1] != 'Q' || magic[2] != 'D' || magic[3] != 'B')
                {
                    Debug.LogError("[FaceQuest] Archivio non riconosciuto.");
                    return;
                }

                var versione = r.ReadInt32();
                if (versione != Versione)
                {
                    Debug.LogWarning($"[FaceQuest] Archivio di versione {versione}, attesa {Versione}: ignorato.");
                    return;
                }

                var n = r.ReadInt32();
                for (var i = 0; i < n; i++)
                {
                    var p = new Persona
                    {
                        PersonID = r.ReadString(),
                        UserAssignedName = r.ReadString(),
                        RepresentativeFaceFile = r.ReadString()
                    };
                    if (p.RepresentativeFaceFile.Length == 0) p.RepresentativeFaceFile = null;

                    var dim = r.ReadInt32();
                    p.FaceEmbedding = new float[dim];
                    for (var d = 0; d < dim; d++) p.FaceEmbedding[d] = r.ReadSingle();

                    var storici = r.ReadInt32();
                    for (var s = 0; s < storici; s++)
                    {
                        var len = r.ReadInt32();
                        var e = new float[len];
                        for (var d = 0; d < len; d++) e[d] = r.ReadSingle();
                        p.EmbeddingHistory.Add(e);
                        p.QualityHistory.Add(r.ReadSingle());
                    }

                    p.FirstSeenTicks = r.ReadInt64();
                    p.LastSeenTicks = r.ReadInt64();
                    p.ObservationCount = r.ReadInt32();
                    p.Persistent = true;
                    p.Anteprima = ArchivioVolti.Carica(p.RepresentativeFaceFile);

                    m_database.Persone.Add(p);
                }

                m_database.RiallineaContatore();
                m_database.NotificaCambio();
                Debug.Log($"[FaceQuest] Caricate {n} persone dall'archivio cifrato.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FaceQuest] Lettura dell'archivio fallita: {e.Message}");
            }
        }

        /// <summary>Cancella tutto, su disco e in memoria.</summary>
        public void CancellaArchivio()
        {
            if (m_database != null) m_database.CancellaTutto();
            Debug.Log("[FaceQuest] Archivio cancellato per intero.");
        }
    }
}
