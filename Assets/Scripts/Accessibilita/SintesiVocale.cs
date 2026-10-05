using System;
using System.Collections.Generic;
using UnityEngine;

namespace Accessibilita
{
    /// <summary>
    /// Sintesi vocale, appoggiandosi al TextToSpeech di Android via JNI.
    ///
    /// Unity non ha sintesi vocale propria. Il TTS di Android funziona offline sul
    /// Quest, che per un'app di assistenza e un requisito e non un vantaggio: non
    /// puo dipendere dalla rete.
    ///
    /// Due insidie affrontate qui. La prima: l'inizializzazione e asincrona e la sua
    /// callback arriva da un thread Java, dove toccare le API di Unity e vietato —
    /// quindi si registra soltanto un esito e lo si legge in Update. La seconda: la
    /// disponibilita della voce italiana sul visore non e garantita, quindi si
    /// verifica e si ripiega sull'inglese dicendo chiaramente cosa e successo.
    /// </summary>
    public class SintesiVocale : MonoBehaviour
    {
        // Costanti di android.speech.tts.TextToSpeech
        private const int SUCCESS = 0;
        private const int QUEUE_FLUSH = 0;
        private const int QUEUE_ADD = 1;
        private const int LANG_MISSING_DATA = -1;
        private const int LANG_NOT_SUPPORTED = -2;

        [Tooltip("Velocita della voce. 1 = normale. Per gli annunci brevi conviene alzarla.")]
        [SerializeField] private float m_velocita = 1.15f;

        [Tooltip("Secondi minimi fra due annunci uguali, per non ripetere a raffica.")]
        [SerializeField] private float m_intervalloRipetizione = 3f;

        private AndroidJavaObject _tts;
        private VoceClip _ripiego;
        private volatile int _esitoInit = int.MinValue;   // scritto dal thread Java
        private bool _pronto;
        private string _ultimoTesto = "";
        private float _ultimoQuando = -99f;

        public static SintesiVocale Istanza { get; private set; }

        /// <summary>Riassunto leggibile, da mostrare a schermo mentre si sviluppa.</summary>
        public string Stato { get; private set; } = "voce: avvio...";

        public bool Pronto => _pronto;

        /// <summary>Dettaglio di cosa sta accadendo alla voce, per il pannello.</summary>
        public string Dettaglio => _ripiego == null
            ? "-"
            : $"chiesto: {_ripiego.UltimaRichiesta}\n" +
              $"suonate: {_ripiego.Storico}\n" +
              $"coda: {_ripiego.InCoda}  saltate: {_ripiego.Saltate}  clip: {_ripiego.ClipCaricate}\n" +
              $"clip mancanti: {_ripiego.Mancanti}";

        /// <summary>
        /// Riceve l'esito di onInit dal thread Java. Non fa altro che registrarlo:
        /// tutto il resto avviene in Update, sul thread di Unity.
        /// </summary>
        private class Ascoltatore : AndroidJavaProxy
        {
            private readonly SintesiVocale _padrone;

            public Ascoltatore(SintesiVocale padrone)
                : base("android.speech.tts.TextToSpeech$OnInitListener") => _padrone = padrone;

            public void onInit(int status) => _padrone._esitoInit = status;
        }

        private void Awake()
        {
            if (Istanza != null && Istanza != this) { Destroy(gameObject); return; }
            Istanza = this;
            DontDestroyOnLoad(gameObject);

#if UNITY_ANDROID && !UNITY_EDITOR
            Inizializza();
#else
            AttivaRipiego("editor");
#endif
        }

        /// <summary>
        /// Passa alle clip pre-generate. Non e un errore da segnalare all'utente ma
        /// il percorso normale sul Quest, dove non esiste alcun motore di sintesi.
        /// </summary>
        private void AttivaRipiego(string motivo)
        {
            _ripiego = gameObject.AddComponent<VoceClip>();

            if (_ripiego.ClipCaricate == 0)
            {
                Stato = $"voce: NESSUNA clip ({motivo}) — esegui Tools/GeneraVoci.ps1";
                Debug.LogError("[Voce] Nessuna clip in Resources/Voci: la voce sara muta.");
            }
            else
            {
                Stato = $"voce: clip pre-generate ({_ripiego.ClipCaricate} frasi)";
                _pronto = true;
            }

            Debug.Log($"[Voce] {Stato}");
        }

        private void Inizializza()
        {
            try
            {
                using var classe = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var attivita = classe.GetStatic<AndroidJavaObject>("currentActivity");

                _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech",
                                             attivita, new Ascoltatore(this));
                Stato = "voce: inizializzazione...";
            }
            catch (Exception e)
            {
                Stato = "voce: costruzione fallita";
                Debug.LogError($"[Voce] Costruzione TextToSpeech fallita: {e.Message}");
            }
        }

        private void Update()
        {
            if (_esitoInit == int.MinValue) return;

            var esito = _esitoInit;
            _esitoInit = int.MinValue;
            CompletaInit(esito);
        }

