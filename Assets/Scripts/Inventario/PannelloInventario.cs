using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace InventarioAR
{
    /// <summary>
    /// Elenca nel visore gli oggetti confermati, uno per riga con la distanza.
    ///
    /// Serve a rispondere alla domanda che il solo conteggio non risolve: dieci
    /// oggetti sono dieci cose diverse, o sette piu tre doppioni? Due righe della
    /// stessa classe a distanze quasi uguali sono un doppione; a distanze diverse
    /// sono due oggetti veri.
    ///
    /// Il pannello sta in basso a sinistra e insegue la testa con un ritardo, cosi
    /// resta leggibile senza coprire quello che stai guardando.
    /// </summary>
    public class PannelloInventario : MonoBehaviour
    {
        [SerializeField] private InventarioManager m_inventario;
        [SerializeField] private Text m_testo;
        [SerializeField] private NavigazioneInventario m_navigazione;
        [SerializeField] private PersistenzaInventario m_persistenza;

        [Tooltip("Riquadro in cui compare lo scatto dell'oggetto selezionato.")]
        [SerializeField] private RawImage m_scatto;

        [Header("Posizione rispetto alla testa")]
        [SerializeField] private float m_distanza = 0.75f;
        [SerializeField] private float m_scostamentoOrizzontale = -0.35f;
        [SerializeField] private float m_scostamentoVerticale = -0.28f;
        [SerializeField] private float m_morbidezza = 4f;

        [Header("Contenuto")]
        [SerializeField] private int m_righeMassime = 14;
        [SerializeField] private float m_intervalloAggiornamento = 0.4f;

        [Tooltip("Mostra lo stato grezzo di controller e pizzichi in fondo al pannello.")]
        [SerializeField] private bool m_mostraDiagnostica = true;

        private readonly StringBuilder _sb = new();

        // Le immagini si tengono in cache: rileggere il PNG da disco a ogni
        // aggiornamento del pannello sarebbe uno spreco, e i file sono pochi e piccoli.
        private readonly Dictionary<string, Texture2D> _cacheScatti = new();
        private string _scattoMostrato;
        private float _prossimoAggiornamento;

        private void LateUpdate()
        {
            SeguiLaTesta();

            if (Time.time < _prossimoAggiornamento) return;
            _prossimoAggiornamento = Time.time + m_intervalloAggiornamento;
            Disegna();
        }

        private void SeguiLaTesta()
        {
            var testa = TrovaTesta();
            if (testa == null) return;

            // Ci si orienta sull'orizzontale: se il pannello seguisse anche il
            // beccheggio della testa, guardando in basso finirebbe per terra.
            var avanti = testa.forward;
            avanti.y = 0f;
            if (avanti.sqrMagnitude < 0.0001f) return;
            avanti.Normalize();

            var destra = Vector3.Cross(Vector3.up, avanti);

            var bersaglio = testa.position
                            + avanti * m_distanza
                            + destra * m_scostamentoOrizzontale
                            + Vector3.up * m_scostamentoVerticale;

            var t = 1f - Mathf.Exp(-m_morbidezza * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, bersaglio, t);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(avanti, Vector3.up), t);
        }

        private void Disegna()
        {
            if (m_testo == null || m_inventario == null) return;

            var testa = TrovaTesta();
            _sb.Clear();
            var ancorati = 0;
            foreach (var o in m_inventario.Oggetti)
                if (o.Confermato && !string.IsNullOrEmpty(o.UuidAncora)) ancorati++;

            _sb.Append("INVENTARIO  ").Append(m_inventario.NumeroConfermati).Append(" visti, ")
               .Append(ancorati).Append(" salvati\n")
               .Append("* = ancorato   A = scorri   stick = annulla   B tenuto = esci\n")
               .Append("stick tenuto 3s = azzera inventario\n\n");

            var righe = 0;
            foreach (var o in m_inventario.Oggetti)
            {
                if (!o.Confermato) continue;

                if (righe >= m_righeMassime)
                {
                    _sb.Append("...");
                    break;
                }

                // Il pallino distingue a colpo d'occhio cosa sopravvivera allo
                // spegnimento da cio che e ancora solo memoria di questa sessione.
                var ancorato = !string.IsNullOrEmpty(o.UuidAncora);
                var scelto = m_navigazione != null && m_navigazione.Selezionato == o;

                _sb.Append(scelto ? ">" : " ");
                _sb.Append(ancorato ? "* " : "  ");

                _sb.Append(o.Classe.PadRight(13));

                if (testa != null)
                    _sb.Append(Vector3.Distance(testa.position, o.Posizione).ToString("0.0")).Append(" m  ");

                if (ancorato)
                    _sb.Append(o.Osservazioni).Append(" oss.");
                else
                    _sb.Append(o.Osservazioni).Append('/').Append(m_inventario.OsservazioniSolide);

                _sb.Append('\n');
                righe++;
            }

            if (righe == 0) _sb.Append("nessun oggetto confermato");

            if (m_navigazione != null && m_navigazione.ProgressoAzzeramento > 0f)
            {
                _sb.Append("\nAZZERO L'INVENTARIO fra ")
                   .Append(((1f - m_navigazione.ProgressoAzzeramento) * 3f).ToString("0.0"))
                   .Append(" s — lascia lo stick per annullare");
            }

            if (m_navigazione != null && m_navigazione.ProgressoUscita > 0f)
            {
                _sb.Append("\nUSCITA fra ")
                   .Append((1f - m_navigazione.ProgressoUscita).ToString("0.0"))
                   .Append(" s — lascia B per annullare");
            }

            if (m_navigazione != null && m_navigazione.Selezionato != null)
            {
                _sb.Append('\n');
                _sb.Append(m_navigazione.Arrivato
                    ? $"--> {m_navigazione.Selezionato.Classe}: dovresti vederlo"
                    : $"--> {m_navigazione.Selezionato.Classe} a {m_navigazione.DistanzaDalBersaglio:0.0} m");
            }

            MostraScatto();

            if (m_mostraDiagnostica)
            {
                _sb.Append('\n');
                if (m_persistenza != null) _sb.Append('\n').Append(m_persistenza.Stato);
                _sb.Append('\n').Append(Comandi.Diagnostica());
            }

            m_testo.text = _sb.ToString();
        }

        /// <summary>
        /// Mostra la foto dell'oggetto selezionato, se ne ha una.
        ///
        /// E il pezzo che chiude il giro: la freccia dice dove andare, la foto dice
        /// che cosa cercare quando si arriva — utile soprattutto per un telecomando
        /// finito sotto un cuscino, dove sapere la direzione non basta.
        /// </summary>
        private void MostraScatto()
        {
            if (m_scatto == null) return;

            var scelto = m_navigazione != null ? m_navigazione.Selezionato : null;
            var nome = scelto != null ? scelto.Scatto : null;

            if (string.IsNullOrEmpty(nome))
            {
                if (m_scatto.enabled) m_scatto.enabled = false;
                _scattoMostrato = null;
                return;
            }

            if (nome != _scattoMostrato)
            {
                if (!_cacheScatti.TryGetValue(nome, out var tex))
                {
                    tex = ScattiInventario.Carica(nome);
                    _cacheScatti[nome] = tex;   // anche null: non riprovare a ogni frame
                }

                m_scatto.texture = tex;
                _scattoMostrato = nome;
            }

            m_scatto.enabled = m_scatto.texture != null;
        }

        private static Transform TrovaTesta()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
