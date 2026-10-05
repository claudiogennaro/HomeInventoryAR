using System.Collections.Generic;
using UnityEngine;

namespace InventarioAR
{
    /// <summary>
    /// Il cuore della Fase 2: trasforma un flusso di rilevamenti istantanei in un
    /// insieme di oggetti stabili.
    ///
    /// Il problema che risolve: lo stesso telecomando produce decine di rilevamenti
    /// al secondo, ognuno in una posizione leggermente diversa, e YOLO ogni tanto
    /// inventa un falso positivo che dura un frame. Contare i rilevamenti darebbe
    /// numeri assurdi.
    ///
    /// La regola: due rilevamenti sono lo stesso oggetto se hanno la stessa classe
    /// e distano meno di SogliaDistanza. Un candidato diventa oggetto confermato
    /// solo dopo OsservazioniPerConferma osservazioni — cosi i lampi di un frame
    /// vengono scartati da soli.
    ///
    /// Classe C# pura, senza dipendenze da MonoBehaviour: si puo testare in editor
    /// senza indossare il visore, che e prezioso perche la Passthrough Camera API
    /// non funziona nel simulatore.
    /// </summary>
    public class RegistroInventario
    {
        /// <summary>
        /// Tolleranza LATERALE: quanto due stime possono scostarsi
        /// perpendicolarmente alla linea di vista ed essere ancora lo stesso oggetto.
        ///
        /// Va tenuta STRETTA. E la direzione in cui il detector e affidabile, ed e
        /// anche quella che separa due oggetti veri affiancati: due mouse sulla
        /// stessa scrivania differiscono lateralmente, non in profondita.
        /// </summary>
        public float TolleranzaLaterale = 0.18f;

        /// <summary>
        /// Tolleranza RADIALE: quanto due stime possono scostarsi lungo la linea di
        /// vista ed essere ancora lo stesso oggetto.
        ///
        /// Va tenuta LARGA. E la direzione in cui il raycast di profondita sbaglia,
        /// ed e anche quella in cui deriva un'ancora rilocalizzata. Due stime dello
        /// stesso mouse a 30 cm di distanza in profondita sono lo stesso mouse.
        ///
        /// Misurare le due direzioni separatamente e cio che permette di essere
        /// permissivi sull'errore senza fondere oggetti distinti: una soglia sferica
        /// deve scegliere fra i due sbagli, questa no.
        /// </summary>
        public float TolleranzaRadiale = 0.9f;
        public int OsservazioniPerConferma = 6;

        /// <summary>Un candidato non rivisto entro questo tempo viene dimenticato.</summary>
        public float ScadenzaCandidati = 4f;

        /// <summary>
        /// Sopra questo numero di osservazioni un oggetto e considerato solido e non
        /// viene piu messo in discussione: e stato visto abbastanza da poterci contare
        /// anche quando esci dalla stanza.
        /// </summary>
        public int OsservazioniSolide = 60;

        /// <summary>
        /// Un oggetto confermato ma ancora debole (sotto OsservazioniSolide) che non
        /// viene rivisto entro questo tempo viene scartato.
        ///
        /// Nasce da un dato osservato: i doppioni si fermano a una quindicina di
        /// osservazioni mentre gli oggetti veri ne accumulano centinaia. Un oggetto
        /// che smette di crescere pur restando nella stanza non era mai esistito.
        /// </summary>
        public float ScadenzaDeboli = 20f;

        /// <summary>
        /// Classi da escludere. Nell'inventario si scartano gli esseri viventi,
        /// perche si muovono da soli e ricordarne la posizione non ha senso.
        ///
        /// Nell'app di assistenza va invece SVUOTATA: per chi non vede, sapere che
        /// c'e una persona o un gatto davanti e la cosa piu importante di tutte.
        /// Lo stesso registro serve due scopi opposti, quindi la decisione non puo
        /// stare qui dentro.
        /// </summary>
        public readonly HashSet<string> ClassiIgnorate = new();

