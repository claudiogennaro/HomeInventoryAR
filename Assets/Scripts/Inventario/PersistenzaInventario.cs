using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InventarioAR
{
    /// <summary>
    /// Rende permanente l'inventario: ogni oggetto solido riceve un'ancora
    /// spaziale, e l'UUID dell'ancora finisce su disco.
    ///
    /// Perche un'ancora e non delle coordinate: le coordinate di questa sessione
    /// non significano nulla nella prossima, perche l'origine del tracking cambia
    /// a ogni avvio. Un'ancora spaziale invece e un punto che il sistema del Quest
    /// riconosce nella stanza reale, e sa ritrovare domani.
    /// </summary>
    [RequireComponent(typeof(InventarioManager))]
    public class PersistenzaInventario : MonoBehaviour
    {
        [Tooltip("Ogni quanti secondi, al massimo, riscrivere il file su disco.")]
        [SerializeField] private float m_intervalloSalvataggio = 5f;

        [Tooltip("Azzera l'archivio all'avvio. Da attivare per ripartire pulito dopo " +
                 "che sessioni precedenti hanno lasciato doppioni salvati su disco.")]
        [SerializeField] private bool m_azzeraArchivioAllAvvio;

        [Tooltip("Quanti secondi attendere la localizzazione di un'ancora al caricamento.")]
        [SerializeField] private double m_timeoutLocalizzazione = 10;

        private InventarioManager _inventario;

        /// <summary>Riassunto leggibile di cosa e successo all'avvio e all'ultimo salvataggio.</summary>
        public string Stato { get; private set; } = "archivio: in lettura...";
        private bool _daSalvare;
        private NavigazioneInventario _navigazione;
        private int _letti;
        private int _ripristinati;
        private float _prossimoSalvataggio;

        private void Awake()
        {
            _inventario = GetComponent<InventarioManager>();
        }

        /// <summary>Collegato dal builder al gesto di azzeramento della navigazione.</summary>
        public void AzzeraTutto()
        {
            _inventario.Svuota();
            ArchivioInventario.Cancella();
            ScattiInventario.CancellaTutti();
            _daSalvare = false;
            _letti = 0;
            _ripristinati = 0;
            Stato = "archivio: azzerato dall'utente";
            Debug.Log("[Persistenza] Inventario e archivio azzerati dall'utente.");
        }

        private void OnEnable()
        {
            _inventario.Solidificato += SuOggettoSolido;
            _inventario.AncoraOrfana += CancellaAncora;

            // Aggancio a runtime invece che dall'Inspector: un event C# non
            // sopravvive alla serializzazione della scena, e cercare il componente
            // qui evita di introdurre una UnityEvent solo per questo.
            _navigazione = FindAnyObjectByType<NavigazioneInventario>();
            if (_navigazione != null) _navigazione.AzzeramentoRichiesto += AzzeraTutto;
            StartCoroutine(RipristinaDaDisco());
        }

        private void OnDisable()
        {
            _inventario.Solidificato -= SuOggettoSolido;
            _inventario.AncoraOrfana -= CancellaAncora;
            if (_navigazione != null) _navigazione.AzzeramentoRichiesto -= AzzeraTutto;
            if (_daSalvare) SalvaOra();
        }

        private void OnApplicationPause(bool inPausa)
        {
            // Su Android l'app puo essere terminata senza passare da OnDisable:
            // la pausa e l'ultimo momento garantito per scrivere.
            if (inPausa && _daSalvare) SalvaOra();
        }

        private void Update()
        {
            if (!_daSalvare || Time.time < _prossimoSalvataggio) return;
            SalvaOra();
        }

        private void SalvaOra()
        {
            var quanti = ArchivioInventario.Salva(_inventario.Oggetti);
            _daSalvare = false;
            Stato = $"archivio: {quanti} salvati, {_ripristinati} ripristinati all'avvio";
            _prossimoSalvataggio = Time.time + m_intervalloSalvataggio;
        }

        // ---------- creazione ----------

        private void SuOggettoSolido(OggettoInventario oggetto)
        {
            StartCoroutine(CreaAncora(oggetto));
        }

        private IEnumerator CreaAncora(OggettoInventario oggetto)
        {
            var go = new GameObject($"Ancora_{oggetto.Classe}_{oggetto.Id}");
            go.transform.position = oggetto.Posizione;
            var ancora = go.AddComponent<OVRSpatialAnchor>();

            // Un'ancora va localizzata prima di poter essere salvata.
            var scadenza = Time.time + 10f;
            while (ancora != null && !ancora.Localized && Time.time < scadenza)
                yield return null;

            if (ancora == null || !ancora.Localized)
            {
                Debug.LogWarning($"[Persistenza] Ancora non localizzata per {oggetto.Classe}, riprovero al prossimo avvio.");
                if (go != null) Destroy(go);
                yield break;
            }

            var awaiter = ancora.SaveAnchorAsync().GetAwaiter();
            while (!awaiter.IsCompleted) yield return null;

            var esito = awaiter.GetResult();
            if (!esito.Success)
            {
                Debug.LogError($"[Persistenza] Salvataggio ancora fallito per {oggetto.Classe}: {esito}");
                Destroy(go);
                yield break;
            }

            oggetto.UuidAncora = ancora.Uuid.ToString();
            _daSalvare = true;

            Debug.Log($"[Persistenza] Ancorato {oggetto.Classe} ({oggetto.Osservazioni} oss.), uuid {oggetto.UuidAncora}");
        }

        // ---------- ripristino ----------

        private IEnumerator RipristinaDaDisco()
        {
            if (m_azzeraArchivioAllAvvio)
            {
                Stato = "archivio: AZZERATO all'avvio (flag attivo)";
                ArchivioInventario.Cancella();
                ScattiInventario.CancellaTutti();
                Debug.Log("[Persistenza] Archivio azzerato su richiesta: si riparte da zero.");
                yield break;
            }

            var dati = ArchivioInventario.Carica();
            _letti = dati.Oggetti.Count;
            Stato = $"archivio: {_letti} letti dal file";
            if (_letti == 0) yield break;

            // Le sessioni precedenti possono aver salvato lo stesso oggetto piu volte.
            // Ripulire qui, prima di rimetterli in vita, evita di trascinarsi dietro
            // per sempre gli errori di una versione passata.
            var scartati = ScartaDoppioniSalvati(dati);
            if (scartati > 0)
                Debug.Log($"[Persistenza] Scartati {scartati} doppioni gia presenti nell'archivio.");

            var uuid = new List<Guid>();
            var perUuid = new Dictionary<Guid, RecordOggetto>();

            foreach (var r in dati.Oggetti)
            {
                if (!Guid.TryParse(r.UuidAncora, out var g)) continue;
                uuid.Add(g);
                perUuid[g] = r;
            }

            if (uuid.Count == 0) yield break;

            var nonLegate = new List<OVRSpatialAnchor.UnboundAnchor>();
            var awaiter = OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuid, nonLegate).GetAwaiter();
            while (!awaiter.IsCompleted) yield return null;

            var esito = awaiter.GetResult();
            if (!esito.Success)
            {
                Debug.LogError($"[Persistenza] Caricamento ancore fallito: {esito.Status}");
                yield break;
            }

            var ripristinati = 0;

            foreach (var nonLegata in nonLegate)
            {
                var anchor = nonLegata;

                if (!anchor.Localized)
                {
                    var loc = anchor.LocalizeAsync(m_timeoutLocalizzazione).GetAwaiter();
                    while (!loc.IsCompleted) yield return null;
                    if (!loc.GetResult())
                    {
                            Stato = $"archivio: {_letti} letti, ancora non localizzabile";
                        Debug.LogWarning($"[Persistenza] Ancora {anchor.Uuid} non localizzabile: forse sei in un'altra stanza.");
                        continue;
                    }
                }

                if (!perUuid.TryGetValue(anchor.Uuid, out var record)) continue;

                var go = new GameObject($"Ancora_{record.Classe}_{record.Id}");
                var componente = go.AddComponent<OVRSpatialAnchor>();
                anchor.BindTo(componente);

                _inventario.Ripristina(record, go.transform.position);
                ripristinati++;
            }

            _ripristinati = ripristinati;
            Stato = $"archivio: {_letti} letti, {ripristinati} ripristinati";
            Debug.Log($"[Persistenza] Ripristinati {ripristinati} oggetti su {dati.Oggetti.Count} salvati.");
        }

        /// <summary>
        /// Rimuove dal sistema l'ancora di un oggetto assorbito da un altro:
        /// senza questo, i doppioni fusi tornerebbero a ogni avvio.
        /// </summary>
        private void CancellaAncora(string uuidTesto)
        {
            if (!Guid.TryParse(uuidTesto, out var uuid)) return;

            // La cancellazione per UUID esiste solo nella forma a lotti.
            _ = OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { uuid });
            _daSalvare = true;
            Debug.Log($"[Persistenza] Cancellata ancora orfana {uuidTesto}");
        }

        /// <summary>
        /// Elimina dai dati letti le registrazioni multiple dello stesso oggetto.
        ///
        /// Non potendo confrontare le posizioni — che si conoscono solo dopo aver
        /// localizzato le ancore — si usa il criterio piu semplice che funziona:
        /// a parita di classe si tiene quella con piu osservazioni, che e la stima
        /// su cui abbiamo raccolto piu evidenza. Il confronto fine per distanza
        /// avviene poi in Consolida(), a oggetti ripristinati.
        /// </summary>
        private static int ScartaDoppioniSalvati(DatiArchivio dati)
        {
            var prima = dati.Oggetti.Count;

            dati.Oggetti.Sort((a, b) => b.Osservazioni.CompareTo(a.Osservazioni));

            var tenuti = new List<RecordOggetto>();
            var perClasse = new Dictionary<string, int>();

            foreach (var r in dati.Oggetti)
            {
                perClasse.TryGetValue(r.Classe, out var quanti);

                // Piu esemplari della stessa classe sono possibili (due tazze), ma
                // oltre due per classe si tratta quasi certamente di doppioni.
                if (quanti >= 2) continue;

                perClasse[r.Classe] = quanti + 1;
                tenuti.Add(r);
            }

            dati.Oggetti = tenuti;
            return prima - tenuti.Count;
        }

        [ContextMenu("Cancella archivio su disco")]
        public void CancellaArchivio() => ArchivioInventario.Cancella();
    }
}
