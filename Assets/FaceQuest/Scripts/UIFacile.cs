// Scorciatoie per costruire UI in world space da codice.
//
// Tutta la UI di FaceQuest nasce da codice e non da prefab. Non e una
// preferenza stilistica: un pannello con dodici riferimenti collegati a mano
// nell'Inspector e impossibile da rigenerare identico dopo una modifica, e i
// riferimenti persi sono il guasto piu comune e piu silenzioso di Unity.

using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public static class UIFacile
    {
        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas Pannello(string nome, Transform padre, Vector2 dimensione, float scala, Color sfondo)
        {
            var go = new GameObject(nome, typeof(Canvas), typeof(CanvasScaler));
            if (padre != null) go.transform.SetParent(padre, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = dimensione;
            rt.localScale = Vector3.one * scala;

            if (sfondo.a > 0f)
            {
                var img = Immagine("Sfondo", go.transform, sfondo);
                Stendi(img.rectTransform);
            }

            return canvas;
        }

        public static Image Immagine(string nome, Transform padre, Color colore)
        {
            var go = new GameObject(nome, typeof(Image));
            go.transform.SetParent(padre, false);
            var img = go.GetComponent<Image>();
            img.color = colore;
            return img;
        }

        public static RawImage Raw(string nome, Transform padre)
        {
            var go = new GameObject(nome, typeof(RawImage));
            go.transform.SetParent(padre, false);
            return go.GetComponent<RawImage>();
        }

        public static Text Testo(string nome, Transform padre, string contenuto, int corpo,
                                 TextAnchor allineamento = TextAnchor.UpperLeft)
        {
            var go = new GameObject(nome, typeof(Text));
            go.transform.SetParent(padre, false);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = corpo;
            t.text = contenuto;
            t.color = Color.white;
            t.alignment = allineamento;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>
        /// Un bottone premibile col puntatore: sfondo, etichetta, collider e
        /// l'evidenziazione al passaggio del raggio.
        /// </summary>
        public static BersaglioAR Bottone(string nome, Transform padre, string etichetta, int corpo,
                                          Vector2 posizione, Vector2 dimensione, Color colore,
                                          System.Action azione)
        {
            var img = Immagine(nome, padre, colore);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = posizione;
            rt.sizeDelta = dimensione;

            var t = Testo("Etichetta", rt, etichetta, corpo, TextAnchor.MiddleCenter);
            Stendi(t.rectTransform);

            Collisore(rt);

            var bersaglio = img.gameObject.AddComponent<BersaglioAR>();
            bersaglio.Azione = azione;
            var normale = colore;
            var acceso = new Color(Mathf.Min(1f, colore.r + 0.25f), Mathf.Min(1f, colore.g + 0.25f),
                                   Mathf.Min(1f, colore.b + 0.25f), Mathf.Min(1f, colore.a + 0.15f));
            bersaglio.Evidenzia = dentro => img.color = dentro ? acceso : normale;

            return bersaglio;
        }

        /// <summary>
        /// Aggiunge il collider con cui il puntatore colpisce l'elemento.
        ///
        /// Il centro non e l'origine del RectTransform: con pivot in alto a
        /// sinistra il rettangolo si estende a destra e in basso, e un collider
        /// centrato sull'origine risulterebbe spostato di mezza altezza — i
        /// bottoni si accenderebbero puntando sopra di loro.
        /// </summary>
        public static BoxCollider Collisore(RectTransform rt)
        {
            var c = rt.gameObject.AddComponent<BoxCollider>();
            var dim = rt.rect.size;
            c.size = new Vector3(dim.x, dim.y, 20f);
            c.center = new Vector3((0.5f - rt.pivot.x) * dim.x, (0.5f - rt.pivot.y) * dim.y, 0f);
            c.isTrigger = true;
            return c;
        }

        public static void Stendi(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Ancoraggio in alto a sinistra, con coordinate in pixel di UI.</summary>
        public static RectTransform Posiziona(RectTransform rt, Vector2 posizione, Vector2 dimensione)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = posizione;
            rt.sizeDelta = dimensione;
            return rt;
        }
    }
}
