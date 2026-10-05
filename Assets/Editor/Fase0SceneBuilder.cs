// Costruisce la scena della Fase 0: passthrough attivo + un cubo davanti all'utente.
// Menu: Inventario AR > Crea scena Fase 0
//
// Perche uno script invece di comporre la scena a mano: le impostazioni che fanno
// funzionare il passthrough in URP sono poche ma non perdonano (in particolare lo
// sfondo trasparente della camera). Metterle in codice le rende ripetibili e
// verificabili, invece che affidate alla memoria di dove si era cliccato.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class Fase0SceneBuilder
{
    private const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
    private const string ScenePath     = "Assets/Scenes/Fase0_Passthrough.unity";

    [MenuItem("Inventario AR/Crea scena Fase 0 (passthrough + cubo)")]
    public static void Build()
    {
        ConfigureProjectCapabilities();

        // Scena nuova, senza gli oggetti di default: la camera la porta il rig di Meta.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLight();
        var rig = CreateCameraRig();
        if (rig == null) return;

        ConfigurePassthrough(rig);
        MakeCamerasTransparent(rig);
        CreateReferenceCube();

        SaveAndRegisterScene();

        Debug.Log("[Fase 0] Scena creata in " + ScenePath +
                  ". Collega il Quest e usa File > Build And Run.");
    }

    // Rigenera la scena E fa subito il build sul visore, in un solo comando.
    // Serve a chiudere una trappola: se si builda senza aver rigenerato la scena,
    // l'APK reimpacchetta la versione vecchia e le correzioni sembrano non aver
    // avuto effetto.
    [MenuItem("Inventario AR/Crea scena Fase 0 e installa sul Quest", priority = 20)]
    public static void BuildAndRun()
    {
        Build();

        const string apk = "Builds/InventarioAR.apk";
        System.IO.Directory.CreateDirectory("Builds");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            target           = BuildTarget.Android,
            targetGroup      = BuildTargetGroup.Android,
            locationPathName = apk,
            options          = BuildOptions.AutoRunPlayer,
        });

        var esito = report.summary.result;
        if (esito == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"[Fase 0] Build riuscita ({report.summary.totalSize / (1024 * 1024)} MB), avviata sul visore.");
        else
            Debug.LogError($"[Fase 0] Build {esito}: {report.summary.totalErrors} errori. Dettagli sopra nel log.");
    }

    // --- Capacita dichiarate nel manifest dell'app -------------------------------
    // Senza queste il visore non concede il passthrough a runtime, e l'app mostra
    // nero al posto della stanza senza alcun errore in console.
    private static void ConfigureProjectCapabilities()
    {
        var cfg = OVRProjectConfig.CachedProjectConfig;
        cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
        cfg.sceneSupport              = OVRProjectConfig.FeatureSupport.Supported;
        cfg.anchorSupport             = OVRProjectConfig.AnchorSupport.Enabled;
        OVRProjectConfig.CommitProjectConfig(cfg);
    }

    private static void CreateLight()
    {
        var go = new GameObject("Directional Light");
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static GameObject CreateCameraRig()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[Fase 0] OVRCameraRig non trovato in " + RigPrefabPath +
                           ". Il pacchetto com.meta.xr.sdk.core non risulta installato.");
            return null;
        }

        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.name = "OVRCameraRig";
        rig.transform.position = Vector3.zero;
        return rig;
    }

    private static void ConfigurePassthrough(GameObject rig)
    {
        var manager = rig.GetComponent<OVRManager>();
        if (manager == null) manager = rig.AddComponent<OVRManager>();
        manager.isInsightPassthroughEnabled = true;

        // Con EyeLevel l'origine (y = 0) sta all'altezza degli occhi, quindi un
        // oggetto a y = 1.2 finisce oltre un metro SOPRA la testa: sul soffitto.
        // FloorLevel mette y = 0 sul pavimento, cosi le altezze sono quelle reali.
        manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;

        // Niente confine del Guardian: l'app si usa camminando per casa, e con il
        // passthrough attivo l'ambiente reale si vede gia. Il sistema puo rifiutare.
        manager.shouldBoundaryVisibilityBeSuppressed = true;

        var layer = rig.GetComponent<OVRPassthroughLayer>();
        if (layer == null) layer = rig.AddComponent<OVRPassthroughLayer>();

        // Nota: overlayType e projectionSurfaceType sono deprecati in MRUK 85.
        // I loro default (Underlay + Reconstructed) sono gia quelli giusti — il
        // passthrough sta dietro alla grafica e il cubo si vede sopra la stanza —
        // e da qui in avanti Meta rendera un unico layer di background. Non li
        // impostiamo piu.
        layer.textureOpacity = 1f;
    }

    // Il punto che fa sbagliare tutti: se la camera cancella lo sfondo con un colore
    // opaco, copre il layer di passthrough e si vede nero. Serve alpha = 0.
    private static void MakeCamerasTransparent(GameObject rig)
    {
        foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            var extra = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (extra != null)
            {
                extra.renderPostProcessing = false;
                extra.renderShadows = false;
            }
        }
    }

    private static void CreateReferenceCube()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "CuboDiProva";
        cube.transform.localScale = Vector3.one * 0.2f;

        // La posizione viene calcolata a runtime rispetto alla testa, non fissata
        // qui: cosi non dipende da come e configurata l'origine del tracking.
        cube.AddComponent<PosizionaDavantiAllUtente>();

        // Materiale URP semplice, cosi il cubo si legge bene sopra la stanza reale.
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.70f, 0.17f, 0.37f) };
            AssetDatabase.CreateAsset(mat, "Assets/Scenes/CuboDiProva.mat");
            cube.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    private static void SaveAndRegisterScene()
    {
        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
