using System.Collections.Generic;

namespace Accessibilita
{
    /// <summary>
    /// Traduce le classi COCO in italiano.
    ///
    /// Non e un vezzo: l'app parla, e sentirsi dire "diningtable" mentre si cerca
    /// di attraversare una stanza e peggio che inutile. Le classi non presenti
    /// vengono pronunciate come sono, ripulite dai trattini bassi.
    /// </summary>
    public static class EtichetteItaliane
    {
        /// <summary>
        /// Tutte e ottanta le classi del modello, tradotte.
        ///
        /// I nomi sono presi dalla lista del modello e non a memoria: questo YOLO
        /// usa "sofa" e non "couch", "motorbike" e non "motorcycle", "aeroplane" e
        /// non "airplane". Le varianti che avevo scritto a memoria non combaciavano,
        /// e per quelle classi la voce restava muta perche mancava la clip.
        ///
        /// Ogni nome con spazi e presente anche nella forma con underscore, perche
        /// il codice del sample sostituisce gli spazi prima di passarci la classe.
        /// </summary>
        private static readonly Dictionary<string, string> _mappa = new()
        {
            { "person", "persona" },
            { "bicycle", "bicicletta" },
            { "car", "automobile" },
            { "motorbike", "motocicletta" },
            { "aeroplane", "aereo" },
            { "bus", "autobus" },
            { "train", "treno" },
            { "truck", "camion" },
            { "boat", "barca" },
            { "traffic light", "semaforo" },
            { "traffic_light", "semaforo" },
            { "fire hydrant", "idrante" },
            { "fire_hydrant", "idrante" },
            { "stop sign", "segnale di stop" },
            { "stop_sign", "segnale di stop" },
            { "parking meter", "parchimetro" },
            { "parking_meter", "parchimetro" },
            { "bench", "panchina" },
            { "bird", "uccello" },
            { "cat", "gatto" },
            { "dog", "cane" },
            { "horse", "cavallo" },
            { "sheep", "pecora" },
            { "cow", "vacca" },
            { "elephant", "elefante" },
            { "bear", "orso" },
            { "zebra", "zebra" },
            { "giraffe", "giraffa" },
            { "backpack", "zaino" },
            { "umbrella", "ombrello" },
            { "handbag", "borsa" },
            { "tie", "cravatta" },
            { "suitcase", "valigia" },
            { "frisbee", "frisbee" },
            { "skis", "sci" },
            { "snowboard", "snowboard" },
            { "sports ball", "palla" },
            { "sports_ball", "palla" },
            { "kite", "aquilone" },
            { "baseball bat", "mazza" },
            { "baseball_bat", "mazza" },
            { "baseball glove", "guantone" },
            { "baseball_glove", "guantone" },
            { "skateboard", "skateboard" },
            { "surfboard", "tavola da surf" },
            { "tennis racket", "racchetta" },
            { "tennis_racket", "racchetta" },
            { "bottle", "bottiglia" },
            { "wine glass", "bicchiere" },
            { "wine_glass", "bicchiere" },
            { "cup", "tazza" },
            { "fork", "forchetta" },
            { "knife", "coltello" },
            { "spoon", "cucchiaio" },
            { "bowl", "ciotola" },
            { "banana", "banana" },
            { "apple", "mela" },
            { "sandwich", "panino" },
            { "orange", "arancia" },
            { "broccoli", "broccolo" },
            { "carrot", "carota" },
            { "hot dog", "hot dog" },
            { "hot_dog", "hot dog" },
            { "pizza", "pizza" },
            { "donut", "ciambella" },
            { "cake", "torta" },
            { "chair", "sedia" },
            { "sofa", "divano" },
            { "pottedplant", "pianta" },
            { "bed", "letto" },
            { "diningtable", "tavolo" },
            { "toilet", "water" },
            { "tvmonitor", "televisore" },
            { "laptop", "computer" },
            { "mouse", "mouse" },
            { "remote", "telecomando" },
            { "keyboard", "tastiera" },
            { "cell phone", "telefono" },
            { "cell_phone", "telefono" },
            { "microwave", "microonde" },
            { "oven", "forno" },
            { "toaster", "tostapane" },
            { "sink", "lavandino" },
            { "refrigerator", "frigorifero" },
            { "book", "libro" },
            { "clock", "orologio" },
            { "vase", "vaso" },
            { "scissors", "forbici" },
            { "teddy bear", "orsetto" },
            { "teddy_bear", "orsetto" },
            { "hair drier", "asciugacapelli" },
            { "hair_drier", "asciugacapelli" },
            { "toothbrush", "spazzolino" },
        };

        public static string Traduci(string classe)
        {
            if (string.IsNullOrEmpty(classe)) return "";

            // Trim difensivo: la sorgente e gia ripulita, ma un nome con spazi o
            // ritorni a capo invisibili fallirebbe la ricerca in silenzio, restituendo
            // la parola inglese — che poi non ha clip e rende muto l'annuncio.
            classe = classe.Trim();

            return _mappa.TryGetValue(classe, out var it) ? it : classe.Replace("_", " ");
        }

        /// <summary>
        /// Distanza detta come la direbbe una persona. "Un metro e mezzo" si capisce
        /// al volo; "1,47 metri" costringe a interpretare mentre si cammina.
        /// </summary>
        public static string Distanza(float metri)
        {
            if (metri < 0.5f) return "a portata di mano";
            if (metri < 0.8f) return "mezzo metro";
            if (metri < 1.3f) return "un metro";
            if (metri < 1.8f) return "un metro e mezzo";
            if (metri < 2.5f) return "due metri";
            if (metri < 3.5f) return "tre metri";
            return "oltre tre metri";
        }

        /// <summary>Le frasi di distanza, come insieme chiuso.</summary>
        public static readonly string[] FrasiDistanza =
        {
            "a portata di mano", "mezzo metro", "un metro", "un metro e mezzo",
            "due metri", "tre metri", "oltre tre metri",
        };

        /// <summary>
        /// Tutte le frasi che l'app puo pronunciare, come coppie chiave/testo.
        ///
        /// E la sorgente unica da cui si generano le clip audio: cosi il dizionario
        /// qui sopra e i file su disco non possono divergere, che e il modo tipico
        /// in cui un'app a clip pre-registrate si rompe in silenzio.
        /// </summary>
        public static IEnumerable<KeyValuePair<string, string>> TutteLeFrasi()
        {
            var viste = new HashSet<string>();

            foreach (var testo in _mappa.Values)
            {
                var chiave = Chiave(testo);
                if (viste.Add(chiave)) yield return new KeyValuePair<string, string>(chiave, testo);
            }

            foreach (var testo in FrasiDistanza)
            {
                var chiave = Chiave(testo);
                if (viste.Add(chiave)) yield return new KeyValuePair<string, string>(chiave, testo);
            }

            foreach (var testo in new[] { "niente davanti", "attenzione", "ostacolo" })
            {
                var chiave = Chiave(testo);
                if (viste.Add(chiave)) yield return new KeyValuePair<string, string>(chiave, testo);
            }
        }

        /// <summary>Nome di file corrispondente a una frase.</summary>
        public static string Chiave(string testo)
        {
            if (string.IsNullOrEmpty(testo)) return "";

            var sb = new System.Text.StringBuilder(testo.Length);
            foreach (var c in testo.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '_' || c == '-') sb.Append('_');
            }
            return sb.ToString();
        }
    }
}
