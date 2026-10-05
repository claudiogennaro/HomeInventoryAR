// Esporta l'elenco delle frasi da pronunciare, per generarne le clip audio.
//
// La sorgente unica e il dizionario in EtichetteItaliane: esportare da codice
// invece di mantenere una lista a mano evita che il dizionario e i file su disco
// divergano, che e il modo tipico in cui un'app a clip pre-registrate si rompe
// in silenzio — una classe tradotta ma senza clip resta semplicemente muta.

using System.IO;
using System.Text;
using Accessibilita;
using UnityEditor;
using UnityEngine;

public static class EsportaFrasi
{
    private const string Percorso = "Tools/frasi.txt";

    [MenuItem("Guida Vocale/Esporta le frasi da generare", priority = 30)]
    public static void Esporta()
    {
        var sb = new StringBuilder();
        var quante = 0;

        foreach (var frase in EtichetteItaliane.TutteLeFrasi())
        {
            sb.Append(frase.Key).Append('|').Append(frase.Value).Append('\n');
            quante++;
        }

        Directory.CreateDirectory("Tools");
        File.WriteAllText(Percorso, sb.ToString(), new UTF8Encoding(false));

        Debug.Log($"[Frasi] Esportate {quante} frasi in {Percorso}. " +
                  "Ora esegui Tools/GeneraVoci.ps1 in PowerShell per creare le clip.");
    }

    [MenuItem("Guida Vocale/Controlla le clip mancanti", priority = 31)]
    public static void Controlla()
    {
        var mancanti = 0;
        var presenti = 0;

        foreach (var frase in EtichetteItaliane.TutteLeFrasi())
        {
            if (File.Exists($"Assets/Resources/Voci/{frase.Key}.wav")) presenti++;
            else
            {
                mancanti++;
                Debug.LogWarning($"[Frasi] Manca la clip: {frase.Key}.wav  (\"{frase.Value}\")");
            }
        }

        Debug.Log($"[Frasi] Clip presenti: {presenti}, mancanti: {mancanti}.");
    }
}