        /// <summary>Popola l'esclusione tipica dell'inventario.</summary>
        public void IgnoraEsseriViventi()
        {
            foreach (var c in new[] { "person", "cat", "dog", "bird", "horse", "sheep",
                                      "cow", "bear", "zebra", "giraffe", "elephant" })
                ClassiIgnorate.Add(c);
        }

        private readonly List<OggettoInventario> _oggetti = new();

        public IReadOnlyList<OggettoInventario> Oggetti => _oggetti;

        /// <summary>
        /// Oggetti che in quest'ultimo aggiornamento hanno superato la soglia di
        /// solidita. Sono quelli da ancorare e salvare: il chiamante reagisce una
        /// volta sola, senza doversi ricordare cosa aveva gia trattato.
        /// </summary>
        public readonly List<OggettoInventario> AppenaSolidi = new();

        /// <summary>UUID di ancore rimaste orfane dopo una fusione: vanno cancellate.</summary>
        public readonly List<string> AncoreDaCancellare = new();

        public int NumeroConfermati
        {
            get
            {
                var n = 0;
                foreach (var o in _oggetti) if (o.Confermato) n++;
                return n;
            }
        }

        /// <summary>
        /// Incorpora le osservazioni di un frame.
        /// Restituisce gli oggetti che sono passati a "confermato" proprio adesso,
        /// cosi il chiamante puo reagire una volta sola (per esempio piazzando un
        /// marker o, in Fase 3, creando un'ancora spaziale).
        /// </summary>
        public List<OggettoInventario> Aggiorna(
            IReadOnlyList<(string classe, int classeId, Vector3 posizione, float raggio)> osservazioni,
            float tempo,
            Vector3 puntoDiVista)
        {
            var appenaConfermati = new List<OggettoInventario>();
            AppenaSolidi.Clear();

            foreach (var oss in osservazioni)
            {
                if (ClassiIgnorate.Contains(oss.classe)) continue;

                var esistente = TrovaPiuVicino(oss.classe, oss.posizione, oss.raggio, puntoDiVista);

                if (esistente == null)
                {
                    _oggetti.Add(new OggettoInventario(oss.classe, oss.classeId, oss.posizione, oss.raggio, tempo));
                    continue;
                }

                esistente.Incorpora(oss.posizione, oss.raggio, tempo);

                if (!esistente.Confermato && esistente.Osservazioni >= OsservazioniPerConferma)
                {
                    esistente.Confermato = true;
                    appenaConfermati.Add(esistente);
                }

                if (!esistente.Solido && esistente.Osservazioni >= OsservazioniSolide)
                {
                    esistente.Solido = true;
                    AppenaSolidi.Add(esistente);
                }
            }

            ScartaCandidatiScaduti(tempo);
            Consolida(puntoDiVista);
            return appenaConfermati;
        }

        /// <summary>
        /// Cerca l'oggetto della stessa classe piu vicino entro la soglia. Prendere
        /// il piu vicino e non il primo che capita evita di agganciare il rilevamento
        /// all'oggetto sbagliato quando due dello stesso tipo sono vicini — due tazze
        /// sullo stesso tavolo, per dire.
        /// </summary>
        private OggettoInventario TrovaPiuVicino(
            string classe, Vector3 posizione, float raggio, Vector3 puntoDiVista)
        {
            OggettoInventario migliore = null;
            var minLaterale = float.MaxValue;

            foreach (var o in _oggetti)
            {
                if (o.Classe != classe) continue;
                if (!StessoOggetto(o.Posizione, o.Raggio, posizione, raggio, puntoDiVista, out var laterale))
                    continue;

                // Fra i candidati ammissibili si sceglie quello lateralmente piu
                // allineato: e la direzione su cui il detector non sbaglia, quindi
                // e il criterio piu informativo per decidere di chi si tratta.
                if (laterale < minLaterale)
                {
                    minLaterale = laterale;
                    migliore = o;
                }
            }

            return migliore;
        }

