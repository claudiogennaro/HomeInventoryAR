using System;
using UnityEngine;

namespace InventarioAR
{
    /// <summary>
    /// Un oggetto che il sistema crede esista davvero nella stanza, ottenuto
    /// fondendo molti rilevamenti istantanei dello stesso oggetto fisico.
    /// </summary>
    [Serializable]
    public class OggettoInventario
    {
        public string Id;
        public string Classe;
        public int ClasseId;

        /// <summary>Media mobile delle posizioni osservate: piu stabile di un singolo frame.</summary>
        public Vector3 Posizione;

        /// <summary>
        /// Meta-dimensione dell'oggetto in metri, dal bounding box. Serve a rendere
        /// la soglia di fusione proporzionata: due stime su un tavolo da pranzo
        /// possono distare un metro ed essere lo stesso tavolo, due su un mouse no.
        /// </summary>
        public float Raggio;

        public int Osservazioni;
        public float PrimaVista;
        public float UltimaVista;

        /// <summary>Diventa vero quando l'oggetto ha superato la soglia di conferme.</summary>
        public bool Confermato;

        /// <summary>
        /// Visto abbastanza volte da meritare un'ancora spaziale e un posto su disco.
        /// Sotto questa soglia un oggetto resta in memoria ma non viene reso permanente:
        /// salvare troppo presto significherebbe rendere eterni i doppioni.
        /// </summary>
        public bool Solido;

        /// <summary>UUID dell'ancora spaziale, quando ne ha una. Vuoto altrimenti.</summary>
        public string UuidAncora = "";

        /// <summary>Nome del file dello scatto ritagliato, se catturato.</summary>
        public string Scatto = "";

        public OggettoInventario(string classe, int classeId, Vector3 posizione, float raggio, float tempo)
        {
            Raggio = raggio;
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Classe = classe;
            ClasseId = classeId;
            Posizione = posizione;
            Osservazioni = 1;
            PrimaVista = tempo;
            UltimaVista = tempo;
            Confermato = false;
        }

        /// <summary>
        /// Peso massimo della media mobile.
        ///
        /// Senza questo tetto, un oggetto con 220 osservazioni si sposterebbe di
        /// 1/221 per ogni nuova osservazione: praticamente immobile. Al rientro
        /// nella stanza l'ancora si rilocalizza con qualche centimetro di deriva,
        /// l'oggetto salvato non riusciva piu a raggiungere la sua posizione reale,
        /// e accanto nasceva un doppione che se ne andava con le osservazioni.
        ///
        /// Con il tetto la media resta reattiva quanto basta per inseguire la
        /// deriva, pur restando stabile contro il rumore di un singolo frame.
        /// </summary>
        public const int PesoMassimoMedia = 30;

        /// <summary>
        /// Incorpora una nuova osservazione. La posizione si aggiorna come media
        /// mobile, con peso limitato da <see cref="PesoMassimoMedia"/>.
        /// </summary>
        public void Incorpora(Vector3 posizione, float raggio, float tempo)
        {
            Osservazioni++;

            var peso = Mathf.Min(Osservazioni, PesoMassimoMedia);
            Posizione += (posizione - Posizione) / peso;
            Raggio += (raggio - Raggio) / peso;
            UltimaVista = tempo;
        }

        /// <summary>
        /// Assorbe un altro oggetto giudicato lo stesso di questo. La posizione
        /// risultante e la media pesata sulle osservazioni: quello visto piu volte
        /// conta di piu, perche la sua stima e piu affidabile.
        /// </summary>
        public void Fondi(OggettoInventario altro)
        {
            var totale = Osservazioni + altro.Osservazioni;
            Posizione = (Posizione * Osservazioni + altro.Posizione * altro.Osservazioni) / totale;
            Osservazioni = totale;
            PrimaVista = Mathf.Min(PrimaVista, altro.PrimaVista);
            UltimaVista = Mathf.Max(UltimaVista, altro.UltimaVista);
            Raggio = Mathf.Max(Raggio, altro.Raggio);
            Confermato = Confermato || altro.Confermato;
        }

        public override string ToString() =>
            $"{Classe}#{Id} @{Posizione} ({Osservazioni} oss., {(Confermato ? "confermato" : "candidato")})";
    }
}
