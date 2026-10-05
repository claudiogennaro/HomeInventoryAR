// Tipi condivisi della pipeline di riconoscimento volti.
//
// Convenzione sulle coordinate, valida in tutto FaceQuest e da non perdere di
// vista: i rilevamenti sono NORMALIZZATI (0..1) con l'origine in ALTO a
// sinistra, come vuole il tensore del detector. Il viewport della camera
// passthrough ha invece l'origine in BASSO a sinistra. Ogni volta che si passa
// da uno all'altro si usa (1 - y): e l'unico punto in cui i due mondi si
// toccano, ed e la sorgente classica di box capovolti.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceQuest
{
    /// <summary>Un volto rilevato in un singolo fotogramma.</summary>
    public struct VoltoRilevato
    {
        /// <summary>Box normalizzato, origine in alto a sinistra.</summary>
        public Rect Box;

        public float Punteggio;

        /// <summary>
        /// I cinque punti di ArcFace, normalizzati e con origine in alto a
        /// sinistra: occhio sinistro, occhio destro, naso, angolo sinistro
        /// della bocca, angolo destro della bocca. "Sinistro" e dal punto di
        /// vista di chi guarda l'immagine.
        /// </summary>
        public Vector2 L0, L1, L2, L3, L4;

        public Vector2 Landmark(int i) => i switch
        {
            0 => L0, 1 => L1, 2 => L2, 3 => L3, _ => L4
        };

        public void ScriviLandmark(int i, Vector2 v)
        {
            switch (i)
            {
                case 0: L0 = v; break;
                case 1: L1 = v; break;
                case 2: L2 = v; break;
                case 3: L3 = v; break;
                default: L4 = v; break;
            }
        }
    }

    /// <summary>Come si e conclusa la ricerca di un volto nel database.</summary>
    public enum EsitoIdentita
    {
        /// <summary>Nessun confronto ancora fatto per questa traccia.</summary>
        DaFare,
        /// <summary>Somiglianza sopra la soglia alta: identita certa.</summary>
        Riconosciuta,
        /// <summary>Zona grigia: si mostra "Unknown" e si raccolgono altri frame.</summary>
        Incerta,
        /// <summary>Somiglianza sotto la soglia bassa: identita nuova.</summary>
        Nuova
    }

    /// <summary>Giudizio di qualita su un volto, deciso prima di toccare il template.</summary>
    public struct QualitaVolto
    {
        public float Punteggio;      // 0..1 complessivo
        public float LatoPixel;      // dimensione del volto nel frame
        public float Nitidezza;      // varianza del laplaciano normalizzata
        public float Luminosita;     // 0..1, meglio vicino a 0.5
        public float Yaw, Pitch, Roll; // gradi, stimati dai landmark
        public bool Accettabile;
        public string Motivo;        // perche e stato scartato, per la diagnostica
    }

    /// <summary>
    /// Una persona nel database. Tutto cio che sta qui e biometrico o deriva da
    /// dati biometrici: non lascia mai il visore.
    /// </summary>
    [Serializable]
    public class Persona
    {
        public string PersonID;                 // AA001, AA002, ...
        public string UserAssignedName;         // vuoto finche l'utente non lo assegna
        public string RepresentativeFaceFile;   // nome del PNG in persistentDataPath
        public float[] FaceEmbedding;           // template aggregato, L2-normalizzato
        public List<float[]> EmbeddingHistory = new List<float[]>();
        public List<float> QualityHistory = new List<float>();
        public long FirstSeenTicks;
        public long LastSeenTicks;
        public int ObservationCount;
        public bool Persistent;                 // true solo se l'utente ha dato un nome

        /// <summary>
        /// Numero progressivo mostrato finche non c'e un nome vero: 1, 2, 3...
        ///
        /// E separato da PersonID di proposito. PersonID resta AA001, AA002...
        /// perche e la chiave interna: nome del file del ritaglio su disco,
        /// aggancio fra traccia e identita, riga di log. Cambiarne il formato
        /// vorrebbe dire toccare tutto cio. Quello che serviva cambiare e solo
        /// cio che l'utente legge sotto la box, e quello e DisplayName.
        /// </summary>
        public int NumeroOspite;

        [NonSerialized] public Texture2D Anteprima;

        /// <summary>Qualita del frame da cui viene l'anteprima: si sostituisce solo con uno migliore.</summary>
        [NonSerialized] public float QualitaAnteprima;

        /// <summary>
        /// Cio che si scrive sotto la box e nel pannello.
        ///
        /// Senza nome assegnato si legge "Ospite 3": dice esplicitamente che la
        /// persona non e stata battezzata, si pronuncia (durante una prova devi
        /// poter dire a voce quale box intendi) e non si confonde con un nome
        /// vero, cosa che conta perche la differenza fra chi hai battezzato e
        /// chi no e la differenza fra cio che sopravvive alla chiusura e cio che
        /// sparisce.
        /// </summary>
        public string DisplayName =>
            !string.IsNullOrEmpty(UserAssignedName) ? UserAssignedName
            : NumeroOspite > 0 ? $"Ospite {NumeroOspite}"
            : PersonID;

        public DateTime PrimaVista => new DateTime(FirstSeenTicks);
        public DateTime UltimaVista => new DateTime(LastSeenTicks);
    }
}