        /// <summary>
        /// Decide se due stime siano lo stesso oggetto, scomponendo lo scostamento
        /// nelle due direzioni che hanno significato diverso: perpendicolare alla
        /// linea di vista (dove il detector e preciso) e lungo la linea di vista
        /// (dove sbaglia).
        /// </summary>
        private bool StessoOggetto(Vector3 a, float raggioA, Vector3 b, float raggioB,
                                   Vector3 puntoDiVista, out float laterale)
        {
            var versoA = a - puntoDiVista;
            var distA = versoA.magnitude;

            if (distA < 0.0001f)
            {
                laterale = Vector3.Distance(a, b);
                return laterale <= TolleranzaLaterale;
            }

            var direzione = versoA / distA;
            var versoB = b - puntoDiVista;

            var distB = Vector3.Dot(versoB, direzione);
            laterale = (versoB - direzione * distB).magnitude;

            // La tolleranza laterale cresce con la dimensione dell'oggetto: il centro
            // del bounding box di un tavolo balla piu di quello di un mouse.
            var limiteLaterale = Mathf.Max(TolleranzaLaterale, Mathf.Max(raggioA, raggioB) * 0.9f);

            return laterale <= limiteLaterale && Mathf.Abs(distB - distA) <= TolleranzaRadiale;
        }

        /// <summary>
        /// Fonde gli oggetti della stessa classe finiti troppo vicini fra loro.
        ///
        /// Serve perche il raycast di profondita e rumoroso: la stessa tv puo essere
        /// stimata a posizioni distanti fra loro piu della soglia, e generare due
        /// oggetti distinti che poi si confermano entrambi. Il controllo al momento
        /// dell'inserimento non basta, perche le due stime possono nascere lontane
        /// e avvicinarsi solo dopo, man mano che le medie mobili convergono.
        ///
        /// Qui si guarda l'insieme a posteriori e si ripara.
        /// </summary>
        private void Consolida(Vector3 puntoDiVista)
        {
            for (var i = 0; i < _oggetti.Count; i++)
            {
                for (var j = _oggetti.Count - 1; j > i; j--)
                {
                    if (_oggetti[i].Classe != _oggetti[j].Classe) continue;

                    if (!StessoOggetto(_oggetti[i].Posizione, _oggetti[i].Raggio,
                                       _oggetti[j].Posizione, _oggetti[j].Raggio,
                                       puntoDiVista, out _)) continue;

                    // L'ancora del doppione va cancellata dal sistema, altrimenti
                    // resta a occupare spazio e verrebbe ricaricata al prossimo avvio.
                    if (!string.IsNullOrEmpty(_oggetti[j].UuidAncora))
                        AncoreDaCancellare.Add(_oggetti[j].UuidAncora);

                    _oggetti[i].Fondi(_oggetti[j]);
                    _oggetti.RemoveAt(j);
                }
            }
        }

        /// <summary>
        /// I candidati non confermati che spariscono dalla vista erano quasi certamente
        /// falsi positivi. Gli oggetti confermati non scadono mai: il punto dell'app e
        /// proprio ricordarli quando non li vedi piu.
        /// </summary>
        private void ScartaCandidatiScaduti(float tempo)
        {
            for (var i = _oggetti.Count - 1; i >= 0; i--)
            {
                var o = _oggetti[i];
                var eta = tempo - o.UltimaVista;

                var candidatoSpento = !o.Confermato && eta > ScadenzaCandidati;
                var confermatoDebole = o.Confermato
                                       && !o.Solido
                                       && eta > ScadenzaDeboli;

                if (candidatoSpento || confermatoDebole)
                {
                    _oggetti.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Reinserisce un oggetto letto da disco. Entra gia confermato e solido:
        /// la sua credibilita e stata guadagnata in una sessione precedente e non
        /// va rimessa in discussione da zero.
        /// </summary>
        public void Ripristina(OggettoInventario oggetto)
        {
            oggetto.Confermato = true;
            oggetto.Solido = true;
            _oggetti.Add(oggetto);
        }

        public bool Contiene(OggettoInventario oggetto) => _oggetti.Contains(oggetto);

        public void Svuota() => _oggetti.Clear();
    }
}
