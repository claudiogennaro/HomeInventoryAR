using UnityEngine;

namespace FaceQuest
{
    /// <summary>
    /// Tiene un pannello in una posizione fissa rispetto alla testa.
    ///
    /// Con una zona morta: un pannello che inseguisse ogni micromovimento della
    /// testa sarebbe impossibile da puntare, perche scapperebbe proprio mentre
    /// si prova a raggiungerlo.
    /// </summary>
    public class SeguiTesta : MonoBehaviour
    {
        [SerializeField] private float m_distanza = 0.75f;
        [SerializeField] private float m_lato = -0.42f;      // negativo = a sinistra
        [SerializeField] private float m_altezza = -0.02f;
        [SerializeField] private float m_zonaMorta = 22f;    // gradi
        [SerializeField] private float m_velocita = 3.5f;

        private Transform m_testa;
        private bool m_inMovimento;

        public void Collega(Transform testa) => m_testa = testa;

        private void Start()
        {
            if (m_testa != null) return;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            m_testa = rig != null ? rig.centerEyeAnchor : Camera.main?.transform;
            if (m_testa != null) Piazza(1f);
        }

        private void Update()
        {
            if (m_testa == null) return;

            var bersaglio = Bersaglio();
            var direzioneAttuale = transform.position - m_testa.position;
            var direzioneBersaglio = bersaglio - m_testa.position;
            var angolo = Vector3.Angle(direzioneAttuale, direzioneBersaglio);

            if (!m_inMovimento && angolo > m_zonaMorta) m_inMovimento = true;
            if (m_inMovimento && angolo < 1.5f) m_inMovimento = false;

            if (m_inMovimento) Piazza(1f - Mathf.Exp(-m_velocita * Time.deltaTime));
            else transform.rotation = Quaternion.Slerp(transform.rotation, Rotazione(), 1f - Mathf.Exp(-m_velocita * Time.deltaTime));
        }

        private Vector3 Bersaglio()
        {
            var avanti = m_testa.forward; avanti.y = 0f;
            if (avanti.sqrMagnitude < 1e-4f) avanti = m_testa.forward;
            avanti.Normalize();

            // Cross(up, avanti) in Unity da la DESTRA: Cross((0,1,0),(0,0,1)) = (1,0,0).
            // Qui c'era un -1 di troppo, e il pannello con m_lato negativo
            // finiva a destra invece che a sinistra.
            var destra = Vector3.Cross(Vector3.up, avanti).normalized;
            return m_testa.position + avanti * m_distanza + destra * m_lato + Vector3.up * m_altezza;
        }

        private Quaternion Rotazione()
        {
            var verso = transform.position - m_testa.position;
            return verso.sqrMagnitude < 1e-4f ? transform.rotation : Quaternion.LookRotation(verso, Vector3.up);
        }

        private void Piazza(float k)
        {
            transform.position = Vector3.Lerp(transform.position, Bersaglio(), k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Rotazione(), k);
        }
    }
}
