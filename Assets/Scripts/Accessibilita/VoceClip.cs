using System.Collections.Generic;
using UnityEngine;

namespace Accessibilita
{
    /// <summary>
    /// Voce basata su clip audio pre-generate, per quando il sistema non offre
    /// alcun motore di sintesi — che sul Quest e il caso.
    ///
    /// Il compromesso e chiaro: si puo dire solo cio che e stato generato prima.
    /// Per questa app non e una limitazione, perche il vocabolario e chiuso: gli
    /// ottanta nomi delle classi COCO e sette frasi di distanza. In cambio si
    /// ottiene latenza nulla, pronuncia sempre identica e nessuna dipendenza dal
    /// software installato sul visore.
    ///
    /// Le clip stanno in Resources/Voci e si chiamano come la chiave della frase.
    /// </summary>
    public class VoceClip : MonoBehaviour
    {
        private const string Cartella = "Voci";

        private AudioSource _sorgente;
        private readonly Dictionary<string, AudioClip> _clip = new();
        private readonly Queue<AudioClip> _coda = new();
        private int _mancanti;
        private readonly HashSet<string> _giaSegnalate = new();
        private readonly LinkedList<string> _storico = new();

        public int ClipCaricate => _clip.Count;
        public int ClipMancanti => _mancanti;

        /// <summary>Cosa e stato chiesto per ultimo, e cosa e stato accodato davvero.</summary>
        public string UltimaRichiesta { get; private set; } = "-";
        public string UltimaSuonata { get; private set; } = "-";
        public int InCoda => _coda.Count;
        public int Saltate { get; private set; }

        /// <summary>Le chiavi per cui manca la clip, per vederle senza Logcat.</summary>
        public string Mancanti => _giaSegnalate.Count == 0
            ? "-"
            : string.Join(", ", _giaSegnalate);

        /// <summary>
        /// Le ultime frasi effettivamente suonate, dalla piu recente.
        ///
        /// Un valore istantaneo non basta a capire un problema di sequenza: se un
        /// nome viene troncato e ripartito, il campo "sta suonando" mostra sempre
        /// qualcosa di plausibile e il difetto resta invisibile. Lo storico lo rende
        /// evidente in un colpo d'occhio.
        /// </summary>
        public string Storico
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var voce in _storico) { if (sb.Length > 0) sb.Append(" < "); sb.Append(voce); }
                return sb.Length > 0 ? sb.ToString() : "-";
            }
        }

        private void Awake()
        {
            _sorgente = gameObject.AddComponent<AudioSource>();
            _sorgente.playOnAwake = false;

            // Voce al centro della testa, non nello spazio: e la voce dell'app che
            // parla all'utente, non un suono che viene da un punto della stanza.
            _sorgente.spatialBlend = 0f;

            Carica();
        }

        private void Carica()
        {
            foreach (var frase in EtichetteItaliane.TutteLeFrasi())
            {
                var clip = Resources.Load<AudioClip>($"{Cartella}/{frase.Key}");
                if (clip != null) _clip[frase.Key] = clip;
                else _mancanti++;
            }

            Debug.Log($"[VoceClip] Caricate {_clip.Count} clip, {_mancanti} mancanti.");
        }

        private void Update()
        {
            if (_sorgente.isPlaying || _coda.Count == 0) return;

            var clip = _coda.Dequeue();
            _sorgente.clip = clip;
            _sorgente.Play();
            UltimaSuonata = clip.name;

            _storico.AddFirst(clip.name);
            while (_storico.Count > 5) _storico.RemoveLast();
        }

        /// <summary>
        /// Accoda le frasi indicate. Se si interrompe, la coda viene svuotata: chi
        /// ha spostato il puntatore vuole sapere cosa c'e adesso, non finire di
        /// ascoltare l'annuncio precedente.
        /// </summary>
        public void Pronuncia(bool interrompi, params string[] frasi)
        {
            if (interrompi)
            {
                _coda.Clear();
                if (_sorgente.isPlaying) _sorgente.Stop();
            }

            UltimaRichiesta = string.Join(" + ", frasi);

            foreach (var frase in frasi)
            {
                if (string.IsNullOrWhiteSpace(frase)) continue;

                var chiave = EtichetteItaliane.Chiave(frase);
                if (_clip.TryGetValue(chiave, out var clip))
                {
                    _coda.Enqueue(clip);
                    continue;
                }

                // Una clip mancante rendeva muto quel nome senza dirlo: il risultato
                // era "si sentono le distanze ma non gli oggetti", un sintomo che non
                // punta alla causa. Ora si lamenta, una volta per chiave.
                Saltate++;
                if (_giaSegnalate.Add(chiave))
                    Debug.LogWarning($"[VoceClip] Manca la clip per \"{frase}\" (chiave: {chiave})");
            }
        }

        public void Ferma()
        {
            _coda.Clear();
            if (_sorgente.isPlaying) _sorgente.Stop();
        }
    }
}
