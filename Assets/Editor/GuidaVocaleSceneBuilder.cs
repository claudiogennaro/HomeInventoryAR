// Costruisce la scena di "Guida Vocale": il detector di Meta piu la sintesi
// vocale, il puntamento col controller destro e la vibrazione sul sinistro.
//
// Vive nello stesso progetto dell'inventario perche il pezzo condiviso e grosso:
// il detector, il modello YOLO e il registro degli oggetti. Cio che cambia e
// l'uscita — voce e vibrazione invece di marker e archivio.
//
// Il package name viene cambiato prima del build, altrimenti le due app si
// sovrascriverebbero a vicenda sul visore.

using Accessibilita;
using InventarioAR;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Meta.XR;
using UnityEngine.UI;

public static class GuidaVocaleSceneBuilder
{
    private const string ScenaSample = "Assets/PassthroughCameraApiSamples/MultiObjectDetection/MultiObjectDetection.unity";
    private const string ScenaGuida  = "Assets/Scenes/GuidaVocale.unity";

    private const string PackageGuida = "it.cnr.isti.guidavocale";
    private const string NomeGuida    = "Guida Vocale";

    [MenuItem("Guida Vocale/Crea scena Guida Vocale", priority = 10)]
    public static void Crea()
    {
        if (!System.IO.File.Exists(ScenaSample))
        {
            Debug.LogError($"[Guida] Non trovo {ScenaSample}.");
            return;
        }

        EditorSceneManager.OpenScene(ScenaSample, OpenSceneMode.Single);

        var ui = Object.FindAnyObjectByType<SentisInferenceUiManager>();
        if (ui == null)
        {
            Debug.LogError("[Guida] SentisInferenceUiManager non trovato nella scena del sample.");
            return;
        }

        // Il DetectionManager del sample risponde ai tasti e piazza marker: qui
        // i tasti servono a noi e i marker non hanno senso per chi non vede.
        var dm = Object.FindAnyObjectByType<DetectionManager>();
        if (dm != null) dm.enabled = false;

        TaraIlDetector();
        SopprimiIlConfine();

        // Il registro serve anche qui, e non per riuso opportunistico: senza
        // identita stabili la voce annuncerebbe lo sfarfallio del detector.
        var go = new GameObject("GuidaVocale");
        var inventario = go.AddComponent<InventarioManager>();
        var voce = go.AddComponent<SintesiVocale>();
        var puntamento = go.AddComponent<PuntamentoVocale>();
        var ostacoli = go.AddComponent<RilevaOstacoli>();

        var soInv = new SerializedObject(inventario);
        soInv.FindProperty("m_uiInference").objectReferenceValue = ui;
        var camera = Object.FindAnyObjectByType<Meta.XR.PassthroughCameraAccess>();
        if (camera != null) soInv.FindProperty("m_cameraAccess").objectReferenceValue = camera;
        // Persone e animali NON si escludono qui: per chi non vede sono la cosa
        // piu importante da sapere.
        soInv.FindProperty("m_ignoraEsseriViventi").boolValue = false;

        // Soglie ribaltate rispetto all'inventario. Li un oggetto incerto veniva
        // ancorato e salvato per sempre, quindi conveniva essere prudenti; qui
        // annunciare qualcosa di incerto costa nulla e tacere costa tutto. Con le
        // soglie dell'inventario il registro conosceva due oggetti in tutto, e non
        // c'era quasi mai niente da dire.
        soInv.FindProperty("m_osservazioniPerConferma").intValue = 2;
        soInv.FindProperty("m_scadenzaCandidati").floatValue = 15f;

        // Nessun prefab di marker: in questa app non si disegna nulla nello spazio.
        soInv.ApplyModifiedPropertiesWithoutUndo();

        var raggio = CreaRaggio();

        var rcSonda = Object.FindAnyObjectByType<EnvironmentRaycastManager>();

        var soPun = new SerializedObject(puntamento);
        soPun.FindProperty("m_inventario").objectReferenceValue = inventario;
        // Stessa profondita del ventaglio, ma un raggio solo e puntabile: e il
        // bastone bianco. Se manca, il puntamento resta solo vocale.
        if (rcSonda != null) soPun.FindProperty("m_raycast").objectReferenceValue = rcSonda;
        soPun.FindProperty("m_raggio").objectReferenceValue = raggio.transform;
        soPun.FindProperty("m_rendererRaggio").objectReferenceValue = raggio.GetComponent<Renderer>();
        soPun.ApplyModifiedPropertiesWithoutUndo();

        var soOst = new SerializedObject(ostacoli);
        var rc = rcSonda;
        if (rc != null) soOst.FindProperty("m_raycast").objectReferenceValue = rc;
        else Debug.LogWarning("[Guida] EnvironmentRaycastManager non trovato: niente rilevamento ostacoli.");

        var shaderRaggi = Shader.Find("Universal Render Pipeline/Unlit");
        if (shaderRaggi != null)
        {
            var matRaggi = new Material(shaderRaggi) { color = new Color(0.2f, 0.7f, 1f, 0.5f) };
            AssetDatabase.CreateAsset(matRaggi, "Assets/Scenes/SondaMat.mat");
            soOst.FindProperty("m_materialeRaggi").objectReferenceValue = matRaggi;
        }
        soOst.ApplyModifiedPropertiesWithoutUndo();

        CreaPannelloDiagnostica(voce, puntamento, inventario, ostacoli);

        if (!System.IO.Directory.Exists("Assets/Resources/Voci") ||
            System.IO.Directory.GetFiles("Assets/Resources/Voci", "*.wav").Length == 0)
        {
            Debug.LogWarning("[Guida] Nessuna clip in Assets/Resources/Voci: l'app sara muta. " +
                             "Usa 'Esporta le frasi da generare' e poi Tools/GeneraVoci.ps1.");
        }

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenaGuida);

