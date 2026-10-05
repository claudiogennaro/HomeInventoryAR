// Le box AR sopra i volti, con l'etichetta sotto.
//
// Due problemi da risolvere qui, e nessuno dei due e il disegno.
//
// Il primo e la PROFONDITA. Il detector da un rettangolo nell'immagine, non una
// posizione nella stanza. Si potrebbe interrogare la mappa di profondita, ma su
// un volto in movimento e rumorosa e in ritardo. Qui si usa invece una
// grandezza che il volto porta con se: la distanza fra le pupille, circa 63 mm
// in un adulto. Dall'angolo fra i due raggi che passano per gli occhi si ricava
// la distanza con un errore di pochi centimetri, senza dipendere dalla scena.
//
// Il secondo e la STABILITA. La pipeline gira a 5-15 Hz, il visore disegna a
// 72 o 90. Se la box si muovesse solo quando arriva un rilevamento si vedrebbe
// scattare. Aggiorna() scrive un bersaglio, Update() ci si avvicina a ogni
// fotogramma: il movimento e continuo anche se l'informazione e discontinua.

using System.Collections.Generic;
using Meta.XR;
using UnityEngine;
using UnityEngine.UI;

namespace FaceQuest
{
    public class BoundingBoxRenderer : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_camera;

        [Header("Aspetto")]
        [SerializeField] private float m_spessore = 0.004f;
        [SerializeField] private Color m_coloreRiconosciuta = new Color(0.30f, 0.90f, 0.40f);
        [SerializeField] private Color m_coloreIncerta = new Color(1.00f, 0.78f, 0.20f);
        [SerializeField] private Color m_coloreNuova = new Color(0.45f, 0.72f, 1.00f);
        [SerializeField] private bool m_mostraConfidenza;

        [Header("Stabilita")]
        [SerializeField, Tooltip("Velocita con cui la box raggiunge la posizione misurata.")]
        private float m_reattivita = 12f;
        [SerializeField, Tooltip("Distanza fra le pupille assunta, in metri.")]
        private float m_distanzaPupille = 0.063f;
        [SerializeField, Tooltip("Altezza del volto assunta, usata quando la stima dalle pupille non regge.")]
        private float m_altezzaVolto = 0.22f;

        [Header("Materiali")]
        [SerializeField, Tooltip("Assegnato dal costruttore di scena: un riferimento serializzato finisce sempre nella build, Shader.Find no.")]
        private Shader m_shaderLinee;

        public bool MostraConfidenza { get => m_mostraConfidenza; set => m_mostraConfidenza = value; }

        /// <summary>Quante volte la distanza e stata stimata dall'altezza del box invece che dalle pupille.</summary>
        public int Ripieghi { get; private set; }

        /// <summary>Quanti volti sono stati rilevati ma non piazzabili nel mondo.</summary>
        public int Scarti { get; private set; }

        private class Box
        {
            public GameObject Go;
            public LineRenderer Linea;
            public Text Etichetta;
            public Transform Cartello;
            public Vector3 PosBersaglio, Pos;
            public Quaternion RotBersaglio, Rot;
            public Vector2 DimBersaglio, Dim;
            public bool Nuova;
            public int IdTraccia;
            public bool Vista;
        }

        /// <summary>Scala che porta i pixel di UI del cartello in metri.</summary>
        private const float ScalaCartello = 0.0012f;

        private static readonly int IdColoreBase = Shader.PropertyToID("_BaseColor");

        private readonly List<Box> m_attive = new List<Box>();
        private readonly List<Box> m_riserva = new List<Box>();
        private Material m_materiale;
        private Font m_font;

