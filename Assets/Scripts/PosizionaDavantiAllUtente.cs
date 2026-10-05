using UnityEngine;

/// <summary>
/// Mette questo oggetto davanti all'utente al primo avvio, calcolando la
/// posizione dalla testa invece di usare una coordinata fissa nel mondo.
///
/// Perche serve: una posizione assoluta come (0, 1, 1) dipende da dove il
/// runtime mette l'origine del tracking. Con EyeLevel y=0 e all'altezza degli
/// occhi, con FloorLevel e sul pavimento: la stessa coordinata finisce sul
/// soffitto o sul petto a seconda dell'impostazione. Posizionandolo rispetto
/// alla testa, il risultato e sempre quello atteso.
/// </summary>
public class PosizionaDavantiAllUtente : MonoBehaviour
{
    [Tooltip("Distanza in metri davanti alla testa.")]
    public float distanza = 1.0f;

    [Tooltip("Scostamento verticale rispetto agli occhi. Negativo = piu in basso.")]
    public float scostamentoVerticale = -0.25f;

    [Tooltip("Frame da attendere prima di posizionare: il tracking non e valido al primo frame.")]
    public int frameDiAttesa = 20;

    private int _attesa;

    private void Start()
    {
        _attesa = frameDiAttesa;
        // Nascondi finche non sappiamo dove metterlo, per non far comparire
        // il cubo in un punto sbagliato per una frazione di secondo.
        var r = GetComponent<Renderer>();
        if (r != null) r.enabled = false;
    }

    private void Update()
    {
        if (_attesa-- > 0) return;

        var testa = TrovaTesta();
        if (testa == null) return;   // riprova al frame successivo

        var avanti = testa.forward;
        avanti.y = 0f;
        if (avanti.sqrMagnitude < 0.0001f) avanti = Vector3.forward;
        avanti.Normalize();

        transform.position = testa.position + avanti * distanza + Vector3.up * scostamentoVerticale;
        transform.rotation = Quaternion.LookRotation(-avanti, Vector3.up);

        var r = GetComponent<Renderer>();
        if (r != null) r.enabled = true;

        Debug.Log($"[Fase 0] Cubo posizionato in {transform.position} (testa: {testa.position}).");
        enabled = false;
    }

    private static Transform TrovaTesta()
    {
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
        return Camera.main != null ? Camera.main.transform : null;
    }
}
