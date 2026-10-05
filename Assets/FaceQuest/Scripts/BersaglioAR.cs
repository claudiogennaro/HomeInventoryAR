// NOTA SUL PERCHE' QUESTE CLASSI STANNO IN FILE SEPARATI
//
// Stavano tutte dentro PuntatoreAR.cs. Sembrava innocuo: C# lo permette e
// nell'editor funzionava. Nella build su Android, no.
//
// Unity crea un asset MonoScript solo per la classe che porta il nome del file.
// Per le altre, un componente messo in scena finisce con un riferimento senza
// guid, e l'editor ci scrive dentro la scena un MonoScript finto e senza nome
// (--- !u!115 &920716856, visto con i miei occhi nel file). L'editor lo
// risolve; il player no, e leggendo quel componente il puntatore va oltre i dati.
//
// Il sintomo non somigliava affatto alla causa:
//   "The file '.../assets/bin/Data/level0' is corrupted!"
//   "[Position out of bounds!]"
// cioe l'app si chiudeva accusando la scena di essere corrotta, mentre ogni
// byte del file era in ordine. Un componente su un GameObject che segue la
// testa ha fatto sembrare rotta l'intera build.
//
// Regola, senza eccezioni: un MonoBehaviour, un file con il suo nome.

using System;
using UnityEngine;

namespace FaceQuest
{
    /// <summary>Qualunque cosa si possa "premere" in aria.</summary>
    public class BersaglioAR : MonoBehaviour
    {
        public Action Azione;
        public Action<bool> Evidenzia;

        public void Premi() => Azione?.Invoke();
    }
}
