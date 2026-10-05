// Il puntatore con cui si tocca la UI in aria.
//
// Perche un raycast fisico fatto a mano invece dell'EventSystem di Unity: la UI
// in world space con i controller richiede canvas raycaster, input module e
// pointer configurati in modo esatto, e quando qualcosa non risponde non c'e
// modo di capire quale dei tre anelli si e rotto. Qui il percorso e uno solo:
// un raggio dall'ancora della mano, i bersagli hanno un collider, chi viene
// colpito riceve la chiamata.
//
// I comandi stanno in FaceComandi.cs, i bersagli in BersaglioAR.cs, il
// pannello che segue la testa in SeguiTesta.cs: un MonoBehaviour per file.

using UnityEngine;

namespace FaceQuest
{
    public class PuntatoreAR : MonoBehaviour
    {
        [SerializeField] private float m_portata = 4f;
        [SerializeField] private Color m_colore = new Color(0.45f, 0.72f, 1f, 0.9f);
        [SerializeField, Tooltip("Assegnato dal costruttore di scena: un riferimento serializzato finisce sempre nella build.")]
        private Shader m_shaderRaggio;

        private Transform m_mano;
        private Transform m_testa;
        private LineRenderer m_raggio;
        private BersaglioAR m_sotto;
        private readonly RaycastHit[] m_colpi = new RaycastHit[8];

        public Transform Testa => m_testa;

        /// <summary>L'ancora da cui parte il raggio. La usa chi trascina un pannello.</summary>
        public Transform Mano => m_mano;

        /// <summary>Cio che il raggio sta indicando adesso, o null.</summary>
        public BersaglioAR Sotto => m_sotto;

        private void Start()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                m_testa = rig.centerEyeAnchor;
                m_mano = rig.rightHandAnchor != null ? rig.rightHandAnchor : rig.centerEyeAnchor;
            }
            else
            {
                var cam = Camera.main;
                if (cam != null) { m_testa = cam.transform; m_mano = cam.transform; }
                Debug.LogWarning("[FaceQuest] Nessun OVRCameraRig: il puntatore usa la telecamera principale.");
            }

            var go = new GameObject("Raggio");
            go.transform.SetParent(transform, false);
            m_raggio = go.AddComponent<LineRenderer>();
            m_raggio.useWorldSpace = true;
            m_raggio.positionCount = 2;
            m_raggio.widthMultiplier = 0.004f;
            var shader = m_shaderRaggio != null
                ? m_shaderRaggio
                : Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null) m_raggio.sharedMaterial = new Material(shader);
            m_raggio.startColor = m_colore;
            m_raggio.endColor = new Color(m_colore.r, m_colore.g, m_colore.b, 0.1f);
            m_raggio.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
            if (m_mano == null) return;

            var origine = m_mano.position;
            var direzione = m_mano.forward;
            var fine = origine + direzione * m_portata;

            // RaycastNonAlloc e non Raycast: la scena contiene i collider del
            // sample e della stanza, e il primo oggetto colpito spesso non e un
            // bersaglio. Si scorre finche non si trova qualcosa di premibile.
            var n = Physics.RaycastNonAlloc(origine, direzione, m_colpi, m_portata);
            BersaglioAR trovato = null;
            var distanza = m_portata;

            for (var i = 0; i < n; i++)
            {
                var b = m_colpi[i].collider.GetComponentInParent<BersaglioAR>();
                if (b == null) continue;
                if (m_colpi[i].distance >= distanza) continue;
                trovato = b;
                distanza = m_colpi[i].distance;
                fine = m_colpi[i].point;
            }

            if (trovato != m_sotto)
            {
                m_sotto?.Evidenzia?.Invoke(false);
                trovato?.Evidenzia?.Invoke(true);
                m_sotto = trovato;
            }

            m_raggio.SetPosition(0, origine);
            m_raggio.SetPosition(1, fine);

            if (m_sotto != null && FaceComandi.ClickPremuto()) m_sotto.Premi();
        }
    }
}