        private void Awake()
        {
            var shader = m_shaderLinee != null
                ? m_shaderLinee
                : Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
                Debug.LogError("[FaceQuest] Nessuno shader per le linee: le box non si vedranno.");
            else
                m_materiale = new Material(shader);
            m_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void OnDestroy()
        {
            if (m_materiale != null) Destroy(m_materiale);
        }

        public void Collega(PassthroughCameraAccess camera) => m_camera = camera;

        /// <summary>Nasconde tutto: serve quando il riconoscimento viene spento.</summary>
        public void Pulisci()
        {
            foreach (var b in m_attive) { b.Go.SetActive(false); m_riserva.Add(b); }
            m_attive.Clear();
        }

        /// <summary>
        /// Ricalcola i bersagli dalle tracce. La posa e quella CATTURATA quando il
        /// fotogramma e stato preso, non quella attuale: nel frattempo la testa si
        /// e mossa, e usare la posa corrente sposterebbe le box di parecchi
        /// centimetri.
        /// </summary>
        public void Aggiorna(IReadOnlyList<Traccia> tracce, Pose posaCamera)
        {
            if (m_camera == null || !m_camera.IsPlaying) return;

            foreach (var b in m_attive) b.Vista = false;

            foreach (var t in tracce)
            {
                if (!Proietta(t, posaCamera, out var centro, out var rotazione, out var dimensione, out var distanza))
                {
                    t.PosizioneValida = false;
                    continue;
                }

                t.PosizioneMondo = centro;
                t.DimensioneMondo = dimensione;
                t.Distanza = distanza;
                t.PosizioneValida = true;

                var box = Trova(t.Id) ?? Prendi(t.Id);
                box.Vista = true;
                box.PosBersaglio = centro;
                box.RotBersaglio = rotazione;
                box.DimBersaglio = dimensione;

                if (box.Nuova)
                {
                    box.Pos = centro; box.Rot = rotazione; box.Dim = dimensione;
                    box.Nuova = false;
                }

                var colore = t.Esito switch
                {
                    EsitoIdentita.Riconosciuta => m_coloreRiconosciuta,
                    EsitoIdentita.Incerta => m_coloreIncerta,
                    _ => m_coloreNuova
                };
                // I colori per vertice del LineRenderer (startColor/endColor) non
                // servono a niente con URP/Unlit: quello shader non li moltiplica,
                // e le box venivano disegnate tutte bianche qualunque fosse lo
                // stato. Il colore va messo nel materiale. 'Linea.material' crea
                // un'istanza per questo renderer al primo accesso e viene
                // distrutta con l'oggetto, quindi non perde memoria.
                var materiale = box.Linea.material;
                if (materiale != null)
                {
                    if (materiale.HasProperty(IdColoreBase)) materiale.SetColor(IdColoreBase, colore);
                    else materiale.color = colore;
                }
                box.Linea.startColor = colore;
                box.Linea.endColor = colore;
                box.Etichetta.color = colore;

                var testo = t.Etichetta;
                if (m_mostraConfidenza && t.Esito == EsitoIdentita.Riconosciuta)
                    testo += $"\n{Mathf.RoundToInt(Mathf.Clamp01(t.Confidenza) * 100f)}%";
                box.Etichetta.text = testo;
            }

            for (var i = m_attive.Count - 1; i >= 0; i--)
            {
                if (m_attive[i].Vista) continue;
                m_attive[i].Go.SetActive(false);
                m_riserva.Add(m_attive[i]);
                m_attive.RemoveAt(i);
            }
        }

        private void Update()
        {
            var k = 1f - Mathf.Exp(-m_reattivita * Time.deltaTime);

            foreach (var b in m_attive)
            {
                b.Pos = Vector3.Lerp(b.Pos, b.PosBersaglio, k);
                b.Rot = Quaternion.Slerp(b.Rot, b.RotBersaglio, k);
                b.Dim = Vector2.Lerp(b.Dim, b.DimBersaglio, k);

                b.Go.transform.SetPositionAndRotation(b.Pos, b.Rot);
                DisegnaRettangolo(b.Linea, b.Dim);

                var altezza = Mathf.Max(0.02f, b.Dim.y);
                b.Cartello.localPosition = new Vector3(0f, -altezza * 0.5f - 0.03f, 0f);

                // La scala del cartello si MOLTIPLICA, non si sostituisce.
                //
                // Qui c'era il guasto piu vistoso di tutti: assegnavo a localScale
                // un valore fra 0,05 e 0,35 per adattare l'etichetta alla
                // dimensione del volto, buttando via il fattore 0,0012 che
                // trasforma i pixel di UI in metri. Un cartello da 260x90 unita
                // diventava alto decine di metri: nel visore si vedevano lettere
                // giganti, gialle o verdi secondo lo stato dell'identita, che
                // coprivano la stanza.
                var fattore = Mathf.Clamp(altezza / 0.18f, 0.55f, 1.6f);
                b.Cartello.localScale = Vector3.one * (ScalaCartello * fattore);
            }
        }

        /// <summary>
        /// Dal box normalizzato al mondo. Restituisce false se la geometria non
        /// permette una stima sensata (volto troppo di profilo per vedere i due
        /// occhi distinti, oppure distanza assurda).
        /// </summary>
        private bool Proietta(Traccia t, Pose posa, out Vector3 centro, out Quaternion rotazione,
                              out Vector2 dimensione, out float distanza)
        {
            centro = default; rotazione = Quaternion.identity; dimensione = default; distanza = 0f;

            var box = t.BoxLisciata;
            var v = t.Ultimo;

            // Attenzione all'asse y: i rilevamenti hanno origine in alto, il
            // viewport della camera in basso.
            var rSx = m_camera.ViewportPointToRay(FaceQuestConfig.AViewport(v.L0), posa);
            var rDx = m_camera.ViewportPointToRay(FaceQuestConfig.AViewport(v.L1), posa);
            var angolo = Vector3.Angle(rSx.direction, rDx.direction) * Mathf.Deg2Rad;

            if (angolo > 1e-4f)
                distanza = m_distanzaPupille / (2f * Mathf.Tan(angolo * 0.5f));

            // Ripiego sull'altezza del box quando la stima dalle pupille non
            // regge: succede di profilo, quando i due occhi si sovrappongono e
            // l'angolo fra i raggi collassa. Un volto adulto e alto ~22 cm, e
            // sbagliare di qualche centimetro la profondita sposta la box di
            // poco — mentre non disegnarla affatto e il guasto che si vede.
            if (distanza < 0.2f || distanza > 6f)
            {
                var rAlto = m_camera.ViewportPointToRay(FaceQuestConfig.AViewport(box.center.x, box.yMin), posa);
                var rBasso = m_camera.ViewportPointToRay(FaceQuestConfig.AViewport(box.center.x, box.yMax), posa);
                var verticale = Vector3.Angle(rAlto.direction, rBasso.direction) * Mathf.Deg2Rad;
                if (verticale > 1e-4f)
                {
                    distanza = m_altezzaVolto / (2f * Mathf.Tan(verticale * 0.5f));
                    Ripieghi++;
                }
            }

            if (distanza < 0.2f || distanza > 6f) { Scarti++; return false; }

            var centroNorm = FaceQuestConfig.AViewport(box.center);
            var raggioCentro = m_camera.ViewportPointToRay(centroNorm, posa);
            centro = raggioCentro.GetPoint(distanza);

            var normale = (centro - posa.position).normalized;
            rotazione = Quaternion.LookRotation(normale, posa.rotation * Vector3.up);

            // Le dimensioni si ricavano intersecando i raggi degli angoli con il
            // piano perpendicolare alla vista che passa per il centro: e l'unico
            // modo di avere una box che copre davvero il volto a ogni distanza.
            var piano = new Plane(normale, centro);
            var rMin = m_camera.ViewportPointToRay(FaceQuestConfig.AViewport(box.xMin, box.yMax), posa);
            var rMax = m_camera.ViewportPointToRay(FaceQuestConfig.AViewport(box.xMax, box.yMin), posa);
            if (!piano.Raycast(rMin, out var dMin) || !piano.Raycast(rMax, out var dMax)) return false;

            var pMin = rMin.GetPoint(dMin);
            var pMax = rMax.GetPoint(dMax);
            var inv = Quaternion.Inverse(rotazione);
            var lMin = inv * (pMin - centro);
            var lMax = inv * (pMax - centro);

            dimensione = new Vector2(Mathf.Abs(lMax.x - lMin.x), Mathf.Abs(lMax.y - lMin.y));
            return dimensione.x > 0.01f && dimensione.y > 0.01f;
        }

        private static void DisegnaRettangolo(LineRenderer lr, Vector2 dim)
        {
            var hx = dim.x * 0.5f; var hy = dim.y * 0.5f;
            lr.SetPosition(0, new Vector3(-hx, -hy, 0f));
            lr.SetPosition(1, new Vector3(hx, -hy, 0f));
            lr.SetPosition(2, new Vector3(hx, hy, 0f));
            lr.SetPosition(3, new Vector3(-hx, hy, 0f));
        }

        private Box Trova(int idTraccia)
        {
            foreach (var b in m_attive) if (b.IdTraccia == idTraccia) return b;
            return null;
        }

        private Box Prendi(int idTraccia)
        {
            Box b;
            if (m_riserva.Count > 0)
            {
                b = m_riserva[m_riserva.Count - 1];
                m_riserva.RemoveAt(m_riserva.Count - 1);
                b.Go.SetActive(true);
            }
            else
            {
                b = Costruisci();
            }

            b.IdTraccia = idTraccia;
            b.Nuova = true;
            m_attive.Add(b);
            return b;
        }

        private Box Costruisci()
        {
            var go = new GameObject("BoxVolto");
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 4;
            lr.widthMultiplier = m_spessore;
            lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sharedMaterial = m_materiale;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // L'etichetta e una canvas in world space: in AR non c'e uno schermo
            // su cui sovrapporre testo, ogni elemento di UI e un oggetto nella
            // stanza. La scala minuscola serve perche un pixel di UI vale un metro.
            var cartello = new GameObject("Cartello", typeof(Canvas), typeof(CanvasScaler));
            cartello.transform.SetParent(go.transform, false);
            var canvas = cartello.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(260f, 90f);
            rt.localScale = Vector3.one * ScalaCartello;

            var sfondoGo = new GameObject("Sfondo", typeof(Image));
            sfondoGo.transform.SetParent(cartello.transform, false);
            var sRt = sfondoGo.GetComponent<RectTransform>();
            sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
            sRt.offsetMin = Vector2.zero; sRt.offsetMax = Vector2.zero;
            sfondoGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var testoGo = new GameObject("Testo", typeof(Text));
            testoGo.transform.SetParent(cartello.transform, false);
            var tRt = testoGo.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero; tRt.offsetMax = Vector2.zero;

            var testo = testoGo.GetComponent<Text>();
            testo.font = m_font;
            testo.fontSize = 34;
            testo.alignment = TextAnchor.MiddleCenter;
            testo.color = Color.white;
            testo.horizontalOverflow = HorizontalWrapMode.Overflow;
            testo.verticalOverflow = VerticalWrapMode.Overflow;

            return new Box
            {
                Go = go,
                Linea = lr,
                Etichetta = testo,
                Cartello = cartello.transform
            };
        }
    }
}
