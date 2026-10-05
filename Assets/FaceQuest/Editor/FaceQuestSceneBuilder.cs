// Costruisce la scena di FaceQuest e la installa sul visore.
//
// Perche da codice: collegare a mano venticinque riferimenti nell'Inspector e
// un'operazione che non si riesce a rifare identica, e un riferimento perso si
// manifesta a runtime come "non funziona niente" senza dire quale. Qui la scena
// si rigenera da zero con un comando, e il comando e la documentazione di come
// e fatta.
//
// Si parte dalla scena del sample di Meta perche contiene il camera rig, il
// passthrough e il componente PassthroughCameraAccess gia configurati. Tutto il
// resto del sample — rilevamento oggetti e la sua interfaccia, banner "AI model:
// Yolo" compreso — viene spento spegnendo l'intera istanza del suo prefab.
//
// FaceQuest si installa come APPLICAZIONE SEPARATA dall'inventario: nome e
// identificativo del pacchetto vengono cambiati solo per la durata della build e
// poi rimessi come erano, cosi le due app convivono sul visore invece di
// sovrascriversi.

using System.Collections.Generic;
using System.Linq;
using FaceQuest;
using PassthroughCameraSamples.MultiObjectDetection;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FaceQuestSceneBuilder
{
    private const string ScenaSample = "Assets/PassthroughCameraApiSamples/MultiObjectDetection/MultiObjectDetection.unity";
    private const string ScenaFaceQuest = "Assets/Scenes/FaceQuest.unity";
    private const string ModelloDetector = "Assets/FaceQuest/Resources/scrfd_500m_384x288.onnx";
    private const string ModelloEmbedder = "Assets/FaceQuest/Resources/arcface_mbf_112.onnx";
    private const string PercorsoShader = "Assets/FaceQuest/Shaders/FaceAffineBlit.shader";

    private const string NomeApp = "FaceQuest";
    private const string IdApp = "it.cnr.isti.facequest";
    private const string PercorsoApk = "Builds/FaceQuest.apk";

    [MenuItem("FaceQuest/1 · Crea la scena", priority = 10)]
    public static void CreaDaMenu() => Crea();

    /// <summary>Restituisce false se manca qualcosa: i comandi di build si fermano.</summary>
    public static bool Crea()
    {
        if (!System.IO.File.Exists(ScenaSample))
        {
            Debug.LogError($"[FaceQuest] Non trovo {ScenaSample}: il sample della Passthrough Camera API non e nel progetto.");
            return false;
        }

        var detector = CaricaModello(ModelloDetector);
        var embedder = CaricaModello(ModelloEmbedder);
        if (detector == null || embedder == null)
        {
            Debug.LogError($"[FaceQuest] Modelli ONNX non importati. Attesi:\n  {ModelloDetector}\n  {ModelloEmbedder}");
            return false;
        }

        var shaderAffine = AssetDatabase.LoadAssetAtPath<Shader>(PercorsoShader);
        if (shaderAffine == null)
        {
            Debug.LogError($"[FaceQuest] Shader non trovato in {PercorsoShader}.");
            return false;
        }

        // Shader.Find nell'editor restituisce l'asset vero: assegnarlo a un campo
        // serializzato e quello che ne garantisce la presenza nella build.
        var shaderLinee = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (shaderLinee == null)
            Debug.LogWarning("[FaceQuest] Nessuno shader unlit trovato: box e puntatore potrebbero non vedersi.");

        IncludiShaderNellaBuild(shaderAffine, shaderLinee);

        // I modelli sono stati spostati dentro Resources muovendo i file su
        // disco, con l'editor aperto. Un database degli asset rimasto indietro
        // rispetto al disco e proprio il tipo di disallineamento che produce una
        // build rotta, quindi prima di ogni cosa lo si riallinea.
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        EditorSceneManager.OpenScene(ScenaSample, OpenSceneMode.Single);
        SpegniIlSample();
        SopprimiIlConfine();

        foreach (var vecchio in Object.FindObjectsByType<FaceQuestManager>(FindObjectsSortMode.None).ToList())
            Object.DestroyImmediate(vecchio.transform.root.gameObject);

        var camera = Object.FindAnyObjectByType<Meta.XR.PassthroughCameraAccess>();
        if (camera == null)
        {
            Debug.LogError("[FaceQuest] PassthroughCameraAccess non trovato nella scena del sample.");
            return false;
        }
        if (!camera.enabled || !camera.gameObject.activeInHierarchy)
        {
            // Senza questo la pipeline resta in attesa per sempre e la riga di
            // diagnostica mostra "cam in attesa".
            camera.enabled = true;
            camera.gameObject.SetActive(true);
            Debug.LogWarning("[FaceQuest] PassthroughCameraAccess era spento: riacceso.");
        }

        var radice = new GameObject("FaceQuest");

        // --- pipeline: tutti i componenti di calcolo su un oggetto solo ---
        var pipeline = new GameObject("Pipeline");
        pipeline.transform.SetParent(radice.transform, false);

        var faceDetector = pipeline.AddComponent<FaceDetector>();
        var faceEmbedder = pipeline.AddComponent<FaceEmbeddingExtractor>();
        var qualita = pipeline.AddComponent<FaceQualityEstimator>();
        var tracker = pipeline.AddComponent<FaceTracker>();
        var matcher = pipeline.AddComponent<IdentityMatcher>();
        var database = pipeline.AddComponent<PersonDatabase>();
        var persistenza = pipeline.AddComponent<PersonPersistenceManager>();
        var manager = pipeline.AddComponent<FaceQuestManager>();

        // --- presentazione ---
        var boxGo = new GameObject("BoxVolti");
        boxGo.transform.SetParent(radice.transform, false);
        var box = boxGo.AddComponent<BoundingBoxRenderer>();

        var puntatoreGo = new GameObject("Puntatore");
        puntatoreGo.transform.SetParent(radice.transform, false);
        var puntatore = puntatoreGo.AddComponent<PuntatoreAR>();

        var pannelloGo = new GameObject("PannelloPeople");
        pannelloGo.transform.SetParent(radice.transform, false);
        var seguiPannello = pannelloGo.AddComponent<PannelloAfferrabile>();
        var pannello = pannelloGo.AddComponent<PeoplePanelController>();

        var editorGo = new GameObject("FinestraNome");
        editorGo.transform.SetParent(radice.transform, false);
        var seguiEditor = editorGo.AddComponent<SeguiTesta>();
        var editor = editorGo.AddComponent<PersonEditorUI>();

        // Il pannello nasce a SINISTRA (m_lato negativo, ora che il segno del
        // vettore destro e corretto) e poi resta fermo li dove lo si lascia:
        // questi tre valori sono solo la posa di partenza e quella a cui la
        // pressione dello stick lo riporta.
        Scrivi(seguiPannello, so =>
        {
            so.FindProperty("m_distanza").floatValue = 0.78f;
            so.FindProperty("m_lato").floatValue = -0.40f;
            so.FindProperty("m_altezza").floatValue = -0.02f;
            so.FindProperty("m_puntatore").objectReferenceValue = puntatore;
        });

        // La finestra del nome sta davanti, non di lato: si apre su richiesta e
        // in quel momento e l'unica cosa che interessa.
        Scrivi(seguiEditor, so =>
        {
            so.FindProperty("m_distanza").floatValue = 0.68f;
            so.FindProperty("m_lato").floatValue = 0.02f;
            so.FindProperty("m_altezza").floatValue = -0.04f;
        });

        Scrivi(faceDetector, so =>
        {
            so.FindProperty("m_backend").intValue = (int)BackendType.CPU;
            so.FindProperty("m_soglia").floatValue = 0.45f;
            so.FindProperty("m_iou").floatValue = 0.4f;
            so.FindProperty("m_altezzaMinima").floatValue = 0.04f;
        });

        Scrivi(faceEmbedder, so => so.FindProperty("m_backend").intValue = (int)BackendType.CPU);

        // I modelli si assegnano a parte, direttamente sul campo e non via
        // SerializedObject, e poi si VERIFICA che il riferimento sia davvero
        // finito nell'oggetto. La prima versione passava da SerializedObject e il
        // riferimento restava a zero senza un solo messaggio: nella scena si
        // leggeva "m_modello: {fileID: 0}" mentre soglie e backend accanto erano
        // scritti correttamente.
        faceDetector.ImpostaModello(detector);
        faceEmbedder.ImpostaModello(embedder);
        EditorUtility.SetDirty(faceDetector);
        EditorUtility.SetDirty(faceEmbedder);
        VerificaModello(faceDetector, "m_modello", detector, "detector");
        VerificaModello(faceEmbedder, "m_modello", embedder, "embedder");

        Scrivi(persistenza, so =>
        {
            so.FindProperty("m_database").objectReferenceValue = database;

            // Persistenza attiva: le persone a cui l'utente ha dato un nome
            // sopravvivono alla chiusura, cifrate; le anonime no.
            // Spuntando 'Solo in memoria' si torna alla modalita demo in cui
            // su disco non si scrive nulla.
            so.FindProperty("m_soloInMemoria").boolValue = false;
            so.FindProperty("m_azzeraArchivioAllAvvio").boolValue = false;
        });

        Scrivi(box, so =>
        {
            so.FindProperty("m_camera").objectReferenceValue = camera;
            so.FindProperty("m_shaderLinee").objectReferenceValue = shaderLinee;
        });

        Scrivi(puntatore, so => so.FindProperty("m_shaderRaggio").objectReferenceValue = shaderLinee);

        Scrivi(pannello, so =>
        {
            so.FindProperty("m_database").objectReferenceValue = database;
            so.FindProperty("m_editor").objectReferenceValue = editor;
            so.FindProperty("m_manager").objectReferenceValue = manager;
            so.FindProperty("m_righeVisibili").intValue = 4;
            so.FindProperty("m_anteprimePipeline").boolValue = true;
        });

        Scrivi(editor, so =>
        {
            so.FindProperty("m_database").objectReferenceValue = database;
            so.FindProperty("m_manager").objectReferenceValue = manager;
            so.FindProperty("m_persistenza").objectReferenceValue = persistenza;
        });

        Scrivi(manager, so =>
        {
            so.FindProperty("m_camera").objectReferenceValue = camera;
            so.FindProperty("m_detector").objectReferenceValue = faceDetector;
            so.FindProperty("m_embedder").objectReferenceValue = faceEmbedder;
            so.FindProperty("m_qualita").objectReferenceValue = qualita;
            so.FindProperty("m_tracker").objectReferenceValue = tracker;
            so.FindProperty("m_matcher").objectReferenceValue = matcher;
            so.FindProperty("m_database").objectReferenceValue = database;
            so.FindProperty("m_persistenza").objectReferenceValue = persistenza;
            so.FindProperty("m_box").objectReferenceValue = box;
            so.FindProperty("m_pannello").objectReferenceValue = pannello;
            so.FindProperty("m_editor").objectReferenceValue = editor;
            so.FindProperty("m_confermePerCreare").intValue = 3;
            so.FindProperty("m_coerenzaCandidati").floatValue = 0.45f;
            so.FindProperty("m_qualitaPerCreare").floatValue = 0.40f;
            so.FindProperty("m_punteggioPerCreare").floatValue = 0.55f;
            so.FindProperty("m_sogliaFusione").floatValue = 0.55f;
            so.FindProperty("m_silenzioFantasmi").floatValue = 25f;
            so.FindProperty("m_osservazioniFantasma").intValue = 2;
            so.FindProperty("m_embeddingPerCiclo").intValue = 2;
            so.FindProperty("m_riconoscimentoAttivo").boolValue = true;
            so.FindProperty("m_diagnosticaVisibile").boolValue = true;
            so.FindProperty("m_tensoreDallAlto").boolValue = true;
            so.FindProperty("m_shaderAffine").objectReferenceValue = shaderAffine;
            so.FindProperty("m_soloDetector").boolValue = false;
        });

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenaFaceQuest);

        if (!VerificaScriptDellaScena(ScenaFaceQuest)) return false;

        Debug.Log($"[FaceQuest] Scena creata in {ScenaFaceQuest} con tutti i riferimenti collegati.");
        return true;
    }

    /// <summary>
    /// Controlla che ogni componente della scena punti a un vero asset di script.
    ///
    /// Nasce da un guasto che e costato giorni. Tre MonoBehaviour stavano in un
    /// unico file (PuntatoreAR.cs): C# lo permette, l'editor pure. Ma Unity crea
    /// un asset MonoScript solo per la classe che porta il nome del file; per le
    /// altre scrive nella scena un MonoScript finto e senza nome, e un
    /// riferimento di script SENZA GUID. L'editor lo risolve, il player no.
    ///
    /// Il sintomo era irriconoscibile: l'app si chiudeva accusando la scena di
    /// essere corrotta —
    ///     "The file '.../assets/bin/Data/level0' is corrupted!"
    ///     "[Position out of bounds!]"
    /// — mentre ogni byte del file costruito era in ordine, verificato uno per uno.
    ///
    /// Due sintomi, un controllo ciascuno: un riferimento di script senza guid, e
    /// un MonoScript (classe 115) dentro un file di scena, dove non ha motivo di
    /// esistere. Se ricapita, ora lo si sa prima della build e non dopo.
    /// </summary>
    private static bool VerificaScriptDellaScena(string percorso)
    {
        var righe = System.IO.File.ReadAllLines(percorso);
        var senzaGuid = 0;
        var monoScriptInterni = 0;

        foreach (var riga in righe)
        {
            if (riga.StartsWith("--- !u!115 &")) monoScriptInterni++;
            if (!riga.Contains("m_Script: {fileID:")) continue;
            if (riga.Contains("guid:")) continue;
            if (riga.Contains("fileID: 0}")) continue;   // script assente: se ne accorge l'editor
            senzaGuid++;
        }

        if (senzaGuid == 0 && monoScriptInterni == 0) return true;

        Debug.LogError(
            $"[FaceQuest] La scena e inservibile per una build: {senzaGuid} componenti con un " +
            $"riferimento di script senza guid e {monoScriptInterni} MonoScript scritti dentro la scena.\n" +
            "Causa quasi certa: un MonoBehaviour dichiarato in un file che non porta il suo nome. " +
            "Sposta quella classe in un file chiamato come lei e rilancia questo comando.\n" +
            "Se si costruisse comunque, sul visore si vedrebbe soltanto " +
            "\"level0 is corrupted / Position out of bounds\", che non dice nulla di tutto questo.");
        return false;
    }

    /// <summary>
    /// Spegne il rilevamento oggetti del sample e tutta la sua interfaccia.
    ///
    /// Si spegne l'intera istanza del prefab, non i singoli pannelli: quel prefab
    /// contiene anche titoli, footer e il banner "AI model: Yolo", che restavano
    /// in aria perche non sono fra i tre pannelli che il menu del sample espone.
    /// Un componente disabilitato, inoltre, non riceve Awake: cosi il modello
    /// YOLO non viene nemmeno caricato in memoria.
    /// </summary>
    private static void SpegniIlSample()
    {
        SpegniIstanzaPrefab(Object.FindAnyObjectByType<DetectionUiMenuManager>(), "interfaccia del sample");
        SpegniIstanzaPrefab(Object.FindAnyObjectByType<SentisInferenceUiManager>(), "UI del rilevamento oggetti");

        var runner = Object.FindAnyObjectByType<SentisInferenceRunManager>();
        if (runner != null) runner.enabled = false;

        var dm = Object.FindAnyObjectByType<DetectionManager>();
        if (dm != null) dm.enabled = false;

        Debug.Log("[FaceQuest] Rilevamento oggetti e interfaccia del sample disattivati.");
    }

    private static void SpegniIstanzaPrefab(Behaviour componente, string cosa)
    {
        if (componente == null) return;

        var radice = PrefabUtility.GetOutermostPrefabInstanceRoot(componente.gameObject);
        var bersaglio = radice != null ? radice : componente.gameObject;

        // Mai spegnere un ramo che contiene l'accesso alla camera: senza quello
        // non c'e nessuna immagine da elaborare.
        if (bersaglio.GetComponentInChildren<Meta.XR.PassthroughCameraAccess>(true) != null)
        {
            componente.enabled = false;
            Debug.LogWarning($"[FaceQuest] {cosa}: l'istanza del prefab contiene la camera, " +
                             "ho spento solo il componente. Se resta della UI del sample in aria, segnalalo.");
            return;
        }

        bersaglio.SetActive(false);
    }

    /// <summary>
    /// Chiede di nascondere il confine del Guardian: l'app si usa stando in
    /// piedi fra le persone, e il confine interromperebbe l'esperienza a ogni
    /// passo. E una richiesta, non una garanzia: il sistema puo rifiutarla.
    /// </summary>
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
    /// Mette gli shader usati a runtime fra gli "Always Included Shaders".
    ///
    /// Ora sono anche assegnati a campi serializzati, che e la garanzia vera;
    /// questo resta come seconda cintura, perche costa nulla e copre il caso di
    /// una scena ricostruita a mano.
    /// </summary>
    private static void IncludiShaderNellaBuild(params Shader[] shader)
    {
        var daIncludere = shader.Where(s => s != null).ToList();
        if (daIncludere.Count == 0) return;

        var impostazioni = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")
            .FirstOrDefault(a => a != null && a.GetType().Name == "GraphicsSettings");
        if (impostazioni == null)
        {
            Debug.LogWarning("[FaceQuest] GraphicsSettings non leggibile: aggiungi a mano gli shader " +
                             "in Project Settings > Graphics > Always Included Shaders.");
            return;
        }

        var so = new SerializedObject(impostazioni);
        var lista = so.FindProperty("m_AlwaysIncludedShaders");

        var aggiunti = 0;
        foreach (var s in daIncludere)
        {
            var presente = false;
            for (var i = 0; i < lista.arraySize; i++)
            {
                if (lista.GetArrayElementAtIndex(i).objectReferenceValue != s) continue;
                presente = true;
                break;
            }
            if (presente) continue;

            lista.InsertArrayElementAtIndex(lista.arraySize);
            lista.GetArrayElementAtIndex(lista.arraySize - 1).objectReferenceValue = s;
            aggiunti++;
        }

        if (aggiunti == 0) return;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log($"[FaceQuest] {aggiunti} shader aggiunti agli Always Included Shaders.");
    }

    /// <summary>
    /// Carica il ModelAsset da un .onnx importato.
    ///
    /// LoadAllAssetsAtPath e non LoadAssetAtPath: nel file importato il
    /// ModelAsset non e necessariamente l'asset principale (nel sample di Meta
    /// ha un fileID interno, non quello dell'asset principale), e la versione
    /// tipizzata di LoadAssetAtPath in quel caso restituisce null.
    /// </summary>
    private static ModelAsset CaricaModello(string percorso)
    {
        var diretto = AssetDatabase.LoadAssetAtPath<ModelAsset>(percorso);
        if (diretto != null) return diretto;

        var tutti = AssetDatabase.LoadAllAssetsAtPath(percorso);
        var trovato = tutti.OfType<ModelAsset>().FirstOrDefault();
        if (trovato != null)
        {
            Debug.Log($"[FaceQuest] {System.IO.Path.GetFileName(percorso)}: ModelAsset trovato fra i sotto-asset.");
            return trovato;
        }

        if (tutti.Length > 0)
            Debug.LogError($"[FaceQuest] {percorso} e importato ma non contiene un ModelAsset. " +
                           $"Oggetti trovati: {string.Join(", ", tutti.Where(a => a != null).Select(a => a.GetType().Name))}");
        return null;
    }

    /// <summary>
    /// Controlla che il riferimento sia sopravvissuto alla serializzazione.
    /// Un'assegnazione che fallisce in silenzio costa un ciclo di build e mezz'ora
    /// di ricerca; questo controllo costa tre righe.
    /// </summary>
    private static void VerificaModello(Object componente, string campo, Object atteso, string nome)
    {
        var so = new SerializedObject(componente);
        var prop = so.FindProperty(campo);
        if (prop != null && prop.objectReferenceValue == atteso) return;

        Debug.LogWarning($"[FaceQuest] Il riferimento al modello {nome} non si e serializzato. " +
                         "Non e bloccante: i modelli stanno in Assets/FaceQuest/Resources e vengono " +
                         "caricati per nome a runtime.");
    }

    private static void Scrivi(Object componente, System.Action<SerializedObject> azione)
    {
        var so = new SerializedObject(componente);
        azione(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------

    [MenuItem("FaceQuest/2 · Crea la scena e installa sul Quest", priority = 20)]
    public static void CreaEInstalla() => Builda(false);

    [MenuItem("FaceQuest/3 · Crea la scena, AZZERA l'archivio e installa", priority = 30)]
    public static void CreaAzzerandoEInstalla() => Builda(true);

    /// <summary>
    /// Prova di isolamento: costruisce la scena del sample COSI' COM'E', senza
    /// nessun oggetto di FaceQuest, con l'identita dell'app FaceQuest.
    ///
    /// Serve a dividere in due un problema che l'analisi statica non risolve. Il
    /// visore si chiude con "level0 is corrupted / Position out of bounds", ma
    /// ogni file della build risulta integro byte per byte: level0 si legge
    /// fino all'ultimo byte dei metadati, sharedassets0 e coerente, nessun
    /// oggetto esce dai limiti. Quindi non e il file a essere rotto: e il player
    /// che non riesce a rileggere un componente.
    ///
    ///   se questa prova PARTE      -> la causa e in uno dei miei componenti
    ///   se questa prova SI CHIUDE  -> la causa e nel progetto o nella build,
    ///                                 e i miei componenti sono innocenti
    ///
    /// Una build, un bit di informazione, zero congetture.
    /// </summary>
    [MenuItem("FaceQuest/4 · PROVA: installa la sola scena del sample", priority = 40)]
    public static void ProvaSoloSample()
    {
        if (!System.IO.File.Exists(ScenaSample))
        {
            Debug.LogError($"[FaceQuest] Non trovo {ScenaSample}.");
            return;
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        Debug.Log("[FaceQuest] PROVA DI ISOLAMENTO: build della scena del sample senza oggetti FaceQuest.");
        Installa(ScenaSample, "prova-sample");
    }

    private static void Builda(bool azzeraArchivio)
    {
        if (!Crea()) return;

        if (azzeraArchivio)
        {
            var persistenza = Object.FindAnyObjectByType<PersonPersistenceManager>();
            if (persistenza != null)
            {
                Scrivi(persistenza, so => so.FindProperty("m_azzeraArchivioAllAvvio").boolValue = true);
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenaFaceQuest);
                Debug.Log("[FaceQuest] Questa build cancellera l'archivio cifrato al primo avvio.");
            }
        }

        Installa(ScenaFaceQuest, azzeraArchivio ? "archivio azzerato" : "normale");
    }

    /// <summary>Build e installazione di una scena qualunque con l'identita di FaceQuest.</summary>
    private static void Installa(string scena, string etichetta)
    {
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scena, true) };
        System.IO.Directory.CreateDirectory("Builds");

        // Identita dell'app cambiata solo per questa build. Con lo stesso
        // identificativo dell'inventario, installare FaceQuest lo SOSTITUIVA sul
        // visore: stesso pacchetto, stessa icona, stessi dati.
        var nomePrec = PlayerSettings.productName;
        var idPrec = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);

        UnityEditor.Build.Reporting.BuildReport report;
        try
        {
            PlayerSettings.productName = NomeApp;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, IdApp);
            Debug.Log($"[FaceQuest] Build PULITA come app separata: '{NomeApp}' ({IdApp}), " +
                      $"scena '{scena}' ({etichetta}). Ci vorra qualche minuto in piu del solito.");

            // CleanBuildCache: la build ricostruisce i dati del player da zero
            // invece di riusare la cache incrementale.
            //
            // Non e prudenza generica. Il visore si chiudeva con
            //   "The file '.../assets/bin/Data/level0' is corrupted!"
            //   "[Position out of bounds!]"  sul thread Loading.Preload
            // cioe la scena COSTRUITA era illeggibile, mentre quella sorgente su
            // disco era integra: il file rotto lo produceva la cache, non il
            // progetto. Costa qualche minuto in piu per build ed e il prezzo
            // giusto finche non siamo stabili.
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scena },
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                locationPathName = PercorsoApk,
                options = BuildOptions.AutoRunPlayer | BuildOptions.CleanBuildCache
            });
        }
        finally
        {
            PlayerSettings.productName = nomePrec;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, idPrec);
            AssetDatabase.SaveAssets();
        }

        if (report != null && report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"[FaceQuest] Build riuscita ({report.summary.totalSize / (1024 * 1024)} MB) in {PercorsoApk}, avviata sul visore.");
        else
            Debug.LogError($"[FaceQuest] Build {(report != null ? report.summary.result.ToString() : "interrotta")}: " +
                           $"{(report != null ? report.summary.totalErrors : 0)} errori.");
    }
}