        private void CompletaInit(int esito)
        {
            if (esito != SUCCESS || _tts == null)
            {
                // -1 e TextToSpeech.ERROR. La causa piu probabile su Horizon OS e
                // che non ci sia alcun motore di sintesi installato: il Quest non e
                // un telefono Android completo. Elencare i motori visti trasforma un
                // codice di errore in un'informazione utilizzabile.
                var motori = ElencaMotori();
                Debug.LogWarning($"[Voce] onInit ha restituito {esito}. Motori disponibili: {motori}");
                AttivaRipiego($"nessun motore ({motori})");
                return;
            }

            var lingua = ImpostaLingua("it", "IT");

            if (lingua == LANG_MISSING_DATA || lingua == LANG_NOT_SUPPORTED)
            {
                Debug.LogWarning($"[Voce] Italiano non disponibile ({lingua}), ripiego sull'inglese.");
                var inglese = ImpostaLingua("en", "US");

                if (inglese == LANG_MISSING_DATA || inglese == LANG_NOT_SUPPORTED)
                {
                    Stato = "voce: nessuna lingua disponibile";
                    return;
                }

                Stato = "voce: pronta (EN, italiano assente)";
            }
            else
            {
                Stato = "voce: pronta (IT)";
            }

            try { _tts.Call<int>("setSpeechRate", m_velocita); }
            catch (Exception e) { Debug.LogWarning($"[Voce] setSpeechRate: {e.Message}"); }

            _pronto = true;
            Debug.Log($"[Voce] {Stato}");
        }

        /// <summary>
        /// Chiede al TextToSpeech quali motori conosce. Funziona anche dopo un init
        /// fallito, perche l'oggetto Java esiste comunque.
        /// </summary>
        private string ElencaMotori()
        {
            if (_tts == null) return "oggetto nullo";

            try
            {
                using var lista = _tts.Call<AndroidJavaObject>("getEngines");
                if (lista == null) return "nessuna lista";

                var quanti = lista.Call<int>("size");
                if (quanti == 0) return "NESSUNO installato";

                var nomi = new System.Text.StringBuilder();
                for (var i = 0; i < quanti; i++)
                {
                    using var info = lista.Call<AndroidJavaObject>("get", i);
                    if (info == null) continue;
                    if (nomi.Length > 0) nomi.Append(", ");
                    nomi.Append(info.Get<string>("name"));
                }

                return nomi.Length > 0 ? nomi.ToString() : $"{quanti} senza nome";
            }
            catch (System.Exception e)
            {
                return $"getEngines fallito: {e.Message}";
            }
        }

        private int ImpostaLingua(string lingua, string paese)
        {
            try
            {
                using var locale = new AndroidJavaObject("java.util.Locale", lingua, paese);
                return _tts.Call<int>("setLanguage", locale);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voce] setLanguage {lingua}: {e.Message}");
                return LANG_NOT_SUPPORTED;
            }
        }

        /// <summary>
        /// Pronuncia un testo. Se e lo stesso di poco fa viene ignorato: puntando
        /// un oggetto la richiesta arriva molte volte al secondo, e ripetere lo
        /// stesso nome a raffica rende l'app inutilizzabile.
        /// </summary>
        /// <param name="interrompi">
        /// Se vero, tronca l'annuncio in corso. Vero e il default giusto per un
        /// puntatore: l'utente ha spostato lo sguardo e vuole sapere cosa c'e
        /// ADESSO, non ascoltare la fine della frase precedente.
        /// </param>
        public void Pronuncia(string testo, bool interrompi = true) => Annuncia(interrompi, testo);

        /// <summary>
        /// Pronuncia una sequenza di frasi.
        ///
        /// Sono frasi separate e non una stringa unica perche i due motori le
        /// trattano in modo diverso: il TTS le unisce in una sola pronuncia, le
        /// clip le suonano in fila. Chi chiama non deve sapere quale dei due c'e.
        /// </summary>
        public void Annuncia(bool interrompi, params string[] frasi)
        {
            if (frasi == null || frasi.Length == 0) return;

            var unito = string.Join(", ", frasi);
            if (string.IsNullOrWhiteSpace(unito)) return;

            if (unito == _ultimoTesto && Time.time - _ultimoQuando < m_intervalloRipetizione) return;

            _ultimoTesto = unito;
            _ultimoQuando = Time.time;

            if (_ripiego != null)
            {
                _ripiego.Pronuncia(interrompi, frasi);
                return;
            }

            if (!_pronto || _tts == null)
            {
                Debug.Log($"[Voce] (non pronta) direi: {unito}");
                return;
            }

            try
            {
                _tts.Call<int>("speak", unito, interrompi ? QUEUE_FLUSH : QUEUE_ADD,
                               null, "gv-" + Time.frameCount);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voce] speak: {e.Message}");
            }
        }

        /// <summary>Dimentica l'ultimo annuncio, cosi il prossimo passa comunque.</summary>
        public void Ripeti() => _ultimoTesto = "";

        private void OnDestroy()
        {
            if (_tts == null) return;
            try { _tts.Call("stop"); _tts.Call("shutdown"); }
            catch (Exception e) { Debug.LogWarning($"[Voce] shutdown: {e.Message}"); }
            _tts = null;
        }
    }
}
