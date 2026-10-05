// Il database delle persone incontrate nella sessione.
//
// Due scelte che decidono se la re-identificazione funziona:
//
// 1. Ogni persona non ha UN embedding ma uno storico. Un volto visto solo di
//    fronte in piena luce non si riconosce piu di tre quarti al buio: serve un
//    ricordo per ciascuna delle condizioni in cui l'abbiamo visto. Il template
//    aggregato resta, ma come riassunto, non come unica memoria.
//
// 2. Quando lo storico e pieno non si butta il piu vecchio ma il piu
//    RIDONDANTE: si cerca la coppia di ricordi piu simili fra loro e si scarta
//    quello dei due con qualita minore. Buttare il piu vecchio riempirebbe lo
//    storico di dodici fotogrammi quasi identici degli ultimi due secondi, che
//    e come avere un solo embedding pagandone dodici.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceQuest
{
    public class PersonDatabase : MonoBehaviour
    {
        public enum ModalitaTemplate
        {
            /// <summary>Media dei ricordi pesata sulla qualita, poi normalizzata L2.</summary>
            MediaPesata,
            /// <summary>Il ricordo piu "centrale" fra quelli in memoria.</summary>
            Medoide
        }

        [SerializeField, Tooltip("Quanti embedding tenere per persona.")]
        private int m_dimensioneStorico = 12;
        [SerializeField] private ModalitaTemplate m_modalita = ModalitaTemplate.MediaPesata;

        private readonly List<Persona> m_persone = new List<Persona>();
        private int m_contatore;

        public List<Persona> Persone => m_persone;

        /// <summary>Scatta a ogni modifica: il pannello e le box si ridisegnano da qui.</summary>
        public event Action Cambiato;

        public void NotificaCambio() => Cambiato?.Invoke();

        public Persona PerID(string id)
        {
            foreach (var p in m_persone) if (p.PersonID == id) return p;
            return null;
        }

        /// <summary>
        /// Identificativi progressivi AA001, AA002, ... e dopo AA999 si passa ad
        /// AB001. Temporanei per costruzione: restano finche l'utente non
        /// assegna un nome, e alla chiusura dell'app le identita che ne hanno
        /// ancora uno vengono buttate.
        ///
        /// Questa e la chiave INTERNA e non si vede: l'utente legge "Ospite 3"
        /// (Persona.DisplayName). Il contatore che le genera e lo stesso, quindi
        /// AA003 e Ospite 3 sono la stessa persona e un log resta leggibile
        /// accanto a cio che si vede nel visore.
        ///
        /// Il numero non viene mai riciclato quando un'anonima scade: e sgradevole
        /// vedere due persone diverse chiamarsi Ospite 3 nella stessa sessione, e
        /// piu sgradevole ancora vedere le etichette gia sul pannello cambiare
        /// numero da sole.
        /// </summary>
        private string ProssimoID()
        {
            while (true)
            {
                m_contatore++;
                var blocco = (m_contatore - 1) / 999;
                var numero = (m_contatore - 1) % 999 + 1;
                var id = $"{(char)('A' + blocco / 26)}{(char)('A' + blocco % 26)}{numero:000}";
                if (PerID(id) == null) return id;
            }
        }

        /// <summary>Allinea il contatore agli ID già presenti (dopo un caricamento).</summary>
        public void RiallineaContatore()
        {
            foreach (var p in m_persone)
            {
                var id = p.PersonID;
                if (id == null || id.Length != 5) continue;
                if (id[0] < 'A' || id[0] > 'Z' || id[1] < 'A' || id[1] > 'Z') continue;
                if (!int.TryParse(id.Substring(2), out var n)) continue;
                var blocco = (id[0] - 'A') * 26 + (id[1] - 'A');
                var indice = blocco * 999 + n;
                if (indice > m_contatore) m_contatore = indice;
            }
        }

        public Persona Crea(float[] embedding, float qualita)
        {
            var ora = DateTime.UtcNow.Ticks;
            var p = new Persona
            {
                PersonID = ProssimoID(),
                NumeroOspite = m_contatore,
                UserAssignedName = string.Empty,
                FaceEmbedding = (float[])embedding.Clone(),
                FirstSeenTicks = ora,
                LastSeenTicks = ora,
                ObservationCount = 1,
                Persistent = false
            };
            p.EmbeddingHistory.Add((float[])embedding.Clone());
            p.QualityHistory.Add(qualita);
            m_persone.Add(p);
            Cambiato?.Invoke();
            return p;
        }

        /// <summary>
        /// Registra un'osservazione. <paramref name="aggiungiAlTemplate"/> viene
        /// da FaceQualityEstimator: i frame scadenti contano come presenza ma non
        /// entrano nella memoria biometrica.
        /// </summary>
        public void Osserva(Persona p, float[] embedding, float qualita, bool aggiungiAlTemplate)
        {
            if (p == null) return;

            p.LastSeenTicks = DateTime.UtcNow.Ticks;
            p.ObservationCount++;

            if (!aggiungiAlTemplate || embedding == null)
            {
                Cambiato?.Invoke();
                return;
            }

            p.EmbeddingHistory.Add((float[])embedding.Clone());
            p.QualityHistory.Add(qualita);

            if (p.EmbeddingHistory.Count > m_dimensioneStorico) PotaStorico(p);
            RicalcolaTemplate(p);
            Cambiato?.Invoke();
        }

        private static void PotaStorico(Persona p)
        {
            var n = p.EmbeddingHistory.Count;
            var migliorSim = -2f;
            int a = -1, b = -1;

            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    var s = FaceEmbeddingExtractor.Somiglianza(p.EmbeddingHistory[i], p.EmbeddingHistory[j]);
                    if (s > migliorSim) { migliorSim = s; a = i; b = j; }
                }
            }

            if (a < 0) { p.EmbeddingHistory.RemoveAt(0); p.QualityHistory.RemoveAt(0); return; }

            var scarta = p.QualityHistory[a] <= p.QualityHistory[b] ? a : b;
            p.EmbeddingHistory.RemoveAt(scarta);
            p.QualityHistory.RemoveAt(scarta);
        }

        private void RicalcolaTemplate(Persona p)
        {
            if (p.EmbeddingHistory.Count == 0) return;

            if (m_modalita == ModalitaTemplate.Medoide)
            {
                var migliore = 0; var miglioreSomma = float.NegativeInfinity;
                for (var i = 0; i < p.EmbeddingHistory.Count; i++)
                {
                    var somma = 0f;
                    for (var j = 0; j < p.EmbeddingHistory.Count; j++)
                    {
                        if (i == j) continue;
                        somma += FaceEmbeddingExtractor.Somiglianza(p.EmbeddingHistory[i], p.EmbeddingHistory[j]);
                    }
                    somma += p.QualityHistory[i];   // a pari centralita vince il frame migliore
                    if (somma > miglioreSomma) { miglioreSomma = somma; migliore = i; }
                }
                p.FaceEmbedding = (float[])p.EmbeddingHistory[migliore].Clone();
                return;
            }

            var dim = p.EmbeddingHistory[0].Length;
            var acc = new float[dim];
            for (var i = 0; i < p.EmbeddingHistory.Count; i++)
            {
                var peso = Mathf.Max(0.05f, i < p.QualityHistory.Count ? p.QualityHistory[i] : 0.5f);
                var e = p.EmbeddingHistory[i];
                for (var d = 0; d < dim; d++) acc[d] += e[d] * peso;
            }

            var norma = 0f;
            for (var d = 0; d < dim; d++) norma += acc[d] * acc[d];
            norma = Mathf.Sqrt(norma);
            if (norma < 1e-6f) return;
            for (var d = 0; d < dim; d++) acc[d] /= norma;
            p.FaceEmbedding = acc;
        }

        public void AssegnaNome(Persona p, string nome)
        {
            if (p == null) return;
            p.UserAssignedName = string.IsNullOrWhiteSpace(nome) ? string.Empty : nome.Trim();
            p.Persistent = p.UserAssignedName.Length > 0;
            Cambiato?.Invoke();
        }

        public void Cancella(Persona p)
        {
            if (p == null) return;
            ArchivioVolti.Cancella(p.RepresentativeFaceFile);
            if (p.Anteprima != null) Destroy(p.Anteprima);
            m_persone.Remove(p);
            Cambiato?.Invoke();
        }

        public void CancellaTutto()
        {
            foreach (var p in m_persone) if (p.Anteprima != null) Destroy(p.Anteprima);
            m_persone.Clear();
            m_contatore = 0;
            Cifratura.DistruggiTutto();
            Cambiato?.Invoke();
        }

        /// <summary>
        /// Fonde le identita che si somigliano troppo per essere persone diverse.
        ///
        /// Serve perche il matching lavora su un fotogramma alla volta e non puo
        /// tornare indietro: se una persona ha generato due identita quando era
        /// mal illuminata, restano due righe nel pannello per sempre. Qui si
        /// guarda il database nel suo insieme, a mente fredda, confrontando ogni
        /// coppia di identita sul MASSIMO fra tutti i loro ricordi.
        ///
        /// Due identita che l'utente ha battezzato con nomi diversi non si
        /// toccano mai: quella e una sua decisione, non un'ipotesi del sistema.
        /// </summary>
        public int FondiSimili(float soglia)
        {
            var fusioni = 0;

            for (var i = 0; i < m_persone.Count; i++)
            {
                for (var j = m_persone.Count - 1; j > i; j--)
                {
                    var a = m_persone[i];
                    var b = m_persone[j];

                    if (a.Persistent && b.Persistent) continue;
                    if (MigliorSomiglianza(a, b) < soglia) continue;

                    // Sopravvive quella battezzata; a pari merito, la piu vecchia.
                    var resta = a; var assorbita = b;
                    if (b.Persistent && !a.Persistent) { resta = b; assorbita = a; }
                    else if (!a.Persistent && !b.Persistent && b.FirstSeenTicks < a.FirstSeenTicks) { resta = b; assorbita = a; }

                    Debug.Log($"[FaceQuest] Fondo {assorbita.PersonID} in {resta.DisplayName}: " +
                              $"somiglianza {MigliorSomiglianza(a, b):0.00}.");
                    Assorbi(resta, assorbita);
                    fusioni++;
                    if (resta == a) continue;

                    // 'a' non esiste piu: al suo indice ora c'e un'altra persona,
                    // che va esaminata e non saltata.
                    i--;
                    break;
                }
            }

            if (fusioni > 0) Cambiato?.Invoke();
            return fusioni;
        }

        /// <summary>Il massimo fra tutti i confronti dei ricordi di due identita.</summary>
        private static float MigliorSomiglianza(Persona a, Persona b)
        {
            var migliore = FaceEmbeddingExtractor.Somiglianza(a.FaceEmbedding, b.FaceEmbedding);
            foreach (var ea in a.EmbeddingHistory)
            {
                foreach (var eb in b.EmbeddingHistory)
                {
                    var s = FaceEmbeddingExtractor.Somiglianza(ea, eb);
                    if (s > migliore) migliore = s;
                }
            }
            return migliore;
        }

        private void Assorbi(Persona resta, Persona assorbita)
        {
            for (var i = 0; i < assorbita.EmbeddingHistory.Count; i++)
            {
                resta.EmbeddingHistory.Add(assorbita.EmbeddingHistory[i]);
                resta.QualityHistory.Add(i < assorbita.QualityHistory.Count ? assorbita.QualityHistory[i] : 0.5f);
                if (resta.EmbeddingHistory.Count > m_dimensioneStorico) PotaStorico(resta);
            }

            resta.ObservationCount += assorbita.ObservationCount;
            if (assorbita.FirstSeenTicks < resta.FirstSeenTicks) resta.FirstSeenTicks = assorbita.FirstSeenTicks;
            if (assorbita.LastSeenTicks > resta.LastSeenTicks) resta.LastSeenTicks = assorbita.LastSeenTicks;

            // L'anteprima migliore delle due: e quella che l'utente vede nel pannello.
            if (assorbita.QualitaAnteprima > resta.QualitaAnteprima && assorbita.Anteprima != null)
            {
                if (resta.Anteprima != null) Destroy(resta.Anteprima);
                resta.Anteprima = assorbita.Anteprima;
                resta.QualitaAnteprima = assorbita.QualitaAnteprima;
                assorbita.Anteprima = null;
                if (resta.Persistent) resta.RepresentativeFaceFile = ArchivioVolti.Salva(resta.Anteprima, resta.PersonID);
            }

            RicalcolaTemplate(resta);

            ArchivioVolti.Cancella(assorbita.RepresentativeFaceFile);
            if (assorbita.Anteprima != null) Destroy(assorbita.Anteprima);
            m_persone.Remove(assorbita);
        }

        /// <summary>
        /// Butta le identita nate da pochi fotogrammi e mai piu riviste.
        ///
        /// E la stessa regola che ha ripulito l'inventario AR: cio che e stato
        /// confermato debolmente e non cresce piu, non era la' davvero. Un volto
        /// vero accumula osservazioni in fretta; un falso positivo resta a una o
        /// due e poi tace per sempre.
        ///
        /// Le identita battezzate non si toccano, qualunque sia il conteggio.
        /// </summary>
        public int PotaFantasmi(float secondiDiSilenzio, int osservazioniMinime)
        {
            var ora = DateTime.UtcNow;
            var potate = 0;

            for (var i = m_persone.Count - 1; i >= 0; i--)
            {
                var p = m_persone[i];
                if (p.Persistent) continue;
                if (p.ObservationCount > osservazioniMinime) continue;
                if ((ora - p.UltimaVista).TotalSeconds < secondiDiSilenzio) continue;

                Debug.Log($"[FaceQuest] Scarto {p.PersonID}: {p.ObservationCount} osservazioni e " +
                          $"{(ora - p.UltimaVista).TotalSeconds:0} s di silenzio.");
                ArchivioVolti.Cancella(p.RepresentativeFaceFile);
                if (p.Anteprima != null) Destroy(p.Anteprima);
                m_persone.RemoveAt(i);
                potate++;
            }

            if (potate > 0) Cambiato?.Invoke();
            return potate;
        }

        /// <summary>
        /// Butta le identita anonime troppo vecchie.
        ///
        /// Il criterio e l'eta dell'identita, non il silenzio: un volto che e
        /// in giro da dieci minuti e non ha ancora un nome non lo avra mai, e
        /// intanto occupa una riga del pannello. E la pulizia che tiene corto
        /// l'elenco durante una demo lunga.
        ///
        /// Due cautele. Le identita battezzate non si toccano mai, qualunque sia
        /// la loro eta: il nome e una decisione dell'utente e vale piu di
        /// qualsiasi timer. E un'anonima che e stata vista proprio adesso non
        /// viene rimossa (parametro <paramref name="secondiDiGrazia"/>): se
        /// sparisse mentre la persona e ancora inquadrata rinascerebbe subito con
        /// un altro AA-numero, che e peggio del problema che stiamo risolvendo.
        /// Sparisce appena esce dal campo visivo.
        /// </summary>
        public int PotaAnonimeScadute(float secondiDiVita, float secondiDiGrazia)
        {
            var ora = DateTime.UtcNow;
            var potate = 0;

            for (var i = m_persone.Count - 1; i >= 0; i--)
            {
                var p = m_persone[i];
                if (p.Persistent) continue;
                if ((ora - p.PrimaVista).TotalSeconds < secondiDiVita) continue;
                if ((ora - p.UltimaVista).TotalSeconds < secondiDiGrazia) continue;

                Debug.Log($"[FaceQuest] {p.PersonID} scaduta: senza nome da " +
                          $"{(ora - p.PrimaVista).TotalMinutes:0.0} minuti.");
                ArchivioVolti.Cancella(p.RepresentativeFaceFile);
                if (p.Anteprima != null) Destroy(p.Anteprima);
                m_persone.RemoveAt(i);
                potate++;
            }

            if (potate > 0) Cambiato?.Invoke();
            return potate;
        }

        /// <summary>Le identita mai battezzate non sopravvivono alla sessione.</summary>
        public int RimuoviAnonime()
        {
            var rimosse = 0;
            for (var i = m_persone.Count - 1; i >= 0; i--)
            {
                if (m_persone[i].Persistent) continue;
                ArchivioVolti.Cancella(m_persone[i].RepresentativeFaceFile);
                m_persone.RemoveAt(i);
                rimosse++;
            }
            if (rimosse > 0) Cambiato?.Invoke();
            return rimosse;
        }
    }
}
