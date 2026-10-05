// L'unico interruttore per la convenzione sull'asse verticale.
//
// Il motore di inferenza di Unity, quando converte una texture in tensore,
// riporta la prima riga del tensore in cima all'immagine: la texture ha
// l'origine in basso, il tensore in alto, e la conversione ribalta. Tutta la
// pipeline e scritta su questa ipotesi.
//
// Se cambiasse (o se una versione del pacchetto si comportasse diversamente) il
// sintomo sarebbe inconfondibile: le box comparirebbero specchiate in verticale
// rispetto ai volti e nessuna identita verrebbe piu riconosciuta, perche i
// ritagli dati ad ArcFace sarebbero capovolti. Invece di andare a cercare i
// "1 - y" sparsi nel codice, c'e questo unico posto da cambiare, dalla casella
// "Tensore dall'alto" del FaceQuestManager.

using UnityEngine;

namespace FaceQuest
{
    public static class FaceQuestConfig
    {
        /// <summary>true = la riga 0 del tensore e la riga in cima all'immagine.</summary>
        public static bool TensoreDallAlto = true;

        /// <summary>Da coordinata normalizzata del rilevamento a punto di viewport della camera.</summary>
        public static Vector2 AViewport(Vector2 p) =>
            new Vector2(p.x, TensoreDallAlto ? 1f - p.y : p.y);

        public static Vector2 AViewport(float x, float y) =>
            new Vector2(x, TensoreDallAlto ? 1f - y : y);

        /// <summary>Da coordinata normalizzata del rilevamento a pixel "dall'alto" del frame.</summary>
        public static Vector2 APixelDallAlto(Vector2 p, float larghezza, float altezza) =>
            new Vector2(p.x * larghezza, (TensoreDallAlto ? p.y : 1f - p.y) * altezza);
    }
}