        Debug.Log($"[Guida] Scena creata in {ScenaGuida}.");
    }

    [MenuItem("Guida Vocale/Crea scena Guida Vocale e installa sul Quest", priority = 11)]
    public static void CreaEInstalla()
    {
        Crea();

        var packagePrecedente = PlayerSettings.applicationIdentifier;
        var nomePrecedente = PlayerSettings.productName;

        try
        {
            // Package name distinto: senza questo, installare una delle due app
            // disinstalla l'altra, perche Android le considera la stessa.
            PlayerSettings.applicationIdentifier = PackageGuida;
            PlayerSettings.productName = NomeGuida;

            System.IO.Directory.CreateDirectory("Builds");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes           = new[] { ScenaGuida },
                target           = BuildTarget.Android,
                targetGroup      = BuildTargetGroup.Android,
                locationPathName = "Builds/GuidaVocale.apk",
                options          = BuildOptions.AutoRunPlayer,
            });

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                Debug.Log($"[Guida] Build riuscita ({report.summary.totalSize / (1024 * 1024)} MB), avviata sul visore.");
            else
                Debug.LogError($"[Guida] Build {report.summary.result}: {report.summary.totalErrors} errori.");
        }
        finally
        {
            // Ripristino sempre, anche se il build fallisce: lasciare il package
            // sbagliato farebbe sovrascrivere l'inventario al build successivo.
            PlayerSettings.applicationIdentifier = packagePrecedente;
            PlayerSettings.productName = nomePrecedente;
        }
    }

    /// <summary>
    /// Un cilindro sottile che fa da raggio. Non serve all'utente finale — che non
    /// lo vedrebbe — ma a chi sviluppa: capire cosa si sta puntando senza dover
    /// dedurlo dall'audio accorcia di molto ogni prova.
    /// </summary>
    private static GameObject CreaRaggio()
    {
        var raggio = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        raggio.name = "RaggioPuntamento";

        // Il cilindro di Unity e alto lungo Y: ruotandolo di 90 gradi la scala Z
        // diventa la lunghezza, che e comoda per allungarlo fino al bersaglio.
        raggio.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Object.DestroyImmediate(raggio.GetComponent<Collider>());

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.6f, 0.6f, 0.65f) };
            AssetDatabase.CreateAsset(mat, "Assets/Scenes/RaggioMat.mat");
            raggio.GetComponent<Renderer>().sharedMaterial = mat;
        }

        return raggio;
    }

    private static void TaraIlDetector()
    {
        var runner = Object.FindAnyObjectByType<SentisInferenceRunManager>();
        if (runner == null) return;

        var so = new SerializedObject(runner);
        so.FindProperty("m_scoreThreshold").floatValue = 0.45f;
        so.FindProperty("m_iouThreshold").floatValue = 0.35f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SopprimiIlConfine()
    {
        var manager = Object.FindAnyObjectByType<OVRManager>();
        if (manager == null) return;

        var so = new SerializedObject(manager);
        var prop = so.FindProperty("shouldBoundaryVisibilityBeSuppressed");
        if (prop == null) return;
        prop.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Pannello per chi sviluppa, non per l'utente finale: mostra lo stato della
    /// voce e cosa si sta puntando. Serve a te per capire cosa sta accadendo
    /// senza dover indossare il visore e fidarti solo dell'udito.
    /// </summary>
    private static void CreaPannelloDiagnostica(SintesiVocale voce, PuntamentoVocale puntamento,
                                                InventarioManager inventario, RilevaOstacoli ostacoli)
    {
        var go = new GameObject("PannelloGuida", typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(460f, 200f);
        rt.localScale = Vector3.one * 0.0009f;

        var sfondo = new GameObject("Sfondo", typeof(Image));
        sfondo.transform.SetParent(go.transform, false);
        var sRt = sfondo.GetComponent<RectTransform>();
        sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
        sRt.offsetMin = Vector2.zero; sRt.offsetMax = Vector2.zero;
        sfondo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        var testoGo = new GameObject("Testo", typeof(Text));
        testoGo.transform.SetParent(go.transform, false);
        var tRt = testoGo.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = new Vector2(16f, 16f); tRt.offsetMax = new Vector2(-16f, -16f);

        var testo = testoGo.GetComponent<Text>();
        testo.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        testo.fontSize = 22;
        testo.color = Color.white;
        testo.alignment = TextAnchor.UpperLeft;
        testo.horizontalOverflow = HorizontalWrapMode.Overflow;
        testo.verticalOverflow = VerticalWrapMode.Overflow;
        testo.text = "GUIDA VOCALE";

        var pannello = go.AddComponent<PannelloGuida>();
        var so = new SerializedObject(pannello);
        so.FindProperty("m_testo").objectReferenceValue = testo;
        so.FindProperty("m_voce").objectReferenceValue = voce;
        so.FindProperty("m_puntamento").objectReferenceValue = puntamento;
        so.FindProperty("m_inventario").objectReferenceValue = inventario;
        so.FindProperty("m_ostacoli").objectReferenceValue = ostacoli;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
