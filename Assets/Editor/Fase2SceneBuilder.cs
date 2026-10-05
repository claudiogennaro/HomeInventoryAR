// Prepara la scena della Fase 2: il detector di Meta piu il nostro registro
// dell'inventario, con i riferimenti gia collegati.
//
// Perche via codice e non trascinando nell'Inspector: collegare a mano tre
// riferimenti e facile da sbagliare e impossibile da rifare identico. Cosi il
// comando e ripetibile e la scena si puo rigenerare da zero in ogni momento.

using System.Linq;
using InventarioAR;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class Fase2SceneBuilder
{
    private const string ScenaSample = "Assets/PassthroughCameraApiSamples/MultiObjectDetection/MultiObjectDetection.unity";
    private const string PrefabMarker = "Assets/PassthroughCameraApiSamples/MultiObjectDetection/DetectionManager/Prefabs/DetectionSpawnMarker.prefab";
    private const string ScenaFase2 = "Assets/Scenes/Fase2_Inventario.unity";

    [MenuItem("Inventario AR/Crea scena Fase 2 (inventario automatico)", priority = 40)]
    public static void Crea()
    {
        if (!System.IO.File.Exists(ScenaSample))
        {
            Debug.LogError($"[Fase 2] Non trovo {ScenaSample}. Il detector di Meta non e nel progetto.");
            return;
        }

        EditorSceneManager.OpenScene(ScenaSample, OpenSceneMode.Single);

        var ui = Object.FindAnyObjectByType<SentisInferenceUiManager>();
        if (ui == null)
        {
            Debug.LogError("[Fase 2] Nella scena del sample non trovo SentisInferenceUiManager.");
            return;
        }

        var marker = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabMarker);
        if (marker == null)
        {
            Debug.LogError($"[Fase 2] Non trovo il prefab del marker in {PrefabMarker}.");
            return;
        }

        // Se la scena e gia stata preparata, riparto pulito invece di aggiungere un doppione.
        foreach (var vecchio in Object.FindObjectsByType<InventarioManager>(FindObjectsSortMode.None).ToList())
            Object.DestroyImmediate(vecchio.gameObject);

        var go = new GameObject("InventarioManager");
        var manager = go.AddComponent<InventarioManager>();
        var persistenza = go.AddComponent<PersistenzaInventario>();

        // Esplicito, non implicito. Il flag di azzeramento resta scritto nella
        // scena, quindi dopo averlo usato una volta ogni build successiva
        // continuerebbe a cancellare l'archivio: un errore silenzioso che si
        // manifesta come "l'app non ricorda niente". Chi vuole azzerare passa
        // dal comando dedicato, che lo rimette a true dopo questa riga.
        var soPers = new SerializedObject(persistenza);
        soPers.FindProperty("m_azzeraArchivioAllAvvio").boolValue = false;
        soPers.ApplyModifiedPropertiesWithoutUndo();

        // I campi sono privati e serializzati: SerializedObject e il modo corretto
        // di scriverli da editor, e registra la modifica per il salvataggio.
        var so = new SerializedObject(manager);
        so.FindProperty("m_uiInference").objectReferenceValue = ui;

        var camera = Object.FindAnyObjectByType<Meta.XR.PassthroughCameraAccess>();
        if (camera != null)
            so.FindProperty("m_cameraAccess").objectReferenceValue = camera;
        else
            Debug.LogWarning("[Fase 2] PassthroughCameraAccess non trovato: niente scatti degli oggetti.");
        so.FindProperty("m_prefabMarker").objectReferenceValue = marker.GetComponent<DetectionSpawnMarkerAnim>();

        var menu = Object.FindAnyObjectByType<DetectionUiMenuManager>();
        if (menu != null)
            so.FindProperty("m_uiMenu").objectReferenceValue = menu;
        else
            Debug.LogWarning("[Fase 2] Non trovo DetectionUiMenuManager: il conteggio non comparira nel visore.");
        so.ApplyModifiedPropertiesWithoutUndo();

        TaraIlDetector();
        SopprimiIlConfine();
        SpegniDetectionManagerDelSample();
        var navigazione = CreaNavigazione(manager);
        CreaPannelloInventario(manager, navigazione, persistenza);

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenaFase2);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenaFase2, true) };

        Debug.Log($"[Fase 2] Scena creata in {ScenaFase2}, riferimenti collegati. " +
                  "Usa 'Crea scena Fase 2 e installa sul Quest' per provarla sul visore.");
    }

    /// <summary>
    /// Costruisce da codice il pannello che elenca gli oggetti confermati.
    ///
    /// Una canvas in world space, non screen space: in VR non esiste uno schermo
    /// su cui sovrapporre la UI, gli elementi sono oggetti nella stanza. La scala
    /// minuscola serve perche un pixel di UI diventa un metro nel mondo.
    /// </summary>
    /// <summary>
    /// Disattiva il DetectionManager del sample.
    ///
    /// Nella nostra versione la conferma e automatica, quindi quel componente non
    /// serve piu — ma continuava a rispondere ai tasti: su A piazzava un marker
    /// per OGNI rilevamento in vista, e su B li cancellava tutti. Da quando A e
    /// diventato "scorri la selezione", ogni pressione aggiungeva una manciata di
    /// etichette sugli stessi oggetti: le label multiple sul mouse venivano da qui,
    /// non dal registro, che infatti ne elencava uno solo.
    /// </summary>
    private static void SpegniDetectionManagerDelSample()
    {
        var dm = Object.FindAnyObjectByType<DetectionManager>();
        if (dm == null)
        {
            Debug.LogWarning("[Fase 2] DetectionManager del sample non trovato.");
            return;
        }

        dm.enabled = false;
        Debug.Log("[Fase 2] DetectionManager del sample disattivato: i tasti sono solo nostri.");
    }

    /// <summary>
    /// Chiede al sistema di nascondere il confine del Guardian.
    ///
    /// Serve perche l'app si usa camminando per casa: con il confine attivo il
    /// Quest interrompe l'esperienza ogni volta che lo si attraversa, e ridefinirlo
    /// stanza per stanza non ha senso quando si vede gia l'ambiente reale.
    ///
    /// Due avvertenze dalla documentazione di Meta: funziona solo a passthrough
    /// inizializzato, e il sistema puo comunque rifiutare la richiesta. Non e una
    /// garanzia, e una preferenza.
    /// </summary>
    private static void SopprimiIlConfine()
    {
        var manager = Object.FindAnyObjectByType<OVRManager>();
        if (manager == null)
        {
            Debug.LogWarning("[Fase 2] Nessun OVRManager: confine lasciato attivo.");
            return;
        }

        var so = new SerializedObject(manager);
        var prop = so.FindProperty("shouldBoundaryVisibilityBeSuppressed");
        if (prop == null)
        {
            Debug.LogWarning("[Fase 2] Questa versione dell'SDK non espone la soppressione del confine.");
            return;
        }

        prop.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("[Fase 2] Richiesta soppressione del confine Guardian.");
    }

    /// <summary>
    /// Costruisce la freccia-bussola e il componente che la governa.
    ///
    /// La freccia e fatta di due cubi invece che di un modello 3D: per una
    /// direzione basta, e non introduce un asset da gestire.
    /// </summary>
    private static NavigazioneInventario CreaNavigazione(InventarioManager manager)
    {
        // Il componente sta su un oggetto SUO, e la freccia e un figlio.
        // Se stessero insieme, spegnere la freccia quando non c'e nulla di
        // selezionato spegnerebbe anche Update(), e nessun tasto arriverebbe piu:
        // e esattamente il bug per cui B sembrava non funzionare.
        var radice = new GameObject("Navigazione");
        var freccia = new GameObject("Freccia");
        freccia.transform.SetParent(radice.transform, false);

        var asta = GameObject.CreatePrimitive(PrimitiveType.Cube);
        asta.name = "Asta";
        asta.transform.SetParent(freccia.transform, false);
        asta.transform.localScale = new Vector3(0.022f, 0.022f, 0.16f);
        asta.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        Object.DestroyImmediate(asta.GetComponent<Collider>());

        var punta = GameObject.CreatePrimitive(PrimitiveType.Cube);
        punta.name = "Punta";
        punta.transform.SetParent(freccia.transform, false);
        punta.transform.localScale = new Vector3(0.055f, 0.055f, 0.055f);
        punta.transform.localPosition = new Vector3(0f, 0f, 0.12f);
        punta.transform.localRotation = Quaternion.Euler(0f, 45f, 45f);
        Object.DestroyImmediate(punta.GetComponent<Collider>());

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.95f, 0.62f, 0.12f) };
            AssetDatabase.CreateAsset(mat, "Assets/Scenes/FrecciaMat.mat");
            asta.GetComponent<Renderer>().sharedMaterial = mat;
            punta.GetComponent<Renderer>().sharedMaterial = mat;
        }

        var nav = radice.AddComponent<NavigazioneInventario>();
        var so = new SerializedObject(nav);
        so.FindProperty("m_inventario").objectReferenceValue = manager;
        so.FindProperty("m_freccia").objectReferenceValue = freccia.transform;
        // Materiali distinti per asta e punta: condividendone uno solo, cambiarne
        // il colore a runtime ne creerebbe una copia per un pezzo e lascerebbe
        // l'altro del colore originale — che e come la freccia diventava bicolore.
        var pezzi = so.FindProperty("m_pezziFreccia");
        pezzi.arraySize = 2;
        pezzi.GetArrayElementAtIndex(0).objectReferenceValue = asta.GetComponent<Renderer>();
        pezzi.GetArrayElementAtIndex(1).objectReferenceValue = punta.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("[Fase 2] Freccia di navigazione creata (A scorre, stick annulla, B esce).");
        return nav;
    }

    private static void CreaPannelloInventario(InventarioManager manager, NavigazioneInventario navigazione,
                                               PersistenzaInventario persistenza)
    {
        var go = new GameObject("PannelloInventario", typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(420f, 340f);
        rt.localScale = Vector3.one * 0.0009f;   // ~38 cm di larghezza reale

        // Sfondo scuro semitrasparente: il testo bianco da solo sparisce sulle
        // pareti chiare della stanza in passthrough.
        var sfondo = new GameObject("Sfondo", typeof(Image));
        sfondo.transform.SetParent(go.transform, false);
        var sfondoRt = sfondo.GetComponent<RectTransform>();
        sfondoRt.anchorMin = Vector2.zero;
        sfondoRt.anchorMax = Vector2.one;
        sfondoRt.offsetMin = Vector2.zero;
        sfondoRt.offsetMax = Vector2.zero;
        sfondo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        var testoGo = new GameObject("Testo", typeof(Text));
        testoGo.transform.SetParent(go.transform, false);
        var testoRt = testoGo.GetComponent<RectTransform>();
        testoRt.anchorMin = Vector2.zero;
        testoRt.anchorMax = Vector2.one;
        testoRt.offsetMin = new Vector2(16f, 16f);
        testoRt.offsetMax = new Vector2(-16f, -16f);

        var testo = testoGo.GetComponent<Text>();
        testo.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        testo.fontSize = 20;
        testo.lineSpacing = 1.15f;
        testo.color = Color.white;
        testo.alignment = TextAnchor.UpperLeft;
        testo.horizontalOverflow = HorizontalWrapMode.Overflow;
        testo.verticalOverflow = VerticalWrapMode.Overflow;
        testo.text = "INVENTARIO";

        // Riquadro dello scatto, in basso a destra del pannello.
        var scattoGo = new GameObject("Scatto", typeof(RawImage));
        scattoGo.transform.SetParent(go.transform, false);
        var scattoRt = scattoGo.GetComponent<RectTransform>();
        scattoRt.anchorMin = new Vector2(1f, 0f);
        scattoRt.anchorMax = new Vector2(1f, 0f);
        scattoRt.pivot = new Vector2(1f, 0f);
        scattoRt.anchoredPosition = new Vector2(-16f, 16f);
        scattoRt.sizeDelta = new Vector2(120f, 120f);
        var scattoImg = scattoGo.GetComponent<RawImage>();
        scattoImg.enabled = false;

        var pannello = go.AddComponent<PannelloInventario>();
        var so = new SerializedObject(pannello);
        so.FindProperty("m_inventario").objectReferenceValue = manager;
        so.FindProperty("m_testo").objectReferenceValue = testo;
        so.FindProperty("m_navigazione").objectReferenceValue = navigazione;
        so.FindProperty("m_persistenza").objectReferenceValue = persistenza;
        so.FindProperty("m_scatto").objectReferenceValue = scattoImg;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("[Fase 2] Pannello inventario creato.");
    }

    /// <summary>
    /// I default del sample sono pensati per mostrare che il modello funziona, non
    /// per costruirci un inventario: soglia di confidenza 0.23 (da cui "person" su
    /// una giacca appesa) e soppressione dei doppioni solo oltre il 60% di
    /// sovrapposizione (da cui quattro box sullo stesso laptop). Li stringiamo.
    /// </summary>
    private static void TaraIlDetector()
    {
        var runner = Object.FindAnyObjectByType<SentisInferenceRunManager>();
        if (runner == null)
        {
            Debug.LogWarning("[Fase 2] Non trovo SentisInferenceRunManager: soglie del detector lasciate ai default.");
            return;
        }

        var so = new SerializedObject(runner);
        so.FindProperty("m_scoreThreshold").floatValue = 0.45f;   // era 0.23
        so.FindProperty("m_iouThreshold").floatValue = 0.35f;     // era 0.60
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("[Fase 2] Detector tarato: confidenza 0.45, IoU 0.35.");
    }

    [MenuItem("Inventario AR/Crea scena Fase 2 e installa AZZERANDO l'inventario", priority = 42)]
    public static void CreaEInstallaAzzerando() => CreaEInstalla(true);

    [MenuItem("Inventario AR/Crea scena Fase 2 e installa sul Quest", priority = 41)]
    public static void CreaEInstalla() => CreaEInstalla(false);

    private static void CreaEInstalla(bool azzeraArchivio)
    {
        Crea();

        if (azzeraArchivio)
        {
            var persistenza = Object.FindAnyObjectByType<PersistenzaInventario>();
            if (persistenza != null)
            {
                var so = new SerializedObject(persistenza);
                so.FindProperty("m_azzeraArchivioAllAvvio").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenaFase2);
                Debug.Log("[Fase 2] Questa build azzerera l'inventario al primo avvio.");
            }
        }

        if (EditorBuildSettings.scenes.Length == 0 || EditorBuildSettings.scenes[0].path != ScenaFase2)
            return;   // Crea() ha gia segnalato l'errore

        System.IO.Directory.CreateDirectory("Builds");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = new[] { ScenaFase2 },
            target           = BuildTarget.Android,
            targetGroup      = BuildTargetGroup.Android,
            locationPathName = "Builds/InventarioAR.apk",
            options          = BuildOptions.AutoRunPlayer,
        });

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"[Fase 2] Build riuscita ({report.summary.totalSize / (1024 * 1024)} MB), avviata sul visore.");
        else
            Debug.LogError($"[Fase 2] Build {report.summary.result}: {report.summary.totalErrors} errori.");
    }
}
