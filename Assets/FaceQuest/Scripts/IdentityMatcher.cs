// Dal vettore di 512 numeri al nome: la ricerca nel database e la decisione.
//
// Tre soglie e non una. Con una soglia sola si e costretti a scegliere fra
// riconoscere in fretta e non sbagliare mai: il nearest neighbour forzato
// assegna SEMPRE un nome, e con volti visti male finisce per attaccare il nome
// di Marco a Laura. Qui in mezzo c'e una zona grigia in cui l'app dice
// "Unknown" e aspetta un frame migliore, che e la risposta onesta.
//
// Oltre alla soglia c'e il MARGINE: se il primo e il secondo candidato sono
// quasi pari, la somiglianza alta non basta, perche vuol dire che
// l'informazione non distingue le due persone. Anche quello e un caso incerto.

using System.Collections.Generic;
using UnityEngine;

namespace FaceQuest
{
    public class IdentityMatcher : MonoBehaviour
    {
        [Header("Soglie di somiglianza (coseno)")]
        [SerializeField, Range(0f, 1f), Tooltip("Sopra questa somiglianza l'identita e considerata certa.")]
        private float m_sogliaAlta = 0.50f;
        [SerializeField, Range(0f, 1f), Tooltip("Sotto questa somiglianza si crea una nuova identita.")]
        private float m_sogliaBassa = 0.32f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Distacco minimo fra il primo e il secondo candidato.")]
        private float m_margineMinimo = 0.06f;

        public float SogliaAlta { get => m_sogliaAlta; set => m_sogliaAlta = value; }
        public float SogliaBassa { get => m_sogliaBassa; set => m_sogliaBassa = value; }
        public float MargineMinimo { get => m_margineMinimo; set => m_margineMinimo = value; }

        public struct Risultato
        {
            public Persona Candidato;
            public float Somiglianza;
            public float Secondo;
            public EsitoIdentita Esito;
        }

        /// <summary>
        /// Cerca l'embedding fra le persone note.
        ///
        /// Il confronto non usa solo il template aggregato ma anche gli embedding
        /// tenuti in memoria per quella persona, prendendo il massimo: un volto
        /// visto di tre quarti somiglia molto al ricordo di tre quarti e poco
        /// alla media di tutti i ricordi.
        /// </summary>
        public Risultato Cerca(float[] embedding, List<Persona> persone)
        {
            var r = new Risultato { Somiglianza = -1f, Secondo = -1f, Esito = EsitoIdentita.Nuova };
            if (embedding == null || persone == null) return r;

            foreach (var p in persone)
            {
                var s = FaceEmbeddingExtractor.Somiglianza(embedding, p.FaceEmbedding);
                foreach (var e in p.EmbeddingHistory)
                {
                    var se = FaceEmbeddingExtractor.Somiglianza(embedding, e);
                    if (se > s) s = se;
                }

                if (s > r.Somiglianza)
                {
                    r.Secondo = r.Somiglianza;
                    r.Somiglianza = s;
                    r.Candidato = p;
                }
                else if (s > r.Secondo)
                {
                    r.Secondo = s;
                }
            }

            if (r.Candidato == null || r.Somiglianza <= m_sogliaBassa)
            {
                r.Esito = EsitoIdentita.Nuova;
                return r;
            }

            if (r.Somiglianza >= m_sogliaAlta)
            {
                var distacco = r.Secondo < 0f ? 1f : r.Somiglianza - r.Secondo;
                r.Esito = distacco >= m_margineMinimo ? EsitoIdentita.Riconosciuta : EsitoIdentita.Incerta;
                return r;
            }

            r.Esito = EsitoIdentita.Incerta;
            return r;
        }
    }
}
