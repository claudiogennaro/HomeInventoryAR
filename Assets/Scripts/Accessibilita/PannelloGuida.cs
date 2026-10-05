using System.Text;
using InventarioAR;
using UnityEngine;
using UnityEngine.UI;

namespace Accessibilita
{
    /// <summary>
    /// Pannello di servizio per lo sviluppo: mostra lo stato della voce, cosa si
    /// sta puntando e quanti oggetti conosce il registro.
    ///
    /// Non fa parte dell'app per l'utente finale — che non lo vedrebbe — ma serve
    /// a chi la costruisce: senza, l'unico modo di capire se il TTS e partito
    /// sarebbe indossare il visore e stare in ascolto.
    /// </summary>
    public class PannelloGuida : MonoBehaviour
    {
        [SerializeField] private Text m_testo;
        [SerializeField] private SintesiVocale m_voce;
        [SerializeField] private PuntamentoVocale m_puntamento;
        [SerializeField] private InventarioManager m_inventario;
        [SerializeField] private RilevaOstacoli m_ostacoli;

        [SerializeField] private float m_distanza = 0.8f;
        [SerializeField] private float m_scostamentoVerticale = -0.3f;
        [SerializeField] private float m_morbidezza = 4f;

        [Tooltip("Secondi di pressione su B per chiudere l'app.")]
        [SerializeField] private float m_tempoUscita = 1.5f;

        private readonly StringBuilder _sb = new();
        private float _premutoDa = -1f;
        private float _progressoUscita;

        private void Update() => GestisciUscita();

        private void LateUpdate()
        {
            SeguiLaTesta();
            Disegna();
        }

        /// <summary>
        /// Uscita tenendo premuto B, come nell'inventario: le due app si usano una
        /// dopo l'altra e avere lo stesso gesto evita di doverselo ricordare.
        /// </summary>
        private void GestisciUscita()
        {
            if (Comandi.UscitaTenuta())
            {
                if (_premutoDa < 0f) _premutoDa = Time.time;
                _progressoUscita = Mathf.Clamp01((Time.time - _premutoDa) / m_tempoUscita);

                if (_progressoUscita >= 1f)
                {
                    Debug.Log("[Guida] Uscita richiesta dall'utente.");
                    Application.Quit();
                }
            }
            else
            {
                _premutoDa = -1f;
                _progressoUscita = 0f;
            }
        }

        private void SeguiLaTesta()
        {
            var testa = TrovaTesta();
            if (testa == null) return;

            var avanti = testa.forward;
            avanti.y = 0f;
            if (avanti.sqrMagnitude < 0.0001f) return;
            avanti.Normalize();

            var bersaglio = testa.position + avanti * m_distanza + Vector3.up * m_scostamentoVerticale;
            var t = 1f - Mathf.Exp(-m_morbidezza * Time.deltaTime);

            transform.position = Vector3.Lerp(transform.position, bersaglio, t);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(avanti, Vector3.up), t);
        }

        private void Disegna()
        {
            if (m_testo == null) return;

            _sb.Clear();
            _sb.Append("GUIDA VOCALE\n\n");

            if (m_voce != null)
            {
                _sb.Append(m_voce.Stato).Append('\n');
                _sb.Append(m_voce.Dettaglio).Append('\n');
            }
            if (m_inventario != null)
                _sb.Append("oggetti noti: ").Append(m_inventario.NumeroConfermati).Append('\n');
            if (m_puntamento != null) _sb.Append(m_puntamento.Stato).Append('\n');
            if (m_ostacoli != null) _sb.Append(m_ostacoli.Stato).Append('\n');

            if (_progressoUscita > 0f)
                _sb.Append("\nUSCITA fra ")
                   .Append(((1f - _progressoUscita) * m_tempoUscita).ToString("0.0"))
                   .Append(" s — lascia B per annullare\n");

            _sb.Append("\nB tenuto = esci\n");
            _sb.Append(Comandi.Diagnostica());

            m_testo.text = _sb.ToString();
        }

        private static Transform TrovaTesta()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
